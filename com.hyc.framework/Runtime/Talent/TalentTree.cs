using System;
using System.Collections.Generic;

namespace HYC.Framework.Talent
{
    /// <summary>
    /// 一棵天赋树的<b>只读运行期视图</b>。把 <see cref="TalentTreeAsset"/> 的线性列表建成索引，
    /// 避免每次查询都遍历。
    ///
    /// 它只回答「这棵树长什么样」，<b>不回答</b>「某个玩家有没有解锁这个节点」——那是玩法层的事，
    /// 取决于玩家的等级、点数、已投入点数，框架不该替游戏做这个决定。
    /// </summary>
    public sealed class TalentTree
    {
        public TalentTreeAsset Asset { get; private set; }
        public long TreeId { get; private set; }

        private readonly Dictionary<int, TalentNode> _nodeById = new Dictionary<int, TalentNode>();
        private readonly Dictionary<int, List<TalentNode>> _preds = new Dictionary<int, List<TalentNode>>();
        private readonly Dictionary<int, List<TalentNode>> _succs = new Dictionary<int, List<TalentNode>>();
        private readonly Dictionary<string, TalentAttribute> _attrs = new Dictionary<string, TalentAttribute>();

        public IEnumerable<TalentNode> Nodes => Asset != null ? Asset.nodes : (IEnumerable<TalentNode>)new TalentNode[0];
        public int NodeCount => Asset != null ? Asset.nodes.Count : 0;

        private TalentTree() { }

        /// <summary>从资产建索引。树里若存在重复 id，后出现的会被忽略。</summary>
        public static TalentTree Build(TalentTreeAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));

            var tree = new TalentTree { Asset = asset, TreeId = asset.treeId };

            tree._nodeById.Clear();
            tree._preds.Clear();
            tree._succs.Clear();
            tree._attrs.Clear();

            for (int i = 0; i < asset.nodes.Count; i++)
            {
                var n = asset.nodes[i];
                if (n == null) continue;
                if (!tree._nodeById.ContainsKey(n.id))
                {
                    tree._nodeById.Add(n.id, n);
                }
            }

            for (int i = 0; i < asset.links.Count; i++)
            {
                var l = asset.links[i];
                if (l == null) continue;
                var from = tree.GetNode(l.from);
                var to = tree.GetNode(l.to);
                if (from == null || to == null) continue;   // 悬空连线：校验会报，这里忽略

                if (!tree._succs.TryGetValue(from.id, out var s))
                {
                    s = new List<TalentNode>();
                    tree._succs.Add(from.id, s);
                }
                if (!s.Contains(to)) s.Add(to);

                if (!tree._preds.TryGetValue(to.id, out var p))
                {
                    p = new List<TalentNode>();
                    tree._preds.Add(to.id, p);
                }
                if (!p.Contains(from)) p.Add(from);
            }

            for (int i = 0; i < asset.blackboard.Count; i++)
            {
                var a = asset.blackboard[i];
                if (a == null || string.IsNullOrEmpty(a.key)) continue;
                if (!tree._attrs.ContainsKey(a.key)) tree._attrs.Add(a.key, a);
            }

            return tree;
        }

        public TalentNode GetNode(int id)
        {
            TalentNode n;
            return _nodeById.TryGetValue(id, out n) ? n : null;
        }

        /// <summary>前置节点列表（可能为空）。返回的列表每次新建，调用方可安全修改。</summary>
        public List<TalentNode> PredecessorsOf(int id)
        {
            List<TalentNode> list;
            return _preds.TryGetValue(id, out list) ? new List<TalentNode>(list) : new List<TalentNode>();
        }

        /// <summary>后继节点列表（可能为空）。</summary>
        public List<TalentNode> SuccessorsOf(int id)
        {
            List<TalentNode> list;
            return _succs.TryGetValue(id, out list) ? new List<TalentNode>(list) : new List<TalentNode>();
        }

        public bool TryGetAttribute(string key, out TalentAttribute attr)
        {
            attr = null;
            return !string.IsNullOrEmpty(key) && _attrs.TryGetValue(key, out attr);
        }

        /// <summary>
        /// 运行期自检：连线指向不存在的节点、出现环、节点消耗了黑板上没有的属性、
        /// 属性没有绑定配置行。返回错误数量，0 表示通过。
        ///
        /// 编辑器侧有一份更严格的校验（<c>TalentTreeValidator</c>，会额外检查
        /// 孤立节点、重复连线、属性无人使用等），这份是运行时的最后一道防线。
        /// </summary>
        public int Validate(List<string> errors)
        {
            if (errors == null) errors = new List<string>();
            if (Asset == null)
            {
                errors.Add("资产为空");
                return errors.Count;
            }

            var name = string.IsNullOrEmpty(Asset.displayName) ? Asset.name : Asset.displayName;

            var seen = new HashSet<int>();
            for (int i = 0; i < Asset.links.Count; i++)
            {
                var l = Asset.links[i];
                if (l == null) continue;
                if (GetNode(l.from) == null) errors.Add(name + ": 连线引用了不存在的源节点 " + l.from);
                if (GetNode(l.to) == null) errors.Add(name + ": 连线引用了不存在的目标节点 " + l.to);
                seen.Add(l.to);
            }

            for (int i = 0; i < Asset.nodes.Count; i++)
            {
                var n = Asset.nodes[i];
                if (n == null) continue;
                if (HasCycleFrom(n.id)) errors.Add(name + ": 节点「" + n.title + "」处在环上，天赋树必须是有向无环图");
                for (int j = 0; j < n.costs.Count; j++)
                {
                    var c = n.costs[j];
                    if (c == null) continue;
                    TalentAttribute dummy;
                    if (!TryGetAttribute(c.attrKey, out dummy))
                        errors.Add(name + ": 节点「" + n.title + "」消耗了黑板上不存在的属性 " + c.attrKey);
                }
            }

            for (int i = 0; i < Asset.blackboard.Count; i++)
            {
                var a = Asset.blackboard[i];
                if (a == null) continue;
                if (a.sourceRow == null || a.sourceRow.IsEmpty)
                    errors.Add(name + ": 黑板属性 " + a.key + " 没有绑定配置行");
            }

            return errors.Count;
        }

        /// <summary>从 <paramref name="startId"/> 出发沿后继方向能否回到自己。</summary>
        private bool HasCycleFrom(int startId)
        {
            var stack = new Stack<int>();
            var visited = new HashSet<int>();
            stack.Push(startId);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                List<TalentNode> next;
                if (!_succs.TryGetValue(cur, out next)) continue;
                for (int i = 0; i < next.Count; i++)
                {
                    var id = next[i].id;
                    if (id == startId) return true;
                    if (visited.Add(id)) stack.Push(id);
                }
            }
            return false;
        }
    }
}
