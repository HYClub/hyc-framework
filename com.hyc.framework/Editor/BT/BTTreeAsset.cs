// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTTreeAsset.cs
// 说明: 行为树资产(ScriptableObject) - 编辑器画树的数据载体
//       导出时经 BTBlobBuilder 序列化为 BTRootBlob
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    [CreateAssetMenu(fileName = "BTTree", menuName = "HYC/BT/Tree")]
    public class BTTreeAsset : ScriptableObject
    {
        [Header("标识")]
        public long TreeId;
        public BTTreeKind Kind;

        [Header("属性枚举")]
        [Tooltip("拖入属性枚举 .cs 文件(如 Attr), 树节点里的属性下拉将使用它")]
        public UnityEditor.MonoScript AttributeEnumScript;

        [Header("节点")]
        public List<BTNodeData> Nodes = new List<BTNodeData>();

        [Header("连线(导出时构造成 children)")]
        public List<BTConnectionData> Connections = new List<BTConnectionData>();

        [Header("黑板定义")]
        public List<BTBlackboardParam> Blackboard = new List<BTBlackboardParam>();

        /// <summary>
        /// 确保树里有且补齐一个 Root(入口)节点，返回它(已有则直接返回已有的)。
        ///
        /// <b>用途</b>：程序化建树时调用(没有 Root 的树无法导出, 见 BTValidator / BTBlobBuilder)。
        /// <b>编辑器 UI 不调用它</b>——编辑器里新树统一从「空画布 + 中央引导文案」开始
        /// (见 BTGraphIMGUI.DrawEmptyCanvasHint), 校验只在画布上有节点后才介入;
        /// 提前塞一个孤立的 Root 反而会立刻触发「Root 未连线」报错。
        /// </summary>
        public BTNodeData EnsureRootNode()
        {
            var root = Nodes.FirstOrDefault(n => n.Type == BTNodeType.Root);
            if (root != null) return root;
            root = new BTNodeData
            {
                NodeId = NewNodeId(),
                Type = BTNodeType.Root,
                Position = new Vector2(40f, 200f),   // 与 ConvertToSubTree 预置 Root 同位
            };
            Nodes.Add(root);
            return root;
        }

        /// <summary>与 BTGraphIMGUI.NewNodeId 同规则：GUID 正整数, 不为 0。</summary>
        private static long NewNodeId()
        {
            var id = Guid.NewGuid().GetHashCode() & 0x7fffffff;
            return id == 0 ? 1 : id;
        }

        /// <summary>列出项目内所有行为树资产(名称 + TreeId), 供节点里的"子树"下拉选择。</summary>
        public static TreeIdList LoadAllTreeIds()
        {
            var guids = AssetDatabase.FindAssets("t:BTTreeAsset");
            var names = new List<string> { "(无)" };
            var ids = new List<long> { 0 };
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var tree = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(path);
                if (tree == null) continue;
                names.Add($"{tree.TreeId}: {tree.name}");
                ids.Add(tree.TreeId);
            }
            return new TreeIdList { names = names.ToArray(), ids = ids.ToArray() };
        }
    }

    /// <summary>树 id 列表(names[0]="(无)" / ids[0]=0), 与下拉索引一一对应。</summary>
    public struct TreeIdList
    {
        public string[] names;
        public long[] ids;
    }

    /// <summary>编辑器节点数据。</summary>
    [Serializable]
    public class BTNodeData
    {
        public long NodeId;
        public BTNodeType Type;
        public Vector2 Position;
        public string Note = "";      // 节点备注(说明文字)
        public List<float> FloatParams = new List<float>();
        public List<long> LongParams = new List<long>();
        public List<string> StringParams = new List<string>();
        // 运行时 children 由 Connections 导出时生成
    }

    /// <summary>编辑器连线(源 → 目标)。PortIndex = 源节点输出端口序号(决定执行顺序)。</summary>
    [Serializable]
    public class BTConnectionData
    {
        public long SourceNodeId;
        public long TargetNodeId;
        public int PortIndex;   // 源节点输出端口序号(0=第一个, 决定 Sequence 子节点顺序)
    }

    /// <summary>黑板参数定义。</summary>
    [Serializable]
    public class BTBlackboardParam
    {
        public string Key;
        public BTBlackboardValueType ValueType;
        public string DefaultValue;
    }
}
