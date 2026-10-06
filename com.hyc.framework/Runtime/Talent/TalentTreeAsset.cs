using System;
using System.Collections.Generic;
using UnityEngine;
using HYC.Framework.Config;

namespace HYC.Framework.Talent
{
    /// <summary>
    /// 一条消耗：消耗<b>黑板上的某个属性</b>多少点。
    /// 注意这里存的是属性 key 而不是数值本身——数值是多少由该属性指向的配置表决定，
    /// 天赋树不认识"力量"是什么，它只认识 key。
    /// </summary>
    [Serializable]
    public class TalentCost
    {
        /// <summary>黑板属性 key，必须能在所属树的黑板里找到，否则校验报错。</summary>
        public string attrKey;

        /// <summary>消耗数量。</summary>
        public int amount;

        public TalentCost() { }

        public TalentCost(string attrKey, int amount)
        {
            this.attrKey = attrKey;
            this.amount = amount;
        }
    }

    // TalentNode 已移到同目录的 TalentNode.cs —— Unity 要求 ScriptableObject 类型与文件同名，
    // 否则 AssetDatabase.AddObjectToAsset 写出的子资源解析不到 MonoScript，重导入后会变成 null。

    /// <summary>节点之间的前置关系：<see cref="from"/> 是 <see cref="to"/> 的前置。整张图必须无环。</summary>
    [Serializable]
    public class TalentLink
    {
        public int from;
        public int to;

        public TalentLink() { }

        public TalentLink(int from, int to)
        {
            this.from = from;
            this.to = to;
        }
    }

    // ConfigRowRef 已迁至 HYC.Framework.Config（见 Runtime/ConfigRowRef.cs）。

    /// <summary>
    /// 黑板上的一条属性。<b>属性来源是配置表</b>——key 是树内唯一符号，
    /// 真正的数据（名字、图标、上限）在 <see cref="sourceRow"/> 指向的配置行里。
    /// </summary>
    [Serializable]
    public class TalentAttribute
    {
        /// <summary>树内唯一的属性符号，节点消耗引用它。</summary>
        public string key;

        /// <summary>指向配置表的一行。为空表示尚未绑定。</summary>
        public ConfigRowRef sourceRow = new ConfigRowRef();

        public TalentAttribute() { }

        public TalentAttribute(string key)
        {
            this.key = key;
        }
    }

    /// <summary>
    /// 一棵天赋树。
    ///
    /// 这个资产放在 <b>Runtime</b> 程序集是有意为之：行为树的 <c>BTTreeAsset</c> 因为放在 Editor
    /// 程序集，打包后一棵树都拿不到，不得不额外补一条「导出 Blob → 运行期读回」的链路。
    /// 天赋树数据量小（几十到几百节点）、查询不频繁，直接用 ScriptableObject 序列化即可，
    /// 打包即用，不需要复刻那套导出流程，也不会出现「编辑器能跑 ≠ 真机能跑」。
    /// </summary>
    [CreateAssetMenu(menuName = "HYC Framework/Talent/Talent Tree", fileName = "NewTalentTree")]
    public class TalentTreeAsset : ScriptableObject
    {
        /// <summary>树标识，全局唯一。运行时用它注册。</summary>
        public long treeId;

        public string displayName;

        public List<TalentAttribute> blackboard = new List<TalentAttribute>();
        public List<TalentNode> nodes = new List<TalentNode>();
        public List<TalentLink> links = new List<TalentLink>();

        /// <summary>保证 treeId 有值。id 取 Guid 前 8 字节，与 <c>ConfigId</c> 的生成规则相同。</summary>
        public void EnsureTreeId()
        {
            if (treeId != 0) return;
            treeId = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
            if (treeId == 0) treeId = DateTime.UtcNow.Ticks;
        }

        public TalentNode NodeById(int id)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && nodes[i].id == id) return nodes[i];
            }
            return null;
        }

        public TalentAttribute AttributeByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < blackboard.Count; i++)
            {
                if (blackboard[i] != null && blackboard[i].key == key) return blackboard[i];
            }
            return null;
        }

        /// <summary>生成节点 id：现有最大 id + 1，空树从 1 开始。</summary>
        public int NextNodeId()
        {
            int max = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && nodes[i].id > max) max = nodes[i].id;
            }
            return max + 1;
        }

        /// <summary><paramref name="node"/> 的全部前置节点。顺序不确定。</summary>
        public List<TalentNode> PredecessorsOf(TalentNode node)
        {
            var list = new List<TalentNode>();
            if (node == null) return list;
            for (int i = 0; i < links.Count; i++)
            {
                var l = links[i];
                if (l == null || l.to != node.id) continue;
                var n = NodeById(l.from);
                if (n != null) list.Add(n);
            }
            return list;
        }

        /// <summary><paramref name="node"/> 的全部后继节点。</summary>
        public List<TalentNode> SuccessorsOf(TalentNode node)
        {
            var list = new List<TalentNode>();
            if (node == null) return list;
            for (int i = 0; i < links.Count; i++)
            {
                var l = links[i];
                if (l == null || l.from != node.id) continue;
                var n = NodeById(l.to);
                if (n != null) list.Add(n);
            }
            return list;
        }
    }
}
