using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 多语言 key 选择。有两条入口，按需要挑：
    /// <list type="bullet">
    /// <item><see cref="OpenDropdown"/> —— <b>Unity 原生风格</b>，基于 <see cref="AdvancedDropdown"/>：
    /// 自带搜索框、层级折叠、键盘导航，外观与 Unity 自己的 "Add Component" 面板一致。**推荐**。</item>
    /// <item><see cref="OpenFoldMenu"/> —— 旧的 GenericMenu 折叠菜单（按 "/" 分层）+ 搜索子窗口。
    /// 配置数据编辑器仍在使用，保留以免影响既有行为。</item>
    /// </list>
    /// 数据来自 <see cref="LocAccess"/>（反射 loc 包）。
    /// </summary>
    public class LocKeyPickerWindow : EditorWindow
    {
        /// <summary>
        /// Unity 原生风格的多语言 key 下拉。数据为空时直接返回（调用方应先判断
        /// <see cref="LocAccess.GetKeys"/> 是否为空并把控件置灰，别让用户点开一个空面板）。
        /// </summary>
        /// <param name="anchor">按钮所在的 <b>GUI 坐标</b> 矩形（内部自己转屏幕坐标，别预先转）。</param>
        /// <param name="onPick">选中回调；用户选第一项「（无）」时回传空字符串。</param>
        public static void OpenDropdown(Rect anchor, Action<string> onPick)
        {
            var keys = LocAccess.GetKeys();
            if (keys.Length == 0)
                return;
            new LocKeyDropdown(new AdvancedDropdownState(), keys, "（无）", onPick).Show(anchor);
        }

        private Action<string> mOnPick;
        private string mFilter = "";
        private string mCurrentKey = "";
        private string mSelectedLang = "cn";
        private string mCategory = "__all__";
        private Vector2 mScroll;
        private Vector2 mCatScroll;

        // 缓存：进入窗口时一次性反射取 key + 各语言译文，避免逐帧反射
        private sealed class Entry
        {
            public string key;
            public string cn;
            public string en;
            public string category;
        }
        private sealed class Category
        {
            public string id;
            public string label;
            public int count;
        }
        private List<Entry> mEntries;
        private List<Category> mCategories;

        private static GUIStyle mSearchStyle;
        private static GUIStyle mKeyStyle;
        private static GUIStyle mValStyle;
        // 顶部「当前」信息行专用：小标签 / key 值（蓝） / 译文值（正文色）
        private static GUIStyle mBarLblStyle;
        private static GUIStyle mBarKeyStyle;
        private static GUIStyle mBarValStyle;
        private static readonly Color SelBg = new Color(0.16f, 0.36f, 0.56f);
        private const float WIN_W = 640f;
        private const float WIN_H = 480f;
        private const float CAT_W = 170f;
        private const float KEY_W = 250f;

        /// <summary>
        /// 折叠多级选择菜单：key 按 "/" 转 "\" 分层；多个来源 Excel 时先出文件层，
        /// 单 Excel（或无来源信息）直接按 key 分层，不再出现"未知来源"。
        /// </summary>
        public static void OpenFoldMenu(Rect anchor, string current, Action<string> onPick)
        {
            var keys = LocAccess.GetKeys();
            var menu = new GenericMenu();
            if (keys.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("未加载本地化数据"));
                menu.DropDown(anchor);
                return;
            }

            menu.AddItem(new GUIContent("搜索 key…"), false, () => OpenSearch(anchor, current, onPick));
            menu.AddSeparator("");

            var excelNames = LocAccess.GetExcelNames();
            var excelIndexes = LocAccess.GetExcelIndexes();
            // 多个来源 Excel 才显示文件层（单 Excel 时直接按 key 分层）
            var multiExcel = excelNames.Length > 1 && excelIndexes.Length == keys.Length;

            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var prefix = "";
                if (multiExcel && excelIndexes[i] >= 0 && excelIndexes[i] < excelNames.Length)
                    prefix = excelNames[excelIndexes[i]] + "/";
                // GenericMenu 以 "/" 分隔子菜单：key 里的 "/" 即自动折叠层级
                var path = prefix + key;
                var captured = key;
                menu.AddItem(new GUIContent(path), current == key, () => onPick(captured));
            }

            menu.DropDown(anchor);
        }

        public static void Open(Rect anchor, string current, Action<string> onPick)
            => OpenSearch(anchor, current, onPick);

        private static void OpenSearch(Rect anchor, string current, Action<string> onPick)
        {
            // 宿主窗口必须在 ShowUtility 之前抓：ShowUtility 之后 focusedWindow 会变成新窗自身，
            // 若那时再读 position 做"居中"，就是以自身为基准算出负偏移，窗口被顶到屏幕左上角、
            // 标题栏跑到屏幕外 → 无法拖动。先抓宿主（优先鼠标所在窗口），再建窗。
            var host = EditorWindow.mouseOverWindow ?? EditorWindow.focusedWindow;
            var h = host != null && host.position.width > 1f && host.position.height > 1f
                ? host.position
                : new Rect(0f, 0f, 1200f, 800f);

            var window = CreateInstance<LocKeyPickerWindow>();
            window.mOnPick = onPick;
            window.mFilter = "";
            window.mCurrentKey = current ?? "";
            window.mSelectedLang = CurrentLang();
            window.titleContent = new GUIContent("多语言 Key 选择");
            window.minSize = new Vector2(420f, 300f);

            // 居中于宿主，并夹进屏幕内：x/y 不允许为负（否则标题栏出屏、拖不动），
            // 右侧/底部也不让越界，保证任何宿主尺寸下整窗都在可视区、标题栏始终可点。
            const float titleBarMargin = 36f;
            var x = Mathf.Clamp(h.x + (h.width - WIN_W) * 0.5f, 0f, Mathf.Max(0f, h.x + h.width - WIN_W));
            var y = Mathf.Clamp(h.y + (h.height - WIN_H) * 0.5f, titleBarMargin, Mathf.Max(titleBarMargin, h.y + h.height - WIN_H));
            var pos = new Rect(x, y, WIN_W, WIN_H);

            window.position = pos;
            window.ShowUtility();
            // 个别 Unity 版本 ShowUtility 会重置尺寸/位置，Show 之后按同一坐标再钉一次
            window.position = pos;
            window.Repaint();
        }

        private static string CurrentLang()
        {
            var l = LocAccess.GetSelectedLanguage();
            return l == "en" ? "en" : "cn";   // 仅 cn/en 两档
        }

        private void OnEnable()
        {
            mSearchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField;
            mKeyStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(8, 6, 3, 3), alignment = TextAnchor.MiddleLeft };
            mKeyStyle.normal.textColor = new Color(0.62f, 0.78f, 0.95f);
            mValStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(8, 6, 3, 3), alignment = TextAnchor.MiddleLeft };

            mBarLblStyle = new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
            mBarLblStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.35f, 0.35f, 0.35f);

            mBarValStyle = new GUIStyle(EditorStyles.label)
            { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
            mBarValStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.83f, 0.83f, 0.83f) : new Color(0.12f, 0.12f, 0.12f);

            mBarKeyStyle = new GUIStyle(mBarValStyle);
            mBarKeyStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.50f, 0.72f, 0.92f) : new Color(0.10f, 0.33f, 0.60f);

            BuildCache();
        }

        private void BuildCache()
        {
            mEntries = new List<Entry>();
            mCategories = new List<Category>();
            var keys = LocAccess.GetKeys();
            if (keys.Length == 0) return;

            var catCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                var entry = new Entry
                {
                    key = k,
                    cn = LocAccess.GetTextByLang(k, "cn"),
                    en = LocAccess.GetTextByLang(k, "en"),
                    category = CategoryOf(k),
                };
                mEntries.Add(entry);
                catCount.TryGetValue(entry.category, out var c);
                catCount[entry.category] = c + 1;
            }

            mCategories.Add(new Category { id = "__all__", label = "全部", count = keys.Length });
            foreach (var kv in catCount)
                mCategories.Add(new Category { id = kv.Key, label = kv.Key, count = kv.Value });
            // "__all__" 字典序最前，保持其在首位
            mCategories.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        }

        /// <summary>分类：ui.xxx → 前两段；其余 → 第一段（如 char）。</summary>
        private static string CategoryOf(string key)
        {
            var p = key.Split('.');
            if (p.Length >= 2 && p[0] == "ui")
                return p[0] + "." + p[1];
            return p[0];
        }

        private void OnGUI()
        {
            if (mEntries == null || mEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("没有加载本地化数据。请先在 loc 插件执行导入（HYC Framework/Localization/Import Excel）。", MessageType.Info);
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { Close(); Event.current.Use(); }
                return;
            }

            DrawCurrentBar();   // 头部「当前 key │ CN │ EN」信息行（固定不滚）
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawCategories();
            DrawKeyList();
            EditorGUILayout.EndHorizontal();
            DrawFooter();

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape) { Close(); Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) { Confirm(); Event.current.Use(); }
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var filter = EditorGUILayout.TextField(mFilter, mSearchStyle, GUILayout.ExpandWidth(true));
            if (filter != mFilter) mFilter = filter;

            var old = GUI.backgroundColor;
            GUI.backgroundColor = mSelectedLang == "cn" ? SelBg : Color.white;
            if (GUILayout.Button("CN", EditorStyles.toolbarButton, GUILayout.Width(38))) mSelectedLang = "cn";
            GUI.backgroundColor = mSelectedLang == "en" ? SelBg : Color.white;
            if (GUILayout.Button("EN", EditorStyles.toolbarButton, GUILayout.Width(38))) mSelectedLang = "en";
            GUI.backgroundColor = old;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategories()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(CAT_W));
            GUILayout.Label("CATEGORIES  (" + mEntries.Count + ")", EditorStyles.miniLabel);
            mCatScroll = EditorGUILayout.BeginScrollView(mCatScroll);
            foreach (var cat in mCategories)
            {
                var old = GUI.backgroundColor;
                GUI.backgroundColor = mCategory == cat.id ? SelBg : Color.white;
                if (GUILayout.Button(cat.label + "  (" + cat.count + ")", EditorStyles.toolbarButton, GUILayout.ExpandWidth(true)))
                    mCategory = cat.id;
                GUI.backgroundColor = old;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawKeyList()
        {
            mScroll = EditorGUILayout.BeginScrollView(mScroll);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("KEY", EditorStyles.miniLabel, GUILayout.Width(KEY_W));
            GUILayout.Label("VALUE (" + mSelectedLang.ToUpperInvariant() + ")", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            var needle = mFilter.Trim();
            var shown = 0;
            foreach (var e in mEntries)
            {
                if (mCategory != "__all__" && e.category != mCategory) continue;
                if (!Match(e, needle)) continue;
                shown++;

                var old = GUI.backgroundColor;
                GUI.backgroundColor = e.key == mCurrentKey ? SelBg : Color.white;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(e.key, mKeyStyle, GUILayout.Width(KEY_W)))
                {
                    mCurrentKey = e.key;
                    if (Event.current.clickCount == 2) Confirm();
                }
                var val = mSelectedLang == "cn" ? e.cn : e.en;
                if (GUILayout.Button(string.IsNullOrEmpty(val) ? "—" : val, mValStyle, GUILayout.ExpandWidth(true)))
                {
                    mCurrentKey = e.key;
                    if (Event.current.clickCount == 2) Confirm();
                }
                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = old;
            }

            if (shown == 0)
                EditorGUILayout.LabelField("无匹配的 key。", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>模糊：key 或 值（cn/en 任一）包含即命中。</summary>
        private bool Match(Entry e, string needle)
        {
            if (needle.Length == 0) return true;
            if (e.key.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if ((e.cn ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if ((e.en ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void Confirm()
        {
            if (string.IsNullOrEmpty(mCurrentKey)) return;
            mOnPick?.Invoke(mCurrentKey);
            Close();
        }

        /// <summary>
        /// 顶部「当前」信息行：一行多列（当前 key │ CN │ EN），固定显示、不随列表滚动。
        /// 未选中显示「—」；文本超宽自动省略号，悬停显示全文。
        /// </summary>
        private void DrawCurrentBar()
        {
            var e = string.IsNullOrEmpty(mCurrentKey) ? null : mEntries?.Find(x => x.key == mCurrentKey);
            var keyText = string.IsNullOrEmpty(mCurrentKey) ? "—" : mCurrentKey;
            var cnText = e != null && !string.IsNullOrEmpty(e.cn) ? e.cn : "—";
            var enText = e != null && !string.IsNullOrEmpty(e.en) ? e.en : "—";

            var bar = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 10f);
            var pro = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(bar, pro ? new Color(0.20f, 0.20f, 0.20f) : new Color(0.82f, 0.82f, 0.82f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f),
                pro ? new Color(0.11f, 0.11f, 0.11f) : new Color(0.62f, 0.62f, 0.62f));

            const float pad = 10f;
            const float sepW = 13f;
            var x = bar.x + pad;
            var total = Mathf.Max(0f, bar.xMax - pad - x);
            var content = Mathf.Max(0f, total - sepW * 2f);
            var wKey = content * 0.40f;
            var wCn = content * 0.30f;
            var wEn = content * 0.30f;

            DrawBarSeg(bar, ref x, wKey, "当前", keyText, mBarKeyStyle);
            DrawBarSep(bar, x); x += sepW;
            DrawBarSeg(bar, ref x, wCn, "CN", cnText, mBarValStyle);
            DrawBarSep(bar, x); x += sepW;
            DrawBarSeg(bar, ref x, wEn, "EN", enText, mBarValStyle);
        }

        /// <summary>画「标签 + 值」一个分段；值超宽按像素省略号截断，悬停 tooltip 显示全文。</summary>
        private static void DrawBarSeg(Rect bar, ref float x, float segW, string label, string value, GUIStyle valueStyle)
        {
            var h = bar.height;
            var lblW = mBarLblStyle.CalcSize(new GUIContent(label)).x;
            GUI.Label(new Rect(x, bar.y, lblW, h), label, mBarLblStyle);

            var vx = x + lblW + 6f;
            var vw = segW - lblW - 6f;
            if (vw > 0f)
                GUI.Label(new Rect(vx, bar.y, vw, h),
                    new GUIContent(Ellipsize(value, valueStyle, vw), value), valueStyle);

            x += segW;
        }

        private static void DrawBarSep(Rect bar, float x)
        {
            var pro = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(new Rect(x + 6f, bar.y + 5f, 1f, bar.height - 10f),
                pro ? new Color(0.30f, 0.30f, 0.30f) : new Color(0.66f, 0.66f, 0.66f));
        }

        /// <summary>
        /// 按像素宽度截断并补「…」。Unity 的 TextClipping 在 2022.3 只暴露 Clip/Overflow，
        /// 没有 Ellipsis，故自己用 CalcSize 二分求最长可容纳前缀。
        /// </summary>
        private static string Ellipsize(string s, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(s) || maxWidth <= 0f) return s ?? "";
            if (style.CalcSize(new GUIContent(s)).x <= maxWidth) return s;

            const string ell = "…";
            var ellW = style.CalcSize(new GUIContent(ell)).x;
            if (maxWidth <= ellW) return ell;

            var budget = maxWidth - ellW;
            var lo = 0;
            var hi = s.Length;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (style.CalcSize(new GUIContent(s.Substring(0, mid))).x <= budget) lo = mid;
                else hi = mid - 1;
            }
            return s.Substring(0, lo) + ell;
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(56))) mCurrentKey = "";
            var old = GUI.backgroundColor;
            GUI.backgroundColor = SelBg;
            if (GUILayout.Button("选择", EditorStyles.toolbarButton, GUILayout.Width(64))) Confirm();
            GUI.backgroundColor = old;
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// 输入联想小窗：输入时实时弹出，前缀/包含过滤 key，最多显示 8 条，点击回填并关闭。
    /// </summary>
    public class LocKeySuggestWindow : EditorWindow
    {
        private Action<string> mOnPick;
        private string mNeedle = "";
        private int mSelected;
        private static GUIStyle mRowStyle;

        public static void Open(Rect anchor, string needle, Action<string> onPick)
        {
            var existing = Resources.FindObjectsOfTypeAll<LocKeySuggestWindow>();
            foreach (var w in existing)
            {
                w.mNeedle = needle;
                w.mOnPick = onPick;
                w.ShowAsDropDown(anchor, new Vector2(Mathf.Max(anchor.width, 280), 180));
                w.Focus();
                return;
            }

            var window = CreateInstance<LocKeySuggestWindow>();
            window.mNeedle = needle;
            window.mOnPick = onPick;
            window.ShowAsDropDown(anchor, new Vector2(Mathf.Max(anchor.width, 280), 180));
        }

        public static void CloseAll()
        {
            var existing = Resources.FindObjectsOfTypeAll<LocKeySuggestWindow>();
            foreach (var w in existing)
                w.Close();
        }

        private void OnEnable()
        {
            mRowStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(8, 6, 3, 3) };
            wantsMouseMove = true;
        }

        private void OnGUI()
        {
            var keys = LocAccess.GetKeys();
            var needle = (mNeedle ?? "").Trim().ToLowerInvariant();
            var matched = new List<string>();
            if (needle.Length > 0)
            {
                foreach (var k in keys)
                {
                    var low = k.ToLowerInvariant();
                    if (low.StartsWith(needle, StringComparison.Ordinal))
                        matched.Add(k);
                }
                foreach (var k in keys)
                {
                    if (matched.Count >= 8)
                        break;
                    var low = k.ToLowerInvariant();
                    if (low.IndexOf(needle, StringComparison.Ordinal) >= 0 && !low.StartsWith(needle, StringComparison.Ordinal))
                        matched.Add(k);
                }
            }

            if (matched.Count == 0)
            {
                Close();
                return;
            }

            var mousePos = Event.current.mousePosition;
            var hoverIndex = -1;
            var height = 22;
            for (var i = 0; i < matched.Count; i++)
            {
                var row = new Rect(0, i * height, position.width, height);
                if (row.Contains(mousePos))
                    hoverIndex = i;

                if (GUILayout.Button(matched[i], mRowStyle, GUILayout.Height(height)))
                {
                    var pick = matched[i];
                    Close();
                    mOnPick?.Invoke(pick);
                    Event.current.Use();
                    return;
                }
            }

            if (Event.current.type == EventType.MouseMove)
                Repaint();

            // 键盘上下键选择 + 回车确认
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.DownArrow)
                {
                    mSelected = Mathf.Min(mSelected + 1, matched.Count - 1);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.keyCode == KeyCode.UpArrow)
                {
                    mSelected = Mathf.Max(mSelected - 1, 0);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    if (mSelected >= 0 && mSelected < matched.Count)
                    {
                        var pick = matched[mSelected];
                        Close();
                        mOnPick?.Invoke(pick);
                    }
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Escape)
                {
                    Close();
                    Event.current.Use();
                }
            }
        }
    }

    /// <summary>
    /// 多语言 key 的 <see cref="AdvancedDropdown"/> 实现 —— Unity 自己的可搜索层级下拉。
    /// key 按 <c>/</c> 与 <c>.</c> 两种分隔符分层（两组数据风格不同，两种都要认）；
    /// 叶子显示<b>完整 key</b>，这样搜索框里输入任意片段都能命中，也不会因为同名叶子产生歧义。
    /// </summary>
    public class LocKeyDropdown : AdvancedDropdown
    {
        private static readonly char[] Separators = { '/', '.', '\\' };

        /// <summary>携带原始 key 的叶子项（<see cref="AdvancedDropdownItem"/> 只有名字，回不去 key）。</summary>
        private sealed class KeyItem : AdvancedDropdownItem
        {
            public readonly string Key;

            public KeyItem(string label, string key) : base(label)
            {
                Key = key;
            }
        }

        private readonly string[] mKeys;
        private readonly string mNoneLabel;
        private readonly Action<string> mOnPick;

        public LocKeyDropdown(AdvancedDropdownState state, string[] keys, string noneLabel, Action<string> onPick)
            : base(state)
        {
            mKeys = keys ?? Array.Empty<string>();
            mNoneLabel = noneLabel;
            mOnPick = onPick;
            minimumSize = new Vector2(340f, 420f);
        }

        /// <summary>叶子标签: key + 暗色翻译预览(若有), 让你在选择面板里也能直接看到内容而非只有 key。</summary>
        private static string LeafLabel(string key)
        {
            var text = LocAccess.GetText(key);
            if (string.IsNullOrEmpty(text)) return key;
            var t = text.Length > 40 ? text.Substring(0, 40) + "…" : text;
            return key + "  →  " + t;
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("本地化 Key");

            // 第一项 = 清空（等价于原来的 × 按钮，Unity 的 "None" 惯例）
            if (!string.IsNullOrEmpty(mNoneLabel))
                root.AddChild(new KeyItem(mNoneLabel, ""));

            var groups = new Dictionary<string, AdvancedDropdownItem>();
            foreach (var key in mKeys)
            {
                if (string.IsNullOrEmpty(key))
                    continue;

                var segments = key.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length <= 1)
                {
                    root.AddChild(new KeyItem(LeafLabel(key), key));
                    continue;
                }

                var parent = root;
                var path = "";
                for (var i = 0; i < segments.Length; i++)
                {
                    path = path.Length == 0 ? segments[i] : path + "/" + segments[i];

                    if (i == segments.Length - 1)
                    {
                        parent.AddChild(new KeyItem(LeafLabel(key), key));
                        continue;
                    }

                    if (!groups.TryGetValue(path, out var group))
                    {
                        group = new AdvancedDropdownItem(segments[i]);
                        groups[path] = group;
                        parent.AddChild(group);
                    }
                    parent = group;
                }
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is KeyItem keyItem)
                mOnPick?.Invoke(keyItem.Key);
        }
    }
}
