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
        private Vector2 mScroll;
        private static GUIStyle mGroupStyle;
        private static GUIStyle mRowStyle;
        private static GUIStyle mHintStyle;

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
            var window = CreateInstance<LocKeyPickerWindow>();
            window.mOnPick = onPick;
            window.mFilter = current ?? "";
            window.titleContent = new GUIContent("搜索 key");
            window.ShowAsDropDown(anchor, new Vector2(Mathf.Max(anchor.width, 360), 340));
        }

        private void OnEnable()
        {
            mGroupStyle = new GUIStyle(EditorStyles.boldLabel) { padding = new RectOffset(6, 6, 3, 1) };
            mRowStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(12, 6, 2, 2) };
            mHintStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
        }

        private void OnGUI()
        {
            var filter = EditorGUILayout.TextField("", mFilter, GUI.skin.FindStyle("ToolbarSearchTextField"));
            if (filter != mFilter)
                mFilter = filter;

            var keys = LocAccess.GetKeys();
            if (keys.Length == 0)
            {
                EditorGUILayout.HelpBox("没有加载本地化数据。请先在 loc 插件执行导入（HYC Framework/Localization/Import Excel）。", MessageType.Info);
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                {
                    Close();
                    Event.current.Use();
                }
                return;
            }

            mScroll = EditorGUILayout.BeginScrollView(mScroll);
            DrawGrouped(filter);
            EditorGUILayout.EndScrollView();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
            }
        }

        private void DrawGrouped(string filter)
        {
            var keys = LocAccess.GetKeys();
            var excelNames = LocAccess.GetExcelNames();
            var excelIndexes = LocAccess.GetExcelIndexes();
            // 仅当有多个来源 Excel 且索引对齐时按文件分组；否则平铺（无"未知来源"）
            var multiExcel = excelNames.Length > 1 && excelIndexes.Length == keys.Length;
            var needle = filter.Trim().ToLowerInvariant();

            var groups = new Dictionary<string, List<string>>();
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (needle.Length > 0 && key.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) < 0)
                    continue;

                var group = multiExcel && excelIndexes[i] >= 0 && excelIndexes[i] < excelNames.Length
                    ? excelNames[excelIndexes[i]]
                    : null;
                if (group == null)
                {
                    // 无分组：平铺显示原始 key（搜索场景看完整 key）
                    if (GUILayout.Button(key, mRowStyle))
                    {
                        mOnPick?.Invoke(key);
                        Close();
                    }
                    continue;
                }

                if (!groups.TryGetValue(group, out var list))
                {
                    list = new List<string>();
                    groups.Add(group, list);
                }
                list.Add(key);
            }

            foreach (var g in groups)
            {
                EditorGUILayout.LabelField(g.Key, mGroupStyle);
                foreach (var key in g.Value)
                {
                    if (GUILayout.Button(key, mRowStyle))
                    {
                        mOnPick?.Invoke(key);
                        Close();
                    }
                }
            }

            if (groups.Count == 0)
                EditorGUILayout.LabelField("没有匹配的 key。", mHintStyle);
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
