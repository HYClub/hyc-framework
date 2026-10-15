// =============================================================================
//  HYCSimple · 行为树示例 · 编辑器建树 + 导出（编辑器专用）
//  放在 Editor/ 子目录 → 编译进 HYCSimple.Editor，可引用 HYC.Framework.BT.Editor。
//  演示「程序化建树资产 → 导出 Blob」这条编辑器链路，对应真机打包前必须做的一步。
//
//  本例建的不再是无意义的 Root→Sequence→[NoOp,NoOp]，而是一棵**有真实行为数据**的
//  敌人 AI 树：
//      Root
//       └─ Selector(EnemyAI)             优先战斗，否则巡逻
//           ├─ Port0 → Sequence(Combat)
//           │    ├─ Port0 → GameCustom(1)  IsTargetInRange（条件）
//           │    └─ Port1 → GameCustom(2)  Attack（动作）
//           └─ Port1 → GameCustom(3)       Patrol（动作，持续 Running）
//  GameCustom(1/2/3) 对应 HYCSimpleEnemyBTNodes.cs 里的三个运行时节点，
//  它们会被 BTNodeRuntimeRegistry 反射自动注册，进 Play 后由 BTInterpreterSystem 真正执行。
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using HYC.Framework.BT;
using HYC.Framework.BT.Editor;

namespace HYC.Simple.Editor
{
    public static class HYCSimpleBTTreeBuilder
    {
        // 900001：刻意避开 1001/10001 这类业务常用 ID。示例树会被导出到
        // StreamingAssets/BTBlob/<treeId>.blob，若用 1001 很可能覆盖宿主工程自己的树。
        private const long SampleTreeId = 900001;
        private const string AssetDir = "Assets/HYCSampleTrees";   // 生成的示例树独立目录（与样例导入目录区分）
        private const string AssetPath = AssetDir + "/HYCSimpleDemoTree.asset";

        /// <summary>程序化建示例树资产 → 导出 Blob（菜单与统一「创建全部示例资产」共用）。</summary>
        public static void CreateSampleTree()
        {
            if (!AssetDatabase.IsValidFolder(AssetDir))
                AssetDatabase.CreateFolder("Assets", "HYCSampleTrees");

            var tree = ScriptableObject.CreateInstance<BTTreeAsset>();
            // 示例 TreeId 固定（稳定导出路径 <treeId>.blob）；节点 ID 由工厂 BTAssetFactory.NextNodeId 统管派发，外部不手填。
            tree.TreeId = SampleTreeId;
            tree.Kind = BTTreeKind.Other;

            // 节点 ID：工厂统管（树内最小空闲，必然唯一）。示例树为空，确定性派发为 1..6。
            long root   = BTAssetFactory.NextNodeId(tree);
            long sel    = BTAssetFactory.NextNodeId(tree);
            long seq    = BTAssetFactory.NextNodeId(tree);
            long cond   = BTAssetFactory.NextNodeId(tree);
            long atk    = BTAssetFactory.NextNodeId(tree);
            long patrol = BTAssetFactory.NextNodeId(tree);

            // —— 节点：内置复合/装饰 + 三个 GameCustom（叶子，代表真实行为）——
            tree.Nodes.Add(new BTNodeData { NodeId = root,   Type = BTNodeType.Root,     Position = new Vector2(40, 280) });
            tree.Nodes.Add(new BTNodeData { NodeId = sel,    Type = BTNodeType.Selector, Position = new Vector2(260, 280) });
            tree.Nodes.Add(new BTNodeData { NodeId = seq,    Type = BTNodeType.Sequence, Position = new Vector2(480, 180) });
            tree.Nodes.Add(new BTNodeData { NodeId = cond,   Type = BTNodeType.GameCustom, Position = new Vector2(720, 80),  LongParams = new List<long> { 1 } });
            tree.Nodes.Add(new BTNodeData { NodeId = atk,    Type = BTNodeType.GameCustom, Position = new Vector2(720, 220), LongParams = new List<long> { 2 } });
            tree.Nodes.Add(new BTNodeData { NodeId = patrol, Type = BTNodeType.GameCustom, Position = new Vector2(480, 400), LongParams = new List<long> { 3 } });

            // —— 连线（PortIndex 决定子节点执行顺序）——
            tree.Connections.Add(new BTConnectionData { SourceNodeId = root,   TargetNodeId = sel,    PortIndex = 0 });
            tree.Connections.Add(new BTConnectionData { SourceNodeId = sel,    TargetNodeId = seq,    PortIndex = 0 }); // 优先战斗
            tree.Connections.Add(new BTConnectionData { SourceNodeId = sel,    TargetNodeId = patrol, PortIndex = 1 }); // 否则巡逻
            tree.Connections.Add(new BTConnectionData { SourceNodeId = seq,    TargetNodeId = cond,   PortIndex = 0 });
            tree.Connections.Add(new BTConnectionData { SourceNodeId = seq,    TargetNodeId = atk,    PortIndex = 1 });

            // 黑板定义（树数据的一部分；运行期若实体挂了黑板即可被节点读取，本例叶子不依赖它）
            tree.Blackboard = new List<BTBlackboardParam>
            {
                new BTBlackboardParam { Key = "PatrolInterval", ValueType = BTBlackboardValueType.Float, DefaultValue = "1.0" },
            };

            // 2) 落盘为资产（删旧建新，避免重复）
            if (File.Exists(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            AssetDatabase.CreateAsset(tree, AssetPath);
            AssetDatabase.SaveAssets();

            // 3) 导出 Blob（只导出本示例树，不清空他人已导出的树；写 StreamingAssets/BTBlob/<treeId>.blob + 同步 manifest）
            int ok = BTBlobExporter.ExportTrees(new[] { tree });
            Debug.Log($"[HYCSimple] 已创建示例树(敌人AI)并导出 {ok} 棵 → {BTBlobExporter.DefaultOutputFolder}。" +
                      "进 Play 后 BTInterpreterSystem 会逐帧 Tick，自定义节点见 HYCSimpleEnemyBTNodes.cs。");
        }

        [MenuItem("HYC Framework/Samples/BT/创建示例树并导出", false, 30)]
        public static void BuildAndExport() => CreateSampleTree();
    }
}
#endif
