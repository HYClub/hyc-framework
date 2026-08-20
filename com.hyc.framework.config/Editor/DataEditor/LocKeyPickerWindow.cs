using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 多语言 key 选择：主交互是 <see cref="OpenFoldMenu"/> 折叠多级菜单
    /// （按 key 的 "/" 分层，多 excel 时先出文件层）；菜单顶部提供
    /// "搜索 key…" 入口，打开本窗口做模糊查询（平铺列表）。
    /// 数据来自 <see cref="LocAccess"/>（反射 loc 包）。
    /// </summary>
    public class LocKeyPickerWindow : EditorWindow
    {
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
}
