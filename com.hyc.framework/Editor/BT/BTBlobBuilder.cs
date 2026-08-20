// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTBlobBuilder.cs
// 说明: 树资产 → Blob 序列化器
//       1. 由 Connections 构造每节点 children
//       2. 平铺节点数组, 参数分池
//       3. 构造黑板表
//       4. 生成 BlobAssetReference<BTRootBlob>
// ============================================================

using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    public static class BTBlobBuilder
    {
        /// <summary>
        /// 从树资产构建 Blob。成功返回 true 并输出引用; 失败返回 false。
        /// 调用方负责在不用时 Dispose 返回的引用。
        /// </summary>
        public static bool Build(BTTreeAsset asset, out BlobAssetReference<BTRootBlob> result)
        {
            result = default;
            if (asset == null || asset.Nodes.Count == 0)
            {
                Debug.LogError("[BT] 树资产为空, 无法导出");
                return false;
            }

            // 1. 按 Connections 构造 children(源节点聚合目标节点)
            var childMap = new Dictionary<long, List<long>>();
            foreach (var n in asset.Nodes) childMap[n.NodeId] = new List<long>();
            foreach (var c in asset.Connections)
            {
                if (childMap.TryGetValue(c.SourceNodeId, out var list) && !list.Contains(c.TargetNodeId))
                    list.Add(c.TargetNodeId);
            }

            // 2. 节点顺序: 保持资产列表顺序, 建立 NodeId → 索引
            var indexOf = new Dictionary<long, int>();
            for (int i = 0; i < asset.Nodes.Count; i++)
                indexOf[asset.Nodes[i].NodeId] = i;

            // 3. 分池参数
            var floats = new List<float>();
            var longs = new List<long>();
            var strings = new List<string>();

            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref var root = ref builder.ConstructRoot<BTRootBlob>();
                root.TreeId = asset.TreeId;
                root.NodeCount = asset.Nodes.Count;

                var nodeArr = builder.Allocate(ref root.Nodes, asset.Nodes.Count);
                var childArr = builder.Allocate(ref root.ChildNodes, asset.Nodes.Count * 4); // 预分配, 后续填实际

                // 第一遍: 计算各池大小并填充节点结构
                // (BlobBuilder 需先分配, 再填充; 这里先收集参数到临时列表)
                foreach (var n in asset.Nodes)
                {
                    floats.AddRange(n.FloatParams);
                    longs.AddRange(n.LongParams);
                    strings.AddRange(n.StringParams);
                }

                var floatArr = builder.Allocate(ref root.Floats, floats.Count);
                var longArr = builder.Allocate(ref root.Longs, longs.Count);
                var stringArr = builder.Allocate(ref root.Strings, strings.Count);

                for (int i = 0; i < floats.Count; i++) floatArr[i] = floats[i];
                for (int i = 0; i < longs.Count; i++) longArr[i] = longs[i];
                for (int i = 0; i < strings.Count; i++)
                    builder.AllocateString(ref stringArr[i], strings[i]);

                // 填充节点
                int floatCursor = 0, longCursor = 0, stringCursor = 0;
                for (int i = 0; i < asset.Nodes.Count; i++)
                {
                    var n = asset.Nodes[i];
                    var children = childMap[n.NodeId];

                    nodeArr[i] = new BTNodeBlob
                    {
                        Type = n.Type,
                        DefaultState = BTNodeState.None,
                        ChildStart = 0,
                        ChildCount = children.Count,
                        FloatStart = floatCursor,
                        FloatCount = n.FloatParams.Count,
                        LongStart = longCursor,
                        LongCount = n.LongParams.Count,
                        StringStart = stringCursor,
                        StringCount = n.StringParams.Count,
                    };

                    floatCursor += n.FloatParams.Count;
                    longCursor += n.LongParams.Count;
                    stringCursor += n.StringParams.Count;
                }

                // 子节点区间: 构建子节点索引表并回填 ChildStart/ChildCount
                var childIdx = new List<int>();
                var childStartByNode = new Dictionary<long, int>();
                foreach (var n in asset.Nodes)
                {
                    int start = childIdx.Count;
                    childStartByNode[n.NodeId] = start;
                    foreach (var cid in childMap[n.NodeId])
                    {
                        if (indexOf.TryGetValue(cid, out int ci))
                            childIdx.Add(ci);
                    }
                    for (int i = 0; i < asset.Nodes.Count; i++)
                    {
                        if (asset.Nodes[i].NodeId == n.NodeId)
                        {
                            nodeArr[i].ChildStart = start;
                            nodeArr[i].ChildCount = childIdx.Count - start;
                            break;
                        }
                    }
                }
                // 写入子节点索引表
                for (int i = 0; i < childIdx.Count; i++)
                    childArr[i] = childIdx[i];

                // 黑板表
                int keyCount = asset.Blackboard?.Count ?? 0;
                var keyArr = builder.Allocate(ref root.BlackboardKeys, keyCount);
                var bbInts = new List<int>();
                var bbFloats = new List<float>();
                var bbLongs = new List<long>();
                var bbStrings = new List<string>();
                for (int i = 0; i < keyCount; i++)
                {
                    var p = asset.Blackboard[i];
                    int idx = 0;
                    switch (p.ValueType)
                    {
                        case BTBlackboardValueType.Int:
                            idx = bbInts.Count;
                            bbInts.Add(int.TryParse(p.DefaultValue, out var iv) ? iv : 0);
                            break;
                        case BTBlackboardValueType.Float:
                            idx = bbFloats.Count;
                            bbFloats.Add(float.TryParse(p.DefaultValue, out var fv) ? fv : 0f);
                            break;
                        case BTBlackboardValueType.Long:
                            idx = bbLongs.Count;
                            bbLongs.Add(long.TryParse(p.DefaultValue, out var lv) ? lv : 0L);
                            break;
                        case BTBlackboardValueType.String:
                            idx = bbStrings.Count;
                            bbStrings.Add(p.DefaultValue ?? "");
                            break;
                        case BTBlackboardValueType.Bool:
                            idx = bbInts.Count;
                            bbInts.Add(bool.TryParse(p.DefaultValue, out var bv) && bv ? 1 : 0);
                            break;
                    }
                    keyArr[i] = new BTBlackboardKeyBlob
                    {
                        KeyHash = BTBlackboardRuntime.HashKey(p.Key),
                        ValueType = p.ValueType,
                        Index = idx,
                    };
                }
                var bbiArr = builder.Allocate(ref root.BlackboardInts, bbInts.Count);
                var bbfArr = builder.Allocate(ref root.BlackboardFloats, bbFloats.Count);
                var bblArr = builder.Allocate(ref root.BlackboardLongs, bbLongs.Count);
                var bbsArr = builder.Allocate(ref root.BlackboardStrings, bbStrings.Count);
                for (int i = 0; i < bbInts.Count; i++) bbiArr[i] = bbInts[i];
                for (int i = 0; i < bbFloats.Count; i++) bbfArr[i] = bbFloats[i];
                for (int i = 0; i < bbLongs.Count; i++) bblArr[i] = bbLongs[i];
                for (int i = 0; i < bbStrings.Count; i++) builder.AllocateString(ref bbsArr[i], bbStrings[i]);

                result = builder.CreateBlobAssetReference<BTRootBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
            return result.IsCreated;
        }
    }
}
