using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Map.Editor
{
    /// <summary>
    /// 地图编辑器主窗口：值层画笔 / 擦除 / 填充 / 矩形 / 道具，右侧是地图设置、网格与规则编辑。
    /// <para>
    /// <b>核心约定——拖动期间不落地</b>：矩形工具按住拖时只维护一份 <c>_preview</c> 网格副本，
    /// 画布渲染全部读这份副本，松手才写回资产。这样「看着就是最终效果」和
    /// 「数据没被动过」两件事同时成立，也可以用 Esc 直接放弃。
    /// </para>
    /// <para>
    /// <b>画布永远是运行期的样子</b>：渲染直接调 <see cref="MapAutoRules.Evaluate"/>，
    /// 与 <see cref="MapView"/> 加载时走同一条路径。所以不存在「编辑器画色块、进游戏才拼边」的两套表现——
    /// 规则调对了画布就对，调错了画布立刻错。
    /// </para>
    /// <para>
    /// <b>编辑的是「某一层」，不是「这张地图」</b>：所有绘制操作都带当前网格下标。
    /// 通行、区域、密度各自是独立的一层数据，规则集通过 <see cref="MapAutoRuleSet.sourceGrid"/>
    /// 决定读哪一层——于是「河流挡路」既可以做成地形材质，也可以做成独立通行层，不用改规则写法。
    /// </para>
    /// <para>
    /// 拆分：本文件管状态、工具逻辑、调色板与网格管理；画布在
    /// <c>MapEditorWindow.Canvas.cs</c>；规则集 / 规则 / 3×3 图案 / 过渡向导在
    /// <c>MapEditorWindow.Rules.cs</c>。IMGUI 控件集合必须逐帧一致，三边都只在 <c>Repaint</c> 里做重活。
    /// </para>
    /// </summary>
    public partial class MapEditorWindow : EditorWindow
    {
        private enum Tool
        {
            Brush = 0,
            Erase = 1,
            Fill = 2,
            Rect = 3,
            Prop = 4,
        }

        private static readonly string[] ToolLabels = { "画笔", "擦除", "填充", "矩形", "道具" };

        [SerializeField] private MapAsset _asset;
        [SerializeField] private Tool _tool = Tool.Brush;
        [SerializeField] private int _brushSize = 1;

        /// <summary>当前编辑的网格层（0 = 地形）。所有绘制都落这一层。</summary>
        [SerializeField] private int _gridIndex;

        /// <summary>当前网格层里选中的值下标（对应 <see cref="MapTileSet.ValuesOf"/> 的下标）。</summary>
        [SerializeField] private int _valueIndex;

        [SerializeField] private int _propIndex;
        [SerializeField] private float _zoom = 1f;
        [SerializeField] private bool _showGrid = true;

        /// <summary>只看某一层：调规则时把其它层关掉，才看得清这一层到底铺了什么。</summary>
        [SerializeField] private bool _soloLayer;
        [SerializeField] private int _soloLayerIndex;

        // 规则编辑的选中项（下标由 ClampSelection 维护）
        [SerializeField] private int _ruleSetIndex;
        [SerializeField] private int _groupIndex;
        [SerializeField] private int _ruleIndex;
        [SerializeField] private bool _showWizard;

        private Vector2 _paletteScroll;
        private Vector2 _sideScroll;
        private Vector2 _poolScroll;

        private Vector2Int _hover = new Vector2Int(-1, -1);
        private Vector2Int _lastPaintCell = new Vector2Int(int.MinValue, int.MinValue);

        private bool _painting;
        private bool _paintingErase;

        /// <summary>本次笔画是在摆道具（而不是刷值）。笔画期间固定，避免中途切工具把两种笔画混成一笔。</summary>
        private bool _paintingProp;

        private bool _rectDragging;
        private bool _rectErase;
        private Vector2Int _rectAnchor;
        private Vector2Int _rectCur;

        /// <summary>预览网格副本：非空表示「屏幕上看到的不是资产里的数据」。</summary>
        private byte[] _preview;
        private bool _hasPreview;

        /// <summary>预览副本对应哪一层（切换网格时预览必须作废，否则会把别层数据当成本层）。</summary>
        private int _previewGrid;

        /// <summary>
        /// 每层一个 <see cref="MapAutoRules.CellAt"/>，闭包只建一次。
        /// <para>每帧每格都新建闭包会持续产生 GC，而 <c>Evaluate</c> 是按格调用的，所以必须缓存。</para>
        /// </summary>
        private MapAutoRules.CellAt[] _gridReaders;
        private int _readerGridCount;

        private int _resizeW = 32;
        private int _resizeH = 24;

        /// <summary>打开（或聚焦）地图编辑器窗口。</summary>
        [MenuItem("HYC Framework/Map/Map Editor", false, 74)]
        public static void Open()
        {
            var win = GetWindow<MapEditorWindow>("地图编辑器");
            win.minSize = new Vector2(1100f, 640f);
            win.Show();
        }

        /// <summary>用指定资产打开窗口（Project 窗口右键/双击可用）。</summary>
        public static void Open(MapAsset asset)
        {
            var win = GetWindow<MapEditorWindow>("地图编辑器");
            win.minSize = new Vector2(1100f, 640f);
            win.SelectAsset(asset);
            win.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            _gridReaders = null;
            _readerGridCount = 0;
        }

        private void OnUndoRedo()
        {
            if (_asset == null) return;
            _asset.EnsureCells();
            ClearPreview();
            InvalidateReaders();
            ClampSelection();
            Repaint();
        }

        /// <summary>切换编辑目标。</summary>
        public void SelectAsset(MapAsset asset)
        {
            _asset = asset;
            _rectDragging = false;
            _painting = false;
            _paintingProp = false;
            _hover = new Vector2Int(-1, -1);
            ClearPreview();
            if (_asset != null)
            {
                _asset.EnsureCells();
                _resizeW = _asset.width;
                _resizeH = _asset.height;
            }
            InvalidateReaders();
            ClampSelection();
            Repaint();
        }

        /// <summary>把各个「选中下标」夹回合法范围（资产/瓦片集在别处被改过也会走到这里）。</summary>
        private void ClampSelection()
        {
            var set = _asset != null ? _asset.tileSet : null;

            int gridCount = set != null ? set.GridCount : 1;
            _gridIndex = Mathf.Clamp(_gridIndex, 0, Mathf.Max(0, gridCount - 1));
            _valueIndex = Mathf.Clamp(_valueIndex, 0, Mathf.Max(0, (set != null ? set.ValueCount(_gridIndex) : 0) - 1));

            int propCount = set != null && set.props != null ? set.props.Count : 0;
            _propIndex = Mathf.Clamp(_propIndex, 0, Mathf.Max(0, propCount - 1));

            int setCount = set != null && set.ruleSets != null ? set.ruleSets.Count : 0;
            _ruleSetIndex = Mathf.Clamp(_ruleSetIndex, 0, Mathf.Max(0, setCount - 1));
            _soloLayerIndex = Mathf.Clamp(_soloLayerIndex, 0, Mathf.Max(0, setCount - 1));

            var ruleSet = setCount > 0 ? set.ruleSets[_ruleSetIndex] : null;
            int groupCount = ruleSet != null && ruleSet.groups != null ? ruleSet.groups.Count : 0;
            _groupIndex = Mathf.Clamp(_groupIndex, 0, Mathf.Max(0, groupCount - 1));

            var group = groupCount > 0 ? ruleSet.groups[_groupIndex] : null;
            int ruleCount = group != null && group.rules != null ? group.rules.Count : 0;
            _ruleIndex = Mathf.Clamp(_ruleIndex, 0, Mathf.Max(0, ruleCount - 1));
        }

        private void OnGUI()
        {
            HandleDragAndDrop();
            DrawToolbar();

            if (_asset == null)
            {
                EditorGUILayout.HelpBox(
                    "先指定一张地图资产（Assets/Create/HYC Framework/Map/Map 新建），或把 MapAsset 拖进本窗口。",
                    MessageType.Info);
                return;
            }
            if (_asset.tileSet == null)
            {
                EditorGUILayout.HelpBox(
                    "本地图没有指定瓦片集（MapTileSet）。没有瓦片集就只能画占位色，且导出前必须先选好——" +
                    "值层的顺序会写进 Blob。",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            DrawPalette();
            DrawCanvas();
            DrawSidePanel();
            EditorGUILayout.EndHorizontal();
        }

        // ---------------- 顶部工具条 ----------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            var picked = (MapAsset)EditorGUILayout.ObjectField(
                _asset, typeof(MapAsset), false, GUILayout.Width(210f));
            if (EditorGUI.EndChangeCheck()) SelectAsset(picked);

            using (new EditorGUI.DisabledScope(_asset == null))
            {
                if (GUILayout.Button("选中", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                    EditorGUIUtility.PingObject(_asset);
            }

            GUILayout.Space(8f);
            _tool = (Tool)GUILayout.Toolbar((int)_tool, ToolLabels, EditorStyles.toolbarButton, GUILayout.Width(268f));

            GUILayout.Space(8f);
            GUILayout.Label("笔刷", EditorStyles.miniLabel, GUILayout.Width(28f));
            _brushSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(_brushSize, 1f, 9f, GUILayout.Width(80f)));
            GUILayout.Label($"{_brushSize}", EditorStyles.miniLabel, GUILayout.Width(16f));

            GUILayout.Space(6f);
            GUILayout.Label("缩放", EditorStyles.miniLabel, GUILayout.Width(28f));
            _zoom = GUILayout.HorizontalSlider(_zoom, 0.35f, 3f, GUILayout.Width(80f));
            GUILayout.Label($"{_zoom:0.0}x", EditorStyles.miniLabel, GUILayout.Width(34f));

            GUILayout.Space(6f);
            _showGrid = GUILayout.Toggle(_showGrid, "网格", EditorStyles.toolbarButton, GUILayout.Width(46f));
            bool solo = GUILayout.Toggle(_soloLayer, "只看层", EditorStyles.toolbarButton, GUILayout.Width(56f));
            if (solo != _soloLayer)
            {
                _soloLayer = solo;
                Repaint();
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_asset == null))
            {
                if (GUILayout.Button("导出 Blob", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    MapBlobExporter.Export(_asset);
                if (GUILayout.Button("导出 Prefab", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                    MapPrefabExporter.Export(_asset);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ---------------- 左侧调色板 ----------------

        private void DrawPalette()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(204f));

            var set = _asset.tileSet;
            if (set == null)
            {
                EditorGUILayout.HelpBox("瓦片集里没有值层。\n先给 MapAsset 指定一个 MapTileSet。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawGridPicker(set);
            DrawValueList(set, set.ValuesOf(_gridIndex));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"左键 = 涂「{set.GridName(_gridIndex)}」", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("右键 = 擦成空（只擦当前层）", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("矩形：拖出范围后松手才落地", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("道具：左键放 / 右键删（按占地）", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Esc = 取消拖动中的矩形", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        /// <summary>编辑哪一层。切换层会作废预览——不同层的格子值含义不同，混着看会误判。</summary>
        private void DrawGridPicker(MapTileSet set)
        {
            int count = set.GridCount;
            var labels = new string[count];
            for (int g = 0; g < count; g++) labels[g] = $"{g}. {set.GridName(g)}";

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup("编辑网格", Mathf.Clamp(_gridIndex, 0, count - 1), labels);
            if (EditorGUI.EndChangeCheck())
            {
                _gridIndex = picked;
                _valueIndex = 0;
                ClearPreview();
                ClampSelection();
                Repaint();
            }
        }

        private void DrawValueList(MapTileSet set, List<MapMaterial> values)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"值（{set.GridName(_gridIndex)}）", EditorStyles.boldLabel);
            if (GUILayout.Button("＋", EditorStyles.miniButton, GUILayout.Width(24f)))
            {
                AddValue(set, values);
            }
            using (new EditorGUI.DisabledScope(values == null || values.Count == 0))
            {
                if (GUILayout.Button("－", EditorStyles.miniButton, GUILayout.Width(24f)))
                    RemoveLastValue(set, values);
            }
            EditorGUILayout.EndHorizontal();

            if (values == null || values.Count == 0)
            {
                EditorGUILayout.HelpBox("这一层还没有值。\n点「＋」加一个。", MessageType.Warning);
                return;
            }

            _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < values.Count; i++)
                DrawValueRow(set, i, values[i]);

            if (set.props != null && set.props.Count > 0)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("道具", EditorStyles.miniBoldLabel);
                for (int i = 0; i < set.props.Count; i++)
                    DrawPropRow(set, i);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 加一个值（即新材质）。
        /// <para>只追加、不动已有下标——值下标直接写进 Blob，中间插入会让整张旧地图错位。</para>
        /// </summary>
        private void AddValue(MapTileSet set, List<MapMaterial> values)
        {
            if (values == null) return;
            Undo.RecordObject(set, "Add Value");

            int n = values.Count;
            var mat = new MapMaterial
            {
                key = $"value{n}",
                displayName = $"值{n}",
                color = Color.HSVToRGB(Mathf.Repeat(n * 0.137f + 0.08f, 1f), 0.45f, 0.85f),
                walkable = true,
            };
            values.Add(mat);

            EditorUtility.SetDirty(set);
            _valueIndex = values.Count - 1;
            if (_asset != null) _asset.EnsureGrids();   // 层数可能刚好变了
            InvalidateReaders();
            ClampSelection();
            Repaint();
        }

        /// <summary>
        /// 删掉最后一个值。<b>只允许删末尾</b>：中间删会让后面所有值的下标整体平移，
        /// 已画好的地图会静默错位成一张「看着正常但全错」的图。
        /// </summary>
        private void RemoveLastValue(MapTileSet set, List<MapMaterial> values)
        {
            if (values == null || values.Count == 0) return;
            var last = values[values.Count - 1];
            string name = last != null ? last.displayName : "(空槽)";
            if (!EditorUtility.DisplayDialog("删除值",
                $"删掉「{set.GridName(_gridIndex)}」末尾的值「{name}」？\n\n" +
                "地图里已经涂成这个值的格子不会变，但会显示为空（因为下标无对应值了）。",
                "删除", "取消"))
                return;

            Undo.RecordObject(set, "Remove Value");
            values.RemoveAt(values.Count - 1);
            EditorUtility.SetDirty(set);
            ClampSelection();
            Repaint();
        }

        /// <summary>值行：色块 + 名称 + key + 「可走」勾选。可走是值级语义，改的是瓦片集。</summary>
        private void DrawValueRow(MapTileSet set, int index, MapMaterial mat)
        {
            bool selected = _valueIndex == index && _tool != Tool.Erase;
            bool walkGrid = _gridIndex == set.WalkableGrid;

            var row = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                MapEditorGUI.Fill(row, selected
                    ? new Color(0.24f, 0.49f, 0.90f, 0.35f)
                    : (row.Contains(Event.current.mousePosition)
                        ? new Color(1f, 1f, 1f, 0.06f)
                        : new Color(0f, 0f, 0f, 0f)));
                if (mat != null)
                {
                    var chip = new Rect(row.x + 5f, row.y + 8f, 18f, 18f);
                    MapEditorGUI.Fill(chip, mat.color);
                    MapEditorGUI.Outline(chip, new Color(0f, 0f, 0f, 0.5f));
                }
            }

            float toggleW = 44f;
            var labelRect = new Rect(row.x + 28f, row.y + 4f, row.width - 30f - toggleW, 15f);
            var subRect = new Rect(row.x + 28f, row.y + 19f, row.width - 30f - toggleW, 12f);
            var walkRect = new Rect(row.xMax - toggleW, row.y + 9f, toggleW - 4f, 16f);

            EditorGUI.LabelField(labelRect, mat != null ? mat.displayName : "(空槽)");
            EditorGUI.LabelField(subRect,
                mat != null ? $"{mat.key}{(mat.walkable ? " · 可走" : " · 阻挡")}" : "-",
                EditorStyles.miniLabel);

            if (mat != null)
            {
                EditorGUI.BeginChangeCheck();
                bool walk = EditorGUI.Toggle(walkRect, mat.walkable);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(set, "Toggle Value Walkable");
                    mat.walkable = walk;
                    EditorUtility.SetDirty(set);
                    Repaint();
                }

                // 不是通行层时勾了也不生效——用橙色小点标出来，避免「明明勾了怎么还能走过去」这种误判
                if (!walkGrid && Event.current.type == EventType.Repaint)
                    MapEditorGUI.Fill(new Rect(walkRect.x - 10f, walkRect.y + 6f, 4f, 4f),
                        new Color(1f, 0.72f, 0.22f, 0.95f));
            }

            if (Event.current.type == EventType.MouseDown && !walkRect.Contains(Event.current.mousePosition)
                && row.Contains(Event.current.mousePosition))
            {
                _valueIndex = index;
                if (_tool == Tool.Erase) _tool = Tool.Brush;
                GUI.changed = true;
                Event.current.Use();
            }
        }

        private void DrawPropRow(MapTileSet set, int index)
        {
            var def = set.props[index];
            bool selected = _propIndex == index && _tool == Tool.Prop;

            var row = GUILayoutUtility.GetRect(0f, 32f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                MapEditorGUI.Fill(row, selected
                    ? new Color(0.95f, 0.68f, 0.20f, 0.35f)
                    : (row.Contains(Event.current.mousePosition)
                        ? new Color(1f, 1f, 1f, 0.06f)
                        : new Color(0f, 0f, 0f, 0f)));
                if (def != null)
                {
                    var sw = new Rect(row.x + 6f, row.y + 6f, 14f, 14f);
                    MapEditorGUI.Fill(sw, def.color);
                    MapEditorGUI.Outline(sw, new Color(0f, 0f, 0f, 0.6f));
                }
            }

            EditorGUI.LabelField(new Rect(row.x + 26f, row.y + 3f, row.width - 66f, 15f),
                def != null ? def.displayName : "(空槽)");
            EditorGUI.LabelField(new Rect(row.x + 26f, row.y + 17f, row.width - 66f, 12f),
                def != null
                    ? $"{Mathf.Max(1, def.width)}×{Mathf.Max(1, def.height)} 格{(def.prefab != null ? " · 有 prefab" : "")}"
                    : "-",
                EditorStyles.miniLabel);
            EditorGUI.LabelField(new Rect(row.xMax - 42f, row.y + 3f, 38f, 15f),
                def != null ? $"#{def.id}" : "-", EditorStyles.miniLabel);

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                _propIndex = index;
                _tool = Tool.Prop;
                GUI.changed = true;
                Event.current.Use();
            }
        }

        /// <summary>当前选中的道具 id（没选/空表返回 0 = 无）。</summary>
        private int SelectedPropId
        {
            get
            {
                var set = _asset != null ? _asset.tileSet : null;
                if (set == null || set.props == null || set.props.Count == 0) return 0;
                int i = Mathf.Clamp(_propIndex, 0, set.props.Count - 1);
                var def = set.props[i];
                return def != null ? def.id : 0;
            }
        }

        // ---------------- 数据访问 ----------------

        /// <summary>
        /// 渲染与规则求值统一从这里读：<b>指定层</b>的预览副本优先，否则读资产。
        /// <para>预览只对 <see cref="_previewGrid"/> 那一层生效，其它层不受拖动影响。</para>
        /// </summary>
        private byte ViewCell(int grid, int x, int y)
        {
            if (_asset == null) return 0;
            if (x < 0 || y < 0 || x >= _asset.width || y >= _asset.height) return 0;
            if (_hasPreview && grid == _previewGrid && _preview != null)
            {
                int i = y * _asset.width + x;
                if ((uint)i < (uint)_preview.Length) return _preview[i];
            }
            return _asset.GetCell(grid, x, y);
        }

        /// <summary>
        /// 取某层的格子读取回调（供 <see cref="MapAutoRules"/> 用）。
        /// 缓存数组，层数变了才重建。越界下标夹到 0 层——宁可显示地形层，
        /// 也不要让一条配错 <see cref="MapAutoRuleSet.sourceGrid"/> 的规则把整层静默变空。
        /// </summary>
        private MapAutoRules.CellAt ReaderOf(int grid)
        {
            EnsureReaders();
            if (_gridReaders == null || _gridReaders.Length == 0) return null;
            return _gridReaders[Mathf.Clamp(grid, 0, _gridReaders.Length - 1)];
        }

        private void EnsureReaders()
        {
            if (_asset == null) return;
            int assetGrids = _asset.GridCount;
            int setGrids = _asset.tileSet != null ? _asset.tileSet.GridCount : 1;
            int need = Mathf.Max(1, Mathf.Max(assetGrids, setGrids));
            if (_gridReaders != null && _readerGridCount == need) return;

            _readerGridCount = need;
            _gridReaders = new MapAutoRules.CellAt[need];
            for (int g = 0; g < need; g++)
            {
                int grid = g;   // 闭包捕获副本，不能用循环变量本身
                _gridReaders[g] = (x, y) => ViewCell(grid, x, y);
            }
        }

        private void InvalidateReaders()
        {
            _gridReaders = null;
            _readerGridCount = 0;
        }

        private byte SelectedValue =>
            _asset == null || _asset.tileSet == null
                ? (byte)0
                : _asset.tileSet.CellValueOf(_gridIndex, _valueIndex);

        private void AfterEdit()
        {
            EditorUtility.SetDirty(_asset);
            ClearPreview();
            Repaint();
        }

        private void ClearPreview()
        {
            _hasPreview = false;
            _preview = null;
        }

        // ---------------- 笔刷 / 填充 / 矩形 ----------------

        private void BeginStroke(bool erase)
        {
            // 一笔一次撤销，而不是一格一次：不然按 Ctrl+Z 会把整条笔画倒放一遍
            Undo.RegisterCompleteObjectUndo(_asset, "Paint Map");
            _painting = true;
            _paintingErase = erase;
            _paintingProp = _tool == Tool.Prop;
            _lastPaintCell = new Vector2Int(int.MinValue, int.MinValue);
        }

        private void EndStroke()
        {
            if (!_painting) return;
            _painting = false;
            _paintingProp = false;
            AfterEdit();
        }

        private void PaintAt(int cx, int cy, bool erase)
        {
            if (_asset.tileSet == null) return;
            byte v = erase ? (byte)0 : SelectedValue;
            int grid = _gridIndex;
            int half = (_brushSize - 1) / 2;
            for (int oy = 0; oy < _brushSize; oy++)
                for (int ox = 0; ox < _brushSize; ox++)
                    _asset.SetCell(grid, cx + ox - half, cy + oy - half, v);
        }

        /// <summary>
        /// 整数 Bresenham 直线遍历。
        /// 鼠标移动事件是离散的，缺插值会漏格（快速拖动时画笔断成一串点），
        /// 值笔刷与道具摆放共用这一处实现。
        /// </summary>
        private static void Bresenham(Vector2Int from, Vector2Int to, Action<int, int> visit)
        {
            int x0 = from.x, y0 = from.y;
            int x1 = to.x, y1 = to.y;
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                visit(x0, y0);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private void PaintLine(Vector2Int from, Vector2Int to, bool erase)
        {
            Bresenham(from, to, (x, y) => PaintAt(x, y, erase));
        }

        private void PropLine(Vector2Int from, Vector2Int to, bool erase)
        {
            Bresenham(from, to, (x, y) => PlacePropAt(x, y, erase));
        }

        /// <summary>在某一格放/删道具。不影响任何值层。</summary>
        private void PlacePropAt(int cx, int cy, bool erase)
        {
            if (_asset == null || _asset.tileSet == null) return;
            int propId = erase ? 0 : SelectedPropId;
            // 选了道具工具但没选具体道具（表空或没点）⇒ 不做事，避免"静默删掉"这种意外
            if (!erase && propId <= 0) return;
            _asset.SetProp(cx, cy, propId);
        }

        private void FloodFill(int x, int y, byte value)
        {
            if (_asset.tileSet == null) return;
            int grid = _gridIndex;
            byte from = _asset.GetCell(grid, x, y);
            if (from == value) return;

            Undo.RegisterCompleteObjectUndo(_asset, "Fill Map");

            int w = _asset.width, h = _asset.height;
            var visited = new bool[w * h];
            var stack = new Stack<Vector2Int>();
            stack.Push(new Vector2Int(x, y));
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h) continue;
                int i = c.y * w + c.x;
                if (visited[i]) continue;
                visited[i] = true;
                if (_asset.GetCell(grid, c.x, c.y) != from) continue;

                _asset.SetCell(grid, c.x, c.y, value);
                stack.Push(new Vector2Int(c.x + 1, c.y));
                stack.Push(new Vector2Int(c.x - 1, c.y));
                stack.Push(new Vector2Int(c.x, c.y + 1));
                stack.Push(new Vector2Int(c.x, c.y - 1));
            }
            AfterEdit();
        }

        private void RectBounds(out int x0, out int y0, out int x1, out int y1)
        {
            x0 = Mathf.Min(_rectAnchor.x, _rectCur.x);
            x1 = Mathf.Max(_rectAnchor.x, _rectCur.x);
            y0 = Mathf.Min(_rectAnchor.y, _rectCur.y);
            y1 = Mathf.Max(_rectAnchor.y, _rectCur.y);
            x0 = Mathf.Max(0, x0);
            y0 = Mathf.Max(0, y0);
            x1 = Mathf.Min(_asset.width - 1, x1);
            y1 = Mathf.Min(_asset.height - 1, y1);
        }

        /// <summary>
        /// 重建拖动预览：复制<b>当前层</b>的整份数据，再把矩形区域改掉。
        /// 画布随后全部读这份副本，于是拖动期间会实时显示「涂完之后规则会排出什么花样」，
        /// 而资产本身一格未动。
        /// </summary>
        private void RebuildRectPreview()
        {
            if (_asset == null) return;
            int n = _asset.CellCount;
            if (_preview == null || _preview.Length != n) _preview = new byte[n];

            var source = _asset.CellsOf(_gridIndex);
            if (source != null && source.Length == n) Array.Copy(source, _preview, n);
            else Array.Clear(_preview, 0, n);

            _previewGrid = _gridIndex;
            byte v = _rectErase ? (byte)0 : SelectedValue;
            RectBounds(out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            {
                int row = y * _asset.width;
                for (int x = x0; x <= x1; x++) _preview[row + x] = v;
            }
            _hasPreview = true;
        }

        private void CommitRect()
        {
            if (_asset.tileSet == null) return;
            Undo.RegisterCompleteObjectUndo(_asset, "Rect Fill Map");
            int grid = _gridIndex;
            byte v = _rectErase ? (byte)0 : SelectedValue;
            RectBounds(out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    _asset.SetCell(grid, x, y, v);
            AfterEdit();
        }

        private void CancelRect()
        {
            _rectDragging = false;
            ClearPreview();
            Repaint();
        }

        // ---------------- 右侧面板（地图设置 / 网格 / 导出） ----------------

        private void DrawSidePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(340f));
            _sideScroll = EditorGUILayout.BeginScrollView(_sideScroll, GUILayout.ExpandHeight(true));

            DrawMapSection();
            DrawHoverInfo();
            if (_asset.tileSet != null) DrawGridSection(_asset.tileSet);
            DrawRulesPanel();
            DrawExportSection();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawMapSection()
        {
            EditorGUILayout.LabelField("地图", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int mapId = EditorGUILayout.IntField("地图 id", _asset.mapId);
            var tileSet = (MapTileSet)EditorGUILayout.ObjectField("瓦片集", _asset.tileSet, typeof(MapTileSet), false);
            float cellSize = EditorGUILayout.FloatField("格边长", _asset.cellSize);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_asset, "Edit Map Settings");
                _asset.mapId = mapId;
                _asset.tileSet = tileSet;
                _asset.cellSize = cellSize;
                EditorUtility.SetDirty(_asset);
                if (_asset.tileSet != null) _asset.EnsureGrids();
                InvalidateReaders();
                ClampSelection();
            }

            EditorGUILayout.LabelField($"尺寸：{_asset.width} × {_asset.height}（{_asset.CellCount} 格）", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            _resizeW = Mathf.Max(1, EditorGUILayout.IntField(_resizeW, GUILayout.Width(52f)));
            _resizeH = Mathf.Max(1, EditorGUILayout.IntField(_resizeH, GUILayout.Width(52f)));
            if (GUILayout.Button("改尺寸(保留)", GUILayout.Width(92f)))
            {
                Undo.RegisterCompleteObjectUndo(_asset, "Resize Map");
                _asset.Resize(_resizeW, _resizeH, true);
                AfterEdit();
            }
            if (GUILayout.Button("清空", GUILayout.Width(48f)))
            {
                if (EditorUtility.DisplayDialog("清空地图",
                    $"把 {_asset.width}×{_asset.height} 的所有网格（共 {_asset.GridCount} 层）都擦成空？", "清空", "取消"))
                {
                    Undo.RegisterCompleteObjectUndo(_asset, "Clear Map");
                    _asset.ClearAll();
                    AfterEdit();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 网格（值层）管理。
        /// <para>
        /// 名字的归属在瓦片集（数据层命名），数据的归属在地图资产；加一层要两边同时加，
        /// 所以这里统一到一个动作里。删层<b>只删定义、不删数据</b>——数据只增不减，
        /// 误删定义还能改回来，删数据就没了。
        /// </para>
        /// </summary>
        private void DrawGridSection(MapTileSet set)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"网格（{set.GridCount}）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("每层一份数据；规则集决定读哪一层。", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            string primary = EditorGUILayout.TextField("第 0 层名", set.primaryGridName);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(set, "Rename Grid");
                set.primaryGridName = primary;
                EditorUtility.SetDirty(set);
            }

            int removeAt = -1;
            for (int g = 1; g < set.GridCount; g++)
            {
                var def = set.extraGrids[g - 1];
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField(
                    $"第 {g} 层名", def != null ? def.name : string.Empty);
                if (EditorGUI.EndChangeCheck() && def != null)
                {
                    Undo.RecordObject(set, "Rename Grid");
                    def.name = name;
                    EditorUtility.SetDirty(set);
                }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20f)))
                    removeAt = g - 1;
                EditorGUILayout.EndHorizontal();
            }
            // 删层延到循环之后：循环中改集合会让本帧控件数量与列表长度错位
            if (removeAt >= 0) RemoveGrid(set, removeAt);

            if (GUILayout.Button("＋ 新网格", EditorStyles.miniButton, GUILayout.Width(84f)))
                AddGrid(set);

            var labels = new string[set.GridCount];
            for (int g = 0; g < labels.Length; g++) labels[g] = $"{g}. {set.GridName(g)}";

            EditorGUI.BeginChangeCheck();
            int walk = EditorGUILayout.Popup(
                new GUIContent("通行层", "哪一层的值决定能不能踩。默认 0 = 地形层自己的 walkable。"),
                set.WalkableGrid, labels);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(set, "Set Walkable Grid");
                set.walkableGridIndex = walk;
                EditorUtility.SetDirty(set);
                Repaint();
            }
        }

        /// <summary>新建一层值层：默认给「可走 / 阻挡」两个值，否则新层连画都画不了。</summary>
        private void AddGrid(MapTileSet set)
        {
            Undo.RecordObject(set, "Add Grid");
            var def = new MapGridDef
            {
                name = $"网格{set.GridCount}",
                values = new List<MapMaterial>
                {
                    new MapMaterial
                    {
                        key = "pass", displayName = "可走",
                        color = new Color(0.35f, 0.85f, 0.45f, 1f), walkable = true,
                    },
                    new MapMaterial
                    {
                        key = "block", displayName = "阻挡",
                        color = new Color(0.95f, 0.30f, 0.30f, 1f), walkable = false,
                    },
                },
            };
            set.extraGrids.Add(def);
            EditorUtility.SetDirty(set);

            if (_asset != null)
            {
                Undo.RecordObject(_asset, "Add Grid");
                _asset.EnsureGrids();
                EditorUtility.SetDirty(_asset);
            }

            InvalidateReaders();
            _gridIndex = set.GridCount - 1;
            _valueIndex = 0;
            ClampSelection();
            Repaint();
        }

        /// <summary>
        /// 删一层定义。数据保留（只增不减），但指向它的配置必须夹回合法范围——
        /// 否则会留下「规则集读第 3 层，而只有 2 层」这种只在运行时才炸的悬空引用。
        /// </summary>
        private void RemoveGrid(MapTileSet set, int extraIndex)
        {
            int grid = extraIndex + 1;
            if (!EditorUtility.DisplayDialog("删除网格",
                $"删掉网格「{set.GridName(grid)}」？\n\n" +
                "地图里这一层的数据不会被删（数据只增不减），重新加回同名网格即可继续用。\n" +
                "指向它的规则集与「通行层」会被改回第 0 层。", "删除", "取消"))
                return;

            Undo.RecordObject(set, "Remove Grid");
            set.extraGrids.RemoveAt(extraIndex);

            int maxGrid = set.GridCount - 1;
            if (set.walkableGridIndex > maxGrid) set.walkableGridIndex = 0;
            if (set.ruleSets != null)
            {
                for (int i = 0; i < set.ruleSets.Count; i++)
                {
                    var ruleSet = set.ruleSets[i];
                    if (ruleSet != null && ruleSet.sourceGrid > maxGrid) ruleSet.sourceGrid = 0;
                }
            }

            EditorUtility.SetDirty(set);
            InvalidateReaders();
            ClampSelection();
            Repaint();
        }

        /// <summary>
        /// 光标信息。除了「每层这格是什么值」，还逐层报出 <see cref="MapAutoRules.Evaluate"/> 的结果——
        /// 规则没配好时，这里是唯一能看出「哪一层没铺、铺错到哪张」的地方。
        /// </summary>
        private void DrawHoverInfo()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("光标", EditorStyles.boldLabel);

            var set = _asset.tileSet;
            if (set == null)
            {
                EditorGUILayout.LabelField("-", EditorStyles.miniLabel);
                return;
            }
            if (_hover.x < 0 || _hover.y < 0)
            {
                EditorGUILayout.LabelField("把鼠标移到画布上查看格子信息", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField($"({_hover.x}, {_hover.y})", EditorStyles.miniLabel);

            int grids = Mathf.Min(set.GridCount, Mathf.Max(1, _asset.GridCount));
            int walkGrid = set.WalkableGrid;
            for (int g = 0; g < grids; g++)
            {
                byte v = ViewCell(g, _hover.x, _hover.y);
                var value = set.ValueOf(g, v);
                string text = value != null ? $"{value.displayName}（{value.key}）" : "空";
                if (g == walkGrid) text += value != null && value.walkable ? " · 可走" : " · 阻挡";
                EditorGUILayout.LabelField($"　{g}. {set.GridName(g)}：{text}", EditorStyles.miniLabel);
            }

            int hoverProp = _asset.GetPropIdAt(_hover.x, _hover.y);
            if (hoverProp > 0)
            {
                _asset.PropSize(hoverProp, out int pw, out int ph);
                EditorGUILayout.LabelField($"　道具 #{hoverProp} · {pw}×{ph} 格", EditorStyles.miniLabel);
            }

            if (set.ruleSets == null) return;
            for (int i = 0; i < set.ruleSets.Count; i++)
            {
                var ruleSet = set.ruleSets[i];
                if (ruleSet == null) continue;
                var reader = ReaderOf(ruleSet.sourceGrid);
                int tile = reader != null
                    ? MapAutoRules.Evaluate(ruleSet, reader, _hover.x, _hover.y)
                    : MapAutoRules.NoTile;
                string src = $"读{set.GridName(ruleSet.sourceGrid)}";
                string text = tile == MapAutoRules.NoTile
                    ? $"　层{i} {ruleSet.name}（{src}）：不铺"
                    : $"　层{i} {ruleSet.name}（{src}）：瓦片 {tile}"
                      + (ruleSet.contributesCollider && !IsWalkableAt(_hover.x, _hover.y) ? "（带碰撞）" : string.Empty);
                EditorGUILayout.LabelField(text, EditorStyles.miniLabel);
            }
        }

        /// <summary>这一格能不能踩（读通行层当前值，含预览）。</summary>
        private bool IsWalkableAt(int x, int y)
        {
            var set = _asset != null ? _asset.tileSet : null;
            if (set == null) return true;
            return set.IsWalkable(ViewCell(set.WalkableGrid, x, y));
        }

        private void DrawExportSection()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("导出", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Blob → Assets/StreamingAssets/Map/<mapId>.blob\n" +
                "Prefab → Grid + 每规则集一层 Tilemap + MapView（运行期读 Blob）",
                MessageType.None);
            if (GUILayout.Button("导出 Blob"))
                MapBlobExporter.Export(_asset);
            if (GUILayout.Button("导出 Prefab"))
                MapPrefabExporter.Export(_asset);
            if (GUILayout.Button("校验本地图"))
                MapValidator.ValidateAsset(_asset, true);
        }

        // ---------------- 拖放 ----------------

        private void HandleDragAndDrop()
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length != 1) return;

            var asset = DragAndDrop.objectReferences[0] as MapAsset;
            DragAndDrop.visualMode = asset != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (e.type == EventType.DragPerform && asset != null)
            {
                DragAndDrop.AcceptDrag();
                SelectAsset(asset);
            }
            e.Use();
        }
    }
}
