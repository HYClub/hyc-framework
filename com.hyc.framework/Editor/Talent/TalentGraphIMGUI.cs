// ============================================================
// HYC Framework - 天赋树模块(Editor)
// 文件: Editor/Talent/TalentGraphIMGUI.cs
// 说明: 天赋树编辑器 —— <b>整个编辑界面</b>全部由 IMGUI 手绘:
//         工具栏 / 左栏(天赋树资源) / 画布 / 右栏(节点属性·黑板·运行时) / 底栏 / minimap。
//       结构与 Editor/BT/BTGraphIMGUI.cs 一一对应, 用来保证两个编辑器的操作手感完全一致:
//         · 左键框选、中键(或 Alt/Shift+左键)平移、滚轮以鼠标为中心缩放
//         · 右键空白 = 加节点 / 右键节点 = 节点菜单
//         · 输出端口拖到目标节点 = 连线(天赋树必须无环, 成环直接拒绝)
//         · Del 删除、Ctrl+C/V 复制粘贴、Ctrl+D 就地复制
//       视觉直接复用 <see cref="BTEditorStyles"/> 的程序化圆角/圆点纹理与深色调色板 ——
//       两个编辑器的"长相"只有一份来源, 改一处两边一起变。
//       节点没有分类(见 TalentNode 注释), 所以卡片色条只有一种颜色。
//       由 TalentTreeWindow 内的 IMGUIContainer 承载。
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using HYC.Framework.BT.Editor;
using HYC.Framework.Config;
using HYC.Framework.Config.Editor;   // LocAccess / LocKeyPickerWindow(多语言 key 选择)

namespace HYC.Framework.Talent.Editor
{
    public class TalentGraphIMGUI
    {
        // ============================================================
        //  布局常量(与 BTGraphIMGUI 同值, 保证两个编辑器分栏宽度一致)
        // ============================================================
        private const float TOOLBAR_H = 21f;
        private const float BOTTOM_H = 88f;
        private const float LEFT_W = 210f;    // 左: 天赋树资源
        private const float RIGHT_W = 262f;   // 右: 节点属性 / 黑板 / 运行时
        private const float MM_W = 210f;
        private const float MM_H = 150f;
        private const float GRID = 20f;       // 网格 / 吸附步长
        private const float NODE_W = 180f;    // 节点卡宽(与 BT 同)
        private const float NODE_H = 44f;     // 节点卡高(无等级行)
        private const float NODE_H_LEVEL = 54f; // 有"每级 · 最高 N 级"行时
        private const float ROW_H = 18f;      // 列表行高
        private const float SEC_H = 22f;      // 分组标题高
        private const float LIB_ROW = 20f;    // 面板里单行控件高
        private const float PORT_SIZE = 12f;  // 端口圆点直径(与 BT 同)
        private const float PORT_HIT = 12f;   // 端口命中半径(画布单位)
        private const float CONN_SIZE = 3f;   // 连线粗细(Connection.defaultSize)
        private const float BORDER_W = 1.5f;  // 卡片描边宽
        private const float STRIPE_W = 5f;    // 左侧色条宽
        private const float LBL_W = 56f;      // 右栏"标签|字段"里的标签宽
        private const double VALIDATE_INTERVAL = 0.25; // 校验结果缓存超时(秒)

        /// <summary>画布底色。同 BT: 底色暗、卡片明显更亮, 否则节点会"隐"进背景。</summary>
        private static readonly Color BG = BTEditorStyles.CanvasBg;
        private static readonly Color PANEL_BG = new Color(0.12f, 0.12f, 0.14f);
        /// <summary>节点卡片左侧色条。天赋树的节点没有分类 → 所有节点一个颜色。</summary>
        private static readonly Color NODE_STRIPE = new Color(0.42f, 0.46f, 0.56f);
        private static readonly Color WARN_COLOR = new Color(0.984f, 0.749f, 0.141f);

        // ============================================================
        //  状态
        // ============================================================
        private TalentTreeAsset _asset;

        // 视图变换
        public Vector2 Pan = new Vector2(60f, 40f);
        public float Zoom = 1f;
        /// <summary>打开一棵树后, 等画布尺寸算出来再自动取景一次 —— 保证"点了左边, 右边一定看得见"。</summary>
        private bool _pendingFit;

        // 选择(支持多选; _primaryId 是右栏 Inspector 展示的那个)
        private readonly HashSet<int> _selectedIds = new HashSet<int>();
        private int _primaryId = -1;

        // 手势
        private bool _panning;
        private Vector2 _panStartMouse, _panStartPan;
        private bool _boxSelecting;
        private Vector2 _boxStart, _boxEnd;
        private bool _draggingNode;
        private Vector2 _dragStartCanvas;
        private readonly Dictionary<int, Vector2> _dragOrigins = new Dictionary<int, Vector2>();
        private int _linkFromId = -1;          // 正在拖线的源节点(-1 = 没在拖)
        private Vector2 _linkMouseCanvas;

        // 偏好(对应 BT 的 Prefs 菜单)
        private bool _showGrid = true;
        private bool _snapGrid;
        private bool _showAssetsPanel = true;
        private bool _showRightPanel = true;
        private float _rigidity = 0.8f;        // 贝塞尔切线刚性, 0=直线 1=最弯

        // 本帧布局(容器坐标)
        private Rect _area, _toolbarRect, _assetsRect, _canvasRect, _rightRect, _miniRect, _bottomRect;
        private Rect _visibleCanvasRect;
        private Vector2 _rawMouse, _mouseCanvas;
        private Vector2 _mmMin, _mmOrigin;
        private float _mmScale;

        // 滚动(手绘, 不用 GUILayout)
        private Vector2 _assetsScroll, _rightScroll;
        private bool _sbDrag;
        private float _sbDragStart;
        private readonly HashSet<string> _collapsed = new HashSet<string>();   // 分组折叠状态

        // 校验
        private List<TalentIssue> _issues = new List<TalentIssue>();
        private bool _graphDirty = true;
        private double _lastValidateTime = -1.0;
        private readonly HashSet<int> _errorNodes = new HashSet<int>();
        private readonly HashSet<int> _warnNodes = new HashSet<int>();

        // 剪贴板
        private readonly List<TalentNode> _clipboard = new List<TalentNode>();

        // 左栏
        private string _assetSearch = "";

        // 右栏
        private float _rightContentH = 480f;   // 上一帧实测内容高(供滚动条)

        // 宿主回调
        public Action OnRepaint;
        public Action OnRequestNewTree;
        public Action OnRequestSave;
        public Action<TalentTreeAsset> OnRequestOpenTree;

        /// <summary>最近一次校验结果(供宿主/底栏读取)。</summary>
        public List<TalentIssue> Issues => _issues;
        public TalentTreeAsset Asset => _asset;

        // 样式(懒初始化)
        private GUIStyle _headLabel, _subLabel, _libLabel, _okLabel, _warnLabel, _errLabel;
        private GUIStyle _headTitle, _headCost, _headSub;
        private GUIStyle _fieldLabel, _rowStyle, _crumbStyle, _tinyDim;

        // ============================================================
        //  生命周期
        // ============================================================
        public TalentGraphIMGUI(TalentTreeAsset asset)
        {
            _asset = asset;
            _pendingFit = asset != null && CountValidNodes(asset) > 0;
        }

        public void SetAsset(TalentTreeAsset asset)
        {
            _asset = asset;
            _selectedIds.Clear();
            _primaryId = -1;
            _graphDirty = true;
            _sbDrag = false;
            _panning = false;
            _boxSelecting = false;
            _draggingNode = false;
            _linkFromId = -1;
            _pendingFit = asset != null && CountValidNodes(asset) > 0;
        }

        /// <summary>Undo/Redo 之后由宿主调用: 失效校验缓存 + 重绘。</summary>
        public void NotifyUndo()
        {
            _graphDirty = true;
            PruneSelection();
            OnRepaint?.Invoke();
        }

        private void MarkDirty(bool save)
        {
            _graphDirty = true;
            PruneSelection();
            if (_asset != null) EditorUtility.SetDirty(_asset);
            if (save) AssetDatabase.SaveAssets();
            OnRepaint?.Invoke();
        }

        private void RecordUndo(string label)
        {
            if (_asset != null) Undo.RecordObject(_asset, label);
        }

        /// <summary>把已不存在的 id 从选中集合里剔掉(删节点 / Undo 之后必须做)。</summary>
        private void PruneSelection()
        {
            if (_asset == null) { _selectedIds.Clear(); _primaryId = -1; return; }
            _selectedIds.RemoveWhere(id => _asset.NodeById(id) == null);
            if (_primaryId >= 0 && _asset.NodeById(_primaryId) == null) _primaryId = -1;
            if (_primaryId < 0 && _selectedIds.Count > 0) _primaryId = _selectedIds.First();
        }

        private void SelectOnly(int id)
        {
            _selectedIds.Clear();
            if (id >= 0) _selectedIds.Add(id);
            _primaryId = id;
        }

        private void ToggleSelect(int id)
        {
            if (_selectedIds.Contains(id)) { _selectedIds.Remove(id); if (_primaryId == id) _primaryId = _selectedIds.Count > 0 ? _selectedIds.First() : -1; }
            else { _selectedIds.Add(id); _primaryId = id; }
        }

        private TalentNode PrimaryNode => _asset != null && _primaryId >= 0 ? _asset.NodeById(_primaryId) : null;

        // ============================================================
        //  主绘制
        // ============================================================
        public void DrawGUI()
        {
            var e = Event.current;
            var area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (area.width <= 1f) return;
            _area = area;
            _rawMouse = e.mousePosition;

            EnsureStyles();

            if (_asset == null)
            {
                EditorGUI.DrawRect(area, BG);
                GUI.Label(new Rect(area.x + 20f, area.y + 20f, area.width - 40f, 24f),
                    "没有可编辑的天赋树资产。用 File → 新建天赋树 创建一棵。", _headLabel);
                return;
            }

            // ---- 分栏 ----
            float ph = Mathf.Max(1f, area.height - TOOLBAR_H - BOTTOM_H);
            _toolbarRect = new Rect(0f, 0f, area.width, TOOLBAR_H);
            _assetsRect = new Rect(0f, TOOLBAR_H, _showAssetsPanel ? LEFT_W : 0f, ph);
            _rightRect = new Rect(area.width - (_showRightPanel ? RIGHT_W : 0f), TOOLBAR_H, _showRightPanel ? RIGHT_W : 0f, ph);
            float cx = _showAssetsPanel ? LEFT_W : 0f;
            float cw = area.width - cx - (_showRightPanel ? RIGHT_W : 0f);
            _canvasRect = new Rect(cx, TOOLBAR_H, Mathf.Max(1f, cw), ph);
            _bottomRect = new Rect(0f, area.height - BOTTOM_H, area.width, BOTTOM_H);

            _mouseCanvas = ScreenToCanvas(_rawMouse);
            _visibleCanvasRect = VisibleCanvasRect();

            // 修复历史 bug 留下的损坏视图/坐标(见 SanitizeViewAndPositions) —— 必须放在取景之前，
            // 否则 ZoomToFit 会被垃圾 Pan/坐标二次污染。
            if (SanitizeViewAndPositions()) MarkDirty(true);

            // 刚打开一棵树 → 自动取景(必须在画布矩形算出来之后, 否则宽度还是 0)
            if (_pendingFit)
            {
                _pendingFit = false;
                ZoomToFit();
                _mouseCanvas = ScreenToCanvas(_rawMouse);
                _visibleCanvasRect = VisibleCanvasRect();
            }

            // 校验缓存: 图变更 or 超时才重算(每帧全量校验大图会卡)。空树不报错(还没开始编辑)。
            if (_graphDirty || (EditorApplication.timeSinceStartup - _lastValidateTime) > VALIDATE_INTERVAL)
            {
                _issues = _asset.nodes.Count == 0 ? new List<TalentIssue>() : TalentTreeValidator.Validate(_asset);
                _graphDirty = false;
                _lastValidateTime = EditorApplication.timeSinceStartup;
                RemapIssueNodes();
            }

            // ===== 1) 画布(最底层先画; 之后所有 chrome 都盖在它上面, 越界内容自然被遮住) =====
            EditorGUI.DrawRect(_canvasRect, BG);
            DrawGridScreen();

            var oldMatrix = BeginCanvasTransform();

            Handles.BeginGUI();
            for (int i = 0; i < _asset.links.Count; i++) DrawConnection(_asset.links[i]);
            if (_linkFromId >= 0) DrawLinkPreview();
            if (_boxSelecting) DrawBoxSelection();
            Handles.EndGUI();

            for (int i = 0; i < _asset.nodes.Count; i++)
            {
                var n = _asset.nodes[i];
                if (n == null) continue;
                if (!NodeRect(n).Overlaps(_visibleCanvasRect)) continue;   // 视口裁剪
                DrawNode(n, e);
            }

            EndCanvasTransform(oldMatrix);

            if (_asset.nodes.Count == 0) DrawEmptyCanvasHint();
            else if (HasOnlyBrokenNodes()) DrawBrokenNodesHint();
            DrawCanvasBorder();
            DrawMiniMap(e);   // 必须在输入处理之前: HandleViewInput 依赖本帧的 _miniRect

            // ===== 2) chrome(永远盖在画布之上) =====
            DrawToolbar();
            if (_showAssetsPanel) DrawAssetsPanel(e, _assetsRect);
            if (_showRightPanel) DrawRightPanel(e, _rightRect);
            DrawBottomBar(e, _bottomRect);

            // ===== 3) 输入 =====
            HandleViewInput(e);
        }

        // ------------------------------------------------------------
        //  画布变换
        //  画布坐标 P -> 屏幕 = _canvasRect.min + (P - Pan) * Zoom
        //  不做 GUIClip 裁剪, 靠"chrome 后画"覆盖(与 BT 完全同一套做法, 原因见 BTGraphIMGUI 的长注释)
        // ------------------------------------------------------------
        private Matrix4x4 BeginCanvasTransform()
        {
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(_canvasRect.x - Pan.x * Zoom, _canvasRect.y - Pan.y * Zoom, 0f),
                Quaternion.identity, new Vector3(Zoom, Zoom, 1f));
            return old;
        }

        private void EndCanvasTransform(Matrix4x4 old) { GUI.matrix = old; }

        private Vector2 ScreenToCanvas(Vector2 screen) => (screen - _canvasRect.min) / Zoom + Pan;
        private Vector2 CanvasCenter() => Pan + _canvasRect.size / (2f * Zoom);

        private Rect VisibleCanvasRect()
        {
            var tl = ScreenToCanvas(_canvasRect.min);
            var br = ScreenToCanvas(_canvasRect.max);
            return Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
        }

        // ------------------------------------------------------------
        //  几何
        // ------------------------------------------------------------
        private static float NodeHeight(TalentNode n) => n != null && n.maxLevel > 1 ? NODE_H_LEVEL : NODE_H;

        private Rect NodeRect(TalentNode n)
            => new Rect(n.editorPosition.x, n.editorPosition.y, NODE_W, NodeHeight(n));

        /// <summary>输入端口: 顶边居中。天赋节点允许多个前置, 汇到同一点(与 BT 一致)。</summary>
        private Vector2 InputPortPos(TalentNode n)
        {
            var r = NodeRect(n);
            return new Vector2(r.center.x, r.yMin);
        }

        /// <summary>输出端口: 底边居中。一个节点只有一个输出点, 所有后继共用。</summary>
        private Vector2 OutputPortPos(TalentNode n)
        {
            var r = NodeRect(n);
            return new Vector2(r.center.x, r.yMax);
        }

        private int NodeAt(Vector2 canvasPos)
        {
            for (int i = _asset.nodes.Count - 1; i >= 0; i--)
            {
                var n = _asset.nodes[i];
                if (n != null && NodeRect(n).Contains(canvasPos)) return i;
            }
            return -1;
        }

        private string ResolveTitle(TalentNode n)
            => string.IsNullOrEmpty(n.title) ? "(未命名)" : n.title;

        private string CostSummary(TalentNode n)
        {
            if (n.costs == null || n.costs.Count == 0) return "无消耗";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n.costs.Count; i++)
            {
                var c = n.costs[i];
                if (c == null) continue;
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(string.IsNullOrEmpty(c.attrKey) ? "(未选)" : c.attrKey).Append(' ').Append(c.amount);
            }
            return sb.Length == 0 ? "无消耗" : sb.ToString();
        }

        private static string JoinTitles(List<TalentNode> nodes)
            => nodes == null || nodes.Count == 0 ? "无" : string.Join("、", nodes.Select(x => x.title).ToArray());

        // ============================================================
        //  背景 / 边框 / 空态引导
        // ============================================================
        private void DrawGridScreen()
        {
            if (!_showGrid) return;
            if (Event.current.type != EventType.Repaint) return;

            float gridSize = Zoom > 0.5f ? GRID : GRID * 5f;
            float step = gridSize * Zoom;
            int every = 1;
            if (step < 16f) { every = 5; step *= 5f; }
            int majorEvery = every * 4;

            float x0 = Mathf.Repeat(-Pan.x * Zoom, step);
            float y0 = Mathf.Repeat(-Pan.y * Zoom, step);
            var minor = new Color(1f, 1f, 1f, 0.055f);
            var major = new Color(1f, 1f, 1f, 0.11f);
            int cx = 0;
            for (float x = _canvasRect.xMin + x0; x < _canvasRect.xMax; x += step, cx++)
            {
                bool xMaj = cx % majorEvery == 0;
                int cy = 0;
                for (float y = _canvasRect.yMin + y0; y < _canvasRect.yMax; y += step, cy++)
                {
                    bool isMajor = xMaj && (cy % majorEvery == 0);
                    if (every > 1 && !isMajor) continue;
                    EditorGUI.DrawRect(new Rect(x - 1f, y - 1f, 2f, 2f), isMajor ? major : minor);
                }
            }
        }

        private void DrawCanvasBorder()
        {
            var c = new Color(1f, 1f, 1f, 0.14f);
            EditorGUI.DrawRect(new Rect(_canvasRect.xMin, _canvasRect.yMin, _canvasRect.width, 1f), c);
            EditorGUI.DrawRect(new Rect(_canvasRect.xMin, _canvasRect.yMax - 1f, _canvasRect.width, 1f), c);
            EditorGUI.DrawRect(new Rect(_canvasRect.xMin, _canvasRect.yMin, 1f, _canvasRect.height), c);
            EditorGUI.DrawRect(new Rect(_canvasRect.xMax - 1f, _canvasRect.yMin, 1f, _canvasRect.height), c);
        }

        private static GUIStyle _hintMain, _hintSub;
        private static void EnsureHintStyles()
        {
            if (_hintMain != null) return;
            _hintMain = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _hintSub = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }

        private void DrawEmptyCanvasHint()
        {
            EnsureHintStyles();

            bool transient = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(_asset));
            string sub = transient
                ? "当前是未保存的临时树 — File → 保存 落盘到 Assets/TalentTrees/"
                : "右键空白处 → 添加节点; 连好后从入口节点开始向下拖出后继";

            float w = Mathf.Min(460f, _canvasRect.width - 40f);
            float y = _canvasRect.y + _canvasRect.height * 0.40f;
            float x = _canvasRect.x + (_canvasRect.width - w) * 0.5f;
            GUI.Label(new Rect(x, y, w, 24f), "空画布 — 右键 → 添加节点", _hintMain);
            GUI.Label(new Rect(x, y + 26f, w, 32f), sub, _hintSub);
        }

        /// <summary>nodes 里有元素、但全部反序列化失败（2.0.6 之前的旧格式资产）。</summary>
        private bool HasOnlyBrokenNodes()
        {
            if (_asset == null || _asset.nodes.Count == 0) return false;
            for (int i = 0; i < _asset.nodes.Count; i++)
                if (_asset.nodes[i] != null) return false;
            return true;
        }

        private static int CountValidNodes(TalentTreeAsset t)
        {
            if (t == null) return 0;
            int c = 0;
            for (int i = 0; i < t.nodes.Count; i++) if (t.nodes[i] != null) c++;
            return c;
        }

        /// <summary>坐标可用吗（排除 NaN / Inf，以及旧 bug 留下的 ±float.MaxValue 之类垃圾值）。</summary>
        private static bool IsSanePos(Vector2 p)
            => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y)
               && Mathf.Abs(p.x) < 1e6f && Mathf.Abs(p.y) < 1e6f;

        /// <summary>
        /// 修复历史 bug 污染的视图与节点坐标。旧版 <see cref="ZoomToFit"/> 在一棵"节点全失效(null)"的树上跑，
        /// min/max 停在 ±float.MaxValue → Pan 变成 ±3.4e38、Zoom 卡 0.25；之后 ScreenToCanvas / 视口裁剪全崩，
        /// 画布永远空白(但 minimap 用 NodeRect 直画，还能看到节点)。
        /// 这里把离谱的 Pan/Zoom 复位、把离谱的节点坐标重排到网格。
        /// </summary>
        /// <returns>是否改动了持久化数据(节点坐标) —— 调用方据此决定要不要 MarkDirty。</returns>
        private bool SanitizeViewAndPositions()
        {
            if (_asset == null) return false;

            // 1) 视图(Pan/Zoom 不持久化，复位 + 自刷新即可)
            if (!IsSanePos(Pan) || float.IsNaN(Zoom) || float.IsInfinity(Zoom) || Zoom <= 0f)
            {
                Pan = new Vector2(60f, 40f);
                Zoom = 1f;
                _mouseCanvas = ScreenToCanvas(_rawMouse);
                _visibleCanvasRect = VisibleCanvasRect();
                OnRepaint?.Invoke();
            }

            // 2) 节点坐标(持久化数据)
            bool changed = false;
            float x = 40f, y = 40f;
            for (int i = 0; i < _asset.nodes.Count; i++)
            {
                var n = _asset.nodes[i];
                if (n == null || IsSanePos(n.editorPosition)) continue;
                n.editorPosition = new Vector2(x, y);
                x += NODE_W + 40f;
                changed = true;
            }
            return changed;
        }

        /// <summary>旧格式资产：节点读不出来 → 画布上说明原因 + 一键清理（删失效节点与悬空连线）。</summary>
        private void DrawBrokenNodesHint()
        {
            EnsureHintStyles();

            float w = Mathf.Min(520f, _canvasRect.width - 40f);
            float x = _canvasRect.x + (_canvasRect.width - w) * 0.5f;
            float y = _canvasRect.y + _canvasRect.height * 0.34f;

            GUI.Label(new Rect(x, y, w, 24f), "节点数据已失效 — 这棵树由旧格式保存", _hintMain);
            GUI.Label(new Rect(x, y + 26f, w, 40f),
                "TalentNode 从 2.0.6 起改为「子资源」，旧 .asset 里的节点是内联数据，读不出来（黑板与连线还在）。\n" +
                "点下面按钮清掉失效节点，再重新添加节点即可。",
                _hintSub);

            float bw = 180f;
            if (GUI.Button(new Rect(_canvasRect.x + (_canvasRect.width - bw) * 0.5f, y + 74f, bw, 24f), "清理失效节点"))
                PurgeBrokenNodes();
        }

        /// <summary>删掉反序列化失败的节点（id 等已丢失），以及指向不存在节点的连线。</summary>
        private void PurgeBrokenNodes()
        {
            RecordUndo("Talent: 清理失效节点");
            _asset.nodes.RemoveAll(n => n == null);
            _asset.links.RemoveAll(l => l == null || _asset.NodeById(l.from) == null || _asset.NodeById(l.to) == null);
            _selectedIds.Clear();
            _primaryId = -1;
            SanitizeViewAndPositions();                       // 顺手修掉历史 bug 留下的垃圾 Pan/坐标
            if (CountValidNodes(_asset) > 0) ZoomToFit();
            MarkDirty(true);
        }

        // ============================================================
        //  节点 / 连线
        // ============================================================
        private static Rect Inset(Rect r, float d)
            => new Rect(r.x + d, r.y + d, r.width - d * 2f, r.height - d * 2f);

        private void DrawNode(TalentNode n, Event e)
        {
            var r = NodeRect(n);
            bool selected = _selectedIds.Contains(n.id);
            bool error = _errorNodes.Contains(n.id);
            bool warn = !error && _warnNodes.Contains(n.id);

            // 1) 阴影(双层柔和圆角, 与 BT 同参数)
            BTEditorStyles.DrawRounded(new Rect(r.x, r.y + 6f, r.width, r.height), new Color(0f, 0f, 0f, 0.22f));
            BTEditorStyles.DrawRounded(new Rect(r.x - 1f, r.y + 2f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, 0.18f));

            // 2) 卡片: 描边 → 色条 → 主体(三层同一轮廓, 色条随圆角走不会露出背景)
            var border = error ? BTEditorStyles.StatusFailure
                       : warn ? WARN_COLOR
                       : selected ? BTEditorStyles.Accent
                                  : BTEditorStyles.NodeBorder;
            BTEditorStyles.DrawRounded(r, border);
            BTEditorStyles.DrawRounded(Inset(r, BORDER_W), NODE_STRIPE);
            BTEditorStyles.DrawRounded(
                new Rect(r.x + STRIPE_W, r.y + BORDER_W, r.width - STRIPE_W - BORDER_W, r.height - 2f * BORDER_W),
                BTEditorStyles.NodeBg);

            // 3) 文字: 标题 / 消耗 / 等级
            float tx = r.x + STRIPE_W + 8f;
            float tw = r.width - (tx - r.x) - 9f;
            bool hasCost = n.costs != null && n.costs.Count > 0;
            GUI.Label(new Rect(tx, r.y + 6f, tw, 14f), ResolveTitle(n), _headTitle);
            GUI.Label(new Rect(tx, r.y + 22f, tw, 12f), CostSummary(n), hasCost ? _headCost : _headSub);
            if (n.maxLevel > 1)
                GUI.Label(new Rect(tx, r.y + 36f, tw, 11f), "每级 · 最高 " + n.maxLevel + " 级", _headSub);

            // 4) 端口
            DrawPort(InputPortPos(n));
            DrawPort(OutputPortPos(n));
        }

        private void DrawPort(Vector2 p)
            => BTEditorStyles.DrawPortDot(p, PORT_SIZE, new Color(0.45f, 0.50f, 0.60f), BTEditorStyles.NodeBg);

        private void DrawConnection(TalentLink l)
        {
            if (l == null) return;
            var src = _asset.NodeById(l.from);
            var dst = _asset.NodeById(l.to);
            if (src == null || dst == null) return;
            if (!NodeRect(src).Overlaps(_visibleCanvasRect) && !NodeRect(dst).Overlaps(_visibleCanvasRect)) return;

            var from = OutputPortPos(src);
            var to = InputPortPos(dst);
            float ty = Mathf.Max(Mathf.Abs(from.y - to.y) * _rigidity, 25f);
            var fromT = new Vector2(0f, ty);
            var toT = new Vector2(0f, -ty);

            bool hot = _selectedIds.Contains(l.from) || _selectedIds.Contains(l.to);
            var col = hot ? BTEditorStyles.EdgeActive : BTEditorStyles.Edge;
            float size = hot ? CONN_SIZE + 1.5f : CONN_SIZE;

            var shadow = new Vector2(2f, 3f);
            Handles.DrawBezier(from, to + shadow, from + shadow + fromT, to + shadow + toT,
                new Color(0f, 0f, 0f, 0.25f), BTEditorStyles.BezierTexture, size + 3f);
            Handles.DrawBezier(from, to, from + fromT, to + toT, col, BTEditorStyles.BezierTexture, size);
            BTEditorStyles.DrawConnEnd(to, 6f, col);
        }

        private void DrawLinkPreview()
        {
            var src = _asset.NodeById(_linkFromId);
            if (src == null) return;
            var from = OutputPortPos(src);
            var to = _linkMouseCanvas;
            float ty = Mathf.Max(Mathf.Abs(from.y - to.y) * _rigidity, 25f);
            Handles.DrawBezier(from, to, from + new Vector2(0f, ty), to + new Vector2(0f, -ty),
                BTEditorStyles.EdgeActive, BTEditorStyles.BezierTexture, CONN_SIZE);
        }

        private void DrawBoxSelection()
        {
            var r = Rect.MinMaxRect(
                Mathf.Min(_boxStart.x, _boxEnd.x), Mathf.Min(_boxStart.y, _boxEnd.y),
                Mathf.Max(_boxStart.x, _boxEnd.x), Mathf.Max(_boxStart.y, _boxEnd.y));
            Handles.DrawSolidRectangleWithOutline(r, new Color(0.4f, 0.6f, 1f, 0.12f), new Color(0.4f, 0.6f, 1f, 0.85f));
        }

        // ============================================================
        //  输入(缩放 / 平移 / 选择 / 拖节点 / 连线 / 菜单 / 快捷键)
        // ============================================================
        private void HandleViewInput(Event e)
        {
            bool gestureActive = _panning || _boxSelecting || _draggingNode || _linkFromId >= 0;

            if (!gestureActive && _miniRect.Contains(_rawMouse))
            {
                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
                { NavigateMiniMap(_rawMouse); e.Use(); }
                return;
            }

            if (!gestureActive && !_canvasRect.Contains(_rawMouse)) return;

            // 缩放: 以鼠标为中心
            if (e.type == EventType.ScrollWheel)
            {
                var before = ScreenToCanvas(_rawMouse);
                Zoom = Mathf.Clamp(Zoom * (e.delta.y < 0 ? 1.1f : 0.9f), 0.25f, 2.5f);
                var after = ScreenToCanvas(_rawMouse);
                Pan += before - after;
                OnRepaint?.Invoke();
                e.Use();
                return;
            }

            bool wantPan = e.button == 2 || (e.button == 0 && (e.alt || e.shift));

            // 右键菜单
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                int idx = NodeAt(_mouseCanvas);
                var menu = new GenericMenu();
                if (idx >= 0) BuildNodeMenu(menu, _asset.nodes[idx]);
                else BuildAddMenu(menu, _mouseCanvas);
                menu.ShowAsContext();
                e.Use();
                return;
            }

            // 连线收尾
            if (_linkFromId >= 0 && e.type == EventType.MouseUp && e.button == 0)
            {
                EndLink(ScreenToCanvas(_rawMouse));
                e.Use();
                return;
            }
            // 连线拖动中必须显式请求重绘, 否则预览线不跟手
            if (e.type == EventType.MouseDrag && _linkFromId >= 0)
            {
                _linkMouseCanvas = ScreenToCanvas(_rawMouse);
                e.Use();
                OnRepaint?.Invoke();
                return;
            }

            // 左键: 端口拖线 / 选中并拖动节点
            if (e.type == EventType.MouseDown && e.button == 0 && !wantPan)
            {
                // 端口命中必须先于节点命中: 输出端口圆心画在节点底边(yMax), 点在圆点上时可能落在卡片外
                for (int i = _asset.nodes.Count - 1; i >= 0; i--)
                {
                    var pn = _asset.nodes[i];
                    if (pn == null) continue;
                    if (!NodeRect(pn).Overlaps(_visibleCanvasRect)) continue;
                    if (Vector2.Distance(_mouseCanvas, OutputPortPos(pn)) < PORT_HIT)
                    {
                        _linkFromId = pn.id;
                        _linkMouseCanvas = _mouseCanvas;
                        e.Use();
                        return;
                    }
                }

                int idx = NodeAt(_mouseCanvas);
                if (idx >= 0)
                {
                    var n = _asset.nodes[idx];
                    if (e.shift) ToggleSelect(n.id);
                    else if (!_selectedIds.Contains(n.id)) SelectOnly(n.id);
                    else _primaryId = n.id;
                    OnRepaint?.Invoke();

                    _dragStartCanvas = _mouseCanvas;
                    _dragOrigins.Clear();
                    foreach (var id in _selectedIds)
                    {
                        var s = _asset.NodeById(id);
                        if (s != null) _dragOrigins[id] = s.editorPosition;
                    }
                    RecordUndo("Talent: 移动节点");
                    _draggingNode = true;
                    e.Use();
                    return;
                }
            }
            if (e.type == EventType.MouseDrag && _draggingNode)
            {
                var delta = _mouseCanvas - _dragStartCanvas;
                foreach (var kv in _dragOrigins)
                {
                    var nd = _asset.NodeById(kv.Key);
                    if (nd == null) continue;
                    var np = kv.Value + delta;
                    if (_snapGrid) { np.x = Mathf.Round(np.x / GRID) * GRID; np.y = Mathf.Round(np.y / GRID) * GRID; }
                    nd.editorPosition = np;
                }
                MarkDirty(false);
                e.Use();
                return;
            }
            if (e.type == EventType.MouseUp && _draggingNode)
            {
                _draggingNode = false;
                _dragOrigins.Clear();
                MarkDirty(true);
                e.Use();
                return;
            }

            // 框选
            if (e.type == EventType.MouseDown && e.button == 0 && !wantPan && NodeAt(_mouseCanvas) < 0)
            {
                _boxSelecting = true;
                _boxStart = _boxEnd = _mouseCanvas;
                if (!e.shift) { _selectedIds.Clear(); _primaryId = -1; }
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
                foreach (var n in _asset.nodes)
                    if (n != null && NodeRect(n).Overlaps(r)) _selectedIds.Add(n.id);
                if (_primaryId < 0 && _selectedIds.Count > 0) _primaryId = _selectedIds.First();
                e.Use();
                return;
            }

            // 快捷键(有输入框聚焦时不抢)
            if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == 0)
            {
                if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                {
                    if (_selectedIds.Count > 0) { DeleteSelected(); e.Use(); }
                }
                else if (e.keyCode == KeyCode.D && (e.control || e.command))
                {
                    if (_selectedIds.Count > 0) { DuplicateSelected(new Vector2(28f, 28f)); e.Use(); }
                }
                else if (e.keyCode == KeyCode.C && (e.control || e.command))
                {
                    CopySelected();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.V && (e.control || e.command))
                {
                    PasteAt(_mouseCanvas);
                    e.Use();
                }
            }

            // 平移
            if (e.type == EventType.MouseDown && wantPan)
            {
                _panning = true; _panStartMouse = e.mousePosition; _panStartPan = Pan; e.Use();
            }
            else if (e.type == EventType.MouseDrag && _panning)
            {
                Pan = _panStartPan - (e.mousePosition - _panStartMouse) / Zoom;
                OnRepaint?.Invoke();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _panning)
            {
                _panning = false;
                e.Use();
            }
        }

        private void EndLink(Vector2 canvasPos)
        {
            var fromId = _linkFromId;
            _linkFromId = -1;
            if (fromId < 0) return;

            int idx = NodeAt(canvasPos);
            if (idx < 0) return;
            var target = _asset.nodes[idx];
            if (target == null || target.id == fromId) return;

            for (int i = 0; i < _asset.links.Count; i++)
            {
                var l = _asset.links[i];
                if (l != null && l.from == fromId && l.to == target.id) return;   // 已存在
            }
            if (WouldCreateCycle(fromId, target.id))
            {
                Debug.LogWarning("[Talent] 拒绝连线: 天赋树必须是有向无环图(会形成环)。");
                return;
            }

            RecordUndo("Talent: 连接节点");
            _asset.links.Add(new TalentLink(fromId, target.id));
            MarkDirty(true);
        }

        /// <summary>加入 from→to 后沿后继方向能否从 to 回到 from。</summary>
        private bool WouldCreateCycle(int from, int to)
        {
            var stack = new Stack<int>();
            var seen = new HashSet<int>();
            stack.Push(to);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur == from) return true;
                for (int i = 0; i < _asset.links.Count; i++)
                {
                    var l = _asset.links[i];
                    if (l == null || l.from != cur) continue;
                    if (seen.Add(l.to)) stack.Push(l.to);
                }
            }
            return false;
        }

        // ============================================================
        //  右键菜单
        // ============================================================
        private void BuildAddMenu(GenericMenu menu, Vector2 canvasPos)
        {
            menu.AddItem(new GUIContent("添加节点"), false, () => AddNode(canvasPos));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("自动布局"), false, AutoLayout);
            menu.AddItem(new GUIContent("适应画布"), false, ZoomToFit);
            menu.AddItem(new GUIContent("重置视图"), false, () =>
            {
                Pan = new Vector2(60f, 40f);
                Zoom = 1f;
                OnRepaint?.Invoke();
            });
        }

        private void BuildNodeMenu(GenericMenu menu, TalentNode n)
        {
            menu.AddItem(new GUIContent("在下方添加后继节点"), false, () => AddSuccessor(n));
            menu.AddItem(new GUIContent("从此节点开始连线"), false, () =>
            {
                SelectOnly(n.id);
                _linkFromId = n.id;
                _linkMouseCanvas = OutputPortPos(n);
                OnRepaint?.Invoke();
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("复制节点"), false, () =>
            {
                SelectOnly(n.id);
                DuplicateSelected(new Vector2(28f, 28f));
            });
            menu.AddItem(new GUIContent("断开全部连线"), false, () =>
            {
                RecordUndo("Talent: 断开连线");
                _asset.links.RemoveAll(l => l == null || l.from == n.id || l.to == n.id);
                MarkDirty(true);
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除节点"), false, () =>
            {
                SelectOnly(n.id);
                DeleteSelected();
            });
        }

        // ============================================================
        //  数据操作
        // ============================================================
        /// <summary>
        /// 创建节点子资源(ScriptableObject)。所属树已落盘则一并 AddObjectToAsset,
        /// 使其能在 Project 窗口作为子资产被拖入 UI slot; 未落盘的临时树仅内存实例,
        /// 保存时由 TalentTreeWindow 统一补 AddObjectToAsset。
        /// </summary>
        private TalentNode CreateNodeInstance(int id, string title, Vector2 pos)
        {
            var n = ScriptableObject.CreateInstance<TalentNode>();
            n.name = title;
            n.id = id;
            n.title = title;
            n.editorPosition = pos;
            if (_asset != null && EditorUtility.IsPersistent(_asset))
                AssetDatabase.AddObjectToAsset(n, _asset);
            return n;
        }

        public void AddNode(Vector2 canvasPos)
        {
            if (_asset == null) return;

            // 兜底：若视图已被历史 bug 污染(Pan≈±3.4e38)，CanvasCenter() 也会是垃圾值，
            // 落成的节点坐标同样是垃圾。先把视图修好，再重算落点。
            if (!IsSanePos(canvasPos))
            {
                SanitizeViewAndPositions();
                canvasPos = CanvasCenter() - new Vector2(NODE_W * 0.5f, NODE_H * 0.5f);
                if (!IsSanePos(canvasPos)) canvasPos = new Vector2(40f, 40f);
            }

            RecordUndo("Talent: 添加节点");
            var n = CreateNodeInstance(_asset.NextNodeId(), "新节点", canvasPos);
            _asset.nodes.Add(n);
            Undo.RegisterCreatedObjectUndo(n, "Talent: 添加节点");
            SelectOnly(n.id);
            MarkDirty(true);
        }

        private void AddSuccessor(TalentNode from)
        {
            if (_asset == null || from == null) return;
            RecordUndo("Talent: 添加后继节点");
            var pos = new Vector2(from.editorPosition.x, from.editorPosition.y + NodeHeight(from) + 62f);
            var n = CreateNodeInstance(_asset.NextNodeId(), "新节点", pos);
            _asset.nodes.Add(n);
            Undo.RegisterCreatedObjectUndo(n, "Talent: 添加后继节点");
            _asset.links.Add(new TalentLink(from.id, n.id));
            SelectOnly(n.id);
            MarkDirty(true);
        }

        public void DeleteSelected()
        {
            if (_asset == null || _selectedIds.Count == 0) return;
            RecordUndo("Talent: 删除节点");
            // 先收集要删的节点，从列表摘除后再用 Undo.DestroyObjectImmediate 真正销毁子资源(否则只摘引用、SO 还会残留在 .asset 里)
            var toDel = _asset.nodes.Where(n => n != null && _selectedIds.Contains(n.id)).ToList();
            _asset.nodes.RemoveAll(n => n == null || _selectedIds.Contains(n.id));
            _asset.links.RemoveAll(l => l == null || _selectedIds.Contains(l.from) || _selectedIds.Contains(l.to));
            foreach (var n in toDel) Undo.DestroyObjectImmediate(n);
            _selectedIds.Clear();
            _primaryId = -1;
            MarkDirty(true);
        }

        /// <summary>复制选中节点(含它们之间的连线)并整体偏移落位。</summary>
        public void DuplicateSelected(Vector2 offset)
        {
            if (_asset == null || _selectedIds.Count == 0) return;
            RecordUndo("Talent: 复制节点");

            var map = new Dictionary<int, int>();
            foreach (var id in _selectedIds.ToList())
            {
                var src = _asset.NodeById(id);
                if (src == null) continue;
                var copy = CreateNodeInstance(_asset.NextNodeId(), src.title, src.editorPosition + offset);
                copy.descLocKey = src.descLocKey;
                copy.maxLevel = src.maxLevel;
                foreach (var c in src.costs)
                    if (c != null) copy.costs.Add(new TalentCost(c.attrKey, c.amount));
                _asset.nodes.Add(copy);
                Undo.RegisterCreatedObjectUndo(copy, "Talent: 复制节点");
                map[src.id] = copy.id;
            }
            foreach (var l in _asset.links.ToList())
            {
                if (l != null && map.ContainsKey(l.from) && map.ContainsKey(l.to))
                    _asset.links.Add(new TalentLink(map[l.from], map[l.to]));
            }

            _selectedIds.Clear();
            foreach (var v in map.Values) _selectedIds.Add(v);
            _primaryId = _selectedIds.Count > 0 ? _selectedIds.First() : -1;
            MarkDirty(true);
        }

        private void CopySelected()
        {
            _clipboard.Clear();
            foreach (var id in _selectedIds)
            {
                var n = _asset.NodeById(id);
                if (n != null) _clipboard.Add(n);
            }
        }

        private void PasteAt(Vector2 canvasPos)
        {
            if (_clipboard.Count == 0) return;
            RecordUndo("Talent: 粘贴节点");

            float minX = _clipboard.Min(x => x.editorPosition.x);
            float minY = _clipboard.Min(x => x.editorPosition.y);
            var map = new Dictionary<int, int>();
            foreach (var src in _clipboard)
            {
                var copy = CreateNodeInstance(_asset.NextNodeId(), src.title, canvasPos + (src.editorPosition - new Vector2(minX, minY)));
                copy.descLocKey = src.descLocKey;
                copy.maxLevel = src.maxLevel;
                foreach (var c in src.costs)
                    if (c != null) copy.costs.Add(new TalentCost(c.attrKey, c.amount));
                _asset.nodes.Add(copy);
                Undo.RegisterCreatedObjectUndo(copy, "Talent: 粘贴节点");
                map[src.id] = copy.id;
            }
            foreach (var src in _clipboard)
                foreach (var l in _asset.links.ToList())
                    if (l != null && l.from == src.id && map.ContainsKey(l.to))
                        _asset.links.Add(new TalentLink(map[l.from], map[l.to]));

            _selectedIds.Clear();
            foreach (var v in map.Values) _selectedIds.Add(v);
            _primaryId = _selectedIds.Count > 0 ? _selectedIds.First() : -1;
            MarkDirty(true);
        }

        /// <summary>按最长路径深度分层, 同层横向铺开。</summary>
        public void AutoLayout()
        {
            if (_asset == null || _asset.nodes.Count == 0) return;
            RecordUndo("Talent: 自动布局");

            var depth = new Dictionary<int, int>();
            foreach (var n in _asset.nodes) if (n != null) depth[n.id] = 0;

            var queue = new Queue<int>();
            foreach (var n in _asset.nodes)
            {
                if (n == null) continue;
                bool hasIn = _asset.links.Any(l => l != null && l.to == n.id);
                if (!hasIn) queue.Enqueue(n.id);
            }

            var seen = new HashSet<int>();
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!seen.Add(cur)) continue;
                int d = depth.ContainsKey(cur) ? depth[cur] : 0;
                foreach (var l in _asset.links)
                {
                    if (l == null || l.from != cur) continue;
                    if (!depth.ContainsKey(l.to) || depth[l.to] < d + 1) depth[l.to] = d + 1;
                    queue.Enqueue(l.to);
                }
            }

            float stepX = NODE_W + 34f;
            float stepY = NODE_H_LEVEL + 64f;
            var perLayer = new Dictionary<int, int>();
            foreach (var n in _asset.nodes)
            {
                if (n == null) continue;
                int layer = depth.ContainsKey(n.id) ? depth[n.id] : 0;
                int col;
                perLayer.TryGetValue(layer, out col);
                n.editorPosition = new Vector2(30f + col * stepX, 30f + layer * stepY);
                perLayer[layer] = col + 1;
            }
            MarkDirty(true);
        }

        /// <summary>缩放到刚好装下整张图。</summary>
        public void ZoomToFit()
        {
            if (_asset == null || _asset.nodes.Count == 0) return;
            if (_canvasRect.width <= 1f || _canvasRect.height <= 1f) return;   // 首帧还没布局

            bool any = false;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in _asset.nodes)
            {
                if (n == null || !IsSanePos(n.editorPosition)) continue;
                any = true;
                minX = Mathf.Min(minX, n.editorPosition.x);
                minY = Mathf.Min(minY, n.editorPosition.y);
                maxX = Mathf.Max(maxX, n.editorPosition.x + NODE_W);
                maxY = Mathf.Max(maxY, n.editorPosition.y + NodeHeight(n));
            }

            // 一个可用节点都没有(全失效 / 已被清理) → 复位视图。
            // 绝不能让 min/max 停在 ±MaxValue 上继续算：那会得出 Pan≈±3.4e38、Zoom=0.25，
            // 之后 ScreenToCanvas 与视口裁剪全崩，画布永远空白。
            if (!any)
            {
                Pan = new Vector2(60f, 40f);
                Zoom = 1f;
                OnRepaint?.Invoke();
                return;
            }

            const float pad = 34f;
            float w = (maxX - minX) + pad * 2f;
            float h = (maxY - minY) + pad * 2f;
            Zoom = Mathf.Clamp(Mathf.Min(_canvasRect.width / w, _canvasRect.height / h), 0.25f, 1f);
            Pan = new Vector2(minX - pad, minY - pad);
            OnRepaint?.Invoke();
        }

        /// <summary>把当前视口居中到某个节点(供左栏列表定位)。</summary>
        public void FocusNode(TalentNode n)
        {
            if (n == null) return;
            SelectOnly(n.id);
            Pan = new Vector2(n.editorPosition.x + NODE_W * 0.5f - _canvasRect.width / (2f * Zoom),
                              n.editorPosition.y + NodeHeight(n) * 0.5f - _canvasRect.height / (2f * Zoom));
            OnRepaint?.Invoke();
        }

        /// <summary>当前选中的节点 id 列表(供生成器决定"仅选中"范围)。</summary>
        public List<int> SelectedNodeIds => _selectedIds != null ? _selectedIds.ToList() : new List<int>();

        /// <summary>打开「生成代码 / UI」向导(对应工具栏「生成代码」按钮)。</summary>
        public void OpenGenerateWizard()
        {
            if (_asset == null) return;
            TalentGenerateWizard.Show(_asset, SelectedNodeIds);
        }

        // ============================================================
        //  工具栏
        // ============================================================
        private void DrawToolbar()
        {
            var r = _toolbarRect;
            EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin ? new Color(0.219f, 0.219f, 0.219f) : new Color(0.76f, 0.76f, 0.76f));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), new Color(0f, 0f, 0f, 0.35f));

            float h = r.height - 2f;
            float y = r.y + 1f;
            float x = r.x + 4f;

            if (GUI.Button(new Rect(x, y, 50f, h), "File", EditorStyles.toolbarDropDown)) BuildFileMenu().ShowAsContext();
            x += 50f;
            if (GUI.Button(new Rect(x, y, 50f, h), "Edit", EditorStyles.toolbarDropDown)) BuildEditMenu().ShowAsContext();
            x += 50f;
            if (GUI.Button(new Rect(x, y, 50f, h), "Prefs", EditorStyles.toolbarDropDown)) BuildPrefsMenu().ShowAsContext();
            x += 60f;

            if (GUI.Button(new Rect(x, y, 78f, h), "选中资产", EditorStyles.toolbarButton))
            {
                Selection.activeObject = _asset;
                EditorGUIUtility.PingObject(_asset);
            }
            x += 82f;

            if (GUI.Button(new Rect(x, y, 74f, h), "添加节点", EditorStyles.toolbarButton))
                AddNode(CanvasCenter() - new Vector2(NODE_W * 0.5f, NODE_H * 0.5f));
            x += 78f;

            if (GUI.Button(new Rect(x, y, 62f, h), "自动布局", EditorStyles.toolbarButton)) AutoLayout();
            x += 66f;

            if (GUI.Button(new Rect(x, y, 62f, h), "适应画布", EditorStyles.toolbarButton)) ZoomToFit();
            x += 66f;

            if (GUI.Button(new Rect(x, y, 78f, h), "生成代码", EditorStyles.toolbarButton)) OpenGenerateWizard();
            x += 82f;

            if (GUI.Button(new Rect(x, y, 74f, h), _showAssetsPanel ? "◧ 资源:开" : "◧ 资源:关", EditorStyles.toolbarButton))
            { _showAssetsPanel = !_showAssetsPanel; OnRepaint?.Invoke(); }
            x += 78f;

            if (GUI.Button(new Rect(x, y, 74f, h), _showRightPanel ? "◨ 面板:开" : "◨ 面板:关", EditorStyles.toolbarButton))
            { _showRightPanel = !_showRightPanel; OnRepaint?.Invoke(); }

            // 右侧: 树名 + 来源 + 版本(与 BT 工具栏右端同布局; 窗口太窄就不画, 免得压到按钮上)
            float infoX = r.xMax - 640f;
            if (infoX > x + 10f)
            {
                if (_crumbStyle == null)
                    _crumbStyle = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleRight, fontSize = 11, wordWrap = false };

                string dn = string.IsNullOrEmpty(_asset.displayName) ? _asset.name : _asset.displayName;
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                GUI.Label(new Rect(infoX, y, 300f, h),
                    string.Format("<b>{0}</b> <color=#ff4d4d>(Talent Tree)</color>", dn), _crumbStyle);
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                GUI.Button(new Rect(r.xMax - 330f, y, 240f, h), "TalentTree @ HYC v1.00", EditorStyles.toolbarButton);
                GUI.color = Color.white;
            }
        }

        private GenericMenu BuildFileMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("新建天赋树"), false, () => OnRequestNewTree?.Invoke());
            menu.AddItem(new GUIContent("保存"), false, () => OnRequestSave?.Invoke());
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("清空画布(删除全部节点)"), false, () =>
            {
                if (EditorUtility.DisplayDialog("清空画布", "将删除当前树的全部节点与连线!\n确定?", "YES", "NO!"))
                {
                    RecordUndo("Talent: 清空画布");
                    _asset.nodes.Clear();
                    _asset.links.Clear();
                    _selectedIds.Clear();
                    _primaryId = -1;
                    MarkDirty(true);
                }
            });
            return menu;
        }

        private GenericMenu BuildEditMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("校验树"), false, () =>
            {
                _issues = _asset.nodes.Count == 0 ? new List<TalentIssue>() : TalentTreeValidator.Validate(_asset);
                RemapIssueNodes();
                MarkDirty(false);
            });
            menu.AddItem(new GUIContent("全选节点"), false, () =>
            {
                _selectedIds.Clear();
                foreach (var n in _asset.nodes) if (n != null) _selectedIds.Add(n.id);
                _primaryId = _selectedIds.Count > 0 ? _selectedIds.First() : -1;
                OnRepaint?.Invoke();
            });
            menu.AddItem(new GUIContent("复制选中"), false, () => { CopySelected(); OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("粘贴到视图中心"), false, () => PasteAt(CanvasCenter()));
            menu.AddItem(new GUIContent("删除选中"), false, DeleteSelected);
            return menu;
        }

        private GenericMenu BuildPrefsMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("显示网格"), _showGrid, () => { _showGrid = !_showGrid; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("网格吸附"), _snapGrid, () => { _snapGrid = !_snapGrid; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("显示左栏"), _showAssetsPanel, () => { _showAssetsPanel = !_showAssetsPanel; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("显示右栏"), _showRightPanel, () => { _showRightPanel = !_showRightPanel; OnRepaint?.Invoke(); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("连线样式/Hard"), Mathf.Approximately(_rigidity, 1f), () => { _rigidity = 1f; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("连线样式/Soft"), Mathf.Approximately(_rigidity, 0.75f), () => { _rigidity = 0.75f; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("连线样式/Softer"), Mathf.Approximately(_rigidity, 0.5f), () => { _rigidity = 0.5f; OnRepaint?.Invoke(); });
            menu.AddItem(new GUIContent("连线样式/Direct"), Mathf.Approximately(_rigidity, 0f), () => { _rigidity = 0f; OnRepaint?.Invoke(); });
            return menu;
        }

        // ============================================================
        //  左栏: 天赋树资源
        // ============================================================
        private static List<TalentTreeAsset> FindAllTrees()
        {
            return AssetDatabase.FindAssets("t:" + nameof(TalentTreeAsset))
                .Select(g => AssetDatabase.LoadAssetAtPath<TalentTreeAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null)
                .OrderBy(t => t.name)
                .ToList();
        }

        private void DrawAssetsPanel(Event e, Rect panel)
        {
            EditorGUI.DrawRect(panel, PANEL_BG);
            EditorGUI.DrawRect(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height), new Color(0.25f, 0.25f, 0.3f));

            float pad = 6f;
            float x = panel.x + pad;
            float w = panel.width - pad * 2f - 10f;   // 预留滚动条
            float y = panel.y + 6f;

            GUI.Label(new Rect(x, y, w, SEC_H), "天赋树资源", _headLabel);
            y += SEC_H;

            GUI.SetNextControlName("TalentAssetSearch");
            _assetSearch = EditorGUI.TextField(new Rect(x, y, w, 16f), _assetSearch, (GUIStyle)"ToolbarSearchTextField");
            y += 20f;

            if (GUI.Button(new Rect(x, y, w, LIB_ROW), "＋ 新建天赋树"))
            {
                // 新建/切换资产会重建宿主的 IMGUIContainer, 不能在 OnGUI 回调里直接做 → 推迟到下一拍
                DeferredNewTree();
                e.Use();
            }
            y += LIB_ROW + 4f;

            var all = FindAllTrees();
            string q = _assetSearch == null ? "" : _assetSearch.Trim().ToLowerInvariant();
            if (q.Length > 0)
                all = all.Where(t => NameOf(t).ToLowerInvariant().Contains(q)).ToList();

            float contentTop = y;
            float viewH = Mathf.Max(1f, panel.yMax - contentTop);

            // 当前树若是"未落盘的临时树", 列表里没有它 → 单独顶一条, 免得用户找不到自己在编辑什么
            bool transient = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(_asset));
            int transientRows = transient ? 1 : 0;

            float contentH = transientRows * (ROW_H + 6f) + all.Count * ROW_H + 10f;
            float maxScroll = Mathf.Max(0f, contentH - viewH);
            if (e.type == EventType.ScrollWheel && panel.Contains(_rawMouse))
            {
                _assetsScroll.y = Mathf.Clamp(_assetsScroll.y + e.delta.y * 14f, 0f, maxScroll);
                e.Use();
            }
            _assetsScroll.y = Mathf.Clamp(_assetsScroll.y, 0f, maxScroll);

            float ry = contentTop - _assetsScroll.y;

            if (transient && ry + ROW_H > contentTop && ry < panel.yMax - 2f)
            {
                var rr = new Rect(x, ry + 2f, w, ROW_H);
                EditorGUI.DrawRect(rr, new Color(0.30f, 0.27f, 0.42f));
                EditorGUI.DrawRect(new Rect(rr.x, rr.y, 2f, rr.height), BTEditorStyles.Accent);
                GUI.Label(new Rect(rr.x + 7f, rr.y, rr.width - 10f, rr.height), NameOf(_asset) + "  (未保存)", _rowStyle);
            }
            ry += transientRows * (ROW_H + 6f);

            if (all.Count == 0 && !transient)
            {
                GUI.Label(new Rect(x, ry, w, ROW_H * 2f), "还没有天赋树资产。\n点上面「＋ 新建天赋树」创建一棵。", _subLabel);
                ry += ROW_H * 2f;
            }

            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                string path = AssetDatabase.GetAssetPath(t);
                var rect = new Rect(x, ry, w, ROW_H);
                bool onScreen = rect.yMax > contentTop && rect.y < panel.yMax - 2f;
                bool isCurrent = ReferenceEquals(t, _asset);

                if (onScreen)
                {
                    bool hover = rect.Contains(_rawMouse) && panel.Contains(_rawMouse);
                    if (isCurrent) EditorGUI.DrawRect(rect, new Color(0.24f, 0.24f, 0.32f));
                    else if (hover) EditorGUI.DrawRect(rect, new Color(0.19f, 0.19f, 0.23f));
                    if (isCurrent) EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), BTEditorStyles.Accent);

                    int valid = CountValidNodes(t);
                    string cnt = valid == t.nodes.Count ? valid.ToString() : valid + "/" + t.nodes.Count + " ⚠";
                    var content = new GUIContent(NameOf(t) + "   (" + cnt + ")", path);
                    GUI.Label(new Rect(rect.x + 7f, rect.y, rect.width - 10f, rect.height), content, _rowStyle);

                    // 改名在右栏「天赋树」分组的名称框里做 —— 列表里只负责切换
                    if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(_rawMouse))
                    {
                        if (!isCurrent) DeferredOpen(t);
                        e.Use();
                    }
                }
                ry += ROW_H;
            }

            if (maxScroll > 0f)
            {
                _assetsScroll.y = DrawVScrollbar(panel, viewH, contentH, maxScroll, _assetsScroll.y, e);
            }
        }

        private static string NameOf(TalentTreeAsset t)
            => t == null ? "" : (string.IsNullOrEmpty(t.displayName) ? t.name : t.displayName);

        /// <summary>切换当前树。切资产会重建宿主的 IMGUIContainer, 所以推迟一拍再执行。</summary>
        private void DeferredOpen(TalentTreeAsset t)
            => EditorApplication.delayCall += () => OnRequestOpenTree?.Invoke(t);

        /// <summary>新建天赋树。同上, 推迟一拍。</summary>
        private void DeferredNewTree()
            => EditorApplication.delayCall += () => OnRequestNewTree?.Invoke();

        // ============================================================
        //  右栏: 节点属性 / 黑板 / 运行时
        // ============================================================
        private void DrawRightPanel(Event e, Rect panel)
        {
            EditorGUI.DrawRect(panel, PANEL_BG);
            EditorGUI.DrawRect(new Rect(panel.x, panel.y, 1f, panel.height), new Color(0.25f, 0.25f, 0.3f));

            float pad = 7f;
            float x = panel.x + pad;
            float w = panel.width - pad * 2f - 10f;
            float contentTop = panel.y + 6f;
            float viewH = Mathf.Max(1f, panel.yMax - contentTop);

            float maxScroll = Mathf.Max(0f, _rightContentH - viewH);
            if (e.type == EventType.ScrollWheel && panel.Contains(_rawMouse))
            {
                _rightScroll.y = Mathf.Clamp(_rightScroll.y + e.delta.y * 16f, 0f, maxScroll);
                e.Use();
            }
            _rightScroll.y = Mathf.Clamp(_rightScroll.y, 0f, maxScroll);

            float y = contentTop - _rightScroll.y;
            y = DrawTreeInfoSection(x, y, w);
            y = DrawNodeSection(x, y, w);
            y = DrawBlackboardSection(x, y, w, e);   // 需要 e: 右键清空绑定
            y = DrawRuntimeSection(x, y, w);

            // 内容高用"上一帧实测"回填: 不必维护一份和绘制逐行对齐的测量函数, 永远不会漂移
            _rightContentH = Mathf.Max(viewH, (y + _rightScroll.y) - contentTop + 10f);

            if (maxScroll > 0f)
                _rightScroll.y = DrawVScrollbar(panel, viewH, _rightContentH, Mathf.Max(0f, _rightContentH - viewH), _rightScroll.y, e);
        }

        private float DrawTreeInfoSection(float x, float y, float w)
        {
            y = SectionHeader("tree", "天赋树", x, y, w);
            if (_collapsed.Contains("tree")) return y;

            GUI.Label(new Rect(x, y, LBL_W, LIB_ROW), "名称", _fieldLabel);
            string dn = EditorGUI.TextField(new Rect(x + LBL_W, y, w - LBL_W, LIB_ROW),
                string.IsNullOrEmpty(_asset.displayName) ? _asset.name : _asset.displayName);
            if (dn != _asset.displayName)
            {
                Undo.RecordObject(_asset, "Talent: 修改树名");
                _asset.displayName = dn;
                EditorUtility.SetDirty(_asset);
                OnRepaint?.Invoke();
            }
            y += LIB_ROW + 2f;

            GUI.Label(new Rect(x, y, w, LIB_ROW),
                "treeId " + _asset.treeId + " · 节点 " + _asset.nodes.Count + " · 连线 " + _asset.links.Count,
                _subLabel);
            return y + LIB_ROW + 4f;
        }

        private float DrawNodeSection(float x, float y, float w)
        {
            y = SectionHeader("node", "节点属性", x, y, w);
            if (_collapsed.Contains("node")) return y;

            var node = PrimaryNode;
            if (node == null)
            {
                y = HelpBox(x, y, w, _selectedIds.Count > 1
                    ? "选中了 " + _selectedIds.Count + " 个节点。\n在画布上单击其中一个即可编辑它的属性。"
                    : "未选中节点。\n在画布上点一个节点, 这里会显示它的消耗、等级与前置关系。");
                return y;
            }

            // 注意: 不能每帧无脑 Undo.RecordObject —— 那样每次 Repaint 都往撤销栈塞一条, Ctrl+Z 直接废。
            // 必须「先取草稿值 → 确认真变了 → 再 RecordObject → 再赋值」。
            GUI.Label(new Rect(x, y, LBL_W, LIB_ROW), "名称", _fieldLabel);
            string title = EditorGUI.TextField(new Rect(x + LBL_W, y, w - LBL_W, LIB_ROW), node.title);
            if (title != node.title)
            {
                Undo.RecordObject(_asset, "Talent: 修改节点名称");
                node.title = title;
                EditorUtility.SetDirty(_asset);
                MarkDirty(false);
            }
            y += LIB_ROW + 2f;

            GUI.Label(new Rect(x, y, LBL_W, LIB_ROW), "最高等级", _fieldLabel);
            int lv = EditorGUI.IntField(new Rect(x + LBL_W, y, w - LBL_W, LIB_ROW), node.maxLevel);
            if (lv != node.maxLevel)
            {
                Undo.RecordObject(_asset, "Talent: 修改最高等级");
                node.maxLevel = Mathf.Max(1, lv);
                EditorUtility.SetDirty(_asset);
                MarkDirty(false);
            }
            y += LIB_ROW + 2f;

            // 本地化 Key: **只能选, 不能手填** —— 手填的 key 拼错了要到运行期才发现。
            // 控件用 EditorGUI.DropdownButton + EditorStyles.popup = Unity 自己的弹出字段外观
            // (和 Inspector 里的枚举/层级下拉同一个皮); 下拉列表用 AdvancedDropdown(Unity 原生可搜索面板)。
            GUI.Label(new Rect(x, y, LBL_W, LIB_ROW), "本地化 Key", _fieldLabel);

            bool hasLoc = !string.IsNullOrEmpty(node.descLocKey);
            // 表里没数据时不乱标红: 只有"确实导入了本地化数据"才敢说 key 不存在
            bool locReady = LocAccess.IsLocInstalled && LocAccess.GetKeys().Length > 0;
            bool locWarn = hasLoc && locReady && !LocAccess.HasKey(node.descLocKey);

            var locRect = new Rect(x + LBL_W, y, w - LBL_W, LIB_ROW);
            // 主显示 = 翻译内容(不是 key); 该语言缺值回退到 key。原始 key 作为暗色副行显示在按钮下方(见后)。
            string locContent = hasLoc && locReady ? LocAccess.GetText(node.descLocKey) : null;
            string locLabel = !locReady ? "(未导入本地化数据)"
                            : hasLoc ? (string.IsNullOrEmpty(locContent) ? node.descLocKey : locContent)
                                     : "选择 Key…";
            string locTip = !locReady
                ? "还没导入本地化数据 —— 先执行 HYC Framework/Localization/Import Excel"
                : hasLoc
                    ? (locWarn ? "⚠ 本地化表里没有这个 key"
                               : "当前语言: " + (LocAccess.GetText(node.descLocKey) ?? "(该语言无翻译)"))
                      + "\n点开重选, 第一项「（无）」= 清空"
                    : "从本地化表里挑一个 key(不能手填)";

            using (new EditorGUI.DisabledScope(!locReady))
            {
                if (EditorGUI.DropdownButton(locRect, new GUIContent(locLabel, locTip), FocusType.Keyboard, EditorStyles.popup))
                {
                    LocKeyPickerWindow.OpenDropdown(locRect, picked =>
                    {
                        Undo.RecordObject(_asset, "Talent: 选择本地化 Key");
                        node.descLocKey = picked;
                        EditorUtility.SetDirty(_asset);
                        MarkDirty(false);
                    });
                }
            }
            y += LIB_ROW + 2f;

            // 选中后把原始 key 作为暗色副行显示(主显示是翻译内容, 见上面 locLabel)
            if (hasLoc && locReady)
            {
                var prevC = GUI.color;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                GUI.Label(new Rect(x + LBL_W, y, w - LBL_W, 14f), "key: " + node.descLocKey, EditorStyles.miniLabel);
                GUI.color = prevC;
                y += 16f;
            }

            // 与配置数据编辑器 GUIDrawer 的「⚠ key 不存在」同一套表现: 字段下方一行红字, 不动控件配色
            if (locWarn)
            {
                var warnPrev = GUI.color;
                GUI.color = new Color(1f, 0.45f, 0.45f);
                GUI.Label(new Rect(x + LBL_W, y, w - LBL_W, 15f), "⚠ 本地化表里没有这个 key", EditorStyles.miniLabel);
                GUI.color = warnPrev;
                y += 15f;
            }
            y += 2f;

            // ---- 消耗(引用黑板上的属性) ----
            GUI.Label(new Rect(x, y, w * 0.6f, LIB_ROW), "消耗(黑板属性)", _fieldLabel);
            var firstAttr = _asset.blackboard.FirstOrDefault(a => a != null);
            bool canAdd = firstAttr != null;
            if (GUI.Button(new Rect(x + w - 52f, y, 52f, LIB_ROW), "＋ 添加") && canAdd)
            {
                Undo.RecordObject(_asset, "Talent: 添加消耗");
                node.costs.Add(new TalentCost(firstAttr.key, 1));
                EditorUtility.SetDirty(_asset);
                MarkDirty(false);
            }
            if (!canAdd)
                EditorGUI.DrawRect(new Rect(x + w - 52f, y, 52f, LIB_ROW), new Color(0f, 0f, 0f, 0.25f));
            y += LIB_ROW + 2f;

            if (!canAdd)
            {
                y = HelpBox(x, y, w, "黑板还是空的 —— 先在下面「黑板 · 属性」里加一条属性, 节点才能消耗它。");
            }
            else
            {
                for (int i = 0; i < node.costs.Count; i++)
                {
                    var c = node.costs[i];
                    if (c == null) continue;
                    float delW = 18f;
                    float numW = 46f;
                    float popW = w - delW - numW - 4f;
                    if (popW < 60f) popW = 60f;

                    var keys = _asset.blackboard.Where(a => a != null).Select(a => a.key ?? "").ToArray();
                    int idx = Array.IndexOf(keys, c.attrKey);
                    if (idx < 0) idx = 0;
                    int next = EditorGUI.Popup(new Rect(x, y, popW, LIB_ROW), idx, keys);
                    if (next != idx && next >= 0 && next < keys.Length)
                    {
                        Undo.RecordObject(_asset, "Talent: 修改消耗属性");
                        c.attrKey = keys[next];
                        EditorUtility.SetDirty(_asset);
                        MarkDirty(false);
                    }

                    int amt = EditorGUI.IntField(new Rect(x + popW + 2f, y, numW, LIB_ROW), c.amount);
                    if (amt != c.amount)
                    {
                        Undo.RecordObject(_asset, "Talent: 修改消耗数值");
                        c.amount = amt;
                        EditorUtility.SetDirty(_asset);
                        MarkDirty(false);
                    }

                    if (GUI.Button(new Rect(x + popW + numW + 4f, y, delW, LIB_ROW), "×"))
                    {
                        Undo.RecordObject(_asset, "Talent: 删除消耗");
                        node.costs.RemoveAt(i);
                        EditorUtility.SetDirty(_asset);
                        MarkDirty(false);
                        break;
                    }
                    y += LIB_ROW + 2f;
                }
                if (node.costs.Count == 0)
                {
                    GUI.Label(new Rect(x, y, w, LIB_ROW), "无消耗", _subLabel);
                    y += LIB_ROW + 2f;
                }
            }

            // ---- 连线关系(只读) ----
            y += 4f;
            GUI.Label(new Rect(x, y, w, LIB_ROW), "连线关系", _fieldLabel);
            y += LIB_ROW + 1f;
            var pre = JoinTitles(_asset.PredecessorsOf(node));
            var nxt = JoinTitles(_asset.SuccessorsOf(node));
            y = ReadonlyBox(x, y, w, "前置: " + pre + "\n后继: " + nxt);
            return y + 6f;
        }

        private float DrawBlackboardSection(float x, float y, float w, Event e)
        {
            y = SectionHeader("bb", "黑板 · 属性", x, y, w);
            if (_collapsed.Contains("bb")) return y;

            GUI.Label(new Rect(x, y, w, LIB_ROW), "节点消耗引用这里的 key; 属性本身指向配置表一行。", _subLabel);
            y += LIB_ROW + 1f;

            if (GUI.Button(new Rect(x, y, w, LIB_ROW), "＋ 添加属性")) AddAttributeRow();
            y += LIB_ROW + 2f;

            if (_asset.blackboard.Count == 0)
            {
                y = HelpBox(x, y, w,
                    "黑板是空的。\n\n" +
                    "天赋树的每个节点消耗的是这里的属性, 而属性必须来自配置表 —— " +
                    "点上面「＋ 添加属性」加一行, 再点行里的「选择配置行」绑定到具体那一行。");
                return y;
            }

            for (int i = 0; i < _asset.blackboard.Count; i++)
            {
                var a = _asset.blackboard[i];
                if (a == null) continue;

                float delW = 18f;
                float keyW = Mathf.Max(48f, w * 0.28f);
                float btnW = w - keyW - delW - 4f;

                string key = EditorGUI.TextField(new Rect(x, y, keyW, LIB_ROW), a.key);
                if (key != a.key)
                {
                    Undo.RecordObject(_asset, "Talent: 修改属性 Key");
                    a.key = key;
                    EditorUtility.SetDirty(_asset);
                    MarkDirty(false);
                }

                bool bound = a.sourceRow != null && !a.sourceRow.IsEmpty;
                var btnRect = new Rect(x + keyW + 2f, y, btnW, LIB_ROW);
                var label = bound ? a.sourceRow.ToString() : "选择配置行 →";
                var tip = bound
                    ? a.sourceRow.typeName + "#" + a.sourceRow.id + "\n(右键 = 清空绑定)"
                    : "点这里挑一行配置表数据";
                var prev = GUI.color;
                if (!bound) GUI.color = new Color(1f, 0.85f, 0.55f);
                if (GUI.Button(btnRect, new GUIContent(label, tip), EditorStyles.miniButton))
                {
                    int idx = i;
                    TalentConfigPicker.Show(row =>
                    {
                        var target = _asset != null && idx < _asset.blackboard.Count ? _asset.blackboard[idx] : null;
                        if (target == null) return;
                        Undo.RecordObject(_asset, "Talent: 绑定属性");
                        target.sourceRow = new ConfigRowRef { typeName = row.TypeName, id = row.Id, displayName = row.DisplayName };
                        EditorUtility.SetDirty(_asset);
                        MarkDirty(true);
                    });
                }
                GUI.color = prev;

                // 右键 = 清空这一行的绑定(不影响别的行)。
                // 用 MouseDown/button==1 而非 ContextClick: 与画布右键菜单同一套判定, 且 GUI.Button
                // 只吃 button==0, 右键会正常穿透到这里。
                if (bound && e.type == EventType.MouseDown && e.button == 1 && btnRect.Contains(_rawMouse))
                {
                    Undo.RecordObject(_asset, "Talent: 清空属性绑定");
                    a.sourceRow = new ConfigRowRef();
                    EditorUtility.SetDirty(_asset);
                    MarkDirty(true);
                    e.Use();
                }

                if (GUI.Button(new Rect(x + keyW + btnW + 4f, y, delW, LIB_ROW), "×"))
                {
                    Undo.RecordObject(_asset, "Talent: 删除属性");
                    _asset.blackboard.RemoveAt(i);
                    EditorUtility.SetDirty(_asset);
                    MarkDirty(true);
                    break;
                }

                y += LIB_ROW + 2f;
            }
            return y + 4f;
        }

        private float DrawRuntimeSection(float x, float y, float w)
        {
            y = SectionHeader("rt", "运行时", x, y, w);
            if (_collapsed.Contains("rt")) return y;

            GUI.Label(new Rect(x, y, w, LIB_ROW),
                "treeId " + _asset.treeId + (_asset.treeId == 0 ? "(未生成)" : ""), _libLabel);
            y += LIB_ROW + 2f;

            if (GUI.Button(new Rect(x, y, w, LIB_ROW), "运行期自检(TalentTree.Validate)"))
            {
                var errors = new List<string>();
                TalentTree.Build(_asset).Validate(errors);
                if (errors.Count == 0) Debug.Log("[Talent] 运行期自检通过: " + NameOf(_asset));
                else Debug.LogWarning("[Talent] 运行期自检发现 " + errors.Count + " 个问题:\n" + string.Join("\n", errors.ToArray()));
            }
            y += LIB_ROW + 2f;

            GUI.Label(new Rect(x, y, w, LIB_ROW * 2f),
                "运行期: 用 TalentTree.Build(asset) 建只读视图;\n「谁解锁了什么」由玩法层决定, 框架不管。", _subLabel);
            return y + LIB_ROW * 2f + 6f;
        }

        private void AddAttributeRow()
        {
            if (_asset == null) return;
            Undo.RecordObject(_asset, "Talent: 添加属性");
            int n = _asset.blackboard.Count + 1;
            string key = "Attr" + n;
            while (_asset.AttributeByKey(key) != null) { n++; key = "Attr" + n; }
            _asset.blackboard.Add(new TalentAttribute(key));
            EditorUtility.SetDirty(_asset);
            MarkDirty(true);
        }

        // ---- 右栏小部件 ----
        private float SectionHeader(string key, string title, float x, float y, float w)
        {
            var r = new Rect(x - 7f, y, w + 14f, SEC_H);
            EditorGUI.DrawRect(r, new Color(0.2f, 0.2f, 0.24f));
            bool collapsed = _collapsed.Contains(key);
            GUI.Label(new Rect(x, y + 1f, w, SEC_H), (collapsed ? "▸ " : "▾ ") + title, _headLabel);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(_rawMouse))
            {
                if (collapsed) _collapsed.Remove(key); else _collapsed.Add(key);
                Event.current.Use();
                OnRepaint?.Invoke();
            }
            return y + SEC_H + 4f;
        }

        private float HelpBox(float x, float y, float w, string text)
        {
            // 高度按行数估: 每行 ~14px + 上下留白(比 EditorGUI.HelpBox 实际所需略宽裕, 保证不被裁)
            int lines = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
            float h = Mathf.Max(30f, lines * 14f + 14f);
            EditorGUI.HelpBox(new Rect(x, y, w, h), text, MessageType.Info);
            return y + h + 4f;
        }

        private float ReadonlyBox(float x, float y, float w, string text)
        {
            int lines = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
            float h = lines * 14f + 10f;
            EditorGUI.DrawRect(new Rect(x, y, w, h), new Color(0.16f, 0.16f, 0.19f));
            GUI.Label(new Rect(x + 5f, y + 4f, w - 10f, h - 6f), text, _subLabel);
            return y + h + 2f;
        }

        // ============================================================
        //  底栏
        // ============================================================
        private void DrawBottomBar(Event e, Rect bar)
        {
            EditorGUI.DrawRect(bar, new Color(0.1f, 0.1f, 0.12f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width, 1f), new Color(0.3f, 0.3f, 0.4f));

            int errs = _issues.Count(x => x.IsError);
            int warns = _issues.Count - errs;
            string summary = errs > 0 ? string.Format("✗ {0} 错误, {1} 警告", errs, warns)
                           : warns > 0 ? string.Format("⚠ {0} 警告", warns)
                                       : "✓ 校验通过";
            GUI.Label(new Rect(bar.x + 10f, bar.y + 6f, bar.width * 0.42f, 18f), summary,
                errs > 0 ? _errLabel : (warns > 0 ? _warnLabel : _okLabel));

            int li = 0;
            for (int i = 0; i < _issues.Count && li < 2; i++, li++)
            {
                var iss = _issues[i];
                GUI.Label(new Rect(bar.x + 22f, bar.y + 26f + li * 16f, bar.width * 0.42f - 12f, 16f),
                    (iss.IsError ? "错误: " : "警告: ") + iss.Message, iss.IsError ? _errLabel : _warnLabel);
            }

            string sel = _selectedIds.Count == 0 ? "未选中节点" : "选中 " + _selectedIds.Count + " 个节点";
            GUI.Label(new Rect(bar.x + bar.width * 0.46f, bar.y + 6f, bar.width * 0.26f, 18f), sel, _libLabel);

            string stat = string.Format("节点 {0} · 连线 {1} · 缩放 {2}%",
                _asset.nodes.Count, _asset.links.Count, Mathf.Round(Zoom * 100f));
            GUI.Label(new Rect(bar.x + bar.width * 0.72f, bar.y + 6f, bar.width * 0.28f, 18f), stat, _libLabel);

            GUI.Label(new Rect(bar.x + bar.width * 0.46f, bar.y + 26f, bar.width * 0.52f, 16f),
                "左键框选 · 中键/Alt+左键 平移 · 滚轮缩放 · 右键菜单 · 端口拖线 · Del 删除 · Ctrl+D 复制",
                _tinyDim);
        }

        // ============================================================
        //  滚动条(手绘; 与 BTGraphIMGUI.DrawVScrollbar 同实现)
        // ============================================================
        private float DrawVScrollbar(Rect area, float viewH, float contentH, float maxScroll, float scroll, Event e)
        {
            float sbW = 10f;
            var sbR = new Rect(area.xMax - sbW, area.y, sbW, area.height);
            EditorGUI.DrawRect(sbR, new Color(0.1f, 0.1f, 0.12f));
            float thumbH = Mathf.Max(20f, viewH * viewH / Mathf.Max(1f, contentH));
            float thumbY = area.y + (maxScroll <= 0f ? 0f : (scroll / maxScroll) * (area.height - thumbH));
            var thumbR = new Rect(area.xMax - sbW, thumbY, sbW, thumbH);
            EditorGUI.DrawRect(thumbR, new Color(0.4f, 0.4f, 0.5f));

            var mp = _rawMouse;
            if (e.type == EventType.MouseDown && e.button == 0 && thumbR.Contains(mp))
            {
                _sbDrag = true;
                _sbDragStart = mp.y - thumbY;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _sbDrag)
            {
                scroll = Mathf.Clamp((mp.y - _sbDragStart - area.y) / Mathf.Max(1f, area.height - thumbH) * maxScroll, 0f, maxScroll);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _sbDrag) { _sbDrag = false; e.Use(); }
            else if (e.type == EventType.MouseDown && e.button == 0 && sbR.Contains(mp) && !thumbR.Contains(mp))
            {
                scroll = Mathf.Clamp(scroll + (mp.y < thumbY ? -viewH : viewH), 0f, maxScroll);
                e.Use();
            }
            return scroll;
        }

        // ============================================================
        //  minimap(与 BTGraphIMGUI.DrawMiniMap 同实现)
        // ============================================================
        private void DrawMiniMap(Event e)
        {
            _miniRect = new Rect(_canvasRect.xMax - MM_W - 6f, _canvasRect.yMax - MM_H - 6f, MM_W, MM_H);
            var r = _miniRect;

            var miniCol = BTEditorStyles.Grey(0.5f); miniCol.a = 0.85f;
            GUI.color = miniCol;
            GUI.Box(r, string.Empty, BTEditorStyles.NodeShadowStyle);
            GUI.color = Color.white;
            GUI.Box(r, _asset.nodes.Count > 0 ? string.Empty : "Minimap", BTEditorStyles.Box);

            if (_asset.nodes.Count == 0) return;

            var vTL = ScreenToCanvas(_canvasRect.min);
            var vBR = ScreenToCanvas(_canvasRect.max);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in _asset.nodes)
            {
                if (n == null) continue;
                var nr = NodeRect(n);
                minX = Mathf.Min(minX, nr.xMin); minY = Mathf.Min(minY, nr.yMin);
                maxX = Mathf.Max(maxX, nr.xMax); maxY = Mathf.Max(maxY, nr.yMax);
            }
            minX = Mathf.Min(minX, vTL.x); minY = Mathf.Min(minY, vTL.y);
            maxX = Mathf.Max(maxX, vBR.x); maxY = Mathf.Max(maxY, vBR.y);
            const float pad = 25f;
            minX -= pad; minY -= pad; maxX += pad; maxY += pad;

            float cw = Mathf.Max(1f, maxX - minX), ch = Mathf.Max(1f, maxY - minY);
            float s = Mathf.Min(r.width / cw, r.height / ch);
            float ox = r.x + (r.width - cw * s) * 0.5f;
            float oy = r.y + (r.height - ch * s) * 0.5f;
            _mmMin = new Vector2(minX, minY); _mmScale = s; _mmOrigin = new Vector2(ox, oy);

            Func<float, float> mx = cx => ox + (cx - minX) * s;
            Func<float, float> my = cy => oy + (cy - minY) * s;

            if (e.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                Handles.color = new Color(0.35f, 0.35f, 0.35f);
                foreach (var l in _asset.links)
                {
                    if (l == null) continue;
                    var src = _asset.NodeById(l.from);
                    var dst = _asset.NodeById(l.to);
                    if (src == null || dst == null) continue;
                    var sc = NodeRect(src).center;
                    var dc = NodeRect(dst).center;
                    Handles.DrawLine(new Vector3(mx(sc.x), my(sc.y), 0f), new Vector3(mx(dc.x), my(dc.y), 0f));
                }
                Handles.color = Color.white;
                Handles.EndGUI();

                foreach (var n in _asset.nodes)
                {
                    if (n == null) continue;
                    var nr = NodeRect(n);
                    EditorGUI.DrawRect(new Rect(mx(nr.xMin), my(nr.yMin), Mathf.Max(2f, nr.width * s), Mathf.Max(2f, nr.height * s)),
                        NODE_STRIPE);
                }
            }

            var lens = new Rect(mx(vTL.x), my(vTL.y), (vBR.x - vTL.x) * s, (vBR.y - vTL.y) * s);
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Box(lens, string.Empty, BTEditorStyles.Box);
            GUI.color = Color.white;
        }

        private void NavigateMiniMap(Vector2 mouse)
        {
            if (_mmScale <= 0f) return;
            var local = mouse - _mmOrigin;
            var canvasPoint = _mmMin + local / _mmScale;
            Pan = canvasPoint - _canvasRect.size / (2f * Zoom);
            OnRepaint?.Invoke();
        }

        // ============================================================
        //  校验 → 节点描边映射
        // ============================================================
        private void RemapIssueNodes()
        {
            _errorNodes.Clear();
            _warnNodes.Clear();
            if (_asset == null) return;

            for (int k = 0; k < _issues.Count; k++)
            {
                var issue = _issues[k];
                string msg = issue.Message;
                int s = msg.IndexOf('「');
                int t = msg.IndexOf('」');
                if (s < 0 || t <= s) continue;
                string title = msg.Substring(s + 1, t - s - 1);
                for (int i = 0; i < _asset.nodes.Count; i++)
                {
                    var n = _asset.nodes[i];
                    if (n == null || n.title != title) continue;
                    if (issue.IsError) _errorNodes.Add(n.id);
                    else _warnNodes.Add(n.id);
                }
            }
        }

        // ============================================================
        //  样式
        // ============================================================
        private void EnsureStyles()
        {
            if (_libLabel != null) return;

            _libLabel = new GUIStyle(EditorStyles.label)
            { normal = { textColor = new Color(0.85f, 0.85f, 0.9f) }, fontSize = 11, richText = true };
            _headLabel = new GUIStyle(EditorStyles.boldLabel)
            { normal = { textColor = new Color(0.92f, 0.92f, 1f) }, fontSize = 11, richText = true };
            _subLabel = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(0.58f, 0.62f, 0.72f) }, fontSize = 10, richText = true, wordWrap = true };
            _tinyDim = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(0.45f, 0.47f, 0.54f) }, fontSize = 10 };
            _okLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.5f, 0.85f, 0.45f) }, fontSize = 11 };
            _warnLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.95f, 0.8f, 0.4f) }, fontSize = 11 };
            _errLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.95f, 0.5f, 0.5f) }, fontSize = 11 };

            _fieldLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.62f, 0.68f, 0.78f) },
                fontSize = 10, alignment = TextAnchor.MiddleLeft,
            };

            _headTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.92f, 0.94f, 0.98f) },
                fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
                richText = true, clipping = TextClipping.Clip,
            };
            _headCost = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.78f, 0.82f, 0.92f) },
                fontSize = 10, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip,
            };
            _headSub = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.55f, 0.58f, 0.66f) },
                fontSize = 9, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip,
            };

            _rowStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.82f, 0.84f, 0.9f) },
                fontSize = 11, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
            };
            _rowStyle.hover.textColor = Color.white;
        }
    }
}
