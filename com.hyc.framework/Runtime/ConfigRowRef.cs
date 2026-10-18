using System;

namespace HYC.Framework.Config
{
    /// <summary>
    /// 引用配置表的一行（类型全名 + 行 id），用于「某实体/某资产引用了某配置行」这类数据。
    /// 原定义于 Talent 模块，现归位到 Config（它是通用引用结构，不专属 Talent）。
    /// 与框架配置管线的约定保持一致：不直接持有 ScriptableObject，
    /// 运行期用 <c>ConfigRegistry.TryGet&lt;T&gt;(id)</c> 取回数据。
    /// </summary>
    [Serializable]
    public class ConfigRowRef
    {
        /// <summary>配置类全名，例如 <c>{命名空间root}.config.AttrConfig</c>（root 由 HYC Settings → CodeGen 派生）。</summary>
        public string typeName;

        /// <summary><c>ConfigId.id</c>。</summary>
        public long id;

        /// <summary>选择时的快照，仅用于引用断裂后还能看清原本指向谁。</summary>
        public string displayName;

        public bool IsEmpty => string.IsNullOrEmpty(typeName) || id == 0;

        public override string ToString()
            => string.IsNullOrEmpty(displayName) ? (IsEmpty ? "(未绑定)" : typeName + "#" + id) : displayName;
    }
}
