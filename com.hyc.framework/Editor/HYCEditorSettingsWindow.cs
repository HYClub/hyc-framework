using System.Collections.Generic;
using HYC.Framework.Config;
using HYC.Framework.Config.Editor;
using HYC.Framework.Audio;
using HYC.Framework.AudioEditor;
using HYC.Framework.UI;
using UnityEditor;
using UnityEngine;
using HYC.Framework.Runtime;

// 别名：静态类 Audio 与命名空间 HYC.Framework.Audio 同名。
// 本文件在 HYC.Framework.Editor 里，且 HYC.Framework 下就有 Audio 命名空间，
// 裸写 `Audio.SetBackend` 会被解析成**命名空间**（CS0234），故用别名指代静态门面。
using AudioFacade = HYC.Framework.Audio.Audio;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// Project &amp; user settings editor：Unity Preferences 式的**左栏分组 / 右栏内容**两栏布局。
    ///
    /// <para><b>分组是配置式的</b>：本窗口自己不写死任何分组清单，所有条目由
    /// <see cref="HYCSettingsGroupAttribute"/> 声明、<see cref="HYCSettingsRegistry"/> 扫描汇总。
    /// 加一项设置 = 在字段或无参 void 方法上加一行特性：
    /// <c>[HYCSettingsGroup("General")]</c>、<c>[HYCSettingsGroup("Audio/Backend")]</c>；
    /// 删/改同理，窗口代码一行不用动。使用方工程（含静态成员）也能往这里挂自己的设置。</para>
    ///
    /// <para>值持久化统一落 <see cref="HYCSettings"/> 资产（<c>Assets/HYC/HYCSettings.asset</c>，
    /// 随工程进版本控制，多人协作共享）；字段条目由注册表自动存取，复杂区块（如下面的 ID 服务器）
    /// 仍由方法自己画、自己存。</para>
    /// </summary>
    public sealed class HYCEditorSettingsWindow : EditorWindow
    {
        // priority=200：进入"项目级低频"档。上一档 Tools 是 100，
        // 差值 100 ≥ 11 ⇒ Unity 自动在 Tools 与本项之间画分隔线。
        [MenuItem("HYC Framework/Settings/Project Settings", false, 200)]
        public static void Open() => GetWindow<HYCEditorSettingsWindow>("HYC Settings");

        private const float LeftPaneWidth = 200f;
        private const float RowHeight = 18f;

        private const string PrefVerbose = "HYC.Framework.Editor.VerboseLogs";
        private const string PrefAutoGen = "HYC.Framework.Editor.AutoGenerate";
        // 项目级音频后端：0 = 框架默认（Unity/Addressables），1 = Wwise。
        // 走自定义绘制（Popup + 状态提示），故不挂特性自动出控件，而是在 DrawAudioBackendSection 里手画；
        // 值本身存 HYCSettings 资产（AssetBackend 字段），与 FrameworkSettings 各司其职：
        // 前者是编辑器会话内的项目级选择，后者是出包生效的运行时设定（Bootstrap 读它）。

        // ---- 字段条目：加一行特性就进左栏对应分组，改动自动写 HYCSettings 资产 ----
        // Key 沿用历史键名（键值条目表的键，稳定不变），避免升级把用户已改过的值冲掉。
        [HYCSettingsGroup("General", Label = "Verbose editor logs", Key = PrefVerbose)]
        private bool _verbose = true;

        [HYCSettingsGroup("General", Label = "Auto-generate on save", Key = PrefAutoGen)]
        private bool _autoGen = true;

        private int _audioBackend;

        // ID 服务器（与数据编辑器设置窗口写同一份 ConfigDataSettings / HYCSettings 资产，两处等价）
        private int _idProviderType;
        private string _idServerIp;
        private int _idServerPort;
        private double _lastProbe;

        [SerializeField] private string _selectedPath;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        private void OnEnable()
        {
            minSize = new Vector2(560f, 320f);

            _audioBackend = HYCSettings.Instance.AudioBackend;

            _idProviderType = ConfigDataSettings.IdProviderType;
            _idServerIp = ConfigDataSettings.IdServerIp;
            _idServerPort = ConfigDataSettings.IdServerPort;

            // 登记自身 → 注册表扫描本窗口带特性的实例字段/方法，并回填已持久化的字段值。
            HYCSettingsRegistry.RegisterHost(this);

            // 状态心跳：仅局域网模式有意义，本地模式 Provider.Ping 恒真不必探。
            // 不加 -= 保护：OnEnable 可能被域重载重复调用，而 update 钩子挂在静态外的
            // 实例方法上，反注册一次避免重复订阅（同 ConfigDataWindow.OnEnable 的做法）。
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            HYCSettingsRegistry.UnregisterHost(this);
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (ConfigIdService.State == ConfigIdState.Connected)
                return; // 已连接不必再探
            if (EditorApplication.timeSinceStartup - _lastProbe < 5.0)
                return;
            _lastProbe = EditorApplication.timeSinceStartup;
            ConfigIdService.ProbeAsync(this);
        }

        // ---------------------------------------------------------------- 两栏布局

        private void OnGUI()
        {
            var root = HYCSettingsRegistry.Root;
            var first = HYCSettingsRegistry.FirstNode();
            if (first == null)
            {
                EditorGUILayout.HelpBox(
                    "没有任何设置分组：在字段或无参 void 方法上加 [HYCSettingsGroup(\"分组名\")] " +
                    "（嵌套用 /，如 \"Audio/Backend\"），它就会出现在左栏。",
                    MessageType.Info);
                return;
            }

            if (string.IsNullOrEmpty(_selectedPath))
                _selectedPath = first.Path;
            var page = HYCSettingsRegistry.Find(_selectedPath) ?? first;

            EditorGUILayout.BeginHorizontal();

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll,
                GUILayout.Width(LeftPaneWidth), GUILayout.ExpandHeight(true));
            DrawTree(root, 0);
            EditorGUILayout.EndScrollView();

            DrawVerticalSeparator();

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            DrawPage(page, 0);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTree(HYCSettingsRegistry.Node node, int depth)
        {
            foreach (var child in node.Children)
            {
                DrawRow(child, depth);
                DrawTree(child, depth + 1);
            }
        }

        private void DrawRow(HYCSettingsRegistry.Node node, int depth)
        {
            // 用仓库内已普遍使用的 (hasLabel, height) 重载：返回整行宽度的控制矩形
            var rowRect = EditorGUILayout.GetControlRect(false, RowHeight);
            var selected = node.Path == _selectedPath;

            // Unity Preferences 原生观感：选中行整行纯色高亮，不额外加竖条/边框。
            if (Event.current.type == EventType.Repaint && selected)
                EditorGUI.DrawRect(rowRect, SelectionColor);

            var indent = 10f + depth * 12f;
            var labelRect = new Rect(rowRect.x + indent, rowRect.y,
                Mathf.Max(0f, rowRect.width - indent), rowRect.height);
            EditorGUI.LabelField(labelRect, node.Name, selected ? SelectedLabelStyle : NormalLabelStyle);

            // 透明按钮吃点击事件：比手写 Event.current 判定更稳（不用管鼠标按键与 Used）
            if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
            {
                _selectedPath = node.Path;
                Repaint();
            }
        }

        /// <summary>
        /// 右栏：本组自己的条目在前，子分组作为子标题依次展开（于是点父分组也能看到全貌）。
        /// </summary>
        private void DrawPage(HYCSettingsRegistry.Node node, int depth)
        {
            if (depth > 0)
                EditorGUILayout.Space();
            EditorGUILayout.LabelField(node.Name, EditorStyles.boldLabel);

            foreach (var item in node.Items)
            {
                var draw = item.Draw;
                if (draw != null)
                    draw();
            }

            foreach (var child in node.Children)
                DrawPage(child, depth + 1);
        }

        // ---------------------------------------------------------------- 样式（懒创建）

        private static readonly Color SelectionColorPro = new Color(0.243f, 0.373f, 0.588f, 1f);
        private static readonly Color SelectionColorPersonal = new Color(0.243f, 0.490f, 0.855f, 1f);

        private static Color SelectionColor =>
            EditorGUIUtility.isProSkin ? SelectionColorPro : SelectionColorPersonal;

        private static GUIStyle _normalLabelStyle;
        private static GUIStyle _selectedLabelStyle;
        private static GUIStyle _separatorStyle;

        private static GUIStyle NormalLabelStyle
        {
            get
            {
                if (_normalLabelStyle == null)
                    _normalLabelStyle = new GUIStyle(EditorStyles.label);
                return _normalLabelStyle;
            }
        }

        private static GUIStyle SelectedLabelStyle
        {
            get
            {
                if (_selectedLabelStyle == null)
                {
                    _selectedLabelStyle = new GUIStyle(EditorStyles.label);
                    // 选中行底色是实蓝，标签必须转成浅色才读得清
                    _selectedLabelStyle.normal.textColor =
                        EditorGUIUtility.isProSkin ? new Color(0.93f, 0.93f, 0.93f, 1f) : Color.white;
                }
                return _selectedLabelStyle;
            }
        }

        private static GUIStyle SeparatorStyle
        {
            get
            {
                if (_separatorStyle == null)
                {
                    _separatorStyle = new GUIStyle();
                    _separatorStyle.normal.background = Texture2D.whiteTexture;
                    _separatorStyle.margin = new RectOffset(0, 0, 0, 0);
                    _separatorStyle.padding = new RectOffset(0, 0, 0, 0);
                }
                return _separatorStyle;
            }
        }

        private static void DrawVerticalSeparator()
        {
            var prev = GUI.color;
            GUI.color = EditorGUIUtility.isProSkin
                ? new Color(0.11f, 0.11f, 0.11f, 1f)
                : new Color(0.62f, 0.62f, 0.62f, 1f);
            GUILayout.Box(GUIContent.none, SeparatorStyle, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            GUI.color = prev;
        }

        // ---------------------------------------------------------------- 分组内容（方法条目）

        /// <summary>
        /// ID 服务器区：状态 + 类型/IP/端口/检测/启动。
        /// 与「数据编辑器设置」窗口操作同一份 <see cref="ConfigDataSettings"/>（HYCSettings 资产），
        /// 两处任一改动即时生效（Provider 在类型变化时重建）。
        /// </summary>
        [HYCSettingsGroup("ID Server")]
        private void DrawIdServerSection()
        {
            // ---- 状态行：圆点 + 文字 + 悬停 tips ----
            EditorGUILayout.BeginHorizontal();
            var dotRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
            // 垂直居中：GetRect 给的是整行高，12px 圆点需下移半行才视觉对齐文字
            dotRect.y += (dotRect.height - 12) * 0.5f;
            dotRect.width = 12;
            dotRect.height = 12;
            GUI.Label(dotRect, new GUIContent(ConfigIdService.GetStatusTexture(), ConfigIdService.StatusTooltip));

            var state = ConfigIdService.State;
            var stateMsg = state == ConfigIdState.Connected
                ? $"已连接（{ConfigIdService.ServerInfo}）"
                : state == ConfigIdState.Connecting
                    ? $"连接中（{ConfigIdService.ServerInfo}）"
                    : $"不可用（{ConfigIdService.ServerInfo}）——本地模式始终可用；局域网模式需先启动 ID 服务器";
            EditorGUILayout.LabelField(stateMsg, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            // ---- 类型 ----
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("类型", GUILayout.Width(120));
            var newType = EditorGUILayout.Popup(_idProviderType, new[] { "本地", "局域网" });
            if (newType != _idProviderType)
            {
                _idProviderType = newType;
                ConfigDataSettings.IdProviderType = newType;
                ConfigIdService.ResetProvider();
            }
            if (GUILayout.Button("检测", GUILayout.Width(60)))
            {
                var ok = _idProviderType == 1
                    ? new NetworkConfigIdProvider(_idServerIp, _idServerPort).Ping()
                    : true;
                EditorUtility.DisplayDialog("检测结果", ok ? "连接正常" : "连接失败", "确定");
            }
            EditorGUILayout.EndHorizontal();

            if (_idProviderType == 1)
            {
                // ---- IP / 端口（改动即时写入，不需点保存）----
                EditorGUI.BeginChangeCheck();
                var newIp = EditorGUILayout.TextField("服务器 IP", _idServerIp);
                var newPort = EditorGUILayout.IntField("端口", _idServerPort);
                if (EditorGUI.EndChangeCheck())
                {
                    if (newPort < 1)
                        newPort = 1;
                    if (newPort > 65535)
                        newPort = 65535;
                    _idServerIp = newIp;
                    _idServerPort = newPort;
                    ConfigDataSettings.IdServerIp = newIp;
                    ConfigDataSettings.IdServerPort = newPort;
                    // IP/端口变了，旧的 Provider 连接已失效，必须重建
                    ConfigIdService.ResetProvider();
                }

                if (ConfigIdService.HasServerLauncherPlugin)
                {
                    if (GUILayout.Button("在 Unity 内启动 ID 服务器"))
                        ConfigIdService.LaunchServerPlugin();
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "未检测到 ID 服务器启动插件（需独立的 ID 服务器 Unity 插件包）。" +
                        "可先在外部启动服务器，再点「检测」验证连通性。",
                        MessageType.Info);
                }
            }
        }

        /// <summary>
        /// 生成代码命名空间 root（所有生成代码共用：配置 / BT 自定义节点 / UI 层 / 属性 / 天赋）。留空 = 取项目公司名，公司名不可用回落 Default。
        /// 各模块命名空间由 root 派生（配置 <c>{root}.config</c>、枚举 <c>{root}.Enum</c>、BT 自定义节点 <c>{root}.BT.Nodes</c>、
        /// UI 层 <c>{root}.ui</c>、属性 <c>{root}.Attr</c>、天赋 <c>{root}.Talent</c>）；
        /// 生成代码目录统一收口在 <c>Assets/HYC/Generated/</c> 下按模块分文件夹（见 <see cref="GeneratedPaths"/>）。
        /// 值存 <see cref="HYCSettings"/> 资产，即时落盘。
        /// </summary>
        [HYCSettingsGroup("CodeGen")]
        private void DrawGeneratedNamespaceRootSection()
        {
            EditorGUI.BeginChangeCheck();
            var root = EditorGUILayout.TextField("命名空间 root", HYCSettings.Instance.GeneratedNamespaceRoot);
            if (EditorGUI.EndChangeCheck())
                HYCSettings.Instance.GeneratedNamespaceRoot = root.Trim();

            // 预览恒定绘制（只换文案）——条件增删控件会破坏 Layout/Repaint 一致性
            var effective = HYCSettings.EffectiveGeneratedNamespaceRoot;
            EditorGUILayout.LabelField($"生效 root：{effective}", EditorStyles.miniLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("全部生成产物的命名空间（宿主引用须与此一致）：", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· 配置主类：{effective}.config", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· 枚举：{effective}.Enum", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· 客户端侧类型：{effective}.config.Client", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· 服务器侧类型：{effective}.config.Server", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· 编辑器工具：{effective}.config.Editor", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· Blob 结构：{effective}.config.Blob", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· BT 自定义节点：{effective}.BT.Nodes", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· UI 层常量：{effective}.ui", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· 属性访问器：{effective}.Attr", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"· 天赋代码：{effective}.Talent", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(
                "留空 = 自动取项目公司名（Project Settings → Company Name）；公司名不可用回落 Default。" +
                "root 可含点号（如 com.example.game）。改动后需重新生成配置代码 / BT 节点代码 / UI 层 / 属性 / 天赋代码。\n" +
                "上面列出的命名空间是所有生成产物的真实落点；宿主手写代码若仍引用旧命名空间（如 *.Generated）会编译失败，须一并改回。\n" +
                "生成代码目录统一收口在 Assets/HYC/Generated/ 下（Config / UI / Attr / Talent / BT 分子目录）；" +
                "存量工程的旧目录（Assets/GeneratedConfigs 等）可用菜单 HYC Framework/Config/Migrate Generated Code 一键搬迁。",
                MessageType.Info);
        }

        /// <summary>
        /// 音频后端（项目级）。下拉选择；选 Wwise 时用 Unity 惯用提示框，按"未装插件 / 未加宏 / 已就绪"三态给出提示。
        /// 选择即时持久化到 HYCSettings 资产，并调用 <c>Audio.SetBackend</c> 应用（编辑器会话内生效）。
        /// </summary>
        [HYCSettingsGroup("Audio")]
        private void DrawAudioBackendSection()
        {
            EditorGUI.BeginChangeCheck();
            // ⚠️ 选项里**不能带 `/`**：EditorGUILayout.Popup 底层是 GenericMenu，`/` 会被当成
            // 子菜单路径分隔符（"框架默认（Unity/Addressables）" 会裂成 "框架默认（Unity" → "Addressables）"）。
            // 用中文顿号代替。
            var newBackend = EditorGUILayout.Popup(
                "后端", _audioBackend,
                new[] { "框架默认（Unity、Addressables）", "Wwise" });
            if (EditorGUI.EndChangeCheck())
            {
                _audioBackend = newBackend;
                HYCSettings.Instance.AudioBackend = newBackend;
                // 真正切换后端门面（编辑器会话内生效；运行时由播放器 bootstrap 决定）。
                AudioFacade.SetBackend(newBackend == 1 ? AudioBackend.Wwise : AudioBackend.Default);
                // 项目级持久化（出包生效）：后端选择同样是项目级设定，落 FrameworkSettings 资产。
                FrameworkSettings.Instance.audioBackend = newBackend == 1 ? AudioBackend.Wwise : AudioBackend.Default;
                EnsureAudioSettingsAsset();
                SaveAudioSettingsAsset();
            }

            // 注意：本段在两种形态下都只画 1 个控件（Default=LabelField / Wwise=HelpBox），
            // 控件数量恒定 —— 避免 Layout 与 Repaint 两阶段不一致（Invalid GUILayout state）。
            if (_audioBackend == 0)
            {
                EditorGUILayout.LabelField("clip: 引用经 Addressables 加载播放。", EditorStyles.miniLabel);
                return;
            }

            if (!AudioManagerWindow.WwiseInstalled)
            {
                // 选了 Wwise 但工程没装插件 —— 红色提示框（MessageType.Error）。
                EditorGUILayout.HelpBox(
                    "工程未检测到 Wwise Unity Integration（没有 Assets/Wwise/Deployment）。" +
                    "wwise: 引用目前不会真正出声。请先安装 Wwise 插件，再用 Wwise Launcher 的 Integrate 导入。",
                    MessageType.Error);
            }
            else if (!AudioManagerWindow.WwiseCompiled)
            {
                EditorGUILayout.HelpBox(
                    "Wwise 插件已就绪，但编译宏 HYC_WWISE 未定义：Wwise 后端当前是空壳（调用会报错）。" +
                    "请在 Player Settings → Scripting Define Symbols 添加 HYC_WWISE 后重新编译。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Wwise 后端已就绪：wwise: 前缀的事件将由 Wwise 播放。", MessageType.Info);
            }
        }

        // ---- 音频音量 / 通道推断：项目级设定，落 FrameworkSettings 资产，出包由 Bootstrap 应用 ----

        /// <summary>确保 FrameworkSettings 已落盘为资产（缺则建于 Assets/Resources），否则出包读不到项目级设定。</summary>
        private static void EnsureAudioSettingsAsset()
        {
            var inst = FrameworkSettings.Instance;
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(inst))) return; // 已是资产
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateAsset(inst, "Assets/Resources/FrameworkSettings.asset");
        }

        /// <summary>把 FrameworkSettings 资产标脏并落盘（仅当已落盘才写，避免凭空造资产）。</summary>
        private static void SaveAudioSettingsAsset()
        {
            var inst = FrameworkSettings.Instance;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(inst))) return;
            EditorUtility.SetDirty(inst);
            AssetDatabase.SaveAssetIfDirty(inst);
        }

        /// <summary>
        /// 拖动滑块 / 编辑输入框时不要每帧落盘（<c>AssetDatabase.SaveAssetIfDirty</c> 会明显卡顿），
        /// 只在鼠标松开或按键抬起的那一刻写一次；拖动过程仅实时预览（内存）+ 标脏。
        /// </summary>
        private static void FlushAudioSettingsOnMouseUp()
        {
            var t = Event.current.type;
            if (t == EventType.MouseUp || t == EventType.KeyUp)
                SaveAudioSettingsAsset();
        }

        /// <summary>
        /// 音量分组（项目级，出包生效）。按 <see cref="FrameworkSettings.audioGroups"/> 动态列出每组默认音量滑块 + 全局静音。
        /// 分组集合本身在 Audio/Groups 配置。改动实时应用到音频门面（编辑器预览），并写入 FrameworkSettings 资产（出包由 Bootstrap 应用）。
        /// </summary>
        [HYCSettingsGroup("Audio/Volumes")]
        private void DrawAudioVolumeSection()
        {
            var s = FrameworkSettings.Instance;
            if (s.audioGroups == null) s.audioGroups = new List<AudioGroupDef>();

            EditorGUI.BeginChangeCheck();
            foreach (var g in s.audioGroups)
            {
                var label = g.id == 0 ? "主音量" : (string.IsNullOrEmpty(g.displayName) ? "(未命名)" : g.displayName);
                // 内部存 0~1 浮点（运行时增益语义），界面用 0~100 整数百分比：拖动更跟手、也符合直觉。
                var pct = Mathf.RoundToInt(Mathf.Clamp01(g.defaultVolume) * 100f);
                pct = EditorGUILayout.IntSlider(label, pct, 0, 100);
                g.defaultVolume = pct / 100f;
            }
            s.audioMuted = EditorGUILayout.Toggle("全局静音", s.audioMuted);
            if (EditorGUI.EndChangeCheck())
            {
                // 实时预览（编辑器会话内立即生效，纯内存不落盘）。g.id 隐式转 AudioChannelId，0 = Master。
                foreach (var g in s.audioGroups)
                    AudioFacade.SetVolume(g.id, g.defaultVolume);
                AudioFacade.SetMuted(s.audioMuted);
                EnsureAudioSettingsAsset();
                EditorUtility.SetDirty(s); // 只标脏；落盘推迟到鼠标松开，避免拖动时每帧写磁盘卡顿
            }
            FlushAudioSettingsOnMouseUp();
        }

        /// <summary>
        /// 通道推断规则（项目级）。扫描音频时按资源路径/名称里包含的关键词自动归类到分组。
        /// 靠上的规则先匹配；空关键词忽略；无命中回退「音效」。分组下拉来自 audioGroups，改名/增删后自动同步。写 FrameworkSettings 资产。
        /// </summary>
        [HYCSettingsGroup("Audio/Inference")]
        private void DrawAudioInferenceSection()
        {
            var s = FrameworkSettings.Instance;
            if (s.audioChannelRules == null) s.audioChannelRules = new List<FrameworkSettings.AudioChannelRule>();

            EditorGUILayout.HelpBox(
                "扫描音频时按资源路径/名称里包含的关键词自动归类到分组。靠上的规则先匹配；留空关键词忽略；无命中回退「音效」。分组下拉来自当前音量分组表。",
                MessageType.Info);

            var groupNames = AudioGroupNames();
            // 倒序遍历，方便在循环内安全移除
            for (int i = s.audioChannelRules.Count - 1; i >= 0; i--)
            {
                var rule = s.audioChannelRules[i];
                if (rule == null) { s.audioChannelRules.RemoveAt(i); continue; }
                EditorGUILayout.BeginHorizontal();
                rule.keyword = EditorGUILayout.TextField("关键词", rule.keyword);
                var idx = GroupIndexById(rule.channel);
                var newIdx = EditorGUILayout.Popup(idx, groupNames);
                if (newIdx != idx && newIdx >= 0 && newIdx < groupNames.Length)
                    rule.channel = FrameworkSettings.Instance.audioGroups[newIdx].id;
                if (GUILayout.Button("×", GUILayout.Width(22)))
                    s.audioChannelRules.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ 添加规则"))
                s.audioChannelRules.Add(new FrameworkSettings.AudioChannelRule { keyword = "", channel = AudioChannelId.Sfx });

            if (GUI.changed)
            {
                EnsureAudioSettingsAsset();
                EditorUtility.SetDirty(s);
            }
            FlushAudioSettingsOnMouseUp();
        }

        /// <summary>
        /// 音量分组表（数据驱动）。增/删/改名都在这里，改动即时写 FrameworkSettings 资产（出包由 Bootstrap 应用）。
        /// id 0 = Master 固定为全局总音量，锁定不可删（删除会破坏增益计算）；其余分组可自由增删改名。
        /// 改名后，扫描/推断/总览里选该组的下拉会同步更新（它们都读 audioGroups）。
        /// </summary>
        [HYCSettingsGroup("Audio/Groups")]
        private void DrawAudioGroupsSection()
        {
            var s = FrameworkSettings.Instance;
            if (s.audioGroups == null) s.audioGroups = new List<AudioGroupDef>();

            EditorGUILayout.HelpBox(
                "音量分组在此增删改名。id 0 = 主音量（Master）锁定不可删；其余分组可自由调整。" +
                "分组集合与每组默认音量一起决定出包后的初始音量。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            for (int i = s.audioGroups.Count - 1; i >= 0; i--)
            {
                var g = s.audioGroups[i];
                if (g == null) { s.audioGroups.RemoveAt(i); continue; }
                var isMaster = g.id == 0;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("id " + g.id, GUILayout.Width(48));
                g.displayName = EditorGUILayout.TextField(g.displayName);
                // 与 Volumes 页一致：0~100 整数百分比（内部仍存 0~1）。
                var pct = Mathf.RoundToInt(Mathf.Clamp01(g.defaultVolume) * 100f);
                pct = EditorGUILayout.IntField(pct, GUILayout.Width(56));
                g.defaultVolume = Mathf.Clamp01(pct / 100f);
                using (new EditorGUI.DisabledScope(isMaster))
                {
                    if (GUILayout.Button("×", GUILayout.Width(22)) && !isMaster)
                    {
                        s.audioGroups.RemoveAt(i);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ 添加分组"))
            {
                var nextId = 1;
                for (int i = 0; i < s.audioGroups.Count; i++)
                    if (s.audioGroups[i] != null && s.audioGroups[i].id >= nextId) nextId = s.audioGroups[i].id + 1;
                s.audioGroups.Add(new AudioGroupDef { id = nextId, displayName = "新分组", defaultVolume = 1f });
            }

            if (EditorGUI.EndChangeCheck())
            {
                // 分组结构变了：把当前默认音量重新应用到门面（路由看 id 不看名字）。
                foreach (var g in s.audioGroups)
                    AudioFacade.SetVolume(g.id, g.defaultVolume);
                EnsureAudioSettingsAsset();
                EditorUtility.SetDirty(s);
            }
            FlushAudioSettingsOnMouseUp();
        }

        /// <summary>取音量分组下拉显示名数组（"id · 显示名"），无分组时返回空数组。</summary>
        private static string[] AudioGroupNames()
        {
            var groups = FrameworkSettings.Instance.audioGroups;
            if (groups == null) return new string[0];
            var names = new string[groups.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = groups[i].id + " · " + (string.IsNullOrEmpty(groups[i].displayName) ? "(未命名)" : groups[i].displayName);
            return names;
        }

        /// <summary>分组 id → 在 audioGroups 中的下标；找不到（陈旧数据）回退 0。</summary>
        private static int GroupIndexById(int id)
        {
            var groups = FrameworkSettings.Instance.audioGroups;
            if (groups == null) return 0;
            for (int i = 0; i < groups.Count; i++)
                if (groups[i] != null && groups[i].id == id) return i;
            return 0;
        }

        /// <summary>配置校验：对当前所有配置资产跑一遍校验器，结果进 Console。</summary>
        [HYCSettingsGroup("Validation")]
        private void DrawValidators()
        {
            if (GUILayout.Button("Run Config Validators"))
                ConfigValidator.ValidateAll();
        }

        /// <summary>
        /// 配置数据检查与导出行为（项目级，落 <see cref="HYCSettings"/> 资产）。
        /// <para><b>导出后总是汇报检查结果</b>：默认关闭 = 导出干净时静默通过（导出频繁时不打扰）；
        /// 开启后每次导出都给出明确反馈——零问题弹「检查完成：未发现问题」，有检查项开明细窗。
        /// 导出中止语义不受影响：始终只有 Error 级中止，Info／Warning 仅展示。</para>
        /// <para>批模式（CI）不受此开关影响：EditorWindow 不渲染，永远只走 Console。</para>
        /// </summary>
        [HYCSettingsGroup("配置")]
        private void DrawConfigCheckSection()
        {
            EditorGUI.BeginChangeCheck();
            var always = EditorGUILayout.Toggle(
                new GUIContent("导出后总是汇报检查结果",
                    "开启后每次导出都弹窗反馈（零问题弹「检查完成：未发现问题」）；" +
                    "关闭则仅在存在提示／警告／错误时弹窗，导出干净时静默通过。批模式（CI）不受影响。"),
                ConfigDataSettings.AlwaysShowCheckWindow);
            if (EditorGUI.EndChangeCheck())
                ConfigDataSettings.AlwaysShowCheckWindow = always;

            // 恒定绘制：只按状态换文案，不增删控件（IMGUI 每帧控件集合必须一致）。
            EditorGUILayout.LabelField(
                ConfigDataSettings.AlwaysShowCheckWindow
                    ? "当前：每次导出都弹窗——零问题弹确认框，有检查项开明细窗。"
                    : "当前：仅在存在检查项时弹窗；导出干净时静默通过（可随时点工具栏「检查」看概览）。",
                EditorStyles.miniLabel);
        }

        // ---- UI 层级：项目级设定，落 FrameworkSettings 资产（出包生效）----

        private const float LayerHandleWidth = 36f;
        private const float LayerNameWidth = 124f;
        private const float LayerFlagWidth = 46f;
        private const float LayerHideFlagWidth = 62f;
        private const float LayerDeleteWidth = 22f;

        /// <summary>正在拖动排序的行下标（-1 = 没在拖）。</summary>
        private int _dragLayer = -1;

        /// <summary>拖动时接管的控件 id（松开时归还）。</summary>
        private int _dragControl;

        /// <summary>本帧各行矩形（自上而下、与层表下标同序），循环结束后才对拖动结算。</summary>
        private readonly List<Rect> _layerRowRects = new List<Rect>();

        /// <summary>上一次「保存并生成」的结果提示。</summary>
        private string _layerGenStatus = "";

        /// <summary>
        /// UI 层表（项目级，出包生效）。<b>行序 = 层表下标序</b>：第 0 行 HUD 显示在最上方、
        /// 实际位于最底层，自上而下正好是从底到顶（决定 Canvas 的 sortingOrder 与 Hierarchy 里的
        /// 兄弟顺序）；行号即生成常量 <c>UILayer.xxx</c> 的值。拖动左侧手柄排序，
        /// 行内改层名与中文描述；三列开关对应 <see cref="UILayerDef"/> 的行为标记。
        ///
        /// <para>群体行为（HUD 自动隐藏 / 挡输入 / 全隐藏）读标记、不读名字，改名与插层自动跟随；
        /// 框架里少数按默认层名查找的 API（HasWindow / ExistOtherWindow* / OpenHud 等角色方法）
        /// 在层名被改后会失效并在初始化时报错——这时用「保存并生成」产出的常量与方法。</para>
        /// </summary>
        [HYCSettingsGroup("UI/层级")]
        private void DrawUILayersSection()
        {
            var s = FrameworkSettings.Instance;
            if (s.uiLayers == null)
                s.uiLayers = UILayerTable.DefaultTable();
            var defs = s.uiLayers;

            EditorGUILayout.HelpBox(
                "自上而下 = 从底到顶（决定 sortingOrder 与 Hierarchy 顺序），行号即生成常量 UILayer.xxx 的值。" +
                "「保存并生成」产出 Assets/GeneratedUI/UILayer.g.cs（具名常量 + 每层一个 Open 方法）。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();

            // ---- 表头：列宽与行内控件严格一致 ----
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("#", GUILayout.Width(LayerHandleWidth));
            GUILayout.Label("层名", GUILayout.Width(LayerNameWidth));
            GUILayout.Label("中文描述");
            GUILayout.Label("HUD", GUILayout.Width(LayerFlagWidth));
            GUILayout.Label("挡输入", GUILayout.Width(LayerFlagWidth));
            GUILayout.Label("全隐藏", GUILayout.Width(LayerHideFlagWidth));
            GUILayout.Label(GUIContent.none, GUILayout.Width(LayerDeleteWidth));
            EditorGUILayout.EndHorizontal();

            // ---- 行（正序 = 层表下标序；删除记下标、循环结束后统一执行，行内不位移）----
            _layerRowRects.Clear();
            var removeAt = -1;
            for (var i = 0; i < defs.Count; i++)
            {
                if (defs[i] == null)
                    defs[i] = new UILayerDef();
                var def = defs[i];

                EditorGUILayout.BeginHorizontal();

                // 固定尺寸手柄：Box(GUIStyle.none) 占位 + GetLastRect 取精确矩形
                // （GetRect 系列实测不钉死尺寸，同窗口圆点先例见 DrawIdServerSection）
                GUILayout.Box(GUIContent.none, GUIStyle.none, GUILayout.Width(LayerHandleWidth), GUILayout.Height(18f));
                var handleRect = GUILayoutUtility.GetLastRect();
                GUI.Label(handleRect, i + " ≡",
                    _dragLayer == i ? EditorStyles.boldLabel : EditorStyles.centeredGreyMiniLabel);
                var controlId = GUIUtility.GetControlID(FocusType.Passive, handleRect);
                CaptureLayerDragStart(i, handleRect, controlId);

                def.name = EditorGUILayout.TextField(def.name, GUILayout.Width(LayerNameWidth));
                def.desc = EditorGUILayout.TextField(def.desc);
                def.hud = GUILayout.Toggle(def.hud, new GUIContent("", "HUD 组：有窗口类层开着时自动隐藏本层"), GUILayout.Width(LayerFlagWidth));
                def.blockInput = GUILayout.Toggle(def.blockInput, new GUIContent("", "挡输入：本层窗口会挡住世界空间输入"), GUILayout.Width(LayerFlagWidth));
                def.hideOnHideAll = GUILayout.Toggle(def.hideOnHideAll, new GUIContent("", "全隐藏：HideAllUI 时一起隐藏（不勾 = 常驻，如 Loading）"), GUILayout.Width(LayerHideFlagWidth));
                var remove = GUILayout.Button("×", GUILayout.Width(LayerDeleteWidth));

                EditorGUILayout.EndHorizontal();

                var rowRect = GUILayoutUtility.GetLastRect();
                _layerRowRects.Add(rowRect);
                if (_dragLayer == i)
                    DrawLayerRowTint(rowRect);

                if (remove)
                    removeAt = i;
            }

            if (removeAt >= 0)
            {
                defs.RemoveAt(removeAt);
                if (_dragLayer == removeAt)
                    ReleaseLayerDrag();
                else if (_dragLayer > removeAt)
                    _dragLayer--;
            }

            // 行内编辑（改名 / 描述 / 开关）与上面的删除都算一次改动：只标脏，
            // 落盘推迟到鼠标松开 / 按键抬起（见 FlushAudioSettingsOnMouseUp）。
            var inlineChanged = EditorGUI.EndChangeCheck();

            // ---- 新增 / 生成 / 恢复默认（结构性改动，立即落盘）----
            if (GUILayout.Button("+ 新增层"))
            {
                defs.Add(new UILayerDef
                {
                    name = NextLayerName(defs),
                    desc = "",
                    hideOnHideAll = true,
                });
                CommitSettingsAsset();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存并生成"))
            {
                // 先落盘再生成：行内改名只标脏、靠鼠标松开才写盘，而本按钮会吃掉这次 MouseUp，
                // 不先保存就可能出现「生成用的是新层名、资产里还是旧层名」的分叉。
                CommitSettingsAsset();
                if (UILayerCodeGen.Generate(out var genError))
                    _layerGenStatus = $"已生成 {UILayerCodeGen.OutputPath}";
                else
                    _layerGenStatus = genError;
            }
            if (GUILayout.Button("恢复默认层表"))
            {
                defs.Clear();
                foreach (var d in UILayerTable.DefaultTable())
                    defs.Add(new UILayerDef { name = d.name, desc = d.desc, hud = d.hud, blockInput = d.blockInput, hideOnHideAll = d.hideOnHideAll });
                CommitSettingsAsset();
                _layerGenStatus = "已恢复默认 10 层（尚未生成代码）";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(_layerGenStatus, EditorStyles.miniLabel);

            if (inlineChanged)
            {
                EnsureAudioSettingsAsset();
                EditorUtility.SetDirty(s);
            }
            FlushAudioSettingsOnMouseUp();

            FinishLayerDrag(defs);
        }

        /// <summary>把 FrameworkSettings 资产落盘（缺资产先建于 Assets/Resources）。音频 / UI 两处项目级设定共用。</summary>
        private static void CommitSettingsAsset()
        {
            EnsureAudioSettingsAsset();
            SaveAudioSettingsAsset();
        }

        /// <summary>在手柄上按下即接管拖动（热控件转到手柄，行内输入框不受影响）。</summary>
        private void CaptureLayerDragStart(int index, Rect handleRect, int controlId)
        {
            if (_dragLayer >= 0)
                return;
            var e = Event.current;
            if (e == null || e.type != EventType.MouseDown || e.button != 0)
                return;
            if (!handleRect.Contains(e.mousePosition))
                return;

            GUIUtility.hotControl = controlId;
            _dragControl = controlId;
            _dragLayer = index;
            e.Use();
            Repaint();
        }

        /// <summary>结束拖动：只在热控件确实是本行手柄时归还，避免误清别的控件的 hotControl。</summary>
        private void ReleaseLayerDrag()
        {
            if (GUIUtility.hotControl == _dragControl)
                GUIUtility.hotControl = 0;
            _dragLayer = -1;
        }

        /// <summary>
        /// 拖动结算。必须在行循环结束后调用——那时 <see cref="_layerRowRects"/> 才包含全部行，
        /// 才能判断松手位置对应哪一行。整个拖动过程只改热控件与高亮，不增删控件（Layout / Repaint 集合恒定）。
        /// </summary>
        private void FinishLayerDrag(List<UILayerDef> defs)
        {
            if (_dragLayer < 0)
                return;
            var e = Event.current;
            if (e == null)
                return;

            if (e.type != EventType.MouseUp)
            {
                if (e.type == EventType.MouseDrag)
                    Repaint();
                return;
            }

            var from = _dragLayer;
            var to = LayerDropIndex(e.mousePosition.y, defs.Count);
            ReleaseLayerDrag();
            e.Use();

            if (to >= 0 && to != from)
            {
                var item = defs[from];
                defs.RemoveAt(from);
                // to 可能等于原行数（末行下方追加）；移除后夹到合法插入位
                defs.Insert(Mathf.Min(to, defs.Count), item);
                CommitSettingsAsset();
            }

            Repaint();
        }

        /// <summary>
        /// 鼠标 Y 对应的插入下标：行内 / 行上方空隙 → 该行下标；首行上方 → 0；末行下方 → 行数（追加）。
        /// 与 <see cref="_layerRowRects"/> 同序（自上而下 = 下标 0→Count-1）。
        /// </summary>
        private int LayerDropIndex(float y, int count)
        {
            for (var i = 0; i < _layerRowRects.Count && i < count; i++)
            {
                if (y <= _layerRowRects[i].yMax)
                    return i;
            }
            return count;
        }

        /// <summary>拖动行高亮（只改颜色，不改控件集合）。</summary>
        private static void DrawLayerRowTint(Rect rowRect)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            var old = GUI.color;
            GUI.color = new Color(0.31f, 0.79f, 1f, 0.10f);
            EditorGUI.DrawRect(rowRect, new Color(0.31f, 0.79f, 1f, 0.10f));
            GUI.color = old;
        }

        /// <summary>给新层挑个不重名的名字：NewLayer / NewLayer2 / NewLayer3 …</summary>
        private static string NextLayerName(List<UILayerDef> defs)
        {
            const string baseName = "NewLayer";
            if (defs.Find(d => d != null && d.name == baseName) == null)
                return baseName;
            for (var n = 2; n < 1000; n++)
            {
                var candidate = baseName + n;
                if (defs.Find(d => d != null && d.name == candidate) == null)
                    return candidate;
            }
            return baseName + System.Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
