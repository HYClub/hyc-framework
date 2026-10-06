using System.Collections.Generic;
using HYC.Framework.Attr;
using HYC.Framework.Attr.Editor;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Attr.Editor
{
    /// <summary>
    /// 属性配置独立面板（优化方向 B 的可视化入口）。
    /// 菜单 <b>HYC Framework/Attributes/Attribute Catalog Editor</b> 打开；自动绑定当前选中或在工程里
    /// 找到的第一个 AttributeCatalog。与 AttributeCatalog 资产的 Inspector 编辑器并存——
    /// 本窗口是「从菜单直接打开」的入口，Inspector 适合跟着资产快速改。
    /// <para>两个分页：</para>
    /// <list type="bullet">
    /// <item><b>属性目录</b>：类型感知表格编辑 entries（Float 显示小数 / Bool 勾选框 / 整数），行首 ≡ 拖拽重排、行尾 ✕ 删除、底部「＋ 添加属性」。</item>
    /// <item><b>属性组</b>：组装 AttributeSetAsset（给 Hero 这类实体挑一篮子属性）。左列组列表、右列勾选要包含的 key；
    /// 新建组自动绑定当前目录，其 values 由 OnValidate/Rebuild 烘焙，运行时由 AttributeInitSystem 灌入实体。</item>
    /// </list>
    /// </summary>
    public class AttributeCatalogWindow : EditorWindow
    {
        private AttributeCatalog _catalog;
        private SerializedObject _serialized;
        private Vector2 _scroll;

        // 分页：0 = 属性目录，1 = 属性组
        private int _page;
        private readonly string[] _tabs = { "属性目录", "属性组" };

        // 属性组页状态
        private AttributeSetAsset _selectedSet;
        private readonly List<AttributeSetAsset> _allSets = new List<AttributeSetAsset>();
        private Vector2 _setScroll;

        // 拖拽重排状态（仅属性目录页）
        private int _dragIndex = -1;
        private readonly List<Rect> _rowRects = new List<Rect>();
        private readonly List<Rect> _handleRects = new List<Rect>();

        // 列宽（表头与数据行共用，保证对齐）
        private const float WHandle = 18f;
        private const float WKey = 150f;
        private const float WType = 74f;
        private const float WValue = 84f;
        private const float WDel = 22f;

        [MenuItem("HYC Framework/Attributes/Attribute Catalog Editor")]
        private static void Open()
        {
            var w = GetWindow<AttributeCatalogWindow>("属性配置");
            w.minSize = new Vector2(560f, 380f);
            w.ResolveCatalog();
            w.RefreshSets();
            w.Show();
        }

        private void OnEnable() { ResolveCatalog(); RefreshSets(); }

        private void ResolveCatalog()
        {
            if (_catalog == null)
                _catalog = Selection.activeObject as AttributeCatalog;
            if (_catalog == null)
            {
                var guids = AssetDatabase.FindAssets("t:AttributeCatalog");
                if (guids.Length > 0)
                    _catalog = AssetDatabase.LoadAssetAtPath<AttributeCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            Rebind();
        }

        private void Rebind()
        {
            _serialized = _catalog != null ? new SerializedObject(_catalog) : null;
        }

        private void RefreshSets()
        {
            _allSets.Clear();
            var guids = AssetDatabase.FindAssets("t:AttributeSetAsset");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var s = AssetDatabase.LoadAssetAtPath<AttributeSetAsset>(p);
                if (s != null) _allSets.Add(s);
            }
        }

        private void OnSelectionChange()
        {
            var cat = Selection.activeObject as AttributeCatalog;
            if (cat != null && cat != _catalog) { _catalog = cat; Rebind(); Repaint(); }
            var set = Selection.activeObject as AttributeSetAsset;
            if (set != null && set != _selectedSet) { _selectedSet = set; Repaint(); }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("属性配置（Attribute Catalog）", EditorStyles.boldLabel);

            _page = GUILayout.Toolbar(_page, _tabs);

            if (_page == 0) DrawCatalogPage();
            else DrawSetPage();

            // 共用底栏：生成访问器 / 校验目录（都基于当前目录资产）
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("生成属性访问器（enum + 映射 + 类型安全 AttrBag）"))
                    AttributeGenerateWizard.Show(_catalog);

                if (GUILayout.Button("校验目录"))
                {
                    var issues = AttributeValidator.Validate(_catalog);
                    if (issues.Count == 0)
                        EditorUtility.DisplayDialog("校验通过", "目录没有错误。", "OK");
                    else
                        EditorUtility.DisplayDialog("校验结果",
                            string.Join("\n", issues.ConvertAll(iss => (iss.IsError ? "[错误] " : "[警告] ") + iss.Message)), "OK");
                }
            }
        }

        // ---------- 属性目录页 ----------
        private void DrawCatalogPage()
        {
            EditorGUILayout.Space();
            var newCat = (AttributeCatalog)EditorGUILayout.ObjectField("目录资产", _catalog, typeof(AttributeCatalog), false);
            if (newCat != _catalog) { _catalog = newCat; Rebind(); }

            if (_catalog == null)
            {
                EditorGUILayout.HelpBox(
                    "未绑定 AttributeCatalog。可在 Project 右键 Create ▸ HYC Framework/Attributes/Attribute Catalog 创建，" +
                    "或用下面的按钮在当前工程里新建。", MessageType.Info);
                if (GUILayout.Button("新建 AttributeCatalog 资产"))
                {
                    _catalog = CreateCatalog();
                    Rebind();
                }
                return;
            }

            if (_serialized == null) Rebind();
            _serialized.Update();

            // 拖拽按下：命中某个手柄则进入拖拽态（用上一帧记录的 rect）
            if (_dragIndex < 0 && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                for (int j = 0; j < _handleRects.Count; j++)
                {
                    if (_handleRects[j].Contains(Event.current.mousePosition))
                    {
                        _dragIndex = j;
                        Event.current.Use();
                        break;
                    }
                }
            }

            DrawTableHeader();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _rowRects.Clear();
            _handleRects.Clear();

            var entries = _serialized.FindProperty("entries");
            if (entries.arraySize == 0)
            {
                EditorGUILayout.HelpBox("还没有属性，点下面的「＋ 添加属性」添加第一条。", MessageType.Info);
            }

            for (int i = 0; i < entries.arraySize; i++)
            {
                if (DrawEntryRow(entries, i)) break; // 删除后跳出本帧重绘
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (GUILayout.Button("＋ 添加属性"))
            {
                entries.InsertArrayElementAtIndex(entries.arraySize);
                var e = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                e.FindPropertyRelative("key").stringValue = "";
                e.FindPropertyRelative("type").intValue = (int)AttributeType.Int;
                e.FindPropertyRelative("defaultRaw").longValue = 0;
                e.FindPropertyRelative("minRaw").longValue = 0;
                e.FindPropertyRelative("maxRaw").longValue = 0;
                _serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(_catalog);
            }

            // 拖拽松开：落到鼠标所在的行
            if (_dragIndex >= 0 && Event.current.type == EventType.MouseUp)
            {
                int target = RowIndexAt(_rowRects, Event.current.mousePosition.y);
                if (target >= 0 && target != _dragIndex)
                {
                    entries.MoveArrayElement(_dragIndex, target);
                    _serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_catalog);
                }
                _dragIndex = -1;
                Event.current.Use();
            }

            _serialized.ApplyModifiedProperties();
        }

        private void DrawTableHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Space(WHandle);
            EditorGUILayout.LabelField("键 (key)", EditorStyles.boldLabel, GUILayout.Width(WKey));
            EditorGUILayout.LabelField("类型", EditorStyles.boldLabel, GUILayout.Width(WType));
            EditorGUILayout.LabelField("最小值", EditorStyles.boldLabel, GUILayout.Width(WValue));
            EditorGUILayout.LabelField("默认", EditorStyles.boldLabel, GUILayout.Width(WValue));
            EditorGUILayout.LabelField("最大值", EditorStyles.boldLabel, GUILayout.Width(WValue));
            GUILayout.Space(WDel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>画一行条目；返回 true 表示本行触发了删除（调用方应跳出本帧循环）。</summary>
        private bool DrawEntryRow(SerializedProperty entries, int i)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            Rect rowRect = EditorGUILayout.BeginHorizontal();
            _rowRects.Add(rowRect);

            // 拖拽手柄：用 Label 而非 Button，便于接管 MouseDown/Up 做重排
            Rect hr = GUILayoutUtility.GetRect(WHandle, EditorGUIUtility.singleLineHeight, GUILayout.Width(WHandle));
            GUI.Label(hr, "≡", _dragIndex == i ? EditorStyles.whiteLabel : EditorStyles.label);
            _handleRects.Add(hr);

            var keyProp = entry.FindPropertyRelative("key");
            EditorGUILayout.PropertyField(keyProp, GUIContent.none, GUILayout.Width(WKey));

            var typeProp = entry.FindPropertyRelative("type");
            EditorGUILayout.PropertyField(typeProp, GUIContent.none, GUILayout.Width(WType));

            var defaultProp = entry.FindPropertyRelative("defaultRaw");
            var minProp = entry.FindPropertyRelative("minRaw");
            var maxProp = entry.FindPropertyRelative("maxRaw");
            var t = (AttributeType)typeProp.intValue;

            switch (t)
            {
                case AttributeType.Float:
                    DrawFloatField(minProp, WValue);
                    DrawFloatField(defaultProp, WValue);
                    DrawFloatField(maxProp, WValue);
                    break;
                case AttributeType.Bool:
                    // 只有默认是有意义的勾选框，min/max 隐藏
                    bool b = EditorGUILayout.Toggle(defaultProp.longValue != 0, GUILayout.Width(WValue));
                    defaultProp.longValue = b ? 1 : 0;
                    EditorGUILayout.LabelField("—", GUILayout.Width(WValue));
                    EditorGUILayout.LabelField("—", GUILayout.Width(WValue));
                    break;
                default: // Int / Long：整数
                    minProp.longValue = EditorGUILayout.LongField(minProp.longValue, GUILayout.Width(WValue));
                    defaultProp.longValue = EditorGUILayout.LongField(defaultProp.longValue, GUILayout.Width(WValue));
                    maxProp.longValue = EditorGUILayout.LongField(maxProp.longValue, GUILayout.Width(WValue));
                    break;
            }

            if (GUILayout.Button("✕", GUILayout.Width(WDel)))
            {
                entries.DeleteArrayElementAtIndex(i);
                _serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(_catalog);
                EditorGUILayout.EndHorizontal();
                return true;
            }

            EditorGUILayout.EndHorizontal();
            return false;
        }

        private static void DrawFloatField(SerializedProperty prop, float width)
        {
            // raw 是 IEEE754 double 的 bits，显示/写回都走 BitConverter，保证往返无损
            double v = System.BitConverter.Int64BitsToDouble(prop.longValue);
            v = EditorGUILayout.DoubleField(v, GUILayout.Width(width));
            prop.longValue = System.BitConverter.DoubleToInt64Bits(v);
        }

        private static int RowIndexAt(List<Rect> rects, float y)
        {
            for (int i = 0; i < rects.Count; i++)
                if (y >= rects[i].y && y < rects[i].y + rects[i].height) return i;
            return -1;
        }

        // ---------- 属性组页 ----------
        private void DrawSetPage()
        {
            if (_selectedSet == null && _allSets.Count > 0) _selectedSet = _allSets[0];

            EditorGUILayout.BeginHorizontal();

            // 左栏：属性组列表
            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            EditorGUILayout.LabelField("属性组", EditorStyles.boldLabel);
            _setScroll = EditorGUILayout.BeginScrollView(_setScroll);
            foreach (var s in _allSets)
            {
                bool sel = s == _selectedSet;
                GUI.backgroundColor = sel ? new Color(0.29f, 0.36f, 0.53f) : Color.white;
                if (GUILayout.Button(s.name + "  (" + s.values.Count + ")"))
                    _selectedSet = s;
                GUI.backgroundColor = Color.white;
            }
            if (_allSets.Count == 0)
                EditorGUILayout.HelpBox("还没有属性组资产。", MessageType.None);
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("＋ 新建属性组"))
                CreateSet();
            EditorGUILayout.EndVertical();

            // 右栏：勾选要包含的属性
            EditorGUILayout.BeginVertical();
            if (_selectedSet == null)
            {
                EditorGUILayout.HelpBox("左侧选择或新建一个属性组。属性组 = 给某类实体（如 Hero）从属性目录里挑一篮子属性。", MessageType.Info);
            }
            else
            {
                var set = _selectedSet;
                EditorGUILayout.LabelField(set.name + " · 从「属性目录」勾选", EditorStyles.boldLabel);

                if (set.catalog == null)
                {
                    EditorGUILayout.HelpBox("该属性组还没有绑定目录，无法列出属性。", MessageType.Warning);
                    var c = (AttributeCatalog)EditorGUILayout.ObjectField("目录", set.catalog, typeof(AttributeCatalog), false);
                    if (c != set.catalog) { set.catalog = c; EditorUtility.SetDirty(set); }
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    foreach (var e in set.catalog.entries)
                    {
                        bool on = set.keys.Contains(e.key);
                        bool next = EditorGUILayout.ToggleLeft(e.key + "  (" + e.type + ")", on);
                        if (next && !on) set.keys.Add(e.key);
                        else if (!next && on) set.keys.Remove(e.key);
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(set);
                        set.Rebuild();
                    }
                    EditorGUILayout.HelpBox("values 已烘焙 " + set.values.Count + " 个属性（OnValidate 自动维护）。", MessageType.None);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void CreateSet()
        {
            var path = EditorUtility.SaveFilePanelInProject("新建属性组", "AttributeSet", "asset",
                "选择保存位置（建议放在 Assets/ 下的配置目录）");
            if (string.IsNullOrEmpty(path)) return;
            var asset = ScriptableObject.CreateInstance<AttributeSetAsset>();
            asset.catalog = _catalog;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            _selectedSet = asset;
            RefreshSets();
        }

        private static AttributeCatalog CreateCatalog()
        {
            var path = EditorUtility.SaveFilePanelInProject("新建 AttributeCatalog", "AttributeCatalog", "asset",
                "选择保存位置（建议放在 Assets/ 下的配置目录）");
            if (string.IsNullOrEmpty(path)) return null;
            var asset = ScriptableObject.CreateInstance<AttributeCatalog>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            return asset;
        }
    }
}
