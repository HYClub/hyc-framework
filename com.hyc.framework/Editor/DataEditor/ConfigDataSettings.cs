using System;
using System.IO;
using HYC.Framework.Editor;
using UnityEditor;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Project-level configuration for the HYC data editor: the asset root
    /// folder scanned by <see cref="ConfigDataTree"/> and a local ID generator
    /// (no server round-trip, unlike a server-backed client).
    ///
    /// <para>值统一存于 <see cref="HYCSettings"/> 资产（<c>Assets/HYC/HYCSettings.asset</c>）：
    /// 随工程进版本控制，多人协作共享同一份设定。本类只做委托 + 助手方法，
    /// 存储语义（默认值 / 钳制 / 空值回落）见 <see cref="HYCSettings"/> 各属性。</para>
    /// </summary>
    public static class ConfigDataSettings
    {
        /// <summary>StreamingAssets 的工程相对根目录。运行期唯一可读位置。</summary>
        public const string StreamingAssetsRoot = "Assets/StreamingAssets";

        private static readonly string DefaultBlobLoadPath = "ConfigBlob";

        /// <summary>ID 构造器类型：0=本地，1=局域网。</summary>
        public static int IdProviderType
        {
            get => HYCSettings.Instance.IdProviderType;
            set => HYCSettings.Instance.IdProviderType = value;
        }

        /// <summary>局域网 ID 服务器 IP。</summary>
        public static string IdServerIp
        {
            get => HYCSettings.Instance.IdServerIp;
            set => HYCSettings.Instance.IdServerIp = value;
        }

        /// <summary>局域网 ID 服务器端口。</summary>
        public static int IdServerPort
        {
            get => HYCSettings.Instance.IdServerPort;
            set => HYCSettings.Instance.IdServerPort = value;
        }

        /// <summary>客户端导出格式：0=JSON, 1=Blob。</summary>
        public static int ClientFormat
        {
            get => HYCSettings.Instance.ClientFormat;
            set => HYCSettings.Instance.ClientFormat = value;
        }

        /// <summary>服务器导出格式：0=JSON。</summary>
        public static int ServerFormat
        {
            get => HYCSettings.Instance.ServerFormat;
            set => HYCSettings.Instance.ServerFormat = value;
        }

        /// <summary>
        /// Blob 文件输出目录（编辑器导出位置）。<b>这是唯一的真值</b>：
        /// 必须位于 <see cref="StreamingAssetsRoot"/> 内——运行期只能从 StreamingAssets 读
        /// （PC / iOS 直接读文件，WebGL / Android 走预取缓存），导出前会校验这一条。
        /// </summary>
        public static string BlobOutputDir
        {
            get => HYCSettings.Instance.BlobOutputDir;
            set => HYCSettings.Instance.BlobOutputDir = value;
        }

        /// <summary>
        /// Blob 文件运行时加载子路径（相对 StreamingAssets 根）。
        /// <para><b>由 <see cref="BlobOutputDir"/> 派生</b>——「导出落点」与「运行期读取点」
        /// 因此永远是同一处，从结构上消除两边各配一个路径、改一边忘另一边的坑。
        /// 要改加载路径，请改 <see cref="BlobOutputDir"/>（设置窗口的「Blob 输出目录」）。</para>
        /// </summary>
        public static string BlobLoadPath => ToStreamingAssetsRelative(BlobOutputDir);

        /// <summary>
        /// <see cref="BlobOutputDir"/> 是否位于 <see cref="StreamingAssetsRoot"/> 内。
        /// Blob 格式导出前必须为 true，否则文件打不进包 / 运行期读不到。
        /// </summary>
        public static bool IsBlobOutputDirUnderStreamingAssets => IsUnderStreamingAssets(BlobOutputDir);

        /// <summary>判断工程相对目录是否位于 <see cref="StreamingAssetsRoot"/> 内。</summary>
        public static bool IsUnderStreamingAssets(string projectRelativeDir)
            => NormalizeDir(projectRelativeDir).StartsWith(StreamingAssetsRoot + "/", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeDir(string dir) => (dir ?? string.Empty).Replace('\\', '/').Trim('/');

        /// <summary>把工程相对目录转成相对 StreamingAssets 根的路径；不在其下则回退默认加载路径。</summary>
        public static string ToStreamingAssetsRelative(string dir)
        {
            var prefix = StreamingAssetsRoot + "/";
            var d = NormalizeDir(dir);
            if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var rel = d.Substring(prefix.Length);
                if (rel.Length > 0) return rel;
            }
            return DefaultBlobLoadPath;
        }

        /// <summary>自定义配置编辑器（XxxConfigEditor.cs）的生成目录。</summary>
        public static string EditorDir
        {
            get => HYCSettings.Instance.EditorDir;
            set => HYCSettings.Instance.EditorDir = value;
        }

        /// <summary>客户端导出目录（绝对路径或项目相对路径）。</summary>
        public static string ClientExportDir
        {
            get => HYCSettings.Instance.ClientExportDir;
            set => HYCSettings.Instance.ClientExportDir = value;
        }

        /// <summary>服务器导出目录（绝对路径或项目相对路径）。</summary>
        public static string ServerExportDir
        {
            get => HYCSettings.Instance.ServerExportDir;
            set => HYCSettings.Instance.ServerExportDir = value;
        }

        /// <summary>客户端/服务器导出类型：true = struct（字段平铺），false = class（保持继承链）。</summary>
        public static bool SideTypeIsStruct
        {
            get => HYCSettings.Instance.SideTypeIsStruct;
            set => HYCSettings.Instance.SideTypeIsStruct = value;
        }

        /// <summary>
        /// 导出后是否总是汇报检查结果：true = 零问题也弹窗确认，有问题开明细窗；
        /// false（默认）= 仅在存在检查项时弹窗，导出干净则静默通过。
        /// 在 HYC Settings 窗口的「配置」分组里勾选；批模式（CI）不受影响。
        /// </summary>
        public static bool AlwaysShowCheckWindow
        {
            get => HYCSettings.Instance.AlwaysShowCheckWindow;
            set => HYCSettings.Instance.AlwaysShowCheckWindow = value;
        }

        /// <summary>Root folder (project-relative, e.g. Assets/ConfigData) scanned by the data editor tree.</summary>
        public static string RootFolder
        {
            get => HYCSettings.Instance.RootFolder;
            set => HYCSettings.Instance.RootFolder = value;
        }

        /// <summary>Output folder for generated config classes.</summary>
        public static string OutputDir
        {
            get => HYCSettings.Instance.OutputDir;
            set => HYCSettings.Instance.OutputDir = value;
        }

        /// <summary>
        /// 生成配置类的命名空间：由「生成代码命名空间 root」派生（<c>{root}.config</c>），不再单独配置。
        /// root 在 HYC Settings 窗口的 CodeGen 分组（或 <see cref="HYCSettings"/> 资产）里配；
        /// 留空 = 项目公司名，公司名不可用回落 <c>Default</c>。
        /// 改 root 后必须重新生成配置代码——资产过滤器与导出类查找都按这个命名空间走。
        /// </summary>
        public static string Namespace => HYCSettings.ConfigCodeNamespace;

        /// <summary>生成的枚举命名空间（<c>{root}.Enum</c>），与 <see cref="Namespace"/>（<c>{root}.config</c>）平级。</summary>
        public static string EnumNamespace => HYCSettings.ConfigEnumNamespace;

        /// <summary>Creates the root folder (and missing parents) on disk, then refreshes the asset database.</summary>
        public static void EnsureRootFolder()
        {
            var folder = RootFolder;
            if (AssetDatabase.IsValidFolder(folder))
                return;

            var normalized = folder.Replace('\\', '/').Trim('/');
            var parts = normalized.Split('/');
            var current = parts[0];
            if (!AssetDatabase.IsValidFolder(current))
                AssetDatabase.CreateFolder("Assets", parts[0]);

            for (var i = 1; i < parts.Length; i++)
            {
                var parent = current;
                current = parent + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(current))
                    AssetDatabase.CreateFolder(parent, parts[i]);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>Returns a locally-unique positive ID (timestamp seed + per-session counter).</summary>
        public static long NextId()
        {
            _counter++;
            return unchecked(_seed + _counter);
        }

        private static readonly long _seed = Math.Abs(DateTime.UtcNow.Ticks);
        private static long _counter;

        /// <summary>Absolute OS path for the root folder, or null when it does not exist yet.</summary>
        public static string GetAbsoluteRootFolder()
        {
            EnsureRootFolder();
            var folder = RootFolder;
            if (!AssetDatabase.IsValidFolder(folder))
                return null;
            return Path.Combine(Directory.GetCurrentDirectory(), folder.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
