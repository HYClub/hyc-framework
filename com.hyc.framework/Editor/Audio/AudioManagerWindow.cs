// ============================================================
// HYC Framework - 音频管理窗口
// 文件: Editor/Audio/AudioManagerWindow.cs
// 说明: 后端切换 + 音频清单大列表 + 扫描导入 + 导出 Blob。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using HYC.Framework.Audio;
using HYC.Framework.Runtime;

namespace HYC.Framework.AudioEditor
{
    /// <summary>
    /// 音频管理窗口。后端切换（框架默认 / Wwise）、音频清单列表（名字 / 事件 / 启用 / 已导出）、
    /// 扫描导入、导出 Blob 都在这里。总览 Asset 存在配置侧，编辑器里可直接编辑。
    /// </summary>
    public class AudioManagerWindow : EditorWindow
    {
        // 菜单 priority 60：与 Talent（50）同档但独立一档，与 Config(0)/BT(10)/UI(20)/Loc(30)/Attr(40) 不画分隔线。
        [MenuItem("HYC Framework/Audio/Audio Manager", false, 60)]
        public static void Open()
        {
            var w = GetWindow<AudioManagerWindow>("音频管理");
            w.minSize = new Vector2(760, 420);
            w.Show();
        }

        // 导出独立子菜单（priority 61，档内 Δ<11 不画线）
        [MenuItem("HYC Framework/Audio/Export Catalog Blob", false, 61)]
        public static void ExportBlobMenu()
        {
            var w = GetWindow<AudioManagerWindow>("音频管理");
            w.minSize = new Vector2(760, 420);
            w.Show();
            w.ExportBlob();
        }

        private AudioCatalogAsset _catalog;
        private Vector2 _scroll;
        private bool _showWwiseHint;
        private string _wwiseMetaOverride = "";
        private AudioScanResult _lastScan;

        /// <summary>本次 OnGUI 内"是否已导出"的判定结果。每帧算一次，避免每行都读文件。</summary>
        private bool _exportFresh;

        /// <summary>拖拽落区矩形（每帧由 GetControlRect 刷新，供拖拽事件命中判定）。</summary>
        private Rect _dropRect;

        /// <summary>拖拽落区样式（懒初始化）。</summary>
        private GUIStyle _dropStyle;

        /// <summary>待删除的条目（跨帧安全：本帧先画完整行，下一帧再移除，避免 IMGUI 控件数中途变化）。</summary>
        private AudioEntry _pendingRemoveEntry;

        /// <summary>待删除的文件夹节点（连带其下子条目）。</summary>
        private AudioFolderNode _pendingRemoveFolder;

        /// <summary>文件夹展开态的 EditorPrefs 键前缀（按文件夹路径区分）。</summary>
        private const string ExpPref = "HYC.Framework.Audio.Manager.Exp.";

        /// <summary>HYC_WWISE 宏是否定义（决定 Wwise 选项能否选）。音频管理窗口与 HYC Settings 共用。</summary>
        internal static bool WwiseCompiled
        {
            get
            {
#if HYC_WWISE
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>工程里是否装了 Wwise（决定 Wwise 选项要不要点亮）。音频管理窗口与 HYC Settings 共用。</summary>
        internal static bool WwiseInstalled
        {
            get
            {
                if (Directory.Exists("Assets/Wwise/Deployment") || Directory.Exists("Assets/WwiseData/Banks"))
                    return true;
                return AssetDatabase.FindAssets("AkSoundEngine").Length > 0;
            }
        }

        /// <summary>配置视图：0 = 框架默认（列表显示 Addressables 地址列），1 = Wwise（显示事件名列）。
        /// 仅决定列表编辑哪一列，与运行时后端无关。</summary>
        private const string PrefConfigView = "HYC.Framework.Audio.ManagerConfigView";
        private int _configView;

        private void OnEnable()
        {
            _catalog = AudioCatalogAssetLocator.Load();
            _configView = EditorPrefs.GetInt(PrefConfigView, 0);

            // 打开窗口即自愈：把历史误归到根目录的条目对齐回各自物理文件夹
            // （旧版拖文件夹把所有 clip 归到拖入根目录的 bug 遗留数据）。
            // 幂等：数据已正确时不产生任何改动。这样用户不必"再拖一次"才能看到正确分组。
            if (_catalog != null && ResyncSourceFolders())
            {
                EditorUtility.SetDirty(_catalog);
                AssetDatabase.SaveAssetIfDirty(_catalog);
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawConfigSection();

            // 注意：不写成 `if (_catalog == null) { HelpBox(...); return; }`。
            // 提前 return 会让「有/无总览」两种状态走**不同长度**的布局，
            // 状态切换那一帧容易触发 "Invalid GUILayout state"。
            // 这里保持调用链固定，缺总览时只画提示、不画表格。
            if (_catalog == null)
            {
                EditorGUILayout.HelpBox(
                    "还没有音频总览资产。点上方「创建总览」生成一个（存在配置侧，可直接在配置树里编辑）。",
                    MessageType.Info);
            }
            else
            {
                // Wwise 配置视图且工程未装 Wwise 插件 → 按用户要求，整个下半区（导出状态 + 列表）整块不显示。
                if (_configView == 1 && !WwiseInstalled)
                {
                    EditorGUILayout.HelpBox(
                        "未检测到 Wwise 插件，暂不显示事件配置列表。请先在 HYC Settings → 音频后端 选装 Wwise。",
                        MessageType.Info);
                }
                else
                {
                    DrawExportStatus();
                    DrawEntryTable();
                }
            }

            DrawMessages();
        }

        // ---------------- 工具栏 ----------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("创建总览", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                _catalog = AudioCatalogAssetLocator.LoadOrCreate();
                EditorGUIUtility.PingObject(_catalog);
            }

            if (GUILayout.Button("扫描工程音频", EditorStyles.toolbarButton, GUILayout.Width(100)))
                ScanAndImport();

            if (GUILayout.Button("扫描 Wwise 事件", EditorStyles.toolbarButton, GUILayout.Width(110)))
                ScanWwiseOnly();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("定位总览资产", EditorStyles.toolbarButton, GUILayout.Width(100)) && _catalog != null)
                EditorGUIUtility.PingObject(_catalog);

            EditorGUILayout.EndHorizontal();
        }

        // ---------------- 引用配置视图（仅切换列表列，不动运行时后端） ----------------

        private void DrawConfigSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("音频引用配置", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("只切换下方编辑的是哪种引用方式，不改变运行时后端（后端由 HYC Settings → 音频后端 决定）。", EditorStyles.miniLabel);

            // 顶栏 = 配置视图切换：仅决定列表显示「Addressables 地址」列还是「Wwise 事件名」列。
            // ★ 不调 Audio.SetBackend —— 运行期后端是项目级设置，归 HYC Settings 管。
            // 控件数量恒定（始终一个 Toolbar），无 IMGUI 重排问题。
            var pick = GUILayout.Toolbar(
                _configView,
                new[] { "框架默认（Unity、Addressables）", "Wwise" },
                GUILayout.Height(22));
            if (pick != _configView)
            {
                _configView = pick;
                EditorPrefs.SetInt(PrefConfigView, _configView);
                Repaint();
            }

            var installed = WwiseInstalled;
            var compiled = WwiseCompiled;

            // Wwise 视图下的环境提示（与 HYC Settings 的后端选择呼应；此处只是提示，不切后端）。
            if (_configView == 1)
            {
                if (!installed)
                {
                    EditorGUILayout.HelpBox(
                        "Wwise 配置视图：工程未检测到 Wwise Unity Integration（没有 Assets/Wwise/Deployment）。" +
                        "wwise: 引用目前不会真正出声。请先在 HYC Settings → 音频后端 选装 Wwise。",
                        MessageType.Warning);
                }
                else if (!compiled)
                {
                    EditorGUILayout.HelpBox(
                        "Wwise 配置视图：编译宏 HYC_WWISE 未定义，Wwise 后端当前是空壳（调用会报错）。" +
                        "请在 Player Settings → Scripting Define Symbols 添加 HYC_WWISE 后重新编译。",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("Wwise 后端已就绪：wwise: 前缀的事件将由 Wwise 播放。", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.LabelField("框架默认后端：clip: 引用经 Addressables 加载播放。", EditorStyles.miniLabel);
            }

            // 操作按钮区：仅 Wwise 视图下给安装 / 加宏入口（与 HYC Settings 呼应）。始终整块绘制，不增删控件。
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6);
            if (_configView == 1 && !installed)
            {
                if (GUILayout.Button("下载 / 安装 Wwise Unity Integration", GUILayout.Width(280)))
                    Application.OpenURL("https://www.audiokinetic.com/en/download/");
                EditorGUILayout.LabelField("装完用 Wwise Launcher 的 Integrate 导入。", EditorStyles.miniLabel);
            }
            else if (_configView == 1 && !compiled)
            {
                using (new EditorGUI.DisabledScope(compiled))
                {
                    if (GUILayout.Button("打开 Player Settings 添加 HYC_WWISE 宏", GUILayout.Width(240)))
                        SettingsService.OpenProjectSettings("Project/Player");
                }
                EditorGUILayout.LabelField("编译宏未生效。", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("（框架默认后端无需 Wwise 插件）", EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ---------------- 导出状态 ----------------

        /// <summary>
        /// 当前总览内容是否与最近一次导出一致（读导出时刻写入的 hash）。
        /// 每次 OnGUI 调一次并缓存到 _exportFresh，不要在表格每行里调。
        /// </summary>
        private bool ExportStatus()
        {
            var statusPath = BlobDir + "/" + StatusFileName;
            if (!File.Exists(statusPath)) return false;
            try
            {
                var txt = File.ReadAllText(statusPath);
                return txt.Contains("\"hash\"") && CatalogHash() == ExtractHash(txt);
            }
            catch { return false; }
        }

        /// <summary>
        /// Blob 输出目录。放在 StreamingAssets 内才能随包发布，
        /// 与配置管线（ConfigDataSettings.BlobLoadPath）/ BT 管线同级。
        /// </summary>
        private static string BlobDir => "Assets/StreamingAssets/AudioBlob";

        /// <summary>
        /// 导出状态文件名（记录导出时刻的内容 hash，供窗口比对是否过期）。
        /// 用 .txt 与 BT 的 manifest.txt 保持一致 —— .json 放在 Assets/ 下容易被
        /// Unity 的资源导入规则特殊对待。
        /// </summary>
        private const string StatusFileName = "export_status.txt";

        private string CatalogHash()
        {
            // 稳定哈希（自己算，不用 string.GetHashCode）——
            // 那个在不同进程/不同运行时版本下不保证一致，跨会话比对会误判"过期"。
            // 这里只需要判断"变了没"，FNV-1a 足够。
            var sb = new System.Text.StringBuilder();
            if (_catalog != null)
            {
                foreach (var e in _catalog.entries)
                {
                    if (e == null) continue;
                    sb.Append(e.id).Append('|').Append(e.displayName).Append('|')
                      .Append(e.addressableKey).Append('|').Append(e.wwiseEvent).Append('|')
                      .Append(e.enabled ? 1 : 0).Append(';');
                }
            }

            unchecked
            {
                const ulong fnvOffset = 14695981039346656037UL;
                const ulong fnvPrime = 1099511628211UL;
                ulong hash = fnvOffset;
                var s = sb.ToString();
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= fnvPrime;
                }
                return hash.ToString();
            }
        }

        private static string ExtractHash(string json)
        {
            const string key = "\"hash\":\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "";
            i += key.Length;
            int j = json.IndexOf('"', i);
            return j < 0 ? "" : json.Substring(i, j - i);
        }

        private void DrawExportStatus()
        {
            _exportFresh = ExportStatus();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                _exportFresh ? "● 已导出到 Blob（StreamingAssets/AudioBlob）" : "○ 未导出 / 已过期",
                _exportFresh ? EditorStyles.boldLabel : EditorStyles.miniLabel);

            // 注意：这里**不能**写成 `if (!_exportFresh) LabelField(...)`。
            // IMGUI 要求每帧控件数量与类型完全一致（Layout 与 Repaint 两阶段要匹配），
            // 条件性地增减控件会报 "Invalid GUILayout state in XXX view"。
            // 所以提示行永远画，只是内容按状态换。
            EditorGUILayout.LabelField(
                _exportFresh
                    ? "导出内容与当前总览一致。"
                    : "列表里的「已导出」列只反映导出时刻的状态，改完记得重新导出。",
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("导出 Blob", GUILayout.Width(100)))
                ExportBlob();

            EditorGUILayout.EndHorizontal();
        }

        // ---------------- 清单列表 ----------------

        /// <summary>
        /// 生成一个不与现有条目重名的默认显示名（"NewAudio 1" / "NewAudio 2" …）。
        /// 避免多次「添加条目」都叫 "NewAudio" 导致数据配置面板下拉里同名难辨。
        /// </summary>
        private string MakeUniqueAudioName()
        {
            int n = 1;
            while (true)
            {
                var candidate = "NewAudio " + n;
                bool clash = false;
                foreach (var e in _catalog.entries)
                {
                    if (e != null && string.Equals(e.displayName, candidate, StringComparison.Ordinal))
                    {
                        clash = true;
                        break;
                    }
                }
                if (!clash) return candidate;
                n++;
            }
        }

        // ---------------- 清单列表（树形：按物理文件夹分组、可展开） ----------------

        /// <summary>按物理文件夹路径取文件夹节点（实体），没有返回 null。</summary>
        private AudioFolderNode FindFolderNode(string key)
        {
            foreach (var n in _catalog.folders)
                if (n != null && n.folderPath == key) return n;
            return null;
        }

        /// <summary>展开态偏好键（按文件夹路径）。</summary>
        private string ExpKey(string key) => ExpPref + key;

        /// <summary>某文件夹下的子条目数。</summary>
        private int CountChildren(string key)
        {
            int c = 0;
            foreach (var e in _catalog.entries)
                if (e != null && e.sourceFolder == key) c++;
            return c;
        }

        private void DrawEntryTable()
        {
            // 引用列随后端切换：框架默认(Addressables)→拖 AudioClip + 只读 AA 地址；Wwise→事件名。两者不同时出现。
            // 列表本身是**树**：按每个条目的 sourceFolder（物理文件夹）归组，文件夹节点是可展开/可操作的实体。
            bool wwiseMode = _configView == 1;

            // 拖拽落区（始终绘制，控件数恒定）。用 GetControlRect 预留高度，再用 GUI.Box 落在该矩形上。
            _dropRect = EditorGUILayout.GetControlRect(false, 54);
            DrawDropZone(_dropRect);

            DrawTableHeader(wwiseMode);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            bool changed = false;

            // 分组键 = 实体文件夹节点路径 ∪ 条目里出现的 sourceFolder（去重排序）。物理文件夹永远有分组，空目录也保留（实体）。
            var groupKeys = new List<string>();
            foreach (var n in _catalog.folders)
                if (n != null && !string.IsNullOrEmpty(n.folderPath) && !groupKeys.Contains(n.folderPath)) groupKeys.Add(n.folderPath);
            foreach (var e in _catalog.entries)
                if (e != null && !string.IsNullOrEmpty(e.sourceFolder) && !groupKeys.Contains(e.sourceFolder)) groupKeys.Add(e.sourceFolder);
            groupKeys.Sort();

            foreach (var key in groupKeys)
            {
                var node = FindFolderNode(key);
                bool expanded = EditorPrefs.GetBool(ExpKey(key), true);
                DrawFolderRow(key, node, expanded, ref changed);
                if (!expanded) continue;
                foreach (var e in _catalog.entries)
                {
                    if (e == null) continue;
                    if (e.sourceFolder != key) continue;
                    DrawClipRow(e, wwiseMode, ref changed);
                }
            }

            EditorGUILayout.EndScrollView();

            // 底部：新建文件夹 + 添加空条目（控件数恒定）。
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 新建文件夹", GUILayout.Width(110)))
                CreateFolderNode();
            if (GUILayout.Button("＋ 添加空条目", GUILayout.Width(100)))
            {
                _catalog.entries.Add(new AudioEntry { id = _catalog.NextId(), displayName = MakeUniqueAudioName() });
                changed = true;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 跨帧安全删除：本帧仍画完整行，下一帧再移除（避免 IMGUI 控件数中途变化）。
            if (_pendingRemoveEntry != null)
            {
                _catalog.entries.Remove(_pendingRemoveEntry);
                _pendingRemoveEntry = null;
                changed = true;
            }
            if (_pendingRemoveFolder != null)
            {
                string fp = _pendingRemoveFolder.folderPath;
                _catalog.entries.RemoveAll(en => en != null && en.sourceFolder == fp);
                _catalog.folders.Remove(_pendingRemoveFolder);
                _pendingRemoveFolder = null;
                changed = true;
            }

            HandleDragEvents();

            if (changed)
            {
                EditorUtility.SetDirty(_catalog);
                // 立即落盘：下游数据配置面板的 AudioEvent 下拉读的是同一份总览资产实例，
                // 落盘后即使发生域重载（重编译）也不会丢；不落盘则临时填的地址/事件会被丢弃。
                AssetDatabase.SaveAssetIfDirty(_catalog);
                // 「已导出」列立刻反映为过期，不用等下一次刷新。
                Repaint();
            }
        }

        private void DrawTableHeader(bool wwiseMode)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("启用", EditorStyles.miniBoldLabel, GUILayout.Width(34));
            EditorGUILayout.LabelField("音频名字", EditorStyles.miniBoldLabel, GUILayout.Width(180));
            if (wwiseMode)
                EditorGUILayout.LabelField("Wwise 事件名", EditorStyles.miniBoldLabel, GUILayout.Width(180));
            else
            {
                EditorGUILayout.LabelField("AudioClip", EditorStyles.miniBoldLabel, GUILayout.Width(200));
                EditorGUILayout.LabelField("AA 地址（只读）", EditorStyles.miniBoldLabel, GUILayout.Width(200));
            }
            // 「音量组」= 原「分组」列（AudioChannel 音量路由）；与物理文件夹分组是两个维度，改名避免歧义。
            EditorGUILayout.LabelField("音量组", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            EditorGUILayout.LabelField("已导出", EditorStyles.miniBoldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("描述", EditorStyles.miniBoldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDropZone(Rect rect)
        {
            if (_dropStyle == null)
            {
                _dropStyle = new GUIStyle(EditorStyles.helpBox);
                _dropStyle.alignment = TextAnchor.MiddleCenter;
                _dropStyle.fontSize = 12;
                _dropStyle.normal.textColor = new Color(0.65f, 0.78f, 0.92f);
            }
            GUI.Box(rect, "把 AudioClip 文件 或 文件夹 拖到这里\n（可多选；文件夹递归扫描并自动登记 Addressables）", _dropStyle);
        }

        /// <summary>文件夹行：展开箭头 + 名称 + 子条目数 + 整组批量操作。实体节点带全部按钮；隐式分组（扫描来的、尚无节点）只给「登记 AA」升级为实体。</summary>
        private void DrawFolderRow(string key, AudioFolderNode node, bool expanded, ref bool changed)
        {
            bool isEntity = node != null;
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(expanded ? "▾" : "▸", GUILayout.Width(22), GUILayout.Height(18)))
            {
                EditorPrefs.SetBool(ExpKey(key), !expanded);
                Repaint();
            }
            // 与原型一致：文件夹名带尾斜杠 + 文件夹图标（展开/折叠用不同图标）。
            var folderIcon = EditorGUIUtility.IconContent(expanded ? "FolderOpened Icon" : "Folder Icon").image;
            EditorGUILayout.LabelField(
                new GUIContent(Path.GetFileName(key.TrimEnd('/', '\\')) + "/", folderIcon),
                EditorStyles.boldLabel, GUILayout.Width(170));
            EditorGUILayout.LabelField("· " + CountChildren(key) + " clip", EditorStyles.miniLabel, GUILayout.Width(70));
            GUILayout.FlexibleSpace();
            if (isEntity)
            {
                if (GUILayout.Button("全启用", GUILayout.Width(60)))
                {
                    foreach (var en in _catalog.entries)
                        if (en != null && en.sourceFolder == key) en.enabled = true;
                    changed = true;
                }
                if (GUILayout.Button("音量组 ▾", GUILayout.Width(72)))
                {
                    var menu = new GenericMenu();
                    var groups = FrameworkSettings.Instance.audioGroups;
                    if (groups != null)
                    {
                        foreach (var g in groups)
                        {
                            if (g == null) continue;
                            var gid = g.id;
                            var label = string.IsNullOrEmpty(g.displayName) ? "(未命名)" : g.displayName;
                            // 防御：显示名里的半角 / 会被 GenericMenu 当成子菜单分隔符，替成全角。
                            menu.AddItem(new GUIContent((gid + " · " + label).Replace('/', '／')), false,
                                () => SetFolderChannel(key, gid));
                        }
                    }
                    menu.ShowAsContext();
                }
                if (GUILayout.Button("登记 AA", GUILayout.Width(64)))
                    RegisterFolderToAA(key);
                if (GUILayout.Button("移除组", GUILayout.Width(64)))
                    _pendingRemoveFolder = node;
            }
            else
            {
                if (GUILayout.Button("登记 AA", GUILayout.Width(64)))
                {
                    _catalog.EnsureFolder(key);
                    RegisterFolderToAA(key);
                    EditorUtility.SetDirty(_catalog);
                    AssetDatabase.SaveAssetIfDirty(_catalog);
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawClipRow(AudioEntry e, bool wwiseMode, ref bool changed)
        {
            bool rowInvalid = e.enabled
                && string.IsNullOrEmpty(e.addressableKey)
                && string.IsNullOrEmpty(e.wwiseEvent);
            var prevBg = GUI.backgroundColor;
            if (rowInvalid) GUI.backgroundColor = new Color(0.55f, 0.15f, 0.15f);
            EditorGUILayout.BeginHorizontal();
            var newEnabled = EditorGUILayout.Toggle(e.enabled, GUILayout.Width(34));
            if (newEnabled != e.enabled) { e.enabled = newEnabled; changed = true; }

            var newName = EditorGUILayout.TextField(e.displayName ?? "", GUILayout.Width(180));
            if (newName != (e.displayName ?? "")) { e.displayName = newName; changed = true; }

            if (wwiseMode)
            {
                var newWw = EditorGUILayout.TextField(e.wwiseEvent ?? "", GUILayout.Width(180));
                if (newWw != (e.wwiseEvent ?? "")) { e.wwiseEvent = newWw; changed = true; }
            }
            else
            {
                var newClip = (AudioClip)EditorGUILayout.ObjectField(e.unityClip, typeof(AudioClip), false, GUILayout.Width(200));
                if (newClip != e.unityClip)
                {
                    e.unityClip = newClip;
                    if (newClip == null) e.addressableKey = "";
                    changed = true;
                }

                string shownAddr;
                if (e.unityClip != null)
                {
                    var live = HYC.Framework.Config.Editor.GUIDrawer.GetAddressableAddress(e.unityClip) ?? "";
                    if (live != e.addressableKey) { e.addressableKey = live; changed = true; }
                    shownAddr = string.IsNullOrEmpty(live) ? "(未标记 Addressable)" : live;
                }
                else
                {
                    shownAddr = e.addressableKey;
                }
                EditorGUILayout.LabelField(shownAddr ?? "", GUILayout.Width(200));
            }

            var groupNames = GroupPopupNames();
            var curIdx = GroupPopupIndex(e.channel);
            var newCh = EditorGUILayout.Popup(curIdx, groupNames, GUILayout.Width(70));
            if (newCh != curIdx && newCh >= 0 && newCh < groupNames.Length)
            {
                e.channel = FrameworkSettings.Instance.audioGroups[newCh].id;
                changed = true;
            }

            bool hasAddr = !string.IsNullOrEmpty(e.addressableKey) || !string.IsNullOrEmpty(e.wwiseEvent);
            bool rowExported = hasAddr && e.enabled && _exportFresh;
            EditorGUILayout.LabelField(rowExported ? "是" : "否", GUILayout.Width(60));

            var newDesc = EditorGUILayout.TextField(e.description ?? "");
            if (newDesc != (e.description ?? "")) { e.description = newDesc; changed = true; }

            if (GUILayout.Button("×", GUILayout.Width(22)))
            {
                _pendingRemoveEntry = e;
                GUI.backgroundColor = prevBg;
                EditorGUILayout.EndHorizontal();
                return;
            }
            GUI.backgroundColor = prevBg;
            EditorGUILayout.EndHorizontal();
        }

        // ---------------- 拖拽与导入 ----------------

        private bool IsDragOver(Rect rect)
        {
            var e = Event.current;
            return (e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && rect.Contains(e.mousePosition);
        }

        private bool DragHasItems()
        {
            if (DragAndDrop.paths != null)
                foreach (var p in DragAndDrop.paths)
                    if (AssetDatabase.IsValidFolder(p) || AssetDatabase.LoadAssetAtPath<AudioClip>(p) != null) return true;
            if (DragAndDrop.objectReferences != null)
                foreach (var o in DragAndDrop.objectReferences)
                    if (o is AudioClip) return true;
            return false;
        }

        private void HandleDragEvents()
        {
            var e = Event.current;
            if (e == null) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (_catalog == null) return;
            if (!IsDragOver(_dropRect)) return;
            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragHasItems() ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                e.Use();
            }
            else
            {
                DragAndDrop.AcceptDrag();
                HandleDrop(DragAndDrop.paths, DragAndDrop.objectReferences);
                e.Use();
            }
        }

        private void HandleDrop(string[] paths, UnityEngine.Object[] objs)
        {
            var folders = new List<string>();
            var clips = new List<AudioClip>();
            if (paths != null)
                foreach (var p in paths)
                {
                    if (AssetDatabase.IsValidFolder(p)) { if (!folders.Contains(p)) folders.Add(p); }
                    else
                    {
                        var c = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                        if (c != null && !clips.Contains(c)) clips.Add(c);
                    }
                }
            if (objs != null)
                foreach (var o in objs)
                    if (o is AudioClip c && !clips.Contains(c)) clips.Add(c);

            // 先自愈旧数据（把历史误归到根目录的条目对齐回物理目录），再导入本次新拖入的。
            bool changed = ResyncSourceFolders();
            foreach (var f in folders) { ImportFolder(f); changed = true; }
            foreach (var c in clips) { ImportClip(c); changed = true; }
            if (changed)
            {
                EditorUtility.SetDirty(_catalog);
                AssetDatabase.SaveAssetIfDirty(_catalog);
                Repaint();
            }
        }

        /// <summary>
        /// 自愈：把所有条目的 sourceFolder 重新对齐到其 clip 当前的物理所在文件夹，并清理因此空掉的「纯中间层」节点。
        /// 用途：修复早期版本把递归扫来的 clip 全归到拖入根目录（如 bgm/music/sfx 下的 clip 全归到 Assets/Audio）而生成的数据。
        /// 因为列表按 sourceFolder 分组（决策 E：显示的都是物理文件夹），对齐后拖一次文件夹即可让旧数据自动归位。
        /// </summary>
        private bool ResyncSourceFolders()
        {
            bool changed = false;

            // 1) 条目重新归位到 clip 的真实物理目录。
            foreach (var en in _catalog.entries)
            {
                if (en == null || en.unityClip == null) continue;
                var p = AssetDatabase.GetAssetPath(en.unityClip);
                var dir = string.IsNullOrEmpty(p) ? "" : Path.GetDirectoryName(p).Replace('\\', '/');
                if (string.IsNullOrEmpty(dir)) continue;
                if (en.sourceFolder != dir)
                {
                    en.sourceFolder = dir;
                    _catalog.EnsureFolder(dir);
                    changed = true;
                }
            }

            // 2) 清理空掉的中间层节点：无子条目、且是某个现存文件夹节点的祖先路径。
            //    只删「纯中间层」，用户手动建的空文件夹（不是任何节点的祖先）保留（决策 C：实体节点可留空）。
            for (int i = _catalog.folders.Count - 1; i >= 0; i--)
            {
                var n = _catalog.folders[i];
                if (n == null || string.IsNullOrEmpty(n.folderPath)) continue;
                if (CountChildren(n.folderPath) > 0) continue;
                bool isAncestor = false;
                foreach (var m in _catalog.folders)
                {
                    if (m == null || m == n) continue;
                    if (!string.IsNullOrEmpty(m.folderPath) && m.folderPath.StartsWith(n.folderPath + "/", StringComparison.Ordinal))
                    {
                        isAncestor = true;
                        break;
                    }
                }
                if (isAncestor) { _catalog.folders.RemoveAt(i); changed = true; }
            }

            return changed;
        }

        /// <summary>拖文件夹：递归扫 clip，按每个 clip 自身的物理所在文件夹归组（决策 E），并自动登记 Addressables（决策 B）。</summary>
        private void ImportFolder(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath)) return;

            // 整棵子树登记为一个 AA 分组：folder entry 覆盖其下全部子资源，避免根 + 子目录各建条目互相嵌套冲突。
            RegisterFolderToAA(folderPath);

            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folderPath });
            foreach (var g in guids)
            {
                var clipPath = AssetDatabase.GUIDToAssetPath(g);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip == null) continue;
                if (_catalog.entries.Exists(en => en != null && en.unityClip == clip)) continue;

                // ★ 关键：按 clip 自身的物理目录归组，而不是拖入的根目录。
                //   否则递归扫来的 clip 会全挤在一个根节点下（列表就看不到 music/、bgm/、sfx/ 这些分组了）。
                var dir = Path.GetDirectoryName(clipPath);
                dir = string.IsNullOrEmpty(dir) ? "" : dir.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir)) _catalog.EnsureFolder(dir);
                AddClipEntry(clip, dir);
            }
        }

        /// <summary>拖散 clip：自动归到物理所在文件夹（决策 E），并确保该文件夹登记 Addressables（决策 B）。</summary>
        private void ImportClip(AudioClip clip)
        {
            if (clip == null) return;
            if (_catalog.entries.Exists(en => en != null && en.unityClip == clip)) return;
            var path = AssetDatabase.GetAssetPath(clip);
            var folderPath = string.IsNullOrEmpty(path) ? "" : Path.GetDirectoryName(path).Replace('\\', '/');
            if (!string.IsNullOrEmpty(folderPath))
            {
                _catalog.EnsureFolder(folderPath);
                RegisterFolderToAA(folderPath);
            }
            AddClipEntry(clip, folderPath);
        }

        private void AddClipEntry(AudioClip clip, string folderPath)
        {
            _catalog.entries.Add(new AudioEntry
            {
                id = _catalog.NextId(),
                displayName = clip != null ? clip.name : "NewAudio",
                unityClip = clip,
                sourceFolder = folderPath,
                enabled = true,
                // 与「扫描工程音频」保持一致：按资源路径推断音量组初值（BGM/Music→音乐，UI→界面…），用户可在列表里改。
                channel = clip != null ? AudioScanner.InferChannel(clip) : AudioChannelId.Sfx,
            });
        }

        /// <summary>把文件夹登记进 Addressables：建/复用同名分组，把文件夹本身作为 folder entry（子资源自动纳入）。幂等。</summary>
        private void RegisterFolderToAA(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            try
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                {
                    Debug.LogWarning($"[HYC.Audio] Addressables 设置未初始化，跳过文件夹登记: {folderPath}");
                    return;
                }
                string groupName = "Audio_" + folderPath.Replace('/', '_').Replace('\\', '_');
                var group = settings.FindGroup(groupName)
                    ?? settings.CreateGroup(groupName, false, false, true, null, typeof(BundledAssetGroupSchema));
                string guid = AssetDatabase.AssetPathToGUID(folderPath);
                if (!string.IsNullOrEmpty(guid))
                {
                    settings.CreateOrMoveEntry(guid, group);
                    EditorUtility.SetDirty(settings);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HYC.Audio] 自动登记 Addressables 失败（文件夹仍已加入列表）: {folderPath} -> {ex.Message}");
            }
        }

        private void SetFolderChannel(string key, int ch)
        {
            foreach (var e in _catalog.entries)
                if (e != null && e.sourceFolder == key) e.channel = ch;
            EditorUtility.SetDirty(_catalog);
            AssetDatabase.SaveAssetIfDirty(_catalog);
            Repaint();
        }

        /// <summary>取音量分组下拉显示名数组（"id · 显示名"，含 / 防御替换）。无分组时返回空数组。</summary>
        private static string[] GroupPopupNames()
        {
            var groups = FrameworkSettings.Instance.audioGroups;
            if (groups == null) return new string[0];
            var names = new string[groups.Count];
            for (int i = 0; i < names.Length; i++)
            {
                var g = groups[i];
                if (g == null) { names[i] = "(空)"; continue; }
                var label = string.IsNullOrEmpty(g.displayName) ? "(未命名)" : g.displayName;
                // 防御：显示名里的半角 / 会被 Popup 当成子菜单分隔符，替成全角。
                names[i] = (g.id + " · " + label).Replace('/', '／');
            }
            return names;
        }

        /// <summary>分组 id → 在 audioGroups 中的下标；找不到（陈旧数据）回退 0。</summary>
        private static int GroupPopupIndex(int id)
        {
            var groups = FrameworkSettings.Instance.audioGroups;
            if (groups == null) return 0;
            for (int i = 0; i < groups.Count; i++)
                if (groups[i] != null && groups[i].id == id) return i;
            return 0;
        }

        /// <summary>手动新建文件夹节点：选工程内物理文件夹 → 建实体 + 自动登记 AA + 递归导入现有 clip。</summary>
        private void CreateFolderNode()
        {
            string abs = EditorUtility.OpenFolderPanel("选择音频文件夹", "Assets", "");
            if (string.IsNullOrEmpty(abs)) return;
            string dataPath = Application.dataPath.Replace('\\', '/');
            string a = abs.Replace('\\', '/');
            if (!a.StartsWith(dataPath))
            {
                Debug.LogWarning("[HYC.Audio] 请选择工程 Assets 目录下的文件夹。");
                return;
            }
            string rel = "Assets" + a.Substring(dataPath.Length);
            if (!AssetDatabase.IsValidFolder(rel))
            {
                Debug.LogWarning($"[HYC.Audio] 不是有效工程文件夹: {rel}");
                return;
            }
            ImportFolder(rel);
            EditorUtility.SetDirty(_catalog);
            AssetDatabase.SaveAssetIfDirty(_catalog);
            Repaint();
        }

        // ---------------- 消息区 ----------------

        private void DrawMessages()
        {
            if (_lastScan == null) return;

            // 同样不能条件性增删控件：消息条数只在扫描后变一次，
            // 那一帧 Layout 与 Repaint 的控件数必然对不上。
            // 改成固定一行「摘要 + 最近一条」，条数不画。
            var msgCount = _lastScan.Messages != null ? _lastScan.Messages.Count : 0;
            if (msgCount == 0) return;

            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(_lastScan.WwiseMetaPath)
                    ? $"扫描结果（{msgCount} 条提示）"
                    : $"扫描结果（{msgCount} 条提示）｜Wwise 元数据: {_lastScan.WwiseMetaPath}",
                EditorStyles.miniLabel);

            // 只显示最后一条：它是最新加入的，也是最相关的。
            EditorGUILayout.HelpBox(_lastScan.Messages[msgCount - 1], MessageType.Warning);
        }

        // ---------------- 操作 ----------------

        private void ScanAndImport()
        {
            // 仅做扫描导入，不隐式改动 Addressables 配置。

            _lastScan = AudioScanner.Scan(_wwiseMetaOverride);
            int added = AudioScanner.ImportClips(_catalog, _lastScan.Clips);
            EditorUtility.SetDirty(_catalog);

            var wwiseHint = _lastScan.WwiseEvents.Count > 0
                ? $"扫到 {_lastScan.WwiseEvents.Count} 个 Wwise 事件。选中条目后在下方填入即可。"
                : "未扫到 Wwise 事件。";
            _lastScan.Messages.Add($"扫到 {_lastScan.Clips.Count} 个 AudioClip，新增 {added} 条。{wwiseHint}");
        }

        private void ScanWwiseOnly()
        {
            _lastScan = AudioScanner.Scan(_wwiseMetaOverride);
            _lastScan.Messages.Add($"扫到 {_lastScan.WwiseEvents.Count} 个 Wwise 事件。");
        }

        private void ExportBlob()
        {
            if (_catalog == null) return;

            // 导出前校验：启用但地址与事件都空的条目无法进 Blob，弹框提示。
            var invalidNames = new System.Collections.Generic.List<string>();
            int validCount = 0;
            foreach (var e in _catalog.entries)
            {
                if (e == null) continue;
                bool hasRef = !string.IsNullOrEmpty(e.addressableKey) || !string.IsNullOrEmpty(e.wwiseEvent);
                if (e.enabled && !hasRef) invalidNames.Add(string.IsNullOrEmpty(e.displayName) ? "(无名)" : e.displayName);
                if (e.enabled && hasRef) validCount++;
            }
            if (invalidNames.Count > 0)
            {
                var preview = new System.Text.StringBuilder();
                int shown = 0;
                foreach (var n in invalidNames)
                {
                    if (shown >= 5) break;
                    if (preview.Length > 0) preview.Append("、");
                    preview.Append(n);
                    shown++;
                }
                if (invalidNames.Count > 5) preview.Append($" 等 {invalidNames.Count} 条");
                var msg = $"有 {invalidNames.Count} 条已启用的条目缺少 Addressables 地址和 Wwise 事件名，将无法导出：\n{preview}\n\n（这些条目在 Audio Manager 表格里已标红。）";
                bool proceed;
                if (validCount > 0)
                {
                    msg += $"\n\n仍将导出 {validCount} 条有效条目？";
                    proceed = EditorUtility.DisplayDialog("音频总览导出", msg, "仍导出有效条目", "取消");
                }
                else
                {
                    proceed = EditorUtility.DisplayDialog("音频总览导出", msg, "知道了");
                }
                if (!proceed) return;
            }

            try
            {
                if (!Directory.Exists(BlobDir)) Directory.CreateDirectory(BlobDir);

                // 构建 Blob
                var usable = new List<AudioEntry>();
                foreach (var e in _catalog.entries)
                {
                    if (e == null || !e.enabled) continue;
                    if (!string.IsNullOrEmpty(e.addressableKey)) usable.Add(e);
                    else if (!string.IsNullOrEmpty(e.wwiseEvent)) usable.Add(e);
                }

                var blobPath = Path.Combine(BlobDir, AudioCatalogBlobSystem.BlobFileName);

                // 与配置管线 / BT 管线同一套 API: BlobAssetReference<T>.Write(builder, path, version)
                var builder = new BlobBuilder(Allocator.Temp);
                try
                {
                    ref var root = ref builder.ConstructRoot<AudioCatalogBlob>();
                    var arr = builder.Allocate(ref root.Rows, usable.Count);
                    for (int i = 0; i < usable.Count; i++)
                    {
                        var e = usable[i];
                        // clip 地址为空时优先当 wwise 事件（总览里允许只填事件名）。
                        bool wwise = string.IsNullOrEmpty(e.addressableKey) && !string.IsNullOrEmpty(e.wwiseEvent);
                        var target = wwise ? e.wwiseEvent : e.addressableKey;
                        arr[i] = new AudioCatalogRow
                        {
                            Id = e.id,
                            Target = wwise ? 1 : 0,
                            Channel = (int)e.channel,
                            // BlobString 用 AllocateString(ref field, value) —— ref 参数，
                            // 不是返回值形式（与配置管线生成代码同一套 API）。
                            Ref = default,
                        };
                        builder.AllocateString(ref arr[i].Ref, target);
                    }
                    BlobAssetReference<AudioCatalogBlob>.Write(builder, blobPath,
                        AudioCatalogBlobSystem.Version);
                }
                finally
                {
                    builder.Dispose();
                }

                // 写导出状态（含内容 hash），供窗口下次打开比对"是否已过期"
                var hash = CatalogHash();
                File.WriteAllText(Path.Combine(BlobDir, StatusFileName),
                    "{\"hash\":\"" + hash + "\",\"count\":" + usable.Count + "}");

                AssetDatabase.Refresh();
                Debug.Log($"[HYC.Audio] 导出完成: {usable.Count} 条 → {blobPath}");
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HYC.Audio] 导出失败: {ex.Message}");
            }
        }
    }
}
