using Unity.Mathematics;

namespace HYC.Framework.Input
{
    /// <summary>
    /// 富状态输入分量（纯数据，不引用 InputSystem）。由 InputComponentGenerator 按 action 的
    /// expectedControlType 选类型生成到组件字段上。类型本身就是「按钮/轴/摇杆」的分类，
    /// 值是「点亮/有数值/偏移量」，宿主捞出来即可直接判。
    /// </summary>

    /// <summary>按钮（0/1，扳机类可取 0..1）。<c>pressed</c> = 点亮。</summary>
    public struct InputButton
    {
        public float value;
        public byte phase;
        public bool justPressed, justReleased;
        public bool pressed => value >= 0.5f;
    }

    /// <summary>轴（可带正负）。<c>active</c> = 有数值。</summary>
    public struct InputAxis
    {
        public float value;
        public byte phase;
        public bool justPressed, justReleased;
        public bool active => math.abs(value) > 1e-5f;
    }

    /// <summary>二维向量（Stick / Vector2）。<c>active</c> = 摇杆有偏移。</summary>
    public struct InputVector2
    {
        public float2 value;
        public byte phase;
        public bool justPressed, justReleased;
        public float magnitude => math.length(value);
        public bool active => magnitude > 1e-5f;
    }

    /// <summary>三维向量（Vector3）。</summary>
    public struct InputVector3
    {
        public float3 value;
        public byte phase;
        public bool justPressed, justReleased;
        public float magnitude => math.length(value);
        public bool active => magnitude > 1e-5f;
    }

    /// <summary>朝向（Quaternion）。一般不关心边沿。</summary>
    public struct InputQuaternion
    {
        public quaternion value;
        public byte phase;
    }

    /// <summary>离散档位（Integer / Discrete）。</summary>
    public struct InputInteger
    {
        public int value;
        public byte phase;
        public bool justPressed, justReleased;
    }
}
