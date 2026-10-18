using System;
using UnityEngine;

namespace HYC.Framework.RedDot
{
    /// <summary>
    /// 红点节点键（强类型）。
    ///
    /// <para><b>代码侧零字符串</b>：业务代码用 <c>new RedDotKey(1001)</c> 或宿主定义的
    /// <c>public static readonly RedDotKey Mail = new(1001);</c> 作为常量；键的比较、字典索引
    /// 全部走 <see cref="Id"/>，不涉及字符串拼接，也就不会出现拼错 Key 静默失效。</para>
    ///
    /// <para><b>预制体侧可序列化</b>：字段是 <c>[SerializeField] int</c>，<see cref="RedDotBehaviour"/>
    /// 挂在预制体上时能直接存盘；编辑器在已注册节点非空时会把 id 显示成节点名（见 <c>RedDotKeyDrawer</c>）。</para>
    ///
    /// <para><b>id 约定</b>：<c>0</c> 视为「无效键 / 无父节点」（<see cref="IsValid"/> 为 false）。
    /// 建议按业务域分段（如 1000 段 = 邮件、2000 段 = 商店），避免多模块撞号。</para>
    /// </summary>
    [Serializable]
    public struct RedDotKey : IEquatable<RedDotKey>
    {
        [SerializeField] private int _id;

        public RedDotKey(int id) => _id = id;

        /// <summary>原始 id（序列化与字典索引用）。</summary>
        public int Id => _id;

        /// <summary>是否为有效键（0 = 无效）。</summary>
        public bool IsValid => _id != 0;

        public bool Equals(RedDotKey other) => _id == other._id;

        public override bool Equals(object obj) => obj is RedDotKey other && _id == other._id;

        public override int GetHashCode() => _id;

        public override string ToString() => RedDotManager.GetDisplayName(_id) ?? ("#" + _id);

        public static bool operator ==(RedDotKey a, RedDotKey b) => a._id == b._id;

        public static bool operator !=(RedDotKey a, RedDotKey b) => a._id != b._id;
    }
}
