using System;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// 把「一段设置 UI」挂到 HYC Settings 窗口的某个分组上——**加/删/改分组只改这一行特性**，
    /// 不用再动窗口绘制代码。
    ///
    /// <para><b>挂在哪</b>：字段或方法上。</para>
    /// <list type="bullet">
    /// <item><b>字段</b>：按类型自动出控件（bool→Toggle、int→IntField、float→FloatField、
    /// string→TextField、枚举→EnumPopup），改动即时写入 <see cref="HYCSettings"/> 资产（可用 <see cref="Persist"/> 关掉）。</item>
    /// <item><b>方法</b>：必须是无参 <c>void</c>，由窗口回调自行绘制（用于带按钮/下拉/提示框的复杂区块）。</item>
    /// </list>
    ///
    /// <para><b>分组路径</b>：<c>/</c> 表示嵌套。<c>"General"</c> 是顶层分组；
    /// <c>"Audio/Backend"</c> 出现在左栏「Audio」下缩进一层的「Backend」。
    /// 分组名直接取路径末段（要中文就写中文，要英文就写英文，改特性即可）。</para>
    ///
    /// <para><b>静态成员</b>：任何工程里的静态字段/方法都能挂（注册表扫描程序集时发现），
    /// 因此使用方工程可以在自己的编辑器代码里扩展本窗口，不必改框架。
    /// <b>实例成员</b>：只有被 <see cref="HYCSettingsRegistry.RegisterHost"/> 登记的对象会被扫描
    /// （窗口登记自身，故窗口内的私有方法/字段也生效）。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class HYCSettingsGroupAttribute : Attribute
    {
        /// <summary>分组路径，<c>/</c> 分隔，如 <c>"Audio/Backend"</c>。</summary>
        public string Path { get; }

        /// <summary>
        /// 显示名。字段=该行左侧标签；方法=留空（方法自己画）。
        /// 为 null 时取成员名并做易读化（去掉前导下划线、驼峰拆词）。
        /// </summary>
        public string Label { get; set; }

        /// <summary>同一分组内的排序（升序）。默认 0。</summary>
        public int Order { get; set; }

        /// <summary>
        /// 字段持久化条目键（存于 <see cref="HYCSettings"/> 资产的键值表）。为 null 时自动生成
        /// <c>HYC.Framework.Settings.&lt;类型全名&gt;.&lt;字段名&gt;</c>。
        /// 显式指定可让键保持稳定，改名/换类型才不会丢已存的值。
        /// </summary>
        public string Key { get; set; }

        /// <summary>字段改动是否写入 <see cref="HYCSettings"/> 资产。默认 true。</summary>
        public bool Persist { get; set; } = true;

        /// <param name="path">分组路径，如 <c>"General"</c> 或 <c>"Audio/Backend"</c>。</param>
        public HYCSettingsGroupAttribute(string path)
        {
            Path = path;
        }
    }
}
