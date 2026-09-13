// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTGraphIMGUI.cs
// 说明: NodeCanvas 风格 IMGUI 手绘画布(零 using NodeCanvas/ParadoxNotion)。
//       移植 NodeCanvas Editor.Node/Editor.Connection/GraphEditor 的绘制与交互逻辑,
//       用 HYC 数据与命名空间重写。由 BTGraphWindow 内 IMGUIContainer 承载。
//       P2: 框选 / 端口拖拽连线 / 右键菜单(装饰·替换·转子树·复制分支·删除分支) /
//           节点上 inline 参数编辑 / 节点库拖拽建节点。全部直接改写 BTTreeAsset 数据。
//       P3: 左栏(行为树资源 + 节点库拖拽 + 黑板) / 右上 minimap / 底部状态条。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using HYC.Framework.BT;

namespace HYC.Framework.BT.Editor
{
    public class BTGraphIMGUI
    {
        private BTTreeAsset _asset;

        // 视图变换(对应 GraphEditor pan/zoom)
        public Vector2 Pan = new Vector2(80, 80);
        public float Zoom = 1f;

        // 选择(支持多选)
        private HashSet<long> _selectedIds = new HashSet<long>();

        // 拖拽/平移状态
        private bool _panning;
        private Vector2 _panStartMouse;
        private Vector2 _panStartPan;

        // 框选
        private bool _boxSelecting;
        private Vector2 _boxStart, _boxEnd;

        // 端口拖拽连线
        private long? _pendingSrcId;
        private int _pendingSrcPort;

        // 节点库拖放
        private List<BTNodeData> _clipboard = new List<BTNodeData>();

        // 本帧布局 / 坐标
        private Rect _area;
        private Rect _canvasRect;     // 画布区域(扣除左栏/底栏)
        private Rect _visibleCanvasRect; // 当前可视区对应的画布坐标系矩形(视口裁剪用)
        private Rect _miniRect;       // minimap 屏幕矩形
        private Vector2 _rawMouse;    // 容器坐标鼠标(用于所有手动命中测试, 避免 GUI 组坐标歧义)
        private Vector2 _mouseCanvas; // 鼠标的画布坐标
        private Vector2 _mmMin;       // minimap 映射: 画布包围盒 min
        private float _mmScale;       // minimap 映射: 缩放
        private Vector2 _mmOrigin;    // minimap 映射: 屏幕原点(组内)

        // 工具栏 / 偏好(对应 NC GraphEditor.Toolbar + Prefs)
        private bool _showGrid = true;          // Prefs.showGrid
        private bool _snapGrid;                 // Prefs.snapToGrid
        private bool _locked;                   // Prefs.isEditorLocked
        private bool _showLeftPanel = true;
        private bool _debugClip = false;         // 调试: 可视化画布变换层(已验证, 默认关; Prefs → 调试/显示裁剪层)
        private bool _panelOnRight = true;      // 面板停靠右侧(Explorer/ROOT 在窗口右上)
        private float _rigidity = 0.8f;         // Prefs.connectionsMLT (默认 0.8)
        private Rect _toolbarRect;
        private Rect _panelRect;                // 右侧 Explorer 面板矩形
        private Rect _rightRect;                // 右侧 Explorer 面板矩形(与 _panelRect 同值)
        private Rect _assetsRect;               // 左侧 行为树资源 面板矩形
        private Rect _libRect;                  // 画布上浮动的 节点库 面板矩形(可拖动)
        private Vector2 _assetsScroll;
        private Vector2 _libScroll;
        private bool _libWinDrag;
        private Vector2 _libWinDragOffset;
        private bool _showAssetsPanel = true;   // 左: 行为树资源
        private bool _showLibPanel = true;      // 浮动: 节点库
        private Vector2 _drawOffset;            // 命中测试偏移(GUI.BeginGroup 组原点; 无组时为零)
        public System.Action OnRequestNewTree;

        // 左栏 = NC GraphExplorer 完整移植
        private const int INDENT_WIDTH = 25;   // GraphExplorer.INDENT_WIDTH
        private const int INDENT_START = 1;    // GraphExplorer.INDENT_START
        private string _explorerSearch;
        private bool _explorerShowTypeNames;
        private BTNodeData _lastHoverNode;      // GraphExplorer.lastHoverElement
        private long _pingNodeId;               // 画布 ping 高亮节点
        private double _pingTime;               // ping 开始时间(1s 淡出)
        private Vector2 _leftScroll;            // 左栏手动滚动
        private bool _sbDrag;
        private float _sbDragStart;
        private readonly HashSet<string> _collapsed = new HashSet<string>(); // 左栏区块/子分组折叠状态(key)
        private const float ROW_H = 18f;        // Explorer 行高
        private const float SEC_H = 22f;        // 分组标题高
        private const float SUB_H = 18f;        // 子分组标题高

        // ---- 行为树资源列表: Windows 文件列表风格(选中 / 单击改名 / 拖拽移动) ----
        private const float DBL_CLICK = 0.4f;                 // 双击判定阈值(s), 与 Unity Project 窗口一致
        private const string RENAME_CTRL = "BTTreeRenameField";
        private string _selAssetPath;                         // 当前选中行(资产或目录路径)
        private double _selAssetClickTime = -10.0;            // 上次单击时刻(EditorApplication.timeSinceStartup)
        private string _pendingRenamePath;                    // MouseDown 记下的"抬起后应进入改名"的行
        private string _assetDragPath;                        // 拖拽候选/进行中的行路径
        private Vector2 _assetDragStartMouse;
        private bool _assetDragMoved;
        private bool _assetRowDragging;                       // 已越过阈值, 正式拖动
        private string _assetDropDir;                         // 当前落点目录(高亮)
        private Rect _renameRect;                             // 内联改名框矩形(点击其外才提交)
        private bool _renameSelectAll;                        // 进入改名时全选文本
        private readonly List<TreeRow> _treeRows = new List<TreeRow>();  // 本帧行矩形(命中测试)

        private class TreeRow { public Rect Rect; public string Path; public bool IsFolder; }

        // 节点拖拽
        private bool _draggingNode;
        private Vector2 _dragStartCanvas;
        private readonly Dictionary<long, Vector2> _dragOrigins = new Dictionary<long, Vector2>();

        // NC HierarchyTree.Element 的 HYC 等价
        private class ExplorerEl
        {
            public BTNodeData Node;
            public List<ExplorerEl> Children = new List<ExplorerEl>();
        }

        // Explorer 扁平可见行(手动布局用)
        private class ExplorerRow
        {
            public BTNodeData Node;
            public int Indent;
            public int Parent;   // 父行索引(-1 = 根级)
            public Rect Rect;
        }
        private readonly List<ExplorerRow> _explorerRows = new List<ExplorerRow>();

        // 节点库拖拽候选
        private BTDragPayload _libDragPayload;   // (遗留) DragAndDrop 通道, 现由 _libPending 手动实现
        private BTDragPayload _libPending;       // 按下待拖动/点击创建的候选
        private Vector2 _libDragStart;
        private bool _libDragging;

        // 校验结果(供底部状态条)
        private List<BTValidationIssue> _issues = new List<BTValidationIssue>();

        // 校验结果缓存: 仅在图变更(_graphDirty)或间隔超时后重算, 避免每帧全量校验。
        private bool _graphDirty = true;
        private double _lastValidateTime = -1.0;
        private const double VALIDATE_INTERVAL = 0.25; // 秒

        public System.Action OnRepaint;
        public System.Action<BTTreeAsset> OnRequestOpenTree;

        // 常量(对应 NodeCanvas 视觉与交互, 数值取自源码)
        private const float HEADER_H = 24f;       // windowTitle 12px + padding(7+5) ≈ 24
        private const float RIGIDITY_DEFAULT = 0.8f; // Prefs.connectionsMLT 默认值
        private const float TOOLBAR_H = 21f;      // GraphEditor.TAB_HEIGHT
        private const float GRID = 20f;           // GraphEditor.GRID_SIZE(网格/吸附步长)
        private const float PORT_SIZE = 12f;      // Editor.Node: sourcePortRect = 12x12 (端口直径)
        private const float CONN_SIZE = 3f;       // Connection.defaultSize
        private const float NODE_W = 180f;
        private const float PORT_HIT = 12f;       // 端口命中半径(画布单位)
        private const float PORT_OUT_OFFSET = 6f; // 输出端口相对右缘外凸(Editor.Node: portOffset=6)
        private const float LEFT_W = 220f;
        private const float BOTTOM_H = 104f;
        private const float MM_W = 210f;
        private const float MM_H = 150f;
        private const float LIB_ROW = 22f;
        private const float HEAD_ROW = 20f;
        // 画布底色(对应 StyleSheet.canvasBG 的亮度): 底色太暗时 NC 的纯黑网格(a0.15)根本看不见
        private static readonly Color BG = new Color(0.24f, 0.24f, 0.27f);

        // 样式(懒初始化)
        private GUIStyle _libLabel, _headLabel, _subLabel, _okLabel, _warnLabel, _errLabel, _dbgLabel;

        // Explorer 行文本(对应 size=9 富文本, 左对齐)
        private GUIStyle _explorerRowStyle;
        private GUIStyle ExplorerRowStyle
        {
            get
            {
                if (_explorerRowStyle == null)
                    _explorerRowStyle = new GUIStyle(GUI.skin.label)
                    {
                        richText = true,
                        alignment = TextAnchor.MiddleLeft,
                        fontSize = 9,
                        wordWrap = false,
                        padding = new RectOffset(2, 4, 0, 0),
                    };
                return _explorerRowStyle;
            }
        }

        public BTGraphIMGUI(BTTreeAsset asset) { _asset = asset; }

        public void SetAsset(BTTreeAsset asset)
        {
            _asset = asset;
            if (_asset != null)
            {
                var p = AssetDatabase.GetAssetPath(_asset);
                if (!string.IsNullOrEmpty(p)) { _selAssetPath = p; _collapsed.Remove("dir:" + Path.GetDirectoryName(p).Replace('\\', '/')); }
            }
            _selectedIds.Clear();
        }

        // ----------------------------------------------------------------

        public void DrawGUI()
        {
            if (_asset == null) return;
            var e = Event.current;

            var area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (area.width <= 1) return;
            _area = area;
            _rawMouse = e.mousePosition;

            // 三栏布局: 左=行为树资源 | 中=画布 | 右=Explorer(ROOT); 节点库=画布上浮动窗口
            float ph = Mathf.Max(1f, area.height - TOOLBAR_H - BOTTOM_H);
            _toolbarRect = new Rect(0, 0, area.width, TOOLBAR_H);
            _assetsRect = new Rect(0f, TOOLBAR_H, _showAssetsPanel ? LEFT_W : 0f, ph);
            _rightRect = new Rect(area.width - (_showLeftPanel ? LEFT_W : 0f), TOOLBAR_H, _showLeftPanel ? LEFT_W : 0f, ph);
            _panelRect = _rightRect;
            float canvasX = _showAssetsPanel ? LEFT_W : 0f;
            float canvasW = area.width - canvasX - (_showLeftPanel ? LEFT_W : 0f);
            _canvasRect = new Rect(canvasX, TOOLBAR_H, Mathf.Max(1f, canvasW), ph);
            if (_libRect.width <= 1f) _libRect = new Rect(canvasX + 20f, TOOLBAR_H + 20f, LEFT_W, 400f);
            _mouseCanvas = ScreenToCanvas(_rawMouse);
            _visibleCanvasRect = VisibleCanvasRect();   // 供连线/节点视口裁剪使用

            EnsureStyles();
            // 校验结果缓存: 图变更或间隔超时才重算(大行为树每帧全量校验会卡)。
            if (_graphDirty || (EditorApplication.timeSinceStartup - _lastValidateTime) > VALIDATE_INTERVAL)
            {
                _issues = BTValidator.Validate(_asset);
                _graphDirty = false;
                _lastValidateTime = EditorApplication.timeSinceStartup;
            }

            // ===== 1) 画布(最底层, 必须先画) =====
            // 工具栏/左右面板/底栏都在它之后绘制 => 永远盖在画布之上。
            // NodeCanvas 的结构就是"画布在底、面板永远盖在画布上"; 顺序反过来时,
            // 一旦裁剪在 IMGUIContainer 里没生效, 节点和它的下拉框就会直接画到面板上面。
            EditorGUI.DrawRect(_canvasRect, BG);

            // 网格(屏幕空间, 对应 GraphEditor.DrawGrid: black a0.15, GRID_SIZE=20)
            DrawGridScreen();

            // ===== 画布变换(对应 GraphEditor.cs:486-518 的 StartZoomArea / EndZoomArea) =====
            // 画布坐标 P -> 屏幕 = _canvasRect.min + (P - Pan) * Zoom。
            // 越界内容不做 GUIClip 裁剪, 而是靠"chrome 后画"覆盖(工具栏/左右面板/底栏/minimap 正好铺满画布之外)。
            var oldMatrix = BeginCanvasTransform();

            // 调试(Prefs → 调试/显示裁剪层): 在裁剪组内铺满超大区域,
            // 屏幕上能看到的洋红范围 == 实际生效的裁剪区。拖动时这块洋红若跟着跑, 就是裁剪区被矩阵带偏了。
            if (_debugClip)
                EditorGUI.DrawRect(new Rect(-100000f, -100000f, 200000f, 200000f), new Color(1f, 0f, 1f, 0.10f));

            // 连线(在节点之下)
            Handles.BeginGUI();
            foreach (var c in _asset.Connections)
                DrawConnection(c);
            // 进行中的连线预览(对应 Editor.Node: clickedPort 拖拽, Resting a0.8, size=3)
            if (_pendingSrcId.HasValue)
            {
                var src = _asset.Nodes.Find(x => x.NodeId == _pendingSrcId.Value);
                if (src != null)
                {
                    var from = OutputPortPos(src, _pendingSrcPort);
                    var col = BTEditorStyles.StatusResting;
                    float tx = Mathf.Max(Mathf.Abs(from.x - _mouseCanvas.x) * _rigidity, 25f);
                    var fromT = new Vector2(tx, 0);
                    var toT = new Vector2(-tx, 0);
                    var shadow = new Vector2(3.5f, 3.5f);
                    Handles.DrawBezier(from + shadow, _mouseCanvas + shadow,
                        from + shadow + fromT + shadow, _mouseCanvas + shadow + toT,
                        new Color(0, 0, 0, 0.1f), BTEditorStyles.BezierTexture, CONN_SIZE + 10f);
                    Handles.DrawBezier(from, _mouseCanvas, from + fromT, _mouseCanvas + toT,
                        col, BTEditorStyles.BezierTexture, CONN_SIZE);
                    BTEditorStyles.DrawCircle(_mouseCanvas, 16f, col);
                }
            }
            // 框选矩形
            if (_boxSelecting)
            {
                var r = Rect.MinMaxRect(
                    Mathf.Min(_boxStart.x, _boxEnd.x), Mathf.Min(_boxStart.y, _boxEnd.y),
                    Mathf.Max(_boxStart.x, _boxEnd.x), Mathf.Max(_boxStart.y, _boxEnd.y));
                Handles.DrawSolidRectangleWithOutline(r, new Color(0.4f, 0.6f, 1f, 0.12f), new Color(0.4f, 0.6f, 1f, 0.85f));
            }
            Handles.EndGUI();

            // 节点(直接在画布坐标系里逐个绘制: 不再用 GUI.Window, 也不再逐节点嵌套 GUI.BeginGroup)
            // 视口裁剪: 仅绘制可视区内的节点, 越界节点直接跳过(大行为树每帧全量绘制会卡)
            for (int i = 0; i < _asset.Nodes.Count; i++)
            {
                var n = _asset.Nodes[i];
                if (!NodeRect(n).Overlaps(_visibleCanvasRect)) continue;
                DrawNode(n, e);
            }

            // Explorer Ping(对应 GraphEditor.PingElement: 悬停行时画布高亮该节点, 1s 淡出)
            DrawPing();

            // 结束画布变换(对应 GraphEditor.EndZoomArea: 还原矩阵)
            EndCanvasTransform(oldMatrix);

            // 调试: 画布矩形边框 + 数值标注(屏幕空间)
            DrawClipDebug();

            // 画布边界(让"裁剪层 = 画布区域"一目了然, 恒固定不随平移移动)
            DrawCanvasBorder();

            // minimap(屏幕空间, 画布右下, 对应 GraphEditor.DrawMinimap)
            // 必须在输入处理之前绘制: HandleViewInput 依赖本帧算出的 _miniRect
            DrawMiniMap(e);

            // ===== 2) Chrome: 工具栏 / 左右面板 / 底栏(永远盖在画布之上) =====
            DrawToolbar();
            if (_showAssetsPanel) DrawAssetsPanel(e, _assetsRect);   // 左: 行为树资源
            if (_showLeftPanel) DrawLeftPanel(e, _rightRect);        // 右: Explorer(ROOT) + 黑板 + 运行时
            DrawBottomBar(e, new Rect(0, area.height - BOTTOM_H, area.width, BOTTOM_H));

            // 输入(命中判定只看坐标, 与绘制顺序无关; 面板/minimap 矩形此时均已就绪)
            HandleLibDrag(e);
            HandleAssetRowDrag(e);
            HandleViewInput(e);

            // 浮动的节点库面板(置顶, 可拖动)
            if (_showLibPanel) DrawLibWindow(e);

            // 拖拽幽灵(节点库 / 资源列表)
            DrawLibDragGhost();
            DrawTreeDragGhost();
        }

        // 画布边界(对应 NC 末尾的 Styles.Draw(canvasRect, canvasBorders))
        private void DrawCanvasBorder()
        {
            var c = new Color(1f, 1f, 1f, 0.14f);
            EditorGUI.DrawRect(new Rect(_canvasRect.xMin, _canvasRect.yMin, _canvasRect.width, 1f), c);
            EditorGUI.DrawRect(new Rect(_canvasRect.xMin, _canvasRect.yMax - 1f, _canvasRect.width, 1f), c);
            EditorGUI.DrawRect(new Rect(_canvasRect.xMin, _canvasRect.yMin, 1f, _canvasRect.height), c);
            EditorGUI.DrawRect(new Rect(_canvasRect.xMax - 1f, _canvasRect.yMin, 1f, _canvasRect.height), c);
        }

        // ================================================================
        //  画布变换(对应 GraphEditor.StartZoomArea / EndZoomArea)
        //  返回进入前的 GUI.matrix, 调用方必须配对调用 EndCanvasTransform。
        //
        //  为什么这里【不使用】GUI.BeginClip / GUI.BeginGroup 做裁剪:
        //  它们的"裁剪矩形"和"内容原点"由同一个 rect 决定:
        //      clipScreen           = M(rect)
        //      local L 的屏幕位置    = M(rect.min + L) - scrollOffset
        //  要同时满足"裁剪区恒等于 _canvasRect" 与 "内容随 Pan 平移", 必须靠 scrollOffset 打破耦合,
        //  而 scrollOffset 按屏幕像素还是局部单位解释, 各 Unity 版本不一致(差一个 zoom 倍率)。
        //  实测: 把 pan 编码进 rect.min 会让内容被钉死(Pan 被 rect.min 正好抵消) —— 就是上一版的 bug。
        //
        //  改为: 不做 GUIClip 裁剪, 靠绘制顺序保证越界内容不可见 ——
        //  画布最先画, 工具栏 / 左右面板 / 底栏 / minimap 全部在它之后, 正好铺满画布之外的所有区域。
        //  与 NodeCanvas 顺序一致: DrawMinimap → StartBreadCrumbNavigation → ShowToolbar → ShowPanels。
        // ================================================================
        private Matrix4x4 BeginCanvasTransform()
        {
            var oldMatrix = GUI.matrix;

            // 变换: 画布坐标 P -> 屏幕 = _canvasRect.min + (P - Pan) * Zoom(与 ScreenToCanvas 完全同一套约定)
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(_canvasRect.x - Pan.x * Zoom, _canvasRect.y - Pan.y * Zoom, 0f),
                Quaternion.identity, new Vector3(Zoom, Zoom, 1f));
            return oldMatrix;
        }

        private void EndCanvasTransform(Matrix4x4 oldMatrix)
        {
            GUI.matrix = oldMatrix;
        }

        // ================================================================
        //  调试: 裁剪层可视化(对应 Prefs → 调试/显示裁剪层, 默认开)
        //   洋红半透明铺底 = 在画布变换内铺满超大区域 =>
        //        最终屏幕上看得到的洋红范围 == 画布可视区(越界部分被工具栏/面板/底栏覆盖)
        //   青绿 3px 边框 = 固定的画布矩形 _canvasRect
        //   红 / 蓝十字   = 画布原点 (0,0) 的 x / y 轴(核对缩放与平移)
        //  中键拖动时判读:
        //    网格 + 节点 + 红蓝十字 一起动, 洋红范围和青绿边框都不动 => 正确
        //    洋红范围 / 青绿边框跟着动 => _canvasRect 被改坏了
        // ================================================================
        private void DrawClipDebug()
        {
            if (!_debugClip) return;

            // 画布原点十字(在裁剪组内按画布坐标绘制)
            var oldMatrix = BeginCanvasTransform();
            EditorGUI.DrawRect(new Rect(-4000f, -1f, 8000f, 2f), new Color(1f, 0.32f, 0.2f, 0.9f));  // 画布 y = 0
            EditorGUI.DrawRect(new Rect(-1f, -4000f, 2f, 8000f), new Color(0.35f, 0.62f, 1f, 0.9f));  // 画布 x = 0
            EndCanvasTransform(oldMatrix);

            // 画布矩形(固定)边框
            var lime = new Color(0.35f, 1f, 0.2f, 0.95f);
            var r = _canvasRect;
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, r.width, 3f), lime);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMax - 3f, r.width, 3f), lime);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, 3f, r.height), lime);
            EditorGUI.DrawRect(new Rect(r.xMax - 3f, r.yMin, 3f, r.height), lime);

            // 数值标注
            GUI.Label(new Rect(r.xMin + 8f, r.yMin + 4f, r.width - 16f, 18f),
                "<b>CLIP</b> 画布矩形=(" + r.x.ToString("0") + "," + r.y.ToString("0") + "," +
                r.width.ToString("0") + "x" + r.height.ToString("0") + ")   pan=(" +
                Pan.x.ToString("0") + "," + Pan.y.ToString("0") + ")   zoom=" + Zoom.ToString("0.00"), _dbgLabel);
        }

        private void EnsureStyles()
        {
            if (_libLabel != null) return;
            _libLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.85f, 0.85f, 0.9f) }, fontSize = 11, richText = true };
            _headLabel = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.92f, 0.92f, 1f) }, fontSize = 11, richText = true };
            _subLabel = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.7f, 0.95f) }, fontSize = 10, richText = true };
            _dbgLabel = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.7f, 1f, 0.45f) }, fontSize = 11, richText = true };
            _okLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.5f, 0.85f, 0.45f) }, fontSize = 11 };
            _warnLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.95f, 0.8f, 0.4f) }, fontSize = 11 };
            _errLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.95f, 0.5f, 0.5f) }, fontSize = 11 };
        }

        // ---- 坐标 ----
        private Vector2 ScreenToCanvas(Vector2 screen) => (screen - _canvasRect.min) / Zoom + Pan;
        private Vector2 CanvasCenter() => Pan + _canvasRect.size / (2f * Zoom);

        // ---- 视图输入: 缩放 / 平移 / 选择 / 连线 / 菜单 / 拖放 ----
        private void HandleViewInput(Event e)
        {
            // 资源列表内联改名进行中 / 正在拖动资源行: 画布快捷键与手势全部让位
            if (_renameTarget != null || _assetRowDragging) return;

            // 手势一旦开始(平移/框选/拖节点/拉线), 就由画布继续接管到松手为止 ——
            // 否则鼠标一移出画布边缘, 手势会中途断掉并残留状态(表现为"拖到一半卡住")。
            bool gestureActive = _panning || _boxSelecting || _draggingNode || _pendingSrcId.HasValue;

            // 浮动节点库面板拦截(它在画布上, 避免误触发框选/缩放/拖动); 手势进行中不拦截
            if (!gestureActive && _showLibPanel && _libRect.Contains(_rawMouse)) return;

            // minimap 导航优先
            if (!gestureActive && _miniRect.Contains(_rawMouse))
            {
                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
                { NavigateMiniMap(_rawMouse); e.Use(); }
                return;
            }
            if (!gestureActive && !_canvasRect.Contains(_rawMouse)) return; // 面板/底栏自行处理

            // 节点库拖放(来自左栏的 BTDragPayload)
            if (e.type == EventType.DragUpdated)
            {
                if (DragAndDrop.GetGenericData("BTNodeLib") is BTDragPayload)
                { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.Use(); }
                return;
            }
            if (e.type == EventType.DragPerform)
            {
                if (DragAndDrop.GetGenericData("BTNodeLib") is BTDragPayload p)
                {
                    DragAndDrop.AcceptDrag();
                    AddNodeFromPayload(p, ScreenToCanvas(_rawMouse));
                    e.Use();
                }
                return;
            }

            // 缩放
            if (e.type == EventType.ScrollWheel)
            {
                var before = ScreenToCanvas(_rawMouse);
                Zoom = Mathf.Clamp(Zoom * (e.delta.y < 0 ? 1.1f : 0.9f), 0.25f, 2.5f);
                var after = ScreenToCanvas(_rawMouse);
                Pan += before - after; // 以鼠标为中心缩放
                OnRepaint?.Invoke();
                e.Use();
                return;
            }

            bool wantPan = e.button == 2 || (e.button == 0 && (e.alt || e.shift));

            // 右键菜单(节点上 / 空白处)
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                int idx = NodeAt(_mouseCanvas);
                var menu = new GenericMenu();
                if (idx >= 0) BuildNodeMenu(menu, _asset.Nodes[idx].NodeId, _mouseCanvas);
                else BuildAddMenu(menu, _mouseCanvas);
                menu.ShowAsContext();
                e.Use();
                return;
            }

            // 端口连线结束
            if (_pendingSrcId.HasValue && e.type == EventType.MouseUp && e.button == 0)
            {
                EndPendingConnect(ScreenToCanvas(_rawMouse));
                e.Use();
                return;
            }

            // 节点: 端口拖线 / 选中并拖动(左键按下, 命中节点)
            if (e.type == EventType.MouseDown && e.button == 0 && !wantPan)
            {
                int idx = NodeAt(_mouseCanvas);
                if (idx >= 0)
                {
                    var n = _asset.Nodes[idx];
                    int oc0 = OutCount(n);
                    for (int i = 0; i < oc0; i++)
                    {
                        if (Vector2.Distance(_mouseCanvas, OutputPortPos(n, i)) < PORT_HIT)
                        {
                            _pendingSrcId = n.NodeId; _pendingSrcPort = i;
                            e.Use(); return;
                        }
                    }
                    // 选中(shift 追加)
                    if (!e.shift && !_selectedIds.Contains(n.NodeId)) _selectedIds.Clear();
                    _selectedIds.Add(n.NodeId);
                    OnRepaint?.Invoke();

                    // 仅在"头部"按下才进入拖拽(节点体留给 inline 参数控件)
                    var nr = NodeRect(n);
                    if (_mouseCanvas.y <= nr.y + HEADER_H)
                    {
                        _dragStartCanvas = _mouseCanvas;
                        _dragOrigins.Clear();
                        foreach (var id in _selectedIds)
                        {
                            var s = _asset.Nodes.Find(x => x.NodeId == id);
                            if (s != null) _dragOrigins[id] = s.Position;
                        }
                        RecordUndo("BT: 移动节点");
                        _draggingNode = true;
                        e.Use(); return;
                    }
                    return; // 身体点击: 不消费事件, 交给窗口内参数控件
                }
            }
            if (e.type == EventType.MouseDrag && _draggingNode)
            {
                var delta = _mouseCanvas - _dragStartCanvas;
                foreach (var kv in _dragOrigins)
                {
                    var nd = _asset.Nodes.Find(x => x.NodeId == kv.Key);
                    if (nd == null) continue;
                    var np = kv.Value + delta;
                    if (_snapGrid) { np.x = Mathf.Round(np.x / GRID) * GRID; np.y = Mathf.Round(np.y / GRID) * GRID; }
                    nd.Position = np;
                }
                MarkDirty(false);
                e.Use(); return;
            }
            if (e.type == EventType.MouseUp && _draggingNode)
            {
                _draggingNode = false; _dragOrigins.Clear();
                MarkDirty(true);
                e.Use(); return;
            }

            // 框选(空白处左键拖拽)
            if (e.type == EventType.MouseDown && e.button == 0 && !wantPan && NodeAt(_mouseCanvas) < 0)
            {
                _boxSelecting = true;
                _boxStart = _boxEnd = _mouseCanvas;
                if (!e.shift) _selectedIds.Clear();
                e.Use();
                return;
            }
            if (e.type == EventType.MouseDrag && _boxSelecting)
            {
                _boxEnd = ScreenToCanvas(_rawMouse);
                e.Use();
                return;
            }
            if (e.type == EventType.MouseUp && _boxSelecting)
            {
                _boxSelecting = false;
                var r = Rect.MinMaxRect(
                    Mathf.Min(_boxStart.x, _boxEnd.x), Mathf.Min(_boxStart.y, _boxEnd.y),
                    Mathf.Max(_boxStart.x, _boxEnd.x), Mathf.Max(_boxStart.y, _boxEnd.y));
                foreach (var n in _asset.Nodes)
                    if (NodeRect(n).Overlaps(r)) _selectedIds.Add(n.NodeId);
                e.Use();
                return;
            }

            // 键盘: 删除 / 复制 / 粘贴(文本框聚焦时不拦截)
            if (e.type == EventType.KeyDown)
            {
                if (GUIUtility.keyboardControl != 0) return;
                if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                {
                    if (_selectedIds.Count > 0)
                    {
                        foreach (var id in new List<long>(_selectedIds)) DeleteNode(id);
                        e.Use();
                    }
                }
                else if (e.keyCode == KeyCode.C && (e.control || e.command))
                {
                    _clipboard.Clear();
                    foreach (var id in _selectedIds)
                    {
                        var nd = _asset.Nodes.Find(x => x.NodeId == id);
                        if (nd != null) _clipboard.Add(nd);
                    }
                    e.Use();
                }
                else if (e.keyCode == KeyCode.V && (e.control || e.command))
                {
                    if (_clipboard.Count > 0) { PasteSelected(); e.Use(); }
                }
            }

            // 平移
            if (e.type == EventType.MouseDown && wantPan)
            {
                _panning = true; _panStartMouse = e.mousePosition; _panStartPan = Pan; e.Use();
            }
            else if (e.type == EventType.MouseDrag && _panning)
            {
                // 中键拖动: 内容跟随鼠标(拖右则内容右移 => Pan 减小)
                Pan = _panStartPan - (e.mousePosition - _panStartMouse) / Zoom;
                OnRepaint?.Invoke();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _panning)
            {
                _panning = false; e.Use();
            }
        }

        // ---- 网格(对应 GraphEditor.DrawGrid: black a0.15, GRID_SIZE=20) ----
        private void DrawGridScreen()
        {
            if (!_showGrid) return;                        // Prefs.showGrid
            if (Event.current.type != EventType.Repaint) return;
            float gridSize = Zoom > 0.5f ? GRID : GRID * 5f;   // GRID_SIZE / (×5)
            float step = gridSize * Zoom;
            Handles.BeginGUI();
            // 对应 GraphEditor.DrawGrid: black a0.15
            Handles.color = new Color(0f, 0f, 0f, 0.15f);
            float xDiff = Mathf.Repeat(-Pan.x * Zoom, step);
            for (float i = _canvasRect.xMin + xDiff; i < _canvasRect.xMax; i += step)
                if (i > _canvasRect.xMin)
                    Handles.DrawLine(new Vector3(i, _canvasRect.yMin, 0), new Vector3(i, _canvasRect.yMax, 0));
            float yDiff = Mathf.Repeat(-Pan.y * Zoom, step);
            for (float i = _canvasRect.yMin + yDiff; i < _canvasRect.yMax; i += step)
                if (i > _canvasRect.yMin)
                    Handles.DrawLine(new Vector3(_canvasRect.xMin, i, 0), new Vector3(_canvasRect.xMax, i, 0));
            Handles.color = Color.white;
            Handles.EndGUI();
        }

        // ---- 节点绘制(对应 Editor.Node.DrawNodeWindow) ----
        // 关键1: 不用 GUI.Window —— 在 IMGUIContainer + GUI.matrix 下 GUI.Window 的内容不渲染(只看到阴影)。
        // 关键2: 也不再逐节点嵌套 GUI.BeginGroup —— 画布已有裁剪区, 嵌套裁剪会在缩放矩阵下产生"画布中缝"。
        //        现在直接按画布绝对坐标绘制。
        private void DrawNode(BTNodeData n, Event e)
        {
            var r = NodeRect(n);
            float w = r.width, h = r.height;
            bool selected = _selectedIds.Contains(n.NodeId);
            var cat = BTEditorStyles.CategoryColor(n.Type);

            // 阴影(对应 windowShadow, 画布坐标)
            BTEditorStyles.DrawNodeShadow(r);

            // 注: 这里不再逐个节点嵌套 GUI.BeginGroup —— 画布本身已有一个裁剪组,
            // 在缩放矩阵下再套一层嵌套裁剪, 就是"画布中间出现一道裁剪缝"的来源。
            // 现在直接在画布坐标系里按绝对矩形绘制(与原版在窗口内绘制等价)。

            // 身体(对应 StyleSheet.window): DrawRect 打底 + GUI.Box 9-slice 圆角纹理
            EditorGUI.DrawRect(new Rect(r.x + 1f, r.y + 1f, w - 2f, h - 2f), BTEditorStyles.NodeBody);
            GUI.Box(r, GUIContent.none, BTEditorStyles.NodeBodyStyle);

            // 头部(对应 GUI.color = nodeColor; Styles.Draw(rect, windowHeader))
            var hr = new Rect(r.x, r.y, w, HEADER_H);
            EditorGUI.DrawRect(new Rect(r.x + 1f, r.y, w - 2f, HEADER_H), cat);
            GUI.color = cat;
            GUI.Box(hr, GUIContent.none, BTEditorStyles.NodeHeaderStyle);
            GUI.color = Color.white;

            // 标题(对应 windowTitle: 居中 12px 粗体, 文字色随头部明暗)
            var ts = BTEditorStyles.NodeTitleStyle;
            ts.normal.textColor = BTEditorStyles.NodeTextColor(cat);
            GUI.Label(hr, "<b>" + ResolveTitle(n) + "</b>", ts);

            // 节点上 inline 参数(给定节点矩形, 内部按画布坐标绘制)
            DrawNodeParams(n, r);

            // 端口 —— 严格对应 Editor.Node(Horizontal flow):
            //   输入: (rect.xMin,          rect.center.y)
            //   输出: (rect.xMax + portOffset, rect.center.y)   portOffset = 6
            // 每个节点只有 1 个输出端口, 所有子连线共用同一个点(不是"一条连线一个端口")
            if (IsInput(n.Type))
                DrawPortVisual(r.xMin, r.center.y, IsConnectedIn(n));
            if (OutCount(n) > 0)
                DrawPortVisual(r.xMax + PORT_OUT_OFFSET, r.center.y, IsConnectedOut(n));

            // 选中高亮(对应 windowHighlight, Resting 浅蓝描边)
            if (selected)
            {
                var hl = BTEditorStyles.StatusResting;
                EditorGUI.DrawRect(new Rect(r.x, r.y, w, 2), hl);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, w, 2), hl);
                EditorGUI.DrawRect(new Rect(r.x, r.y, 2, h), hl);
                EditorGUI.DrawRect(new Rect(r.xMax - 2, r.y, 2, h), hl);
            }
        }

        private bool IsConnectedIn(BTNodeData n) => _asset.Connections.Any(c => c.TargetNodeId == n.NodeId);
        private bool IsConnectedOut(BTNodeData n) => _asset.Connections.Any(c => c.SourceNodeId == n.NodeId);

        private void DrawPortVisual(float x, float y, bool connected)
        {
            // 12x12 圆: 已连接=亮, 未连接=暗(对应 nodePortConnected / nodePortEmpty)
            var col = connected ? new Color(0.85f, 0.85f, 0.9f) : new Color(0.2f, 0.2f, 0.26f);
            BTEditorStyles.DrawCircle(new Vector2(x, y), PORT_SIZE, col);
        }

        // ---- 连线(对应 Editor.Connection.DrawConnectionGUI / DrawConnection) ----
        private void DrawConnection(BTConnectionData c)
        {
            var src = _asset.Nodes.Find(x => x.NodeId == c.SourceNodeId);
            var dst = _asset.Nodes.Find(x => x.NodeId == c.TargetNodeId);
            if (src == null || dst == null) return;
            // 视口裁剪: 两端节点都不在可视区内则跳过(连线绘制比节点便宜, 粗裁即可)
            if (!NodeRect(src).Overlaps(_visibleCanvasRect) && !NodeRect(dst).Overlaps(_visibleCanvasRect))
                return;

            var from = OutputPortPos(src, c.PortIndex);
            var to = InputPortPos(dst);

            // 切线(对应 CurveUtils.ResolveTangents, horizontal flow, connectionsMLT=0.8)
            float tx = Mathf.Max(Mathf.Abs(from.x - to.x) * _rigidity, 25f);
            var fromT = new Vector2(tx, 0);
            var toT = new Vector2(-tx, 0);

            // 高亮: 端点之一被选中 -> alpha=1, size+2(对应 NC highlight)
            // 非运行态默认色 = defaultColor = GetStatusColor(Resting) = 浅蓝(0.7,0.7,1,0.8)
            // 仅被禁用的连线才为 Grey(0.3); BT 无禁用态, 故默认浅蓝。
            bool highlight = _selectedIds.Contains(c.SourceNodeId) || _selectedIds.Contains(c.TargetNodeId);
            var color = BTEditorStyles.StatusResting;
            if (highlight) color.a = 1f;
            float size = highlight ? CONN_SIZE + 2f : CONN_SIZE;

            // 阴影(对应 NC: black a0.1, 偏移(3.5,3.5), size+10)
            var shadow = new Vector2(3.5f, 3.5f);
            Handles.DrawBezier(from + shadow, to + shadow,
                from + shadow + fromT + shadow, to + shadow + toT,
                new Color(0, 0, 0, 0.1f), BTEditorStyles.BezierTexture, size + 10f);
            // 主连线
            Handles.DrawBezier(from, to, from + fromT, to + toT, color, BTEditorStyles.BezierTexture, size);

            // 末端圆点(对应 TipConnectionStyle.Circle, 16px endRect)
            BTEditorStyles.DrawCircle(to, 16f, color);
        }

        // ---- 节点几何 ----
        private Rect NodeRect(BTNodeData n)
        {
            float h = NodeContentHeight(n);
            return new Rect(n.Position.x, n.Position.y, NODE_W, h);
        }

        /// <summary>当前可视区在画布坐标系下的矩形(用于视口裁剪)。</summary>
        private Rect VisibleCanvasRect()
        {
            var tl = ScreenToCanvas(_canvasRect.min);
            var br = ScreenToCanvas(_canvasRect.max);
            return Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
        }

        // 端口位置严格对应 Editor.Node(PlanarDirection.Horizontal):
        //   输入 = (rect.xMin, rect.center.y)
        //   输出 = (rect.xMax + portOffset, rect.center.y)   // portOffset = 6
        // 每个节点只有 1 个输出端口, 所有子连线共用同一个点。
        private Vector2 InputPortPos(BTNodeData n)
        {
            var r = NodeRect(n);
            return new Vector2(r.xMin, r.center.y);
        }

        private Vector2 OutputPortPos(BTNodeData n, int index)
        {
            var r = NodeRect(n);
            return new Vector2(r.xMax + PORT_OUT_OFFSET, r.center.y);
        }

        // 每个节点固定 1 个输出端口(对应 Editor.Node 的单个 outPort; End 无输出)
        private int OutCount(BTNodeData n) => n.Type == BTNodeType.End ? 0 : 1;

        private static bool IsInput(BTNodeType t) => t != BTNodeType.Root;
        private static bool IsDynamic(BTNodeType t) =>
            t == BTNodeType.Sequence || t == BTNodeType.Selector ||
            t == BTNodeType.RandomSelector || t == BTNodeType.RandomSequence ||
            t == BTNodeType.Parallel || t == BTNodeType.FlipSelector ||
            t == BTNodeType.UtilitySelector || t == BTNodeType.ProbabilitySelector ||
            t == BTNodeType.StepSequencer || t == BTNodeType.Switch ||
            t == BTNodeType.BinarySelector;

        private int NodeAt(Vector2 p)
        {
            for (int i = _asset.Nodes.Count - 1; i >= 0; i--)
                if (NodeRect(_asset.Nodes[i]).Contains(p)) return i;
            return -1;
        }

        private string ResolveTitle(BTNodeData n)
        {
            if (n.Type == BTNodeType.GameCustom && n.LongParams.Count > 0 &&
                BTGameNodeRegistry.TryGet(_asset.Kind, n.LongParams[0], out var info) && !string.IsNullOrEmpty(info.Name))
                return info.Name;
            return BTNodeCatalog.DisplayName(n.Type);
        }

        // ---- 节点上 inline 参数 ----
        private int ParamRowCount(BTNodeData n)
        {
            switch (n.Type)
            {
                case BTNodeType.Wait:
                case BTNodeType.CooldownGate:
                case BTNodeType.TimeLimit:
                case BTNodeType.Repeat:
                case BTNodeType.CheckBlackboard:
                case BTNodeType.SubTree:
                case BTNodeType.GameCustom:
                    return 1;
                case BTNodeType.Conditional:
                case BTNodeType.CheckDistance:
                    return 2;
                default:
                    return 0;
            }
        }

        private float NodeContentHeight(BTNodeData n)
        {
            int rows = ParamRowCount(n) + 1; // +1 备注
            // 端口在节点外(左右缘垂直中心), 不占节点高度; 但要保证最小高度, 让端口居中时看起来协调
            return Mathf.Max(HEADER_H + 8f + rows * 20f + 8f, 58f);
        }

        private void DrawNodeParams(BTNodeData n, Rect nr)
        {
            // 直接按画布坐标绘制(不再逐节点嵌套 GUI.BeginGroup, 避免嵌套裁剪产生"画布中缝")
            float px = nr.x;
            float w = nr.width;
            float y = nr.y + HEADER_H + 8f;
            float rh = 20f;
            float labelW = 52f;

            switch (n.Type)
            {
                case BTNodeType.Wait:
                case BTNodeType.CooldownGate:
                case BTNodeType.TimeLimit:
                    EnsureFloat(n, 0, 1f);
                    GUI.Label(new Rect(px + 4, y, labelW, rh), "秒");
                    n.FloatParams[0] = EditorGUI.FloatField(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), n.FloatParams[0]);
                    break;
                case BTNodeType.Repeat:
                    EnsureLong(n, 0, 3);
                    GUI.Label(new Rect(px + 4, y, labelW, rh), "次数");
                    n.LongParams[0] = EditorGUI.IntField(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), (int)n.LongParams[0]);
                    break;
                case BTNodeType.Conditional:
                    EnsureLong(n, 0, 0); EnsureLong(n, 1, 0);
                    GUI.Label(new Rect(px + 4, y, labelW, rh), "未满足");
                    n.LongParams[0] = EditorGUI.Popup(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), Mathf.Clamp((int)n.LongParams[0], 0, 2), new[] { "失败", "成功", "Optional" });
                    y += rh;
                    GUI.Label(new Rect(px + 4, y, labelW, rh), "dynamic");
                    n.LongParams[1] = EditorGUI.Popup(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), (int)n.LongParams[1], new[] { "否", "是" });
                    break;
                case BTNodeType.CheckDistance:
                    EnsureFloat(n, 0, 1f); EnsureLong(n, 1, 0);
                    GUI.Label(new Rect(px + 4, y, labelW, rh), "阈值");
                    n.FloatParams[0] = EditorGUI.FloatField(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), n.FloatParams[0]);
                    y += rh;
                    GUI.Label(new Rect(px + 4, y, labelW, rh), "比较");
                    n.LongParams[1] = EditorGUI.Popup(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), (int)n.LongParams[1], new[] { "小于", "大于" });
                    break;
                case BTNodeType.CheckBlackboard:
                    EnsureLong(n, 1, 0);
                    GUI.Label(new Rect(px + 4, y, labelW, rh), "期望");
                    n.LongParams[1] = EditorGUI.Popup(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), (int)n.LongParams[1], new[] { "False", "True" });
                    break;
                case BTNodeType.SubTree:
                    EnsureLong(n, 0, 0);
                    GUI.Label(new Rect(px + 4, y, labelW, rh), "子树");
                    var trees = BTTreeAsset.LoadAllTreeIds();
                    int cur = Array.IndexOf(trees.ids, n.LongParams[0]);
                    if (cur < 0) cur = 0;
                    int ns = EditorGUI.Popup(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), cur, trees.names);
                    if (ns >= 0 && ns < trees.ids.Length) n.LongParams[0] = trees.ids[ns];
                    break;
                case BTNodeType.GameCustom:
                    if (n.LongParams.Count == 0) n.LongParams.Add(0);
                    GUI.Label(new Rect(px + 4, y, labelW, rh), "子类型");
                    var opts = new List<string>();
                    var vals = new List<long>();
                    foreach (var t in BTCustomNodeScanner.AllNodeTypes)
                    {
                        var inst = (BTCustomNode)Activator.CreateInstance(t);
                        if (inst.TreeKind != _asset.Kind) continue;
                        opts.Add(inst.NodeName + " (#" + inst.SubType + ")");
                        vals.Add(inst.SubType);
                    }
                    if (opts.Count == 0)
                        GUI.Label(new Rect(px + 4 + labelW, y, w - labelW - 8, rh), "(无自定义节点)");
                    else
                    {
                        int c = vals.IndexOf(n.LongParams[0]);
                        if (c < 0) c = 0;
                        int picked = EditorGUI.Popup(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), c, opts.ToArray());
                        if (picked >= 0 && picked < vals.Count) n.LongParams[0] = vals[picked];
                    }
                    break;
                default:
                    break;
            }

            // 备注(始终可编辑)
            y = nr.y + HEADER_H + 8f + ParamRowCount(n) * rh;
            GUI.Label(new Rect(px + 4, y, labelW, rh), "备注");
            var note = EditorGUI.TextField(new Rect(px + 4 + labelW, y, w - labelW - 8, rh - 2), n.Note);
            if (note != n.Note) { RecordUndo("BT: 编辑备注"); n.Note = note; MarkDirty(false); }
        }

        // ================================================================
        //  顶部工具栏(对应 GraphEditor.Toolbar.ShowToolbar, 逐项移植)
        // ================================================================
        private void DrawToolbar()
        {
            var r = _toolbarRect;
            // 工具栏底(对应 EditorStyles.toolbar; 纯 GUI 绘制, 不使用 GUILayout, 避免 IMGUIContainer 布局组报错)
            EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin ? new Color(0.219f, 0.219f, 0.219f) : new Color(0.76f, 0.76f, 0.76f));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), new Color(0, 0, 0, 0.35f));

            float h = r.height - 2f;
            float y = r.y + 1f;
            float x = r.x + 4f;

            if (GUI.Button(new Rect(x, y, 50, h), "File", EditorStyles.toolbarDropDown)) BuildFileMenu().ShowAsContext();
            x += 50f;
            if (GUI.Button(new Rect(x, y, 50, h), "Edit", EditorStyles.toolbarDropDown)) BuildEditMenu().ShowAsContext();
            x += 50f;
            if (GUI.Button(new Rect(x, y, 50, h), "Prefs", EditorStyles.toolbarDropDown)) BuildPrefsMenu().ShowAsContext();
            x += 60f;

            if (GUI.Button(new Rect(x, y, 88, h), "Select Tree", EditorStyles.toolbarButton))
            {
                Selection.activeObject = _asset;
                EditorGUIUtility.PingObject(_asset);
            }
            x += 92f;

            // Explorer 开关(对应 NC 工具栏的 lens 按钮) —— 右侧 ROOT 层级树面板
            if (GUI.Button(new Rect(x, y, 88, h), _showLeftPanel ? "◀ Explorer" : "▶ Explorer", EditorStyles.toolbarButton))
            { _showLeftPanel = !_showLeftPanel; OnRepaint?.Invoke(); }
            x += 92f;

            // 左侧"行为树资源"面板开关
            if (GUI.Button(new Rect(x, y, 76, h), _showAssetsPanel ? "◧ 资源:开" : "◧ 资源:关", EditorStyles.toolbarButton))
            { _showAssetsPanel = !_showAssetsPanel; OnRepaint?.Invoke(); }
            x += 80f;

            // 浮动"节点库"面板开关
            if (GUI.Button(new Rect(x, y, 76, h), _showLibPanel ? "▣ 节点库:开" : "▣ 节点库:关", EditorStyles.toolbarButton))
            { _showLibPanel = !_showLibPanel; OnRepaint?.Invoke(); }

            // 右侧: 树名/来源(右上角) + 版本信息 + Lock(对应 NC 右侧)
            if (_crumbStyle == null)
                _crumbStyle = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleRight, fontSize = 11, wordWrap = false };
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.Label(new Rect(r.xMax - 640f, y, 240f, h),
                string.Format("<b>{0}</b> <color=#ff4d4d>(Asset Reference)</color>", _asset.name), _crumbStyle);
            GUI.color = new Color(1, 1, 1, 0.4f);
            GUI.Button(new Rect(r.xMax - 400f, y, 210f, h), "BehaviourTree @ HYC v1.00", EditorStyles.toolbarButton);
            GUI.color = Color.white;
            _locked = GUI.Toggle(new Rect(r.xMax - 180f, y, 60f, h), _locked, "Lock", EditorStyles.toolbarButton);
        }

        private GenericMenu BuildFileMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("新建行为树"), false, () => OnRequestNewTree?.Invoke());
            menu.AddItem(new GUIContent("导出 Blob 并注册运行时"), false, ExportBlobAndRegister);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("清空画布(删除全部节点)"), false, () =>
            {
                if (EditorUtility.DisplayDialog("清空画布", "将删除当前树的全部节点!\n确定?", "YES", "NO!"))
                { _asset.Nodes.Clear(); _asset.Connections.Clear(); _selectedIds.Clear(); MarkDirty(true); }
            });
            return menu;
        }

        private GenericMenu BuildEditMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("校验树"), false, () => { _issues = BTValidator.Validate(_asset); OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("全选节点"), false, () =>
            {
                _selectedIds.Clear();
                foreach (var n in _asset.Nodes) _selectedIds.Add(n.NodeId);
                OnRepaint?.Invoke();
            });
            menu.AddItem(new GUIContent("删除选中节点"), false, DeleteSelected);
            return menu;
        }

        private GenericMenu BuildPrefsMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("显示网格"), _showGrid, () => { _showGrid = !_showGrid; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("网格吸附"), _snapGrid, () => { _snapGrid = !_snapGrid; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("显示类型名"), _explorerShowTypeNames, () => { _explorerShowTypeNames = !_explorerShowTypeNames; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("显示左侧面板"), _showLeftPanel, () => { _showLeftPanel = !_showLeftPanel; OnRepaint?.Invoke(); });
            menu.AddSeparator("");
            // 调试: 裁剪层可视化(洋红=实际生效的裁剪区, 青绿边框=画布矩形, 红/蓝十字=画布原点)
            menu.AddItem(new GUIContent("调试/显示裁剪层"), _debugClip, () => { _debugClip = !_debugClip; OnRepaint?.Invoke(); });
            // 对应 NC Prefs 里的 Connection Style(connectionsMLT)
            menu.AddItem(new GUIContent("连线样式/Hard"), Mathf.Approximately(_rigidity, 1f), () => { _rigidity = 1f; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("连线样式/Soft"), Mathf.Approximately(_rigidity, 0.75f), () => { _rigidity = 0.75f; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("连线样式/Softer"), Mathf.Approximately(_rigidity, 0.5f), () => { _rigidity = 0.5f; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("连线样式/Direct"), Mathf.Approximately(_rigidity, 0f), () => { _rigidity = 0f; OnRepaint?.Invoke(); });
            return menu;
        }

        // 面包屑(对应 GraphEditor.DoBreadCrumbNavigationStep: 树名 + 来源; 按要求放画布右上角)
        private GUIStyle _crumbStyle;
        private void DrawBreadcrumb()
        {
            if (_crumbStyle == null)
                _crumbStyle = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.UpperRight, fontSize = 9, wordWrap = false };
            var info = "<color=#ff4d4d>(Asset Reference)</color>";
            GUI.color = new Color(1, 1, 1, 0.5f);
            GUI.Label(new Rect(_canvasRect.xMin + 10, _canvasRect.yMin + 5, _canvasRect.width - 24, 30),
                string.Format("<b><size=22>{0} {1}</size></b>", _asset.name, info), _crumbStyle);
            GUI.color = Color.white;
        }

        // Blob 导出 + 注册(工具栏 File 菜单)
        private void ExportBlobAndRegister()
        {
            if (BTBlobBuilder.Build(_asset, out var blob))
            {
                BTManager.Register(_asset.TreeId, blob);
                Debug.Log(string.Format("[BT] 已构建并注册 Blob: treeId={0}", _asset.TreeId));
            }
            else
                Debug.LogWarning(string.Format("[BT] Blob 构建失败: treeId={0}", _asset.TreeId));
        }

        // ================================================================
        //  左栏 = NodeCanvas GraphExplorer 完整移植 + HYC 专属区
        //  源码对应: GraphExplorer.cs(搜索/类型名开关/ROOT/缩进树/树连接线/
        //  hover Ping/点击聚焦/选中高亮/根级分隔线) + EditorUtils(SearchField/Separator/BoldSeparator)
        // ================================================================
        private void DrawLeftPanel(Event e, Rect panel)
        {
            EditorGUI.DrawRect(panel, new Color(0.12f, 0.12f, 0.14f));
            // 与画布相邻的一侧画分界线(面板在右时线在左缘)
            float edgeX = _panelOnRight ? panel.x : panel.xMax - 1f;
            EditorGUI.DrawRect(new Rect(edgeX, panel.y, 1, panel.height), new Color(0.25f, 0.25f, 0.3f));

            float pad = 6f;
            float x = panel.x + pad;
            float w = panel.width - pad * 2f - 10f;   // 预留滚动条
            float y = panel.y + 6f;

            // ---- Explorer 头(固定, 不随内容滚动; 对应 GraphExplorer.OnGUI: HelpBox + 搜索 + 类型名开关) ----
            var helpRect = new Rect(x, y, w, 32f);
            EditorGUI.HelpBox(helpRect, "树结构层级列表: 搜索 / 定位。悬停 Ping 节点, 点击聚焦。", MessageType.Info);
            y += helpRect.height + 4f;

            GUI.SetNextControlName("BTExplorerSearch");
            _explorerSearch = EditorGUI.TextField(new Rect(x, y, Mathf.Max(40f, w - 100f), 16f), _explorerSearch, (GUIStyle)"ToolbarSearchTextField");
            _explorerShowTypeNames = EditorGUI.ToggleLeft(new Rect(x + w - 96f, y, 96f, 16f), "显示类型名", _explorerShowTypeNames);
            y += 20f;

            BoldSeparator(x, y, w); y += 16f;
            GUI.Label(new Rect(x, y, w, 20f), "<size=12><b> ROOT</b></size>", _headLabel); y += 20f;
            Separator(x, y, w); y += 8f;

            // ---- 内容区(手动滚动) ----
            float contentTop = y;
            _explorerRows.Clear();
            CollectExplorerRows(BuildExplorerTree(), INDENT_START, -1);

            float viewH = panel.yMax - contentTop;
            float contentH = ComputeLeftContentHeight();
            float maxScroll = Mathf.Max(0f, contentH - viewH);
            if (e.type == EventType.ScrollWheel && panel.Contains(_rawMouse))
            {
                _leftScroll.y = Mathf.Clamp(_leftScroll.y + e.delta.y * 14f, 0f, maxScroll);
                e.Use();
            }
            _leftScroll.y = Mathf.Clamp(_leftScroll.y, 0f, maxScroll);
            float scroll = _leftScroll.y;

            // 逐行绘制 Explorer 树(行高 18, y 顺序递增; 命中用容器坐标 _rawMouse)
            float ry = contentTop - scroll;
            for (int i = 0; i < _explorerRows.Count; i++)
            {
                var row = _explorerRows[i];
                row.Rect = new Rect(x, ry, w, ROW_H);

                if (row.Rect.yMax > contentTop && row.Rect.y < panel.yMax - 2f)
                {
                    GUI.color = new Color(0, 0, 0, row.Indent == INDENT_START ? 0.6f : 0.3f);
                    GUI.Box(row.Rect, string.Empty, (GUIStyle)"box");
                    GUI.color = Color.white;

                    var displayText = string.Format("<size=9><b>{0}</b>{1}</size>", ResolveTitle(row.Node),
                        _explorerShowTypeNames ? " (" + row.Node.Type + ")" : string.Empty);
                    GUI.Label(new Rect(row.Rect.x + row.Indent * INDENT_WIDTH, row.Rect.y,
                        Mathf.Max(10f, row.Rect.width - row.Indent * INDENT_WIDTH - 4f), row.Rect.height),
                        displayText, ExplorerRowStyle);
                }

                // 树连接线(竖 2px + 横 2px, 对应 GraphExplorer lineVer/lineHor)
                if (row.Parent >= 0 && row.Parent < i && row.Rect.yMax > contentTop && row.Rect.y < panel.yMax)
                {
                    var parentRect = _explorerRows[row.Parent].Rect;
                    float colX = row.Rect.xMin + (row.Indent * INDENT_WIDTH) - (INDENT_WIDTH / 2);
                    var lineVer = new Rect(colX, parentRect.yMax + 2f, 2f, (row.Rect.center.y - 2f) - (parentRect.yMax + 2f));
                    var lineHor = new Rect(colX, row.Rect.center.y - 1f, INDENT_WIDTH / 2f, 2f);
                    GUI.color = BTEditorStyles.Grey(EditorGUIUtility.isProSkin ? 0.6f : 0.3f);
                    if (lineVer.height > 0f) GUI.DrawTexture(lineVer, Texture2D.whiteTexture);
                    GUI.DrawTexture(lineHor, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }

                // 交互: 悬停 Ping / 点击聚焦 / 选中高亮
                if (row.Rect.yMax > contentTop && row.Rect.y < panel.yMax - 2f)
                {
                    EditorGUIUtility.AddCursorRect(row.Rect, MouseCursor.Link);
                    if (row.Rect.Contains(_rawMouse))
                    {
                        if (row.Node != (_lastHoverNode)) { _lastHoverNode = row.Node; PingNode(row.Node); }
                        GUI.color = new Color(0.5f, 0.5f, 1, 0.3f);
                        GUI.DrawTexture(row.Rect, EditorGUIUtility.whiteTexture);
                        GUI.color = Color.white;
                        if (e.type == EventType.MouseDown && e.button == 0) { FocusNode(row.Node); e.Use(); }
                    }
                    if (_selectedIds.Contains(row.Node.NodeId))
                    {
                        GUI.color = new Color(0.5f, 0.5f, 1, 0.1f);
                        GUI.DrawTexture(row.Rect, EditorGUIUtility.whiteTexture);
                        GUI.color = Color.white;
                    }
                }

                ry += ROW_H;
            }

            // 该面板其余区块: 黑板 + 运行时(行为树资源/节点库已拆到独立面板)
            ry = DrawBlackboardGUI(x, ry, w);
            ry = DrawRuntimeGUI(x, ry, w);

            if (maxScroll > 0f) _leftScroll.y = DrawVScrollbar(panel, viewH, contentH, maxScroll, _leftScroll.y, e);
        }

        // ================================================================
        //  左面板: 行为树资源(独立面板, 自己的滚动)
        // ================================================================
        private void DrawAssetsPanel(Event e, Rect panel)
        {
            EditorGUI.DrawRect(panel, new Color(0.12f, 0.12f, 0.14f));
            EditorGUI.DrawRect(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height), new Color(0.25f, 0.25f, 0.3f));

            float pad = 6f;
            float x = panel.x + pad;
            float w = panel.width - pad * 2f - 10f;
            float top = panel.y + 6f;

            // 滚动
            float viewH = panel.height - 12f;
            // 标题 + 新建按钮行 + 目标目录提示行 + 所有行
            float contentH = SEC_H + 4f + (LIB_ROW + 2f) * (1 + CountTreeAssetRows(BT_ROOT)) + 40f;
            float maxScroll = Mathf.Max(0f, contentH - viewH);
            if (e.type == EventType.ScrollWheel && panel.Contains(_rawMouse))
            {
                _assetsScroll.y = Mathf.Clamp(_assetsScroll.y + e.delta.y * 14f, 0f, maxScroll);
                e.Use();
            }

            // 拖到面板上下边缘 -> 自动滚动(Windows 文件列表行为), 否则深目录拖不上去
            if (_assetRowDragging && e.type == EventType.MouseDrag)
            {
                if (_rawMouse.y < panel.y + 24f) _assetsScroll.y -= 10f;
                else if (_rawMouse.y > panel.yMax - 24f) _assetsScroll.y += 10f;
            }
            _assetsScroll.y = Mathf.Clamp(_assetsScroll.y, 0f, maxScroll);

            // 从 Unity Project 窗口拖 .asset 进来
            HandleAssetsExternalDrop(e, panel);

            _treeRows.Clear();
            DrawTreeAssetsGUI(e, x, top - _assetsScroll.y, w);
            if (maxScroll > 0f) _assetsScroll.y = DrawVScrollbar(panel, viewH, contentH, maxScroll, _assetsScroll.y, e);
        }

        // ================================================================
        //  资源列表: 拖拽移动(手动实现, 只在面板内) + 外部拖入
        // ================================================================
        private void HandleAssetRowDrag(Event e)
        {
            if (_assetRowDragging)
            {
                if (e.type == EventType.MouseDrag)
                {
                    UpdateAssetDropDir();
                    e.Use();
                    OnRepaint?.Invoke();
                    return;
                }
                if (e.type == EventType.MouseUp)
                {
                    UpdateAssetDropDir();
                    MoveAssetTo(_assetDragPath, _assetDropDir ?? BT_ROOT);
                    _assetRowDragging = false;
                    _assetDragPath = null;
                    _assetDropDir = null;
                    _pendingRenamePath = null;
                    e.Use();
                    OnRepaint?.Invoke();
                    return;
                }
            }

            if (e.type == EventType.MouseDrag && _assetDragPath != null && !_assetRowDragging)
            {
                if ((_rawMouse - _assetDragStartMouse).magnitude > 4f) { _assetDragMoved = true; _assetRowDragging = true; e.Use(); }
                return;
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                // 没拖动 -> 判定"选中行上再单击一次" = 进入改名(与 Project 窗口一致)
                if (!_assetDragMoved && _pendingRenamePath != null && HitTreeRowPath() == _pendingRenamePath)
                    BeginRename(_pendingRenamePath);
                _pendingRenamePath = null;
                _assetDragPath = null;
                _assetDragMoved = false;
            }
        }

        private void HandleAssetsExternalDrop(Event e, Rect panel)
        {
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!panel.Contains(_rawMouse)) return;

            var paths = new List<string>();
            foreach (var p in DragAndDrop.paths)
                if (!string.IsNullOrEmpty(p) && AssetDatabase.LoadAssetAtPath<BTTreeAsset>(p) != null)
                    paths.Add(p.Replace('\\', '/'));
            if (paths.Count == 0) return;

            UpdateAssetDropDir();
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var dir = _assetDropDir ?? BT_ROOT;
                foreach (var p in paths) MoveAssetTo(p, dir);
                _assetDropDir = null;
            }
            e.Use();
            OnRepaint?.Invoke();
        }

        private void UpdateAssetDropDir()
        {
            var hit = HitTreeRowPath();
            _assetDropDir = hit != null
                ? (AssetDatabase.IsValidFolder(hit) ? hit : Path.GetDirectoryName(hit).Replace('\\', '/'))
                : BT_ROOT;
            if (!AssetDatabase.IsValidFolder(_assetDropDir)) _assetDropDir = BT_ROOT;
        }

        private string HitTreeRowPath()
        {
            for (int i = 0; i < _treeRows.Count; i++)
                if (_treeRows[i].Rect.Contains(_rawMouse)) return _treeRows[i].Path;
            return null;
        }

        // 实际执行移动(含环形检测 / 同名冲突 / 同目录短路)
        private void MoveAssetTo(string src, string dstDir)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dstDir)) return;
            src = src.Replace('\\', '/');
            dstDir = dstDir.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dstDir)) dstDir = BT_ROOT;

            var parent = Path.GetDirectoryName(src).Replace('\\', '/');
            if (parent == dstDir) return;                                   // 原地, 不动

            bool isFolder = AssetDatabase.IsValidFolder(src);
            // 不能把目录移进自己或自己的子孙
            if (isFolder && (dstDir == src || dstDir.StartsWith(src + "/", System.StringComparison.Ordinal)))
            {
                Debug.LogWarning("[BT] 不能把文件夹移动到它自己或其子文件夹里: " + src);
                return;
            }
            if (src == BT_ROOT) return;                                     // 根目录不可移动

            var dst = AssetDatabase.GenerateUniqueAssetPath(dstDir + "/" + Path.GetFileName(src));
            var err = AssetDatabase.MoveAsset(src, dst);
            if (!string.IsNullOrEmpty(err)) { Debug.LogWarning("[BT] 移动失败: " + err); return; }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _selAssetPath = dst;
            _collapsed.Remove("dir:" + dstDir);                             // 目标目录展开, 让用户看到结果
            OnRepaint?.Invoke();
        }

        private void DrawTreeDragGhost()
        {
            if (!_assetRowDragging || string.IsNullOrEmpty(_assetDragPath)) return;
            var r = new Rect(_rawMouse.x + 12f, _rawMouse.y - 10f, 160f, 22f);
            EditorGUI.DrawRect(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), new Color(0f, 0f, 0f, 0.30f));
            EditorGUI.DrawRect(r, new Color(0.17f, 0.17f, 0.21f, 0.96f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), new Color(0.45f, 0.62f, 0.95f));
            GUI.Label(new Rect(r.x + 6f, r.y, r.width - 12f, r.height),
                (AssetDatabase.IsValidFolder(_assetDragPath) ? "📁 " : "") + Path.GetFileName(_assetDragPath), _libLabel);
        }

        // ================================================================
        //  画布上浮动的节点库面板(标题栏可拖动)
        // ================================================================
        private void DrawLibWindow(Event e)
        {
            var r = _libRect;

            // 阴影 + 面板
            EditorGUI.DrawRect(new Rect(r.x + 3f, r.y + 3f, r.width, r.height), new Color(0f, 0f, 0f, 0.28f));
            EditorGUI.DrawRect(r, new Color(0.14f, 0.14f, 0.17f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), new Color(0.3f, 0.3f, 0.36f));

            // 标题栏(可拖动 + 折叠)
            var title = new Rect(r.x, r.y, r.width, LIB_ROW + 2f);
            EditorGUI.DrawRect(title, new Color(0.2f, 0.2f, 0.25f));
            GUI.Label(new Rect(title.x + 6f, title.y, title.width - 40f, title.height), "节点库（拖到画布）", _headLabel);
            if (GUI.Button(new Rect(title.xMax - 22f, title.y + 1f, 20f, title.height - 2f), "×")) { _showLibPanel = false; OnRepaint?.Invoke(); }

            EditorGUIUtility.AddCursorRect(title, MouseCursor.MoveArrow);
            if (e.type == EventType.MouseDown && e.button == 0 && title.Contains(_rawMouse))
            { _libWinDrag = true; _libWinDragOffset = _rawMouse - new Vector2(r.x, r.y); e.Use(); }
            else if (e.type == EventType.MouseDrag && _libWinDrag)
            { _libRect.position = _rawMouse - _libWinDragOffset; e.Use(); OnRepaint?.Invoke(); }
            else if (e.type == EventType.MouseUp && _libWinDrag) { _libWinDrag = false; e.Use(); }

            // 内容(滚动)
            float pad = 6f;
            float x = r.x + pad, w = r.width - pad * 2f - 10f;
            float contentTop = r.y + LIB_ROW + 4f;
            float viewH = r.yMax - contentTop - 4f;
            if (viewH <= 0f) return;

            float contentH = ComputeLibContentHeight();
            float maxScroll = Mathf.Max(0f, contentH - viewH);
            if (e.type == EventType.ScrollWheel && r.Contains(_rawMouse))
            {
                _libScroll.y = Mathf.Clamp(_libScroll.y + e.delta.y * 14f, 0f, maxScroll);
                e.Use();
            }
            _libScroll.y = Mathf.Clamp(_libScroll.y, 0f, maxScroll);

            // 裁剪内容区(组内局部坐标 + 命中偏移)
            GUI.BeginGroup(new Rect(r.x, contentTop, r.width, viewH));
            var oldOff = _drawOffset;
            try
            {
                _drawOffset = new Vector2(r.x, contentTop);
                DrawNodeLibraryGUI(e, pad, -_libScroll.y, w);
            }
            finally { _drawOffset = oldOff; GUI.EndGroup(); }

            if (maxScroll > 0f) _libScroll.y = DrawVScrollbar(new Rect(r.x, contentTop, r.width, viewH), viewH, contentH, maxScroll, _libScroll.y, e);
        }

        private float ComputeLibContentHeight()
        {
            float h = 2f * (LIB_ROW + 2f); // Root/End
            foreach (var cat in BTNodeCatalog.Categories)
            {
                h += SUB_H + 2f;
                if (!_collapsed.Contains("cat:" + cat)) h += BTNodeCatalog.ByCategory(cat).Count() * (LIB_ROW + 2f);
            }
            h += SUB_H + 2f;
            if (!_collapsed.Contains("cat:custom"))
            {
                foreach (var ty in BTCustomNodeScanner.AllNodeTypes)
                {
                    var inst = (BTCustomNode)Activator.CreateInstance(ty);
                    if (inst.TreeKind == _asset.Kind) h += LIB_ROW + 2f;
                }
                h += LIB_ROW + 6f;
            }
            return h + 10f;
        }

        // ---- Explorer 树: 收集可见行(对应 GraphExplorer.DoElement 的遍历; 手动布局, 不用 GUILayout) ----
        private void CollectExplorerRows(ExplorerEl element, int indent, int parentIndex)
        {
            if (element.Children == null) return;

            foreach (var child in element.Children)
            {
                if (child.Node == null) continue;

                var toString = ResolveTitle(child.Node);
                var typeName = child.Node.Type.ToString();
                var searchText = toString + " " + typeName;

                int myIndex = _explorerRows.Count;
                bool visible = string.IsNullOrEmpty(_explorerSearch) || SearchMatch(_explorerSearch, searchText);
                if (visible)
                    _explorerRows.Add(new ExplorerRow { Node = child.Node, Indent = indent, Parent = parentIndex });

                CollectExplorerRows(child, indent + 1, visible ? myIndex : parentIndex);
            }
        }

        // ---- 树构建(对应 Graph.GetFullMetaGraph: Root 优先; 不可达节点附加根级防丢失) ----
        private ExplorerEl BuildExplorerTree()
        {
            var root = new ExplorerEl();
            var visited = new HashSet<long>();
            var rootNode = _asset.Nodes.FirstOrDefault(n => n.Type == BTNodeType.Root);
            if (rootNode != null) root.Children.Add(BuildExplorerNode(rootNode, visited));
            foreach (var n in _asset.Nodes)
                if (!visited.Contains(n.NodeId))
                    root.Children.Add(BuildExplorerNode(n, visited));
            return root;
        }

        private ExplorerEl BuildExplorerNode(BTNodeData n, HashSet<long> visited)
        {
            visited.Add(n.NodeId);
            var el = new ExplorerEl { Node = n };
            foreach (var c in _asset.Connections.Where(c => c.SourceNodeId == n.NodeId).OrderBy(c => c.PortIndex))
            {
                var t = _asset.Nodes.FirstOrDefault(x => x.NodeId == c.TargetNodeId);
                if (t != null && !visited.Contains(t.NodeId)) // 防环(对应 targetNode.ID > node.ID)
                    el.Children.Add(BuildExplorerNode(t, visited));
            }
            return el;
        }

        private void PingNode(BTNodeData n)
        {
            _pingNodeId = n.NodeId;
            _pingTime = EditorApplication.timeSinceStartup;
            OnRepaint?.Invoke();
        }

        private void FocusNode(BTNodeData n)
        {
            _selectedIds.Clear();
            _selectedIds.Add(n.NodeId);
            var r = NodeRect(n);
            Pan = r.center - _canvasRect.size / (2f * Zoom); // 对应 GraphEditor.FocusElement 居中
            OnRepaint?.Invoke();
        }

        // ---- 画布 Ping 高亮(对应 GraphEditor.PingElement 淡出) ----
        private void DrawPing()
        {
            if (_pingNodeId == 0) return;
            float t = (float)(EditorApplication.timeSinceStartup - _pingTime);
            if (t > 1f) { _pingNodeId = 0; return; }
            var n = _asset.Nodes.FirstOrDefault(x => x.NodeId == _pingNodeId);
            if (n == null) return;
            var r = NodeRect(n);
            r.x -= 6; r.y -= 6; r.width += 12; r.height += 12;
            float a = 1f - t;
            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(r,
                new Color(0.5f, 0.5f, 1f, 0.05f * a), new Color(0.5f, 0.5f, 1f, a));
            Handles.EndGUI();
            OnRepaint?.Invoke(); // 淡出动画期间持续重绘
        }

        // ---- StringUtils.SearchMatch 移植(忽略大小写包含) ----
        private static bool SearchMatch(string search, string content)
        {
            if (string.IsNullOrEmpty(content)) return string.IsNullOrEmpty(search);
            return content.ToLower().Contains(search.ToLower());
        }

        // ---- EditorUtils.Separator / BoldSeparator 移植(固定矩形) ----
        private static void Separator(float x, float y, float w)
        {
            GUI.color = new Color(0, 0, 0, 0.3f);
            GUI.DrawTexture(new Rect(x, y, w, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static void BoldSeparator(float x, float y, float w)
        {
            GUI.color = new Color(0, 0, 0, 0.3f);
            GUI.DrawTexture(new Rect(x, y, w, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y, w, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y + 3f, w, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ---- ② 行为树资源(文件夹树 + 新建/改名/删除/建目录) ----
        private const string BT_ROOT = "Assets/BTTrees";
        private string _renameTarget;
        private string _renameBuffer;

        private float DrawTreeAssetsGUI(Event e, float x, float y, float w)
        {
            y = SectionHeader("trees", "行为树资源", x, y, w);
            if (_collapsed.Contains("trees")) return y;

            var createDir = CurrentCreateDir();
            var b1 = new Rect(x, y, w * 0.5f - 2f, LIB_ROW - 2f);
            var b2 = new Rect(x + w * 0.5f + 2f, y, w * 0.5f - 2f, LIB_ROW - 2f);
            if (GUI.Button(b1, "＋ 新建树")) CreateTreeIn(createDir);
            if (GUI.Button(b2, "＋ 新建目录")) CreateFolderIn(createDir);
            y += LIB_ROW;

            // 目标目录提示(新建落在"当前选中目录"内, 对应 Windows: 在哪个文件夹里就建在哪)
            var hint = createDir == BT_ROOT ? "BTTrees（根）" : Path.GetFileName(createDir);
            GUI.Label(new Rect(x, y, w, LIB_ROW - 4f), "新建于 ▸ " + hint, _libLabel);
            y += LIB_ROW - 2f;

            if (!AssetDatabase.IsValidFolder(BT_ROOT)) return y;

            y = CommitRenameIfNeeded(e, y);
            y = DrawDirNode(BT_ROOT, x, y, w, e, 0);
            return y + 6f;
        }

        // 新建目标目录: 选中目录本身 / 选中资产所在目录 / 根
        private string CurrentCreateDir()
        {
            if (!string.IsNullOrEmpty(_selAssetPath))
            {
                if (AssetDatabase.IsValidFolder(_selAssetPath)) return _selAssetPath.Replace('\\', '/');
                var d = Path.GetDirectoryName(_selAssetPath).Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(d)) return d;
            }
            return BT_ROOT;
        }

        // 目录树(递归): 子目录在前 + 目录内行为树(Windows 文件列表: 单击选中, 再单击改名, 可拖动移动)
        private float DrawDirNode(string dir, float x, float y, float w, Event e, int indent)
        {
            var subDirs = new List<string>(AssetDatabase.GetSubFolders(dir));
            subDirs.Sort();

            float ind = indent * 12f;
            float rx = x + ind;
            float rw = w - ind;

            foreach (var sub in subDirs)
            {
                bool collapsed = _collapsed.Contains("dir:" + sub);
                var r = new Rect(rx, y, rw, LIB_ROW);
                var arrow = new Rect(rx, y, 16f, LIB_ROW);
                _treeRows.Add(new TreeRow { Rect = r, Path = sub, IsFolder = true });

                DrawTreeRowBg(r, _selAssetPath == sub, false, _assetRowDragging && _assetDropDir == sub);

                if (_renameTarget == sub)
                {
                    var fr = new Rect(r.x + 16f, r.y + 1f, r.width - 20f, r.height - 2f);
                    _renameRect = fr;
                    DrawRenameField(fr);
                }
                else
                {
                    GUI.Label(arrow, collapsed ? "▸" : "▾", _libLabel);
                    GUI.Label(new Rect(r.x + 16f, r.y, r.width - 20f, r.height), "📁 " + Path.GetFileName(sub), _libLabel);
                }

                TreeRowInput(e, r, arrow, sub, true, null);
                y += LIB_ROW + 2f;
                if (!collapsed) y = DrawDirNode(sub, x, y, w, e, indent + 1);
            }

            // 目录内行为树
            var trees = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:BTTreeAsset", new[] { dir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetDirectoryName(p).Replace('\\', '/') == dir) trees.Add(p);
            }
            trees.Sort();

            foreach (var tp in trees)
            {
                var tree = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(tp);
                var r = new Rect(rx, y, rw, LIB_ROW);
                _treeRows.Add(new TreeRow { Rect = r, Path = tp, IsFolder = false });

                bool cur = tree != null && _asset != null && _asset.TreeId == tree.TreeId;   // 当前打开的树
                bool drop = _assetRowDragging && _assetDropDir == Path.GetDirectoryName(tp).Replace('\\', '/');
                DrawTreeRowBg(r, _selAssetPath == tp, cur, false);
                // 落点在"某个资产行"上 = 放进它所在的目录: 给该目录所有行一个淡蓝底
                if (drop) EditorGUI.DrawRect(r, new Color(0.20f, 0.30f, 0.48f, 0.85f));

                if (_renameTarget == tp)
                {
                    var fr = new Rect(r.x + 4f, r.y + 1f, r.width - 8f, r.height - 2f);
                    _renameRect = fr;
                    DrawRenameField(fr);
                }
                else
                {
                    GUI.Label(new Rect(r.x + 16f, r.y, r.width - 20f, r.height),
                        tree != null ? tree.TreeId + ": " + tree.name : Path.GetFileNameWithoutExtension(tp), _libLabel);
                }

                TreeRowInput(e, r, Rect.zero, tp, false, tree);
                y += LIB_ROW + 2f;
            }
            return y;
        }

        // 行底色: 落点 > 当前打开 > 选中 > 悬停 > 常态
        private void DrawTreeRowBg(Rect r, bool selected, bool current, bool dropHere)
        {
            bool hover = r.Contains(_rawMouse);
            Color c;
            if (dropHere) c = new Color(0.20f, 0.38f, 0.62f);
            else if (selected) c = new Color(0.24f, 0.36f, 0.55f);
            else if (current) c = new Color(0.26f, 0.34f, 0.46f);
            else if (hover) c = new Color(0.20f, 0.20f, 0.26f);
            else c = new Color(0.15f, 0.15f, 0.18f);
            EditorGUI.DrawRect(r, c);

            if (dropHere)
            {
                // 落点描边(Windows: 拖到文件夹上高亮)
                var lc = new Color(0.45f, 0.70f, 1f);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), lc);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), lc);
                EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), lc);
                EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), lc);
            }
            else if (selected)
                EditorGUI.DrawRect(new Rect(r.x, r.y, 2f, r.height), new Color(0.45f, 0.70f, 1f));
        }

        // 行鼠标事件: 三角=折叠 / 单击=选中 / 已选中再单击=改名 / 双击=打开 / 拖动=移动
        private void TreeRowInput(Event e, Rect row, Rect arrow, string path, bool isFolder, BTTreeAsset tree)
        {
            if (!row.Contains(_rawMouse)) return;
            bool overArrow = isFolder && arrow.Contains(_rawMouse);

            if (e.type != EventType.MouseDown) return;

            if (e.button == 1)
            {
                _selAssetPath = path;
                _selAssetClickTime = -10.0;
                _pendingRenamePath = null;
                if (isFolder) BuildFolderMenu(path).ShowAsContext();
                else BuildTreeAssetMenu(path, tree).ShowAsContext();
                e.Use();
                return;
            }
            if (e.button != 0) return;

            if (overArrow && _renameTarget != path)
            {
                ToggleCollapse(path);
                _selAssetPath = path;
                _selAssetClickTime = -10.0;
                _pendingRenamePath = null;
                e.Use();
                return;
            }
            if (_renameTarget == path) { e.Use(); return; }   // 编辑中, 交给 TextField

            double now = EditorApplication.timeSinceStartup;
            bool wasSelected = _selAssetPath == path;
            double dt = now - _selAssetClickTime;
            _selAssetClickTime = now;

            if (wasSelected && dt < DBL_CLICK)      // 双击: 打开树 / 展开折叠
            {
                _selAssetClickTime = -10.0;
                _pendingRenamePath = null;
                if (isFolder) ToggleCollapse(path);
                else if (tree != null) OnRequestOpenTree?.Invoke(tree);
                e.Use();
                return;
            }

            _selAssetPath = path;
            _pendingRenamePath = (wasSelected && dt >= DBL_CLICK) ? path : null;  // 抬起时进入改名
            _assetDragPath = path;
            _assetDragStartMouse = _rawMouse;
            _assetDragMoved = false;
            OnRepaint?.Invoke();
            e.Use();
        }

        private void ToggleCollapse(string dir)
        {
            string k = "dir:" + dir;
            if (_collapsed.Contains(k)) _collapsed.Remove(k); else _collapsed.Add(k);
        }

        private void BeginRename(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _renameTarget = path;
            _renameBuffer = AssetDatabase.IsValidFolder(path)
                ? Path.GetFileName(path)
                : Path.GetFileNameWithoutExtension(path);
            _renameSelectAll = true;
            OnRepaint?.Invoke();
        }

        // 内联改名框: 进入时聚焦并全选(与 Project 窗口一致)
        private void DrawRenameField(Rect r)
        {
            if (_renameSelectAll) GUI.FocusControl(RENAME_CTRL);
            GUI.SetNextControlName(RENAME_CTRL);
            _renameBuffer = EditorGUI.TextField(r, _renameBuffer);
            if (_renameSelectAll && GUI.GetNameOfFocusedControl() == RENAME_CTRL)
            {
                // TextEditor 在 UnityEngine 命名空间下(不是 UnityEditor), 用它做"进入改名即全选"
                var te = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
                if (te != null) te.SelectAll();
                _renameSelectAll = false;
            }
        }

        // 内联改名: Enter 提交 / Esc 取消 / 点别处提交(Windows 行为)
        private float CommitRenameIfNeeded(Event e, float y)
        {
            // F2: 对选中项改名(Project 窗口同款)
            if (_renameTarget == null && e.type == EventType.KeyDown && e.keyCode == KeyCode.F2
                && !string.IsNullOrEmpty(_selAssetPath))
            {
                BeginRename(_selAssetPath);
                e.Use();
                return y;
            }

            if (_renameTarget == null) return y;

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)                                   // 取消
                { _renameTarget = null; _renameSelectAll = false; e.Use(); return y; }
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) // 提交
                { CommitRename(); e.Use(); return y; }
            }
            else if (e.type == EventType.MouseDown && !_renameRect.Contains(_rawMouse))
            {
                CommitRename();                                                    // 点到别处 = 提交
            }
            return y;
        }

        private void CommitRename()
        {
            if (_renameTarget == null) return;
            var newName = (_renameBuffer ?? "").Trim();
            bool exists = AssetDatabase.IsValidFolder(_renameTarget)
                          || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_renameTarget) != null;

            if (newName.Length == 0 || !exists || newName == Path.GetFileNameWithoutExtension(_renameTarget)
                || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _renameTarget = null; _renameSelectAll = false; OnRepaint?.Invoke(); return;
            }

            var err = AssetDatabase.RenameAsset(_renameTarget, newName);
            if (string.IsNullOrEmpty(err))
            {
                bool folder = AssetDatabase.IsValidFolder(_renameTarget);
                var renamed = Path.GetDirectoryName(_renameTarget).Replace('\\', '/') + "/" + newName
                              + (folder ? "" : ".asset");
                _selAssetPath = renamed;
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            else Debug.LogWarning("[BT] 改名失败: " + err);

            _renameTarget = null;
            _renameSelectAll = false;
            OnRepaint?.Invoke();
        }

        private static void EnsureBTRoot()
        {
            if (!AssetDatabase.IsValidFolder(BT_ROOT)) AssetDatabase.CreateFolder("Assets", "BTTrees");
        }

        private void CreateTreeIn(string dir)
        {
            EnsureBTRoot();
            if (!AssetDatabase.IsValidFolder(dir)) dir = BT_ROOT;
            var tree = ScriptableObject.CreateInstance<BTTreeAsset>();
            tree.TreeId = DateTime.Now.Ticks % 100000;
            tree.name = "NewTree";
            var path = AssetDatabase.GenerateUniqueAssetPath(dir + "/NewTree.asset");
            AssetDatabase.CreateAsset(tree, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _collapsed.Remove("dir:" + dir);
            _selAssetPath = path;
            OnRequestOpenTree?.Invoke(tree);
            BeginRename(path);                       // Windows: 新建后立刻进入改名
        }

        private void CreateFolderIn(string dir)
        {
            EnsureBTRoot();
            if (!AssetDatabase.IsValidFolder(dir)) dir = BT_ROOT;
            var p = AssetDatabase.GenerateUniqueAssetPath(dir + "/NewFolder");
            AssetDatabase.CreateFolder(dir, Path.GetFileName(p));
            AssetDatabase.Refresh();
            _collapsed.Remove("dir:" + dir);
            _selAssetPath = p;
            BeginRename(p);
        }

        private GenericMenu BuildTreeAssetMenu(string path, BTTreeAsset tree)
        {
            var m = new GenericMenu();
            m.AddItem(new GUIContent("打开"), false, () => { if (tree != null) OnRequestOpenTree?.Invoke(tree); });
            m.AddItem(new GUIContent("重命名"), false, () => BeginRename(path));
            m.AddSeparator("");
            m.AddItem(new GUIContent("在 Project 窗口中显示"), false, () =>
            {
                var o = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (o != null) { EditorGUIUtility.PingObject(o); Selection.activeObject = o; }
            });
            m.AddItem(new GUIContent("删除"), false, () =>
            {
                if (EditorUtility.DisplayDialog("删除行为树", "删除 " + path + " ?", "删除", "取消"))
                {
                    AssetDatabase.DeleteAsset(path); AssetDatabase.Refresh();
                    if (_selAssetPath == path) _selAssetPath = null;
                    if (_renameTarget == path) _renameTarget = null;
                    OnRepaint?.Invoke();
                }
            });
            return m;
        }

        private GenericMenu BuildFolderMenu(string dir)
        {
            var m = new GenericMenu();
            m.AddItem(new GUIContent("新建行为树"), false, () => CreateTreeIn(dir));
            m.AddItem(new GUIContent("新建子文件夹"), false, () => CreateFolderIn(dir));
            m.AddItem(new GUIContent("重命名文件夹"), false, () => BeginRename(dir));
            m.AddSeparator("");
            m.AddItem(new GUIContent("在 Project 窗口中显示"), false, () =>
            {
                var o = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dir);
                if (o != null) { EditorGUIUtility.PingObject(o); Selection.activeObject = o; }
            });
            m.AddItem(new GUIContent("删除文件夹"), false, () =>
            {
                if (EditorUtility.DisplayDialog("删除文件夹", "删除 " + dir + " (含其中内容)?", "删除", "取消"))
                {
                    AssetDatabase.DeleteAsset(dir); AssetDatabase.Refresh();
                    if (_selAssetPath == dir) _selAssetPath = null;
                    if (_renameTarget == dir) _renameTarget = null;
                    OnRepaint?.Invoke();
                }
            });
            return m;
        }

        // ---- ③ 节点库(拖拽建节点 / 点击创建) ----
        private float DrawNodeLibraryGUI(Event e, float x, float y, float w)
        {
            y = SectionHeader("lib", "节点库(拖到画布 / 点击创建)", x, y, w);
            if (_collapsed.Contains("lib")) return y;
            y = LibItemRow(x, y, w, "开始 Root", new BTDragPayload { Type = BTNodeType.Root, Name = "开始" }, e);
            y = LibItemRow(x, y, w, "结束 End", new BTDragPayload { Type = BTNodeType.End, Name = "结束" }, e);
            foreach (var cat in BTNodeCatalog.Categories)
            {
                string catKey = "cat:" + cat;
                y = SectionSub(catKey, BTNodeCatalog.CategoryLabel(cat), x, y, w);
                if (_collapsed.Contains(catKey)) continue;
                foreach (var t in BTNodeCatalog.ByCategory(cat))
                    y = LibItemRow(x, y, w, BTNodeCatalog.DisplayName(t), new BTDragPayload { Type = t, Name = BTNodeCatalog.DisplayName(t) }, e);
            }
            // 游戏层自定义节点
            y = SectionSub("cat:custom", "游戏层(自定义)", x, y, w);
            if (!_collapsed.Contains("cat:custom"))
            {
                bool any = false;
                foreach (var ty in BTCustomNodeScanner.AllNodeTypes)
                {
                    var inst = (BTCustomNode)Activator.CreateInstance(ty);
                    if (inst.TreeKind != _asset.Kind) continue;
                    any = true;
                    y = LibItemRow(x, y, w, inst.NodeName,
                        new BTDragPayload { Type = BTNodeType.GameCustom, SubType = inst.SubType, IsCustom = true, Name = inst.NodeName }, e);
                }
                if (!any) { GUI.Label(new Rect(x + 6, y, w, LIB_ROW), "(无自定义节点)", _libLabel); y += LIB_ROW + 2f; }
                if (GUI.Button(new Rect(x, y, w, LIB_ROW), "＋ 新建自定义节点…")) BTNodeCreatorWindow.Open();
                y += LIB_ROW + 6f;
            }
            return y;
        }

        // ---- ④ 黑板 ----
        private float DrawBlackboardGUI(float x, float y, float w)
        {
            y = SectionHeader("bb", "黑板 Blackboard", x, y, w);
            if (_collapsed.Contains("bb")) return y;
            if (_asset.Blackboard == null) _asset.Blackboard = new List<BTBlackboardParam>();
            if (GUI.Button(new Rect(x, y, w, LIB_ROW), "+ 添加参数"))
            {
                _asset.Blackboard.Add(new BTBlackboardParam { Key = "newKey", ValueType = BTBlackboardValueType.Int });
                EditorUtility.SetDirty(_asset); AssetDatabase.SaveAssets();
            }
            y += LIB_ROW + 2f;
            for (int i = 0; i < _asset.Blackboard.Count; i++)
            {
                var p = _asset.Blackboard[i];
                float kw = w * 0.46f, tw = w * 0.34f, bw = w - kw - tw;
                p.Key = EditorGUI.TextField(new Rect(x, y, kw, LIB_ROW), p.Key);
                p.ValueType = (BTBlackboardValueType)EditorGUI.EnumPopup(new Rect(x + kw, y, tw, LIB_ROW), p.ValueType);
                if (GUI.Button(new Rect(x + kw + tw, y, bw, LIB_ROW), "x"))
                {
                    _asset.Blackboard.RemoveAt(i); i--;
                    EditorUtility.SetDirty(_asset); AssetDatabase.SaveAssets();
                    continue;
                }
                y += LIB_ROW + 2f;
            }
            return y + 4f;
        }

        // ---- ⑤ 运行时 Blob / ECS(第二步: 编辑器一键构建 Blob 并注册 BTManager) ----
        private float DrawRuntimeGUI(float x, float y, float w)
        {
            y = SectionHeader("rt", "运行时 (Blob / ECS)", x, y, w);
            if (_collapsed.Contains("rt")) return y;
            GUI.Label(new Rect(x, y, w, LIB_ROW),
                string.Format("TreeId {0} · 节点 {1} · 连线 {2}", _asset.TreeId, _asset.Nodes.Count, _asset.Connections.Count), _libLabel);
            y += LIB_ROW + 2f;
            if (GUI.Button(new Rect(x, y, w, LIB_ROW), "构建 Blob 并注册运行时"))
            {
                if (BTBlobBuilder.Build(_asset, out var blob))
                {
                    BTManager.Register(_asset.TreeId, blob);
                    Debug.Log(string.Format("[BT] 已构建并注册 Blob: treeId={0} nodes={1} conns={2}",
                        _asset.TreeId, _asset.Nodes.Count, _asset.Connections.Count));
                }
                else
                {
                    Debug.LogWarning(string.Format("[BT] Blob 构建失败(树数据无效): treeId={0}", _asset.TreeId));
                }
            }
            y += LIB_ROW + 2f;
            GUI.Label(new Rect(x, y, w, LIB_ROW), "运行: 实体挂 RunningBT{TreeId} → BTInterpreterSystem", _subLabel);
            return y + LIB_ROW + 4f;
        }

        // 分组标题(可点击折叠; 返回新的 y)
        private float SectionHeader(string key, string t, float x, float y, float w)
        {
            var mp = _rawMouse - _drawOffset;
            var r = new Rect(x - 6f, y, w + 12f, SEC_H);
            EditorGUI.DrawRect(r, new Color(0.2f, 0.2f, 0.24f));
            bool collapsed = _collapsed.Contains(key);
            GUI.Label(new Rect(x, y + 1f, w, SEC_H), (collapsed ? "▸ " : "▾ ") + t, _headLabel);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(mp))
            {
                if (collapsed) _collapsed.Remove(key); else _collapsed.Add(key);
                Event.current.Use();
            }
            return y + SEC_H + 4f;
        }

        // 子分组(分类)标题(可点击折叠; key 建议 "cat:XXX")
        private float SectionSub(string key, string t, float x, float y, float w)
        {
            var mp = _rawMouse - _drawOffset;
            var r = new Rect(x - 6f, y, w + 12f, SUB_H);
            bool collapsed = _collapsed.Contains(key);
            GUI.Label(new Rect(x, y, w, SUB_H), (collapsed ? "▸ " : "▾ ") + t, _subLabel);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(mp))
            {
                if (collapsed) _collapsed.Remove(key); else _collapsed.Add(key);
                Event.current.Use();
            }
            return y + SUB_H + 2f;
        }

        private float LibItemRow(float x, float y, float w, string label, BTDragPayload payload, Event e)
        {
            var mp = _rawMouse - _drawOffset;
            var r = new Rect(x, y, w, LIB_ROW);
            bool hover = r.Contains(mp);
            EditorGUI.DrawRect(r, hover ? new Color(0.26f, 0.26f, 0.32f) : new Color(0.16f, 0.16f, 0.2f));
            Color ac = payload.IsCustom ? BTNodeCatalog.ColorOf(BTNodeType.GameCustom) : BTNodeCatalog.ColorOf(payload.Type);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), ac);
            GUI.Label(new Rect(x + 8, y, w - 12, LIB_ROW), label, _libLabel);

            // 按下即记录候选; 真正"拖动/点击创建"由 HandleLibDrag 统一处理(不依赖 DragAndDrop API)
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(mp))
            {
                _libPending = payload;
                _libDragStart = _rawMouse;
                _libDragging = false;
                e.Use();
            }
            return y + LIB_ROW + 2f;
        }

        /// <summary>节点库 cell → 画布 拖拽(手动实现): 拖动超过阈值进入拖拽, 松手在画布则建节点; 未移动则点击在画布中心建节点。</summary>
        private void HandleLibDrag(Event e)
        {
            if (_libPending == null) return;

            if (e.type == EventType.MouseDrag && !_libDragging &&
                Vector2.Distance(_rawMouse, _libDragStart) > 6f)
            {
                _libDragging = true;
                e.Use();
            }

            if (e.type == EventType.MouseUp)
            {
                if (_libDragging)
                {
                    if (_canvasRect.Contains(_rawMouse))
                        AddNodeFromPayload(_libPending, ScreenToCanvas(_rawMouse));
                }
                else
                {
                    AddNodeFromPayload(_libPending, CanvasCenter()); // 点击创建
                }
                _libPending = null; _libDragging = false;
                OnRepaint?.Invoke();
                e.Use();
            }
        }

        // 拖拽中的幽灵标签(屏幕空间)
        private void DrawLibDragGhost()
        {
            if (!_libDragging || _libPending == null) return;
            var r = new Rect(_rawMouse.x + 12f, _rawMouse.y + 8f, 150f, 18f);
            EditorGUI.DrawRect(r, new Color(0.2f, 0.3f, 0.45f, 0.9f));
            GUI.Label(r, "  " + _libPending.Name, _libLabel);
        }

        // ---- (右侧 Explorer 面板)内容总高: 树行 + 黑板 + 运行时(供滚动条) ----
        private float ComputeLeftContentHeight()
        {
            float h = _explorerRows.Count * ROW_H;

            // 黑板
            if (_asset.Blackboard == null) _asset.Blackboard = new List<BTBlackboardParam>();
            h += SEC_H + 4f;
            if (!_collapsed.Contains("bb")) h += LIB_ROW + _asset.Blackboard.Count * (LIB_ROW + 2f) + 4f;

            // 运行时
            h += SEC_H + 4f;
            if (!_collapsed.Contains("rt")) h += 3f * (LIB_ROW + 2f) + 4f;

            return h + 20f;
        }

        // ---- 行为树资源 行数统计(供滚动高; 计入折叠) ----
        private int CountTreeAssetRows(string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir)) return 0;
            int n = 0;
            foreach (var sub in AssetDatabase.GetSubFolders(dir))
            {
                n++;
                if (!_collapsed.Contains("dir:" + sub)) n += CountTreeAssetRows(sub);
            }
            foreach (var g in AssetDatabase.FindAssets("t:BTTreeAsset", new[] { dir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetDirectoryName(p).Replace('\\', '/') == dir) n++;
            }
            return n;
        }

        // 通用竖直滚动条; 返回新的 scroll 值(调用方回写)
        private float DrawVScrollbar(Rect area, float viewH, float contentH, float maxScroll, float scroll, Event e)
        {
            float sbW = 10f;
            var sbR = new Rect(area.xMax - sbW, area.y, sbW, area.height);
            EditorGUI.DrawRect(sbR, new Color(0.1f, 0.1f, 0.12f));
            float thumbH = Mathf.Max(20f, viewH * viewH / contentH);
            float thumbY = area.y + (scroll / maxScroll) * (area.height - thumbH);
            var thumbR = new Rect(area.xMax - sbW, thumbY, sbW, thumbH);
            EditorGUI.DrawRect(thumbR, new Color(0.4f, 0.4f, 0.5f));

            var mp = _rawMouse;
            if (e.type == EventType.MouseDown && e.button == 0 && thumbR.Contains(mp))
            { _sbDrag = true; _sbDragStart = mp.y - thumbY; e.Use(); }
            else if (e.type == EventType.MouseDrag && _sbDrag)
            {
                scroll = Mathf.Clamp((mp.y - _sbDragStart - area.y) / Mathf.Max(1f, area.height - thumbH) * maxScroll, 0f, maxScroll);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _sbDrag) { _sbDrag = false; e.Use(); }
            else if (e.type == EventType.MouseDown && e.button == 0 && sbR.Contains(mp) && !thumbR.Contains(mp))
            { scroll = Mathf.Clamp(scroll + (mp.y < thumbY ? -viewH : viewH), 0f, maxScroll); e.Use(); }

            return scroll;
        }

        private BTTreeAsset FindTree(long id)
        {
            var guids = AssetDatabase.FindAssets("t:BTTreeAsset");
            foreach (var g in guids)
            {
                var t = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(AssetDatabase.GUIDToAssetPath(g));
                if (t != null && t.TreeId == id) return t;
            }
            return null;
        }

        // ================================================================
        //  minimap(对应 GraphEditor.DrawMinimap: 画布右下, windowShadow + box + 视口 lens + 节点 blip)
        // ================================================================
        private void DrawMiniMap(Event e)
        {
            _miniRect = new Rect(_canvasRect.xMax - MM_W - 6, _canvasRect.yMax - MM_H - 6, MM_W, MM_H);
            var r = _miniRect;

            // 阴影 + 面板(windowShadow + box)
            GUI.color = new Color(1, 1, 1, 0.85f);
            GUI.Box(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), string.Empty, BTEditorStyles.NodeShadowStyle);
            GUI.color = Color.white;
            GUI.Box(r, _asset.Nodes.Count > 0 ? string.Empty : "Minimap", BTEditorStyles.Box);

            if (_asset.Nodes.Count == 0) return;

            // 映射边界(节点 + 视口, 外扩 25 对应 NC finalBound.ExpandBy(25))
            var vTL = ScreenToCanvas(_canvasRect.min);
            var vBR = ScreenToCanvas(_canvasRect.max);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in _asset.Nodes)
            {
                var nr = NodeRect(n);
                minX = Mathf.Min(minX, nr.xMin); minY = Mathf.Min(minY, nr.yMin);
                maxX = Mathf.Max(maxX, nr.xMax); maxY = Mathf.Max(maxY, nr.yMax);
            }
            minX = Mathf.Min(minX, vTL.x); minY = Mathf.Min(minY, vTL.y);
            maxX = Mathf.Max(maxX, vBR.x); maxY = Mathf.Max(maxY, vBR.y);
            float pad = 25f; minX -= pad; minY -= pad; maxX += pad; maxY += pad;
            float cw = Mathf.Max(1, maxX - minX), ch = Mathf.Max(1, maxY - minY);
            float s = Mathf.Min(r.width / cw, r.height / ch);
            float ox = r.x + (r.width - cw * s) / 2;
            float oy = r.y + (r.height - ch * s) / 2;
            _mmMin = new Vector2(minX, minY); _mmScale = s; _mmOrigin = new Vector2(ox, oy);

            System.Func<float, float> mx = cx => ox + (cx - minX) * s;
            System.Func<float, float> my = cy => oy + (cy - minY) * s;

            if (e.type == EventType.Repaint)
            {
                // 连线(灰 0.35, 对应 NC Handles.DrawAAPolyLine(snp,sp,tp,tnp))
                Handles.BeginGUI();
                Handles.color = new Color(0.35f, 0.35f, 0.35f);
                foreach (var c in _asset.Connections)
                {
                    var src = _asset.Nodes.Find(x => x.NodeId == c.SourceNodeId);
                    var dst = _asset.Nodes.Find(x => x.NodeId == c.TargetNodeId);
                    if (src == null || dst == null) continue;
                    var sc = NodeRect(src).center; var dc = NodeRect(dst).center;
                    Handles.DrawLine(new Vector3(mx(sc.x), my(sc.y), 0), new Vector3(mx(dc.x), my(dc.y), 0));
                }
                Handles.color = Color.white;
                Handles.EndGUI();

                // 节点 blip(实心矩形, 节点分类色, 对应 NC node.nodeColor)
                foreach (var n in _asset.Nodes)
                {
                    var nr = NodeRect(n);
                    EditorGUI.DrawRect(new Rect(mx(nr.xMin), my(nr.yMin), Mathf.Max(2, nr.width * s), Mathf.Max(2, nr.height * s)), BTNodeCatalog.ColorOf(n.Type));
                }
            }

            // 视口 lens(白色 0.8 box, 对应 NC lensRect)
            var lens = new Rect(mx(vTL.x), my(vTL.y), (vBR.x - vTL.x) * s, (vBR.y - vTL.y) * s);
            GUI.color = new Color(1, 1, 1, 0.8f);
            GUI.Box(lens, string.Empty, BTEditorStyles.Box);
            GUI.color = Color.white;
        }

        private void NavigateMiniMap(Vector2 mouse)
        {
            var local = mouse - _mmOrigin;
            var canvasPoint = _mmMin + local / _mmScale;
            Pan = canvasPoint - _canvasRect.size / (2f * Zoom);
            OnRepaint?.Invoke();
        }

        // ================================================================
        //  P3 - 底部状态条
        // ================================================================
        private void DrawBottomBar(Event e, Rect bar)
        {
            EditorGUI.DrawRect(bar, new Color(0.1f, 0.1f, 0.12f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width, 1), new Color(0.3f, 0.3f, 0.4f));

            int errs = _issues.Count(x => x.IsError);
            int warns = _issues.Count - errs;
            string summary = errs > 0 ? $"✗ {errs} 错误, {warns} 警告"
                            : (warns > 0 ? $"⚠ {warns} 警告" : "✓ 校验通过");
            GUI.Label(new Rect(bar.x + 10, bar.y + 6, bar.width * 0.42f, 18), summary,
                errs > 0 ? _errLabel : (warns > 0 ? _warnLabel : _okLabel));

            int li = 0;
            for (int i = 0; i < _issues.Count && li < 2; i++, li++)
            {
                var iss = _issues[i];
                GUI.Label(new Rect(bar.x + 22, bar.y + 26 + li * 16, bar.width * 0.42f - 12, 16),
                    (iss.IsError ? "错误: " : "警告: ") + iss.Message, iss.IsError ? _errLabel : _warnLabel);
            }

            string sel = _selectedIds.Count == 0 ? "未选中节点" : $"选中 {_selectedIds.Count} 个节点";
            GUI.Label(new Rect(bar.x + bar.width * 0.46f, bar.y + 6, bar.width * 0.26f, 18), sel, _libLabel);
            string stat = $"节点 {_asset.Nodes.Count} · 连线 {_asset.Connections.Count} · 缩放 {Mathf.Round(Zoom * 100)}%";
            GUI.Label(new Rect(bar.x + bar.width * 0.72f, bar.y + 6, bar.width * 0.28f, 18), stat, _libLabel);
            GUI.Label(new Rect(bar.x + bar.width * 0.46f, bar.y + 26, bar.width * 0.52f, 16),
                "运行时轨迹高亮 = P4(暂未接入)", _subLabel);
        }

        // ================================================================
        //  数据操作(直接改写 BTTreeAsset, 与 GraphView 版等价)
        // ================================================================

        private static long NewNodeId()
        {
            var id = Guid.NewGuid().GetHashCode() & 0x7fffffff;
            return id == 0 ? 1 : id;
        }

        private void MarkDirty(bool save)
        {
            _graphDirty = true;   // 图已变更, 下次 DrawGUI 重算校验(见 DrawGUI 缓存逻辑)
            EditorUtility.SetDirty(_asset);
            if (save) AssetDatabase.SaveAssets();
            OnRepaint?.Invoke();
        }

        /// <summary>把当前 BTTreeAsset 状态压入 Unity 撤销栈(须在改动之前调用)。</summary>
        private void RecordUndo(string label)
        {
            if (_asset != null) Undo.RecordObject(_asset, label);
        }

        /// <summary>Undo/Redo 后由宿主窗口调用: 失效校验缓存并请求重绘。</summary>
        public void NotifyUndo()
        {
            _graphDirty = true;
            OnRepaint?.Invoke();
        }

        // 删除选中节点(连带其连线); Root 保护
        private void DeleteSelected()
        {
            if (_selectedIds.Count == 0) return;
            RecordUndo("BT: 删除选中节点");
            _asset.Nodes.RemoveAll(n => _selectedIds.Contains(n.NodeId) && n.Type != BTNodeType.Root);
            _asset.Connections.RemoveAll(c => _selectedIds.Contains(c.SourceNodeId) || _selectedIds.Contains(c.TargetNodeId));
            _selectedIds.RemoveWhere(id => !_asset.Nodes.Any(n => n.NodeId == id));
            MarkDirty(true);
        }

        private void AddNode(BTNodeType type, Vector2 pos)
        {
            RecordUndo("BT: 新增节点");
            var data = new BTNodeData { NodeId = NewNodeId(), Type = type, Position = pos };
            _asset.Nodes.Add(data);
            _selectedIds.Clear(); _selectedIds.Add(data.NodeId);
            MarkDirty(true);
        }

        private void AddCustomNode(System.Type type, long subType, Vector2 pos)
        {
            RecordUndo("BT: 新增自定义节点");
            var data = new BTNodeData { NodeId = NewNodeId(), Type = BTNodeType.GameCustom, Position = pos };
            data.LongParams.Add(subType);
            _asset.Nodes.Add(data);
            _selectedIds.Clear(); _selectedIds.Add(data.NodeId);
            MarkDirty(true);
        }

        private void AddNodeFromPayload(BTDragPayload p, Vector2 pos)
        {
            RecordUndo("BT: 从节点库添加节点");
            if (p.IsCustom)
            {
                var data = new BTNodeData { NodeId = NewNodeId(), Type = BTNodeType.GameCustom, Position = pos };
                data.LongParams.Add(p.SubType);
                _asset.Nodes.Add(data);
                _selectedIds.Clear(); _selectedIds.Add(data.NodeId);
            }
            else
                AddNode(p.Type, pos);
            MarkDirty(true);
        }

        private void ConnectNodes(long srcId, int srcPort, long dstId)
        {
            RecordUndo("BT: 连接节点");
            if (srcId == dstId) return;
            bool dup = false;
            foreach (var c in _asset.Connections)
                if (c.SourceNodeId == srcId && c.TargetNodeId == dstId && c.PortIndex == srcPort) { dup = true; break; }
            if (dup) return;
            _asset.Connections.Add(new BTConnectionData { SourceNodeId = srcId, TargetNodeId = dstId, PortIndex = srcPort });
            MarkDirty(true);
        }

        private void EndPendingConnect(Vector2 cm)
        {
            if (!_pendingSrcId.HasValue) return;
            long src = _pendingSrcId.Value; int port = _pendingSrcPort;
            _pendingSrcId = null;
            foreach (var m in _asset.Nodes)
            {
                if (m.NodeId == src) continue;
                if (!IsInput(m.Type)) continue;
                if (Vector2.Distance(cm, InputPortPos(m)) < PORT_HIT)
                {
                    ConnectNodes(src, port, m.NodeId);
                    return;
                }
            }
        }

        private void DeleteNode(long nodeId)
        {
            RecordUndo("BT: 删除节点");
            var n = _asset.Nodes.Find(x => x.NodeId == nodeId);
            if (n != null && n.Type == BTNodeType.Root) return; // Root 不可删
            _asset.Connections.RemoveAll(c => c.SourceNodeId == nodeId || c.TargetNodeId == nodeId);
            _asset.Nodes.RemoveAll(x => x.NodeId == nodeId);
            _selectedIds.Remove(nodeId);
            MarkDirty(true);
        }

        private HashSet<long> CollectBranch(long rootId)
        {
            var ids = new HashSet<long> { rootId };
            var queue = new Queue<long>();
            queue.Enqueue(rootId);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var c in _asset.Connections)
                    if (c.SourceNodeId == cur && ids.Add(c.TargetNodeId))
                        queue.Enqueue(c.TargetNodeId);
            }
            return ids;
        }

        private void DeleteBranch(long nodeId)
        {
            RecordUndo("BT: 删除分支");
            var ids = CollectBranch(nodeId);
            _asset.Connections.RemoveAll(c => ids.Contains(c.SourceNodeId) || ids.Contains(c.TargetNodeId));
            _asset.Nodes.RemoveAll(n => ids.Contains(n.NodeId));
            foreach (var id in ids) _selectedIds.Remove(id);
            MarkDirty(true);
        }

        private static BTNodeData CloneNodeData(BTNodeData src, long newId, Vector2 offset)
        {
            var copy = new BTNodeData
            {
                NodeId = newId,
                Type = src.Type,
                Position = src.Position + offset,
            };
            copy.FloatParams.AddRange(src.FloatParams);
            copy.LongParams.AddRange(src.LongParams);
            copy.StringParams.AddRange(src.StringParams);
            copy.Note = src.Note;
            return copy;
        }

        private void DuplicateBranch(long nodeId)
        {
            RecordUndo("BT: 复制分支");
            var ids = CollectBranch(nodeId);
            var map = new Dictionary<long, long>();
            foreach (var id in ids) map[id] = NewNodeId();
            foreach (var id in ids)
            {
                var src = _asset.Nodes.Find(n => n.NodeId == id);
                if (src == null) continue;
                _asset.Nodes.Add(CloneNodeData(src, map[id], new Vector2(60, 60)));
            }
            foreach (var c in _asset.Connections.FindAll(c => ids.Contains(c.SourceNodeId) && ids.Contains(c.TargetNodeId)))
                _asset.Connections.Add(new BTConnectionData
                {
                    SourceNodeId = map[c.SourceNodeId],
                    TargetNodeId = map[c.TargetNodeId],
                    PortIndex = c.PortIndex,
                });
            MarkDirty(true);
        }

        private void ReplaceNodeType(long nodeId, BTNodeType newType)
        {
            RecordUndo("BT: 替换节点类型");
            var n = _asset.Nodes.Find(x => x.NodeId == nodeId);
            if (n == null) return;
            n.Type = newType;
            MarkDirty(true);
        }

        private void DecorateNode(long nodeId, BTNodeType decoType)
        {
            RecordUndo("BT: 装饰节点");
            var node = _asset.Nodes.Find(x => x.NodeId == nodeId);
            if (node == null || node.Type == BTNodeType.Root) return;
            var deco = new BTNodeData
            {
                NodeId = NewNodeId(),
                Type = decoType,
                Position = node.Position + new Vector2(-160, 0),
            };
            _asset.Nodes.Add(deco);
            foreach (var c in _asset.Connections)
                if (c.TargetNodeId == nodeId)
                    c.TargetNodeId = deco.NodeId;
            _asset.Connections.Add(new BTConnectionData
            {
                SourceNodeId = deco.NodeId,
                TargetNodeId = nodeId,
                PortIndex = 0,
            });
            MarkDirty(true);
        }

        private void ConvertToSubTree(long nodeId)
        {
            RecordUndo("BT: 转换为子树");
            var node = _asset.Nodes.Find(x => x.NodeId == nodeId);
            if (node == null || node.Type == BTNodeType.Root) return;
            var ids = CollectBranch(nodeId);

            var sub = ScriptableObject.CreateInstance<BTTreeAsset>();
            sub.TreeId = DateTime.Now.Ticks % 100000;
            sub.name = "SubTree_" + sub.TreeId;
            sub.Kind = _asset.Kind;

            long rootId = NewNodeId();
            sub.Nodes.Add(new BTNodeData { NodeId = rootId, Type = BTNodeType.Root, Position = new Vector2(40, 200) });

            var map = new Dictionary<long, long>();
            foreach (var id in ids) map[id] = NewNodeId();
            foreach (var id in ids)
            {
                var src = _asset.Nodes.Find(n => n.NodeId == id);
                if (src == null) continue;
                sub.Nodes.Add(CloneNodeData(src, map[id], Vector2.zero));
            }
            sub.Connections.Add(new BTConnectionData { SourceNodeId = rootId, TargetNodeId = map[nodeId], PortIndex = 0 });
            foreach (var c in _asset.Connections.FindAll(c => ids.Contains(c.SourceNodeId) && ids.Contains(c.TargetNodeId)))
                sub.Connections.Add(new BTConnectionData
                {
                    SourceNodeId = map[c.SourceNodeId],
                    TargetNodeId = map[c.TargetNodeId],
                    PortIndex = c.PortIndex,
                });

            if (!AssetDatabase.IsValidFolder("Assets/BTTrees"))
                AssetDatabase.CreateFolder("Assets", "BTTrees");
            AssetDatabase.CreateAsset(sub, $"Assets/BTTrees/{sub.name}.asset");
            AssetDatabase.SaveAssets();

            var subNode = new BTNodeData
            {
                NodeId = NewNodeId(),
                Type = BTNodeType.SubTree,
                Position = node.Position,
            };
            subNode.LongParams.Add(sub.TreeId);
            foreach (var c in _asset.Connections)
                if (c.TargetNodeId == nodeId)
                    c.TargetNodeId = subNode.NodeId;
            _asset.Connections.RemoveAll(c => ids.Contains(c.SourceNodeId) || ids.Contains(c.TargetNodeId));
            _asset.Nodes.RemoveAll(n => ids.Contains(n.NodeId));
            foreach (var id in ids) _selectedIds.Remove(id);
            _asset.Nodes.Add(subNode);
            _selectedIds.Clear(); _selectedIds.Add(subNode.NodeId);
            MarkDirty(true);
        }

        private void CopyNode(long nodeId)
        {
            var n = _asset.Nodes.Find(x => x.NodeId == nodeId);
            if (n == null) return;
            _clipboard.Clear();
            _clipboard.Add(n);
        }

        private void PasteSelected()
        {
            RecordUndo("BT: 粘贴节点");
            if (_clipboard.Count == 0) return;
            var offset = new Vector2(30, 30);
            foreach (var src in _clipboard)
            {
                var data = CloneNodeData(src, NewNodeId(), offset);
                _asset.Nodes.Add(data);
                _selectedIds.Add(data.NodeId);
            }
            MarkDirty(true);
        }

        // ---- 右键菜单 ----
        private void BuildAddMenu(GenericMenu menu, Vector2 pos)
        {
            menu.AddItem(new GUIContent("入口/开始"), false, () => AddNode(BTNodeType.Root, pos));
            menu.AddItem(new GUIContent("出口/结束"), false, () => AddNode(BTNodeType.End, pos));
            foreach (var cat in BTNodeCatalog.Categories)
            {
                menu.AddSeparator(cat + "/");
                foreach (var t in BTNodeCatalog.ByCategory(cat))
                    menu.AddItem(new GUIContent(cat + "/" + BTNodeCatalog.DisplayName(t)), false, () => AddNode(t, pos));
            }
            menu.AddSeparator("游戏层/");
            bool any = false;
            foreach (var ty in BTCustomNodeScanner.AllNodeTypes)
            {
                var inst = (BTCustomNode)Activator.CreateInstance(ty);
                if (inst.TreeKind != _asset.Kind) continue;
                any = true;
                var t2 = ty; var st = inst.SubType;
                menu.AddItem(new GUIContent("游戏层/" + inst.NodeName), false, () => AddCustomNode(t2, st, pos));
            }
            if (!any) menu.AddDisabledItem(new GUIContent("游戏层/(暂无自定义节点)"));
        }

        private void BuildNodeMenu(GenericMenu menu, long nodeId, Vector2 pos)
        {
            var n = _asset.Nodes.Find(x => x.NodeId == nodeId);
            if (n == null) return;
            var type = n.Type;

            menu.AddItem(new GUIContent("复制"), false, () => CopyNode(nodeId));
            menu.AddItem(new GUIContent("删除"), false, () => DeleteNode(nodeId));
            menu.AddItem(new GUIContent("删除分支"), false, () => DeleteBranch(nodeId));
            menu.AddItem(new GUIContent("复制分支"), false, () => DuplicateBranch(nodeId));
            menu.AddItem(new GUIContent("转换为子树"), false, () => ConvertToSubTree(nodeId));
            menu.AddSeparator("");

            // 同分类替换(Root 不可替换, 否则树失去入口)
            if (type != BTNodeType.Root)
            {
                var cat = BTNodeCatalog.CategoryOf(type);
                foreach (var t in BTNodeCatalog.ByCategory(cat))
                    if (t != type)
                        menu.AddItem(new GUIContent("替换类型/" + BTNodeCatalog.DisplayName(t)), false, () => ReplaceNodeType(nodeId, t));
            }

            // 装饰
            if (type != BTNodeType.Root)
            {
                menu.AddSeparator("装饰/");
                foreach (var dt in BTNodeCatalog.ByCategory(BTNodeCategory.Decorator))
                    menu.AddItem(new GUIContent("装饰/" + BTNodeCatalog.DisplayName(dt)), false, () => DecorateNode(nodeId, dt));
            }
        }

        // ---- 参数辅助 ----
        private static void EnsureFloat(BTNodeData n, int idx, float def)
        {
            while (n.FloatParams.Count <= idx) n.FloatParams.Add(n.FloatParams.Count == idx ? def : 0f);
        }
        private static void EnsureLong(BTNodeData n, int idx, long def)
        {
            while (n.LongParams.Count <= idx) n.LongParams.Add(n.LongParams.Count == idx ? def : 0L);
        }
    }
}
