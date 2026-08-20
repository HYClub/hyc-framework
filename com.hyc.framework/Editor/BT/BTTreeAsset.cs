// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTTreeAsset.cs
// 说明: 行为树资产(ScriptableObject) - 编辑器画树的数据载体
//       导出时经 BTBlobBuilder 序列化为 BTRootBlob
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    /// <summary>树资产类型: 技能树 / 角色AI树 / 其他。</summary>
    public enum BTTreeKind
    {
        Skill = 0,
        AI = 1,
        Other = 2,
    }

    [CreateAssetMenu(fileName = "BTTree", menuName = "HYC/BT/Tree")]
    public class BTTreeAsset : ScriptableObject
    {
        [Header("标识")]
        public long TreeId;
        public BTTreeKind Kind;

        [Header("节点")]
        public List<BTNodeData> Nodes = new List<BTNodeData>();

        [Header("连线(导出时构造成 children)")]
        public List<BTConnectionData> Connections = new List<BTConnectionData>();

        [Header("黑板定义")]
        public List<BTBlackboardParam> Blackboard = new List<BTBlackboardParam>();
    }

    /// <summary>编辑器节点数据。</summary>
    [Serializable]
    public class BTNodeData
    {
        public long NodeId;
        public BTNodeType Type;
        public Vector2 Position;
        public List<float> FloatParams = new List<float>();
        public List<long> LongParams = new List<long>();
        public List<string> StringParams = new List<string>();
        // 运行时 children 由 Connections 导出时生成
    }

    /// <summary>编辑器连线(源 → 目标)。</summary>
    [Serializable]
    public class BTConnectionData
    {
        public long SourceNodeId;
        public long TargetNodeId;
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
