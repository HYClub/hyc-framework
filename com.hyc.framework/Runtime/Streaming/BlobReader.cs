using Unity.Entities;
using Unity.Entities.Serialization;

namespace HYC.Framework.IO
{
    /// <summary>
    /// 从字节数组内存解析 <see cref="BlobAssetReference{T}"/>。
    ///
    /// <para><b>为什么需要它：</b>跨端加载统一走「<see cref="StreamingAssets.ReadBytes"/> 拿字节 →
    /// 内存解析 Blob」这条路（WebGL / Android 不能直接读文件）。而内存解析必须用
    /// <see cref="MemoryBinaryReader"/> + <c>fixed</c>，即 <b>unsafe 代码</b>——本框架 Runtime 程序集
    /// 已开 <c>allowUnsafeCode</c>，unsafe 全部收在这里。</para>
    ///
    /// <para><b>对调用方的价值：</b>生成的 <c>XxxBlobSystem</c>、<c>BTBlobLoader</c> 等只调本方法，
    /// <b>不需要 unsafe 上下文</b>——宿主工程无需开启 Player Settings 的 "Allow unsafe code"。</para>
    /// </summary>
    public static class BlobReader
    {
        /// <summary>
        /// 把 <paramref name="bytes"/> 解析成 <see cref="BlobAssetReference{T}"/>（调用方负责 <c>Dispose</c>）。
        /// </summary>
        /// <param name="bytes">Blob 文件字节（通常来自 <see cref="StreamingAssets.ReadBytes"/>）。</param>
        /// <param name="version">期望的文件格式版本；不一致返回 false。</param>
        /// <param name="blob">解析结果；失败时为 default。</param>
        /// <typeparam name="T">Blob 根类型（须为 unmanaged，如 <c>BlobRoot&lt;CfgXxx&gt;</c> / <c>BTRootBlob</c>）。</typeparam>
        /// <returns>解析成功 true；字节为空 / 版本不符 / 数据损坏返回 false。</returns>
        /// <remarks>
        /// ⚠️ <b>类型参数必须显式指定</b>（如 <c>TryRead&lt;BTRootBlob&gt;(…)</c>）：<c>T</c> 只出现在
        /// out 参数上，而 C# 类型推断不使用 out 参数，省略类型参数会报 CS0411。
        /// </remarks>
        public static unsafe bool TryRead<T>(byte[] bytes, int version, out BlobAssetReference<T> blob)
            where T : unmanaged
        {
            if (bytes == null || bytes.Length == 0)
            {
                blob = default;
                return false;
            }

            fixed (byte* ptr = bytes)
            {
                using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                return BlobAssetReference<T>.TryRead(reader, version, out blob);
            }
        }
    }
}
