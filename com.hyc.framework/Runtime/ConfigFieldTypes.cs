namespace HYC.Framework.Config
{
    /// <summary>
    /// <see cref="ConfigFieldType"/> 的**纯类型判定**（无状态、无 Unity 依赖）。
    ///
    /// <para><b>为什么集中在这里</b>：模板编辑器（UI 该给哪些选项）与代码生成器
    /// （该不该拦、该生成什么类型）必须对「哪些字段类型属于哪一类」有完全一致的判断。
    /// 此前两侧各写一份 <c>switch</c>，只靠注释「必须与另一侧保持一致」维系——
    /// 新增字段类型时漏改任何一处，就会出现「UI 允许选、生成期却拦」或
    /// 「UI 已禁选、生成器却仍放行」的错位，且不会报错，只会静默产出错误数据。</para>
    ///
    /// <para><b>维护约定</b>：新增 <see cref="ConfigFieldType"/> 成员时，先在这里归类，
    /// 再补 <c>UnityTypeName</c> / <c>BlobElemInfoOf</c> / <c>GUIType</c> 三处映射。
    /// 编辑器侧与生成器侧的判定方法都委托到这里，不再各自维护 switch。</para>
    /// </summary>
    public static class ConfigFieldTypes
    {
        /// <summary>
        /// Unity 资源/对象引用族（编辑器里存的是对象引用，不是值）。
        /// 与「可导出为 资源名 / 路径 / AA地址」的集合完全相同。
        /// </summary>
        public static bool IsAssetRef(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.Sprite:
                case ConfigFieldType.Texture2D:
                case ConfigFieldType.GameObject:
                case ConfigFieldType.AudioClip:
                case ConfigFieldType.Material:
                case ConfigFieldType.Mesh:
                case ConfigFieldType.PhysicMaterial:
                case ConfigFieldType.Font:
                case ConfigFieldType.Shader:
                case ConfigFieldType.TextAsset:
                case ConfigFieldType.Object:
                case ConfigFieldType.AnimationClip:
                case ConfigFieldType.AnimatorController:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 是否存在真正的「空」态（null / 空串），即「非空」检查是否有意义。
        /// 字符串族 / 配置引用 / Unity 对象族 / 行为树 / Gradient / 曲线支持；
        /// 纯值类型（数值 / 布尔 / 枚举 / 结构体）永远有值，「非空」无从判起。
        /// </summary>
        public static bool SupportsNotEmpty(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.String:
                case ConfigFieldType.LocalizedKey:
                case ConfigFieldType.Addressable:
                case ConfigFieldType.AudioEvent:
                case ConfigFieldType.Reference:
                case ConfigFieldType.BehaviourTree:
                case ConfigFieldType.Sprite:
                case ConfigFieldType.Texture2D:
                case ConfigFieldType.GameObject:
                case ConfigFieldType.AudioClip:
                case ConfigFieldType.Material:
                case ConfigFieldType.Mesh:
                case ConfigFieldType.PhysicMaterial:
                case ConfigFieldType.Font:
                case ConfigFieldType.Shader:
                case ConfigFieldType.TextAsset:
                case ConfigFieldType.Object:
                case ConfigFieldType.AnimationClip:
                case ConfigFieldType.AnimatorController:
                case ConfigFieldType.Gradient:
                case ConfigFieldType.AnimationCurve:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>是否为数值字段类型（可配 Min/Max、「非0」「范围」检查）。</summary>
        public static bool IsNumeric(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.Int:
                case ConfigFieldType.Long:
                case ConfigFieldType.Float:
                case ConfigFieldType.Double:
                case ConfigFieldType.Short:
                case ConfigFieldType.Byte:
                case ConfigFieldType.UInt:
                case ConfigFieldType.Decimal:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 服务器侧「直接导出」不可用的类型：Unity 对象引用族（Newtonsoft 会撞上
        /// <c>gameObject</c> 自引用循环）+ 非数值结构 Color/Color32 + 不可 JSON 序列化的
        /// Gradient/AnimationCurve。
        ///
        /// <para>两个用途共用同一集合：① 模板编辑器据此在**服务器导出下拉里只提供「不导出」**
        /// （这类字段在服务器数据里没有意义）；② 生成期据此把「服务器 + 直接导出」判为硬错误，
        /// 拦住历史存档里的残留配置。</para>
        /// </summary>
        public static bool IsServerUnsupported(ConfigFieldType type)
            => IsAssetRef(type)
               || type == ConfigFieldType.Color
               || type == ConfigFieldType.Color32
               || type == ConfigFieldType.Gradient
               || type == ConfigFieldType.AnimationCurve;
    }
}
