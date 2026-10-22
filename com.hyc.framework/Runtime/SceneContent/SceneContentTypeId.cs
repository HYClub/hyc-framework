using System.Text;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 场景内容类型 id 的算法（单一真源）。生成器、导出器、运行期共用一个实现，
    /// 保证「生成代码里写死的常量」与「导出进 blob 的 id」永远是同一个数。
    /// <para>
    /// 用 <b>FNV-1a 32 位</b>对 <c>className</c> 的 UTF8 字节做哈希——与框架属性系统
    /// （<c>AttributeKey::Hash</c>）同一族算法，纯函数、跨机跨会话稳定，无需服务器发号。
    /// </para>
    /// <para><b>约束</b>：id 由类名派生，<b>改类名 = 换 id</b>，已导出的 blob 必须重导；
    /// 不同定义的 id 撞车在生成期检测并报错（见 <c>SceneContentTypeCodeGen</c>）。</para>
    /// </summary>
    public static class SceneContentTypeId
    {
        private const uint OffsetBasis = 2166136261;
        private const uint Prime = 16777619;

        /// <summary>把类型类名折算成内容类型 id（结果落在 int 范围，生成代码里以 const 常量输出）。</summary>
        public static int Compute(string className)
        {
            if (string.IsNullOrEmpty(className))
                return 0;

            var bytes = Encoding.UTF8.GetBytes(className);
            var hash = OffsetBasis;
            for (var i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= Prime;
            }
            return unchecked((int)hash);
        }
    }

    /// <summary>导出侧。场景内容可以按侧导出成两份 blob（客户端 / 服务端）。</summary>
    public enum SceneContentSide
    {
        /// <summary>客户端：完整数据（含可视对象引用）。</summary>
        Client,
        /// <summary>服务端：通常只要逻辑字段，GO 引用默认不导出。</summary>
        Server,
    }
}
