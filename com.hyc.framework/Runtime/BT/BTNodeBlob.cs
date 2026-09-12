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

        // ---- 参数"字面 / 变量"统一约定 ----
        // 约定: Float[i] 存字面值; Long[i] 若非 0 则视为黑板键, 运行时优先读黑板(取不到回退字面值)。
        // 这对应 NodeCanvas 的 BBParameter<T>(要么是字面值, 要么是命名变量的引用)。

        /// <summary>读 float 参数: Long[i] 非 0 时读黑板变量, 否则用 Float[i] 字面值。</summary>
        public float GetFloatVar(ref BTContext ctx, int i)
        {
            if (Node.FloatCount > i)
            {
                float literal = Floats[Node.FloatStart + i];
                long key = Node.LongCount > i ? Longs[Node.LongStart + i] : 0;
                return key != 0 && ctx.Blackboard.IsCreated ? ctx.Blackboard.GetFloat((ulong)key, literal) : literal;
            }
            return 0f;
        }

        /// <summary>读 int 参数(存于 Long[i] 字面, 或 Long[i] 作为黑板键读 int 变量)。</summary>
        public int GetIntVar(ref BTContext ctx, int i, int def = 0)
        {
            if (Node.LongCount <= i) return def;
            long raw = Longs[Node.LongStart + i];
            return ctx.Blackboard.IsCreated && raw != 0 ? ctx.Blackboard.GetInt((ulong)raw, def) : (int)raw;
        }
    }
}
