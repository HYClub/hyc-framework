// ============================================================
// HYC Framework - 天赋树模块(Editor)
// 文件: Editor/Talent/TalentConfigPicker.cs
// 说明: 配置行拾取窗口 —— <b>独立窗口</b>, 不是贴着按钮弹出的小下拉。
//       天赋树黑板上的属性必须指向配置表里的一行, 本窗口把工程内所有配置数据实例
//       列出来供选择, 回传 {类型全名, ConfigId.id}。
//       复用框架已有的 ConfigIdService.TryReadId 读 ID, 不自己反射。
//       操作: 搜索 / 按配置表过滤 / ↑↓ 移动 / 回车确定 / Esc 关闭 / 双击确定。
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using HYC.Framework.Config;
using HYC.Framework.Config.Editor;

namespace HYC.Framework.Talent.Editor
{
    /// <summary>配置表里的一行，供拾取。</summary>
    public class ConfigRowEntry
    {
        public string TypeName;
        public long Id;
        public string DisplayName;
        public string AssetPath;
        public ScriptableObject Asset;
        public string Category;
    }

    public class TalentConfigPicker : EditorWindow
    {
        private const float RowH = 20f;
        private const float CatH = 22f;
        private const float WinW = 560f;
        private const float WinH = 600f;

        private readonly List<ConfigRowEntry> _rows = new List<ConfigRowEntry>();
        private List<ConfigRowEntry> _filtered = new List<ConfigRowEntry>();
        private readonly List<string> _cats = new List<string>();
        private Vector2 _scroll;
        private string _search = "";
        private int _catFilter;          // 0 = 全部
        private int _cursor;             // 键盘光标(在 _filtered 里的下标)
        private Action<ConfigRowEntry> _onPick;
        private GUIStyle _rowStyle, _catStyle, _dimStyle, _footStyle;
        private bool _needFocusSearch;

        /// <summary>打开拾取窗口。<paramref name="onPick"/> 在用户选定一行时回调。</summary>
        public static void Show(Action<ConfigRowEntry> onPick)
        {
            // 同一时刻只留一个拾取窗口, 免得连点几次叠出一摞
            foreach (var old in Resources.FindObjectsOfTypeAll<TalentConfigPicker>())
                if (old != null) old.Close();

            var w = CreateInstance<TalentConfigPicker>();
            w._onPick = onPick;
            w.titleContent = new GUIContent("选择配置行");
            w.minSize = new Vector2(420f, 320f);

            // 居中到主窗口(比贴在按钮上的小下拉宽敞得多: 名字/ID/路径都能完整显示)
            Rect pos;
            try
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                pos = new Rect(main.center.x - WinW * 0.5f, main.center.y - WinH * 0.5f, WinW, WinH);
            }
            catch
            {
                pos = new Rect(200f, 160f, WinW, WinH);
            }
            w.position = pos;
            w._needFocusSearch = true;
            w.ShowUtility();
            w.Focus();
        }

        private void OnEnable()
        {
            // 扫描工程内的配置实例有点重, 推迟一拍做, 别卡在窗口创建上
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                Refresh();
                Repaint();
            };
        }

        // ------------------------------------------------------------
        //  数据
        // ------------------------------------------------------------
        private void Refresh()
        {
            _rows.Clear();
            _cats.Clear();

            var cfgTypes = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }   // 少数程序集会抛 ReflectionTypeLoadException, 跳过即可

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (!typeof(ScriptableObject).IsAssignableFrom(t)) continue;
                    if (!t.GetCustomAttributes(typeof(CfgAssetAttribute), true).Any()) continue;
                    cfgTypes.Add(t);
                }
            }

            foreach (var t in cfgTypes)
            {
                var attr = (CfgAssetAttribute)t.GetCustomAttributes(typeof(CfgAssetAttribute), true)[0];
                string catName = string.IsNullOrEmpty(attr.Name) ? t.Name : attr.Name;

                foreach (var g in AssetDatabase.FindAssets("t:" + t.Name))
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (asset == null) continue;
                    if (!t.IsAssignableFrom(asset.GetType())) continue;

                    long id;
                    string guidStr;
                    if (!ConfigIdService.TryReadId(asset, out id, out guidStr)) continue;   // 没有 ID/GUID 字段 → 不是数据实例

                    _rows.Add(new ConfigRowEntry
                    {
                        TypeName = t.FullName,
                        Id = id,
                        DisplayName = asset.name,
                        AssetPath = path,
                        Asset = asset,
                        Category = catName,
                    });
                }
            }

            _rows.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Category, b.Category);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.DisplayName, b.DisplayName);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });

            _cats.AddRange(_rows.Select(r => r.Category).Distinct().OrderBy(x => x, StringComparer.Ordinal));
            _cats.Insert(0, "全部配置表");
            if (_catFilter >= _cats.Count) _catFilter = 0;

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = _search == null ? "" : _search.Trim().ToLowerInvariant();
            string cat = _catFilter > 0 && _catFilter < _cats.Count ? _cats[_catFilter] : null;

            _filtered = _rows.Where(r =>
            {
                if (cat != null && r.Category != cat) return false;
                if (q.Length == 0) return true;
                return (r.DisplayName != null && r.DisplayName.ToLowerInvariant().Contains(q))
                    || (r.Category != null && r.Category.ToLowerInvariant().Contains(q))
                    || (r.TypeName != null && r.TypeName.ToLowerInvariant().Contains(q))
                    || r.Id.ToString().Contains(q);
            }).ToList();

            _cursor = Mathf.Clamp(_cursor, 0, Mathf.Max(0, _filtered.Count - 1));
        }

        // ------------------------------------------------------------
        //  绘制
        // ------------------------------------------------------------
        private void EnsureStyles()
        {
            if (_rowStyle != null) return;

            _rowStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                richText = false,
            };
            _rowStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.84f, 0.85f, 0.9f) : new Color(0.14f, 0.14f, 0.18f);

            _catStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            _catStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.62f, 0.72f, 0.95f) : new Color(0.2f, 0.3f, 0.55f);

            _dimStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
            _dimStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.5f, 0.52f, 0.58f) : new Color(0.45f, 0.45f, 0.5f);

            _footStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
        }

        private void OnGUI()
        {
            EnsureStyles();

            // ---- 顶部: 搜索 + 配置表过滤 + 刷新 ----
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (_needFocusSearch)
            {
                GUI.SetNextControlName("TalentPickerSearch");
                GUI.FocusControl("TalentPickerSearch");
                _needFocusSearch = false;
            }
            EditorGUI.BeginChangeCheck();
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
            if (EditorGUI.EndChangeCheck()) ApplyFilter();

            _catFilter = EditorGUILayout.Popup(_catFilter, _cats.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(140f));
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                Refresh();
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            if (_rows.Count == 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(
                    "没有找到任何配置数据实例。\n\n" +
                    "天赋树黑板的属性必须来自配置表 —— 请先在 HYC Framework/Config/Data Editor 里建好配置并生成代码, " +
                    "配置数据实例(带 ConfigId 的 ScriptableObject)才会出现在这里。",
                    MessageType.Warning);
                return;
            }

            // ---- 列表 ----
            float listH = position.height - 24f - 22f;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Max(60f, listH)));

            string lastCat = null;
            for (int i = 0; i < _filtered.Count; i++)
            {
                var r = _filtered[i];

                if (r.Category != lastCat)
                {
                    lastCat = r.Category;
                    var catRect = EditorGUILayout.GetControlRect(false, CatH);
                    EditorGUI.DrawRect(new Rect(catRect.x, catRect.y + 3f, catRect.width, catRect.height - 5f), new Color(0.24f, 0.24f, 0.29f));
                    EditorGUI.DrawRect(new Rect(catRect.x, catRect.y + 3f, 2f, catRect.height - 5f), new Color(0.43f, 0.66f, 1f));
                    GUI.Label(new Rect(catRect.x + 8f, catRect.y, catRect.width - 12f, catRect.height), r.Category, _catStyle);
                }

                var rect = EditorGUILayout.GetControlRect(false, RowH);
                bool hover = rect.Contains(Event.current.mousePosition);
                bool isCursor = i == _cursor;

                if (Event.current.type == EventType.Repaint)
                {
                    if (isCursor) EditorGUI.DrawRect(rect, new Color(0.24f, 0.30f, 0.45f));
                    else if (hover) EditorGUI.DrawRect(rect, new Color(0.24f, 0.24f, 0.32f));
                }

                // 左: 行名 + #id ; 右: 资产路径(右对齐, 窄了自动裁)
                float pathW = Mathf.Min(240f, rect.width * 0.45f);
                float nameW = rect.width - pathW - 14f;
                GUI.Label(new Rect(rect.x + 10f, rect.y, nameW, rect.height), r.DisplayName + "    #" + r.Id, _rowStyle);
                GUI.Label(new Rect(rect.x + rect.width - pathW - 6f, rect.y, pathW, rect.height), r.AssetPath, _dimStyle);

                if (Event.current.type == EventType.MouseDown && hover)
                {
                    _cursor = i;
                    if (Event.current.button == 0)
                    {
                        Event.current.Use();
                        Pick(r);
                        return;
                    }
                }
            }

            // 键盘导航(必须在循环外处理一次, 否则循环几次就动几格)
            if (Event.current.type == EventType.KeyDown && _filtered.Count > 0)
            {
                var key = Event.current.keyCode;
                if (key == KeyCode.DownArrow || key == KeyCode.UpArrow)
                {
                    _cursor = key == KeyCode.DownArrow
                        ? Mathf.Min(_filtered.Count - 1, _cursor + 1)
                        : Mathf.Max(0, _cursor - 1);
                    Event.current.Use();

                    // 让光标行滚进可视区
                    float cursorY = 0f;
                    string c2 = null;
                    for (int i = 0; i <= _cursor && i < _filtered.Count; i++)
                    {
                        if (_filtered[i].Category != c2) { c2 = _filtered[i].Category; cursorY += CatH; }
                        cursorY += RowH;
                    }
                    if (cursorY - RowH < _scroll.y) _scroll.y = Mathf.Max(0f, cursorY - RowH);
                    else if (cursorY > _scroll.y + listH - RowH) _scroll.y = cursorY - listH + RowH;
                    Repaint();
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    Event.current.Use();
                    Pick(_filtered[_cursor]);
                    return;
                }
                else if (key == KeyCode.Escape)
                {
                    Event.current.Use();
                    Close();
                    GUIUtility.ExitGUI();
                    return;
                }
            }

            EditorGUILayout.EndScrollView();

            // ---- 底部提示 ----
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(
                _filtered.Count + " / " + _rows.Count + " 行   ·   ↑↓ 移动 · 回车确定 · Esc 取消 · 想取消绑定请直接点黑板的「×」删掉那一行",
                _footStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void Pick(ConfigRowEntry row)
        {
            var cb = _onPick;
            _onPick = null;      // 防重入
            Close();
            cb?.Invoke(row);
            GUIUtility.ExitGUI();
        }
    }
}
