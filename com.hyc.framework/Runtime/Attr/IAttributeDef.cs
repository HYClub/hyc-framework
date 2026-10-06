namespace HYC.Framework.Attr
{
    /// <summary>
    /// 属性目录行（AttributeDef 配置行）与框架之间的接缝。
    /// 使用方用配置管线生成的配置行去实现它；框架只读接口，不直接认识 "HP"/"ATK" 等具体属性名。
    /// 实现此接口的结构体必须 unmanaged（配置行本就是 blittable 结构体；string 属性请返回常量、不要存引用字段）。
    /// </summary>
    public interface IAttributeDef
    {
        /// <summary>属性符号，全局唯一（如 "HP"）。运行时据此算 keyHash。</summary>
        string Key { get; }
        /// <summary>值类型。</summary>
        AttributeType ValueType { get; }
        /// <summary>默认值（Float 需用 System.BitConverter.DoubleToInt64Bits 预存 bits）。</summary>
        long DefaultRaw { get; }
        /// <summary>下限（钳制用，Float 同样存 bits）。</summary>
        long MinRaw { get; }
        /// <summary>上限（钳制用，Float 同样存 bits）。</summary>
        long MaxRaw { get; }
    }
}
