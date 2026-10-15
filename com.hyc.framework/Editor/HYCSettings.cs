using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// HYC 框架的**项目级设置资产**（纯 ScriptableObject，落 <c>Assets/HYC/HYCSettings.asset</c>）。
    ///
    /// <para><b>为什么用 SO 而不是 EditorPrefs</b>：EditorPrefs 只存本机（Windows 注册表 / macOS plist），
    /// 不进版本控制、多人协作不同步、换机即丢；SO 资产随工程提交，全团队共享同一份设定。</para>
    ///
    /// <para><b>两块数据</b>：</para>
    /// <list type="bullet">
    /// <item><b>具名字段</b>：配置数据编辑器全项（<see cref="HYC.Framework.Config.Editor.ConfigDataSettings"/> 委托到此读写）
    /// + HYC Settings 窗口的音频后端选择。</item>
    /// <item><b>键值条目</b>（<see cref="HasEntry"/> / <see cref="GetEntry"/> / <see cref="SetEntry"/>）：
    /// 供 <see cref="HYCSettingsRegistry"/> 持久化 <c>[HYCSettingsGroup]</c> 字段
    /// （bool / int / float / string / 枚举统一转字符串存放，写入与解析严格配对）。</item>
    /// </list>
    ///
    /// <para>所有写操作（属性 setter 与 <see cref="SetEntry"/>）都**立即落盘**
    /// （<c>EditorUtility.SetDirty</c> + <c>AssetDatabase.SaveAssetIfDirty</c>）——
    /// 设置是低频操作，不需要「延迟到鼠标松开」那套（那只对拖动滑块有必要）。</para>
    /// </summary>
    public sealed class HYCSettings : ScriptableObject
    {
        /// <summary>约定路径。找不到时回退全工程搜索（用户把资产移到别处也能用）。</summary>
        public const string DefaultPath = "Assets/HYC/HYCSettings.asset";

        // ---------------------------------------------------------------- 默认值（唯一真源）

        private const string DefaultIdServerIp = "127.0.0.1";
        private const int DefaultIdServerPort = 8920;
        private const string DefaultRootFolder = "Assets/ConfigData";
        private const string DefaultOutputDir = "Assets/GeneratedConfigs";
        private const string DefaultEditorDir = "Assets/GeneratedConfigs/Editor";
        private const string DefaultClientExportDir = "Exports/Client";
        private const string DefaultServerExportDir = "Exports/Server";
        private const string DefaultBlobOutputDir = "Assets/StreamingAssets/ConfigBlob";

        /// <summary>生成代码命名空间 root 的最终回落值（设置与公司名都不可用时）。</summary>
        private const string FallbackNamespaceRoot = "Default";

        // ---------------------------------------------------------------- 配置数据编辑器（ConfigDataSettings 委托到此）

        /// <summary>ID 构造器类型：0=本地，1=局域网。</summary>
        [SerializeField] private int _idProviderType;

        /// <summary>局域网 ID 服务器 IP（空值回落 127.0.0.1）。</summary>
        [SerializeField] private string _idServerIp = DefaultIdServerIp;

        /// <summary>局域网 ID 服务器端口（1~65535，越界钳制）。</summary>
        [SerializeField] private int _idServerPort = DefaultIdServerPort;

        /// <summary>客户端导出格式：0=JSON, 1=Blob。</summary>
        [SerializeField] private int _clientFormat;

        /// <summary>服务器导出格式：0=JSON。</summary>
        [SerializeField] private int _serverFormat;

        /// <summary>
        /// Blob 文件输出目录（编辑器导出位置）。必须位于 Assets/StreamingAssets 内——
        /// 运行期只能从 StreamingAssets 读（PC / iOS 直接读文件，WebGL / Android 走预取缓存）。
        /// </summary>
        [SerializeField] private string _blobOutputDir = DefaultBlobOutputDir;

        /// <summary>自定义配置编辑器（XxxConfigEditor.cs）的生成目录。</summary>
        [SerializeField] private string _editorDir = DefaultEditorDir;

        /// <summary>客户端导出目录（绝对路径或项目相对路径）。</summary>
        [SerializeField] private string _clientExportDir = DefaultClientExportDir;

        /// <summary>服务器导出目录（绝对路径或项目相对路径）。</summary>
        [SerializeField] private string _serverExportDir = DefaultServerExportDir;

        /// <summary>客户端/服务器导出类型：true=struct（字段平铺），false=class（保持继承链）。</summary>
        [SerializeField] private bool _sideTypeIsStruct;

        /// <summary>配置数据树扫描的根目录（工程相对，必须在 Assets 内）。</summary>
        [SerializeField] private string _rootFolder = DefaultRootFolder;

        /// <summary>生成的配置类输出目录。</summary>
        [SerializeField] private string _outputDir = DefaultOutputDir;

        /// <summary>
        /// 生成代码命名空间 root（可含点号，如 <c>com.example.game</c>）。
        /// 留空 = 自动取项目公司名（<c>PlayerSettings.companyName</c>），公司名不可用时回落 <c>Default</c>。
        /// 配置代码落 <c>{root}.config</c>，BT 自定义节点落 <c>{root}.BT.Nodes</c>。
        /// </summary>
        [SerializeField] private string _generatedNamespaceRoot = "";

        // ---------------------------------------------------------------- HYC Settings 窗口项

        /// <summary>项目级音频后端：0=框架默认（Unity/Addressables），1=Wwise。</summary>
        [SerializeField] private int _audioBackend;

        // ---------------------------------------------------------------- [HYCSettingsGroup] 字段条目（键值平行表）

        [SerializeField] private List<string> _entryKeys = new List<string>();
        [SerializeField] private List<string> _entryValues = new List<string>();

        // ---------------------------------------------------------------- 实例

        private static HYCSettings _instance;

        /// <summary>工程内唯一实例；资产不存在则就地创建（落 <see cref="DefaultPath"/>）。</summary>
        public static HYCSettings Instance
        {
            get
            {
                if (_instance == null || _instance.Equals(null))
                    _instance = LoadOrCreate();
                return _instance;
            }
        }

        /// <summary>
        /// 加载设置资产：约定路径 → 全工程搜索（用户可能把资产移到别处）→ 都没有则新建并落盘。
        /// </summary>
        public static HYCSettings LoadOrCreate()
        {
            var asset = AssetDatabase.LoadAssetAtPath<HYCSettings>(DefaultPath);
            if (asset == null)
            {
                var guids = AssetDatabase.FindAssets("t:HYCSettings");
                if (guids.Length > 0)
                    asset = AssetDatabase.LoadAssetAtPath<HYCSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (asset != null)
                return asset;

            EnsureFolder(Path.GetDirectoryName(DefaultPath));
            asset = CreateInstance<HYCSettings>();
            AssetDatabase.CreateAsset(asset, DefaultPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>标脏并落盘。所有写操作都经过它，杜绝「改了没保存、域重载后还原」。</summary>
        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
        }

        // ---------------------------------------------------------------- 配置数据编辑器项

        /// <summary>ID 构造器类型：0=本地，1=局域网。</summary>
        public int IdProviderType
        {
            get => _idProviderType;
            set { _idProviderType = value; Save(); }
        }

        /// <summary>局域网 ID 服务器 IP（空值回落 127.0.0.1）。</summary>
        public string IdServerIp
        {
            get => string.IsNullOrEmpty(_idServerIp) ? DefaultIdServerIp : _idServerIp;
            set { _idServerIp = value; Save(); }
        }

        /// <summary>局域网 ID 服务器端口（1~65535，越界钳制）。</summary>
        public int IdServerPort
        {
            get => _idServerPort < 1 || _idServerPort > 65535 ? DefaultIdServerPort : _idServerPort;
            set { _idServerPort = value < 1 ? 1 : value > 65535 ? 65535 : value; Save(); }
        }

        /// <summary>客户端导出格式：0=JSON, 1=Blob。</summary>
        public int ClientFormat
        {
            get => _clientFormat;
            set { _clientFormat = value; Save(); }
        }

        /// <summary>服务器导出格式：0=JSON。</summary>
        public int ServerFormat
        {
            get => _serverFormat;
            set { _serverFormat = value; Save(); }
        }

        /// <summary>Blob 文件输出目录（空值回落默认路径）。</summary>
        public string BlobOutputDir
        {
            get => string.IsNullOrEmpty(_blobOutputDir) ? DefaultBlobOutputDir : _blobOutputDir;
            set { _blobOutputDir = value; Save(); }
        }

        /// <summary>自定义配置编辑器（XxxConfigEditor.cs）的生成目录（空值回落默认路径）。</summary>
        public string EditorDir
        {
            get => string.IsNullOrEmpty(_editorDir) ? DefaultEditorDir : _editorDir;
            set { _editorDir = value; Save(); }
        }

        /// <summary>客户端导出目录（空值回落默认路径）。</summary>
        public string ClientExportDir
        {
            get => string.IsNullOrEmpty(_clientExportDir) ? DefaultClientExportDir : _clientExportDir;
            set { _clientExportDir = value; Save(); }
        }

        /// <summary>服务器导出目录（空值回落默认路径）。</summary>
        public string ServerExportDir
        {
            get => string.IsNullOrEmpty(_serverExportDir) ? DefaultServerExportDir : _serverExportDir;
            set { _serverExportDir = value; Save(); }
        }

        /// <summary>客户端/服务器导出类型：true=struct（字段平铺），false=class（保持继承链）。</summary>
        public bool SideTypeIsStruct
        {
            get => _sideTypeIsStruct;
            set { _sideTypeIsStruct = value; Save(); }
        }

        /// <summary>配置数据树扫描的根目录（空值回落默认路径）。</summary>
        public string RootFolder
        {
            get => string.IsNullOrEmpty(_rootFolder) ? DefaultRootFolder : _rootFolder;
            set { _rootFolder = value; Save(); }
        }

        /// <summary>生成的配置类输出目录（空值回落默认路径）。</summary>
        public string OutputDir
        {
            get => string.IsNullOrEmpty(_outputDir) ? DefaultOutputDir : _outputDir;
            set { _outputDir = value; Save(); }
        }

        // ---------------------------------------------------------------- 生成代码命名空间（root 派生，配置/BT 共用）

        /// <summary>生成的配置类命名空间：由「生成代码命名空间 root」派生（<c>{root}.config</c>），不再单独配置。</summary>
        public static string ConfigCodeNamespace => EffectiveGeneratedNamespaceRoot + ".config";

        /// <summary>
        /// 生成的枚举命名空间：由同一 root 派生（<c>{root}.Enum</c>），与配置类命名空间（<c>{root}.config</c>）平级。
        /// <para>平级而非子级是有意的——配置资产过滤器用 <c>type.Namespace.StartsWith(ConfigCodeNamespace)</c>
        /// 识别配置类（见 <c>ConfigDataTree</c>/<c>ConfigExportService</c>/<c>ConfigIdService</c>），若枚举落在
        /// <c>{root}.config.Enum</c> 会被前缀匹配误当成配置类去导出/收集，故枚举独立成 <c>{root}.Enum</c>。</para>
        /// </summary>
        public static string ConfigEnumNamespace => EffectiveGeneratedNamespaceRoot + ".Enum";

        /// <summary>BT 自定义节点代码的命名空间：由同一 root 派生（<c>{root}.BT.Nodes</c>）。</summary>
        public static string BTNodesCodeNamespace => EffectiveGeneratedNamespaceRoot + ".BT.Nodes";

        /// <summary>生成代码命名空间 root 的**原值**（可能是空串=自动模式）。</summary>
        public string GeneratedNamespaceRoot
        {
            get => _generatedNamespaceRoot ?? string.Empty;
            set { _generatedNamespaceRoot = value; Save(); }
        }

        /// <summary>
        /// 生效的命名空间 root：设置值 → 项目公司名（<c>PlayerSettings.companyName</c>）→ <c>Default</c>。
        /// 净化规则：按点号分段，逐段只保留字母/数字/下划线；数字开头的段整段丢弃；
        /// 全部丢干净时回落 <c>Default</c>。
        /// </summary>
        public static string EffectiveGeneratedNamespaceRoot
        {
            get
            {
                var raw = Instance.GeneratedNamespaceRoot;
                if (string.IsNullOrWhiteSpace(raw))
                    raw = PlayerSettings.companyName;
                return SanitizeNamespaceRoot(raw);
            }
        }

        /// <summary>把任意字符串净化为合法点分命名空间 root；不可用时回落 <see cref="FallbackNamespaceRoot"/>。</summary>
        public static string SanitizeNamespaceRoot(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return FallbackNamespaceRoot;

            var parts = raw.Split('.');
            var sb = new StringBuilder();
            var first = true;
            foreach (var part in parts)
            {
                var seg = CleanSegment(part);
                if (seg.Length == 0)
                    continue;
                if (!first)
                    sb.Append('.');
                sb.Append(seg);
                first = false;
            }

            var result = sb.ToString();
            return result.Length == 0 ? FallbackNamespaceRoot : result;
        }

        /// <summary>净化单个点分段：剔非法字符；空段或数字开头返回空串（调用方丢弃该段）。</summary>
        private static string CleanSegment(string segment)
        {
            var sb = new StringBuilder(segment.Length);
            foreach (var c in segment)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
            }
            var s = sb.ToString();
            if (s.Length == 0 || char.IsDigit(s[0]))
                return string.Empty;
            return s;
        }

        // ---------------------------------------------------------------- HYC Settings 窗口项

        /// <summary>项目级音频后端：0=框架默认（Unity/Addressables），1=Wwise。</summary>
        public int AudioBackend
        {
            get => _audioBackend;
            set { _audioBackend = value; Save(); }
        }

        // ---------------------------------------------------------------- [HYCSettingsGroup] 字段条目

        /// <summary>条目键是否已存过值（<c>Persist=true</c> 的字段才有条目）。</summary>
        public bool HasEntry(string key)
        {
            if (string.IsNullOrEmpty(key) || _entryKeys == null)
                return false;
            return _entryKeys.Contains(key);
        }

        /// <summary>取条目值；键不存在返回 null（调用方保留代码里的默认值）。</summary>
        public string GetEntry(string key)
        {
            if (string.IsNullOrEmpty(key) || _entryKeys == null)
                return null;
            var i = _entryKeys.IndexOf(key);
            if (i < 0 || _entryValues == null || i >= _entryValues.Count)
                return null;
            return _entryValues[i];
        }

        /// <summary>写条目（同键覆盖、新键追加）并立即落盘。value 为 null 存空串。</summary>
        public void SetEntry(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (_entryKeys == null) _entryKeys = new List<string>();
            if (_entryValues == null) _entryValues = new List<string>();

            var i = _entryKeys.IndexOf(key);
            if (i >= 0)
            {
                if (i < _entryValues.Count)
                    _entryValues[i] = value ?? string.Empty;
                else
                    _entryValues.Add(value ?? string.Empty);
            }
            else
            {
                _entryKeys.Add(key);
                _entryValues.Add(value ?? string.Empty);
            }
            Save();
        }

        // ---------------------------------------------------------------- 内部工具

        /// <summary>逐级创建工程相对目录（已存在则跳过；<c>Assets</c> 本身已存在）。</summary>
        private static void EnsureFolder(string projectRelative)
        {
            var normalized = (projectRelative ?? string.Empty).Replace('\\', '/').Trim('/');
            if (normalized.Length == 0)
                return;

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
        }
    }
}
