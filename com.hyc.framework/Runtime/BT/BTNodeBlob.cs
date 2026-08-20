// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTNodeBlob.cs
// 说明: 行为树 Blob 数据布局(纯数据, Burst 友好)
//       节点参数分池存储(floats/longs/strings), 节点内保存区间索引
// ============================================================

using Unity.Entities;

namespace HYC.Framework.BT
{
    /// <summary>Blob 节点。参数通过区间索引指向并列的参数池。</summary>
    public struct BTNodeBlob
    {
        public BTNodeType Type;
        public BTNodeState DefaultState;    // 节点首次执行时的状态(None)

        // 子节点区间(children 数组)
        public int ChildStart;
        public int ChildCount;

        // 参数区间
        public int FloatStart;
        public int FloatCount;
        public int LongStart;
        public int LongCount;
        public int StringStart;
        public int StringCount;
    }

    /// <summary>整棵树的 Blob 根。</summary>
    public struct BTRootBlob
    {
        public long TreeId;
        public int NodeCount;
        public BlobArray<BTNodeBlob> Nodes;
        public BlobArray<int> ChildNodes; // 子节点索引表(每个节点的 ChildStart 指向这里)
        public BlobArray<float> Floats;
        public BlobArray<long> Longs;
        public BlobArray<BlobString> Strings;

        // 黑板定义(键表)
        public BlobArray<BTBlackboardKeyBlob> BlackboardKeys;
        public BlobArray<int> BlackboardInts;
        public BlobArray<float> BlackboardFloats;
        public BlobArray<long> BlackboardLongs;
        public BlobArray<BlobString> BlackboardStrings;
    }

    /// <summary>黑板键定义: 键名哈希 + 类型 + 索引。</summary>
    public struct BTBlackboardKeyBlob
    {
        public ulong KeyHash;           // FNV-1a 64 位哈希
        public BTBlackboardValueType ValueType;
        public int Index;
    }

    /// <summary>黑板值类型。</summary>
    public enum BTBlackboardValueType : byte
    {
        Int = 0,
        Float = 1,
        Long = 2,
        String = 3,
        Bool = 4,
    }

    /// <summary>参数访问器(解释器用, 避免重复数组边界检查)。</summary>
    public unsafe struct BTNodeView
    {
        public BTNodeBlob Node;
        public float* Floats;
        public long* Longs;
        public BlobString* Strings;

        public int ChildCount => Node.ChildCount;
        public int ChildStart => Node.ChildStart;

        public float GetFloat(int i) => Floats[Node.FloatStart + i];
        public long GetLong(int i) => Longs[Node.LongStart + i];
        public BlobString* GetString(int i) => &Strings[Node.StringStart + i];
    }
}
