namespace HYC.Framework.Attr
{
    /// <summary>
    /// 属性值类型。与 <see cref="AttributeValue.raw"/> 的解释方式一一对应：
    /// Int/Long 直接存整数，Bool 用 0/1，Float 用 IEEE754 bits（经 System.BitConverter 存取）。
    /// </summary>
    public enum AttributeType : byte
    {
        Int = 0,
        Float = 1,
        Long = 2,
        Bool = 3,
    }
}
