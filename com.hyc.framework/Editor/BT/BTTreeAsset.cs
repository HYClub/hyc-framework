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
