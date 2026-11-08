using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Map.Editor
{
    /// <summary>
    /// 地图编辑器第二半：画布渲染 / 画布输入。
    /// <para>
    /// <b>画布上看到的每一格都是 <see cref="MapView"/> 加载后的样子</b>：逐规则集调
    /// <see cref="MapAutoRules.Evaluate"/> 取瓦片下标，与运行期同一条路径。
    /// 没有「编辑器画色块、进游戏才拼边」的第二套表现，所以规则调错时画布立刻能看出来。
    /// </para>
    /// <para>
    /// <b>只看当前编辑层，其余层当底色</b>：值和值的含义逐层不同（地形是沙/水，通行是可走/阻挡），
    /// 全叠在一起会变成一堆看不出所以然的颜色。所以底色永远画第 0 层（地形），
    /// 正在编辑的层如果不是第 0 层，再叠一层半透明色标出「这一层在本格有什么值」。
    /// 规则层（瓦片）压在最上面，与运行期顺序一致。
    /// </para>
    /// </summary>
    public partial class MapEditorWindow
    {
        private const float BaseCellPx = 18f;

        private Vector2 _canvasScroll;

        // ---------------- 画布 ----------------

        private void DrawCanvas()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            var e = Event.current;
            float cellPx = Mathf.Max(4f, Mathf.Round(BaseCellPx * _zoom));

            _canvasScroll = EditorGUILayout.BeginScrollView(_canvasScroll,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            var area = GUILayoutUtility.GetRect(_asset.width * cellPx, _asset.height * cellPx,
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            if (e.type == EventType.Repaint)
            {
                MapEditorGUI.Fill(area, new Color(0.13f, 0.13f, 0.14f, 1f));
                if (_asset.tileSet != null)
                {
                    DrawGridUnderlay(area, cellPx, 0, 0.85f);
                    if (_gridIndex != 0) DrawGridUnderlay(area, cellPx, _gridIndex, 0.55f);
                    DrawLayers(area, cellPx);
                }
                if (_showGrid) DrawGridLines(area, cellPx);
                DrawProps(area, cellPx);
                DrawRectOverlay(area, cellPx);
                DrawBrushPreview(area, cellPx);
                MapEditorGUI.Outline(area, new Color(0f, 0f, 0f, 0.85f), 2f);
            }

            HandleCanvasInput(area, cellPx, e);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void VisibleRange(float cellPx, out int x0, out int x1, out int y0, out int y1)
        {
            // 视口尺寸用窗口尺寸近似（横向会多画几列，反正被 ScrollView 裁掉）
            float viewW = Mathf.Max(256f, position.width);
            float viewH = Mathf.Max(256f, position.height);

            x0 = Mathf.Clamp(Mathf.FloorToInt(_canvasScroll.x / cellPx) - 1, 0, Mathf.Max(0, _asset.width - 1));
            x1 = Mathf.Clamp(Mathf.CeilToInt((_canvasScroll.x + viewW) / cellPx) + 1, 0, Mathf.Max(0, _asset.width - 1));
            y0 = Mathf.Clamp(Mathf.FloorToInt(_canvasScroll.y / cellPx) - 1, 0, Mathf.Max(0, _asset.height - 1));
            y1 = Mathf.Clamp(Mathf.CeilToInt((_canvasScroll.y + viewH) / cellPx) + 1, 0, Mathf.Max(0, _asset.height - 1));
        }

        /// <summary>
        /// 某一层的值底色。空格不画，露出的深灰就是「没数据」。
        /// <paramref name="alpha"/> 由调用方定：第 0 层当底色画得实，当前编辑层当标注画得淡。
        /// </summary>
        private void DrawGridUnderlay(Rect area, float cellPx, int grid, float alpha)
        {
            var set = _asset.tileSet;
            int h = _asset.height;
            VisibleRange(cellPx, out int x0, out int x1, out int y0, out int y1);

            for (int y = y0; y <= y1; y++)
            {
                float py = area.y + (h - 1 - y) * cellPx;
                for (int x = x0; x <= x1; x++)
                {
                    byte v = ViewCell(grid, x, y);
                    if (v == 0) continue;
                    var value = set.ValueOf(grid, v);
                    var c = value != null ? value.color : new Color(0.55f, 0.2f, 0.55f, 1f);
                    c.a = alpha;
                    MapEditorGUI.Fill(new Rect(area.x + x * cellPx, py, cellPx, cellPx), c);
                }
            }
        }

        /// <summary>
        /// 逐规则集叠瓦片。规则集顺序即图层顺序（0 在最下），与 <see cref="MapView"/> 建层顺序一致。
        /// <para>
        /// 每层读哪份数据由 <see cref="MapAutoRuleSet.sourceGrid"/> 决定——同一条规则写法
        /// 可以作用在地形层，也可以作用在通行层，求值器本身不知道「网格」的存在。
        /// </para>
        /// <para>
        /// 这里每帧现场求值、不建缓存：可见格数受视口限制（万级），规则多数在「比对中心格」那一步就被否掉，
        /// 一次重绘的量级远小于画瓦片本身。加缓存反而要处理「改一格要失效周围 3×3」这类问题。
        /// </para>
        /// </summary>
        private void DrawLayers(Rect area, float cellPx)
        {
            var set = _asset.tileSet;
            var ruleSets = set.ruleSets;
            if (ruleSets == null || ruleSets.Count == 0) return;

            int first = 0, last = ruleSets.Count - 1;
            if (_soloLayer)
            {
                first = Mathf.Clamp(_soloLayerIndex, 0, ruleSets.Count - 1);
                last = first;
            }

            int h = _asset.height;
            VisibleRange(cellPx, out int x0, out int x1, out int y0, out int y1);

            for (int rank = first; rank <= last; rank++)
            {
                var ruleSet = ruleSets[rank];
                if (ruleSet == null || ruleSet.RuleCount() == 0) continue;

                var reader = ReaderOf(ruleSet.sourceGrid);
                if (reader == null) continue;

                for (int y = y0; y <= y1; y++)
                {
                    float py = area.y + (h - 1 - y) * cellPx;
                    for (int x = x0; x <= x1; x++)
                    {
                        int tile = MapAutoRules.Evaluate(ruleSet, reader, x, y);
                        if (tile == MapAutoRules.NoTile) continue;
                        var sprite = set.TileAt(tile);
                        if (sprite == null) continue;   // 没配图：露出下面的值底色，不额外画占位
                        MapEditorGUI.DrawSprite(new Rect(area.x + x * cellPx, py, cellPx, cellPx), sprite, Color.white);
                    }
                }
            }
        }

        private void DrawGridLines(Rect area, float cellPx)
        {
            if (cellPx < 6f) return;
            int w = _asset.width;
            int h = _asset.height;
            VisibleRange(cellPx, out int x0, out int x1, out int y0, out int y1);

            var line = new Color(1f, 1f, 1f, 0.055f);
            for (int x = x0; x <= x1 + 1 && x <= w; x++)
                MapEditorGUI.Fill(new Rect(area.x + x * cellPx, area.y, 1f, area.height), line);
            for (int y = y0; y <= y1 + 1 && y <= h; y++)
                MapEditorGUI.Fill(new Rect(area.x, area.y + y * cellPx, area.width, 1f), line);
        }

        /// <summary>
        /// 画道具标记。<b>按占地矩形画</b>（锚点在左下角），所以 2×2 的树在画布上就是 2×2 的块，
        /// 一眼能看出它压了哪几格、有没有越界。
        /// <para>
        /// 道具是稀疏条目、数量远小于格数，所以整表遍历 + 视口剔除，不建逐格索引。
        /// </para>
        /// </summary>
        private void DrawProps(Rect area, float cellPx)
        {
            var set = _asset.tileSet;
            if (set == null || _asset.props == null || _asset.props.Count == 0) return;

            VisibleRange(cellPx, out int x0, out int x1, out int y0, out int y1);
            float h = _asset.height;

            for (int i = 0; i < _asset.props.Count; i++)
            {
                var p = _asset.props[i];
                if (p == null) continue;

                _asset.PropSize(p.propId, out int pw, out int ph);
                int pxMax = p.x + pw - 1;
                int pyMax = p.y + ph - 1;
                if (pxMax < x0 || p.x > x1 || pyMax < y0 || p.y > y1) continue;

                var bounds = new Rect(
                    area.x + p.x * cellPx,
                    area.y + (h - 1 - pyMax) * cellPx,
                    pw * cellPx,
                    ph * cellPx);

                var tint = PropColorOf(set, p.propId);
                MapEditorGUI.Fill(bounds, new Color(tint.r, tint.g, tint.b, 0.32f));
                MapEditorGUI.Outline(bounds, tint, 1f);

                // 锚点那一格单独标出来：多格物件「到底是哪一格摆下去的」必须一眼可见，
                // 否则删一个 3×3 的树之后不知道删的是哪一棵。
                MapEditorGUI.Fill(new Rect(area.x + p.x * cellPx, area.y + (h - 1 - p.y) * cellPx, cellPx, cellPx),
                    new Color(0f, 0f, 0f, 0.35f));
            }
        }

        /// <summary>道具标记色：优先用瓦片集里登记的颜色；没登记的用品红，一眼看出配置漏了。</summary>
        private static Color PropColorOf(MapTileSet set, int propId)
        {
            if (set != null && set.props != null)
            {
                for (int i = 0; i < set.props.Count; i++)
                {
                    var d = set.props[i];
                    if (d != null && d.id == propId) return d.color;
                }
            }
            return new Color(1f, 0.25f, 0.85f, 0.9f);
        }

        /// <summary>拖动中的矩形。只画框，落地的数据在 <c>_preview</c> 里，画布本身已经按预览渲染。</summary>
        private void DrawRectOverlay(Rect area, float cellPx)
        {
            if (!_rectDragging) return;
            RectBounds(out int x0, out int y0, out int x1, out int y1);
            float h = _asset.height;
            var r = new Rect(
                area.x + x0 * cellPx,
                area.y + (h - 1 - y1) * cellPx,
                (x1 - x0 + 1) * cellPx,
                (y1 - y0 + 1) * cellPx);
            MapEditorGUI.Fill(r, new Color(1f, 0.85f, 0.25f, 0.14f));
            MapEditorGUI.Outline(r, new Color(1f, 0.85f, 0.25f, 0.95f), 2f);
        }

        private void DrawBrushPreview(Rect area, float cellPx)
        {
            if (_hover.x < 0 || _hover.y < 0) return;
            if (_rectDragging || _painting) return;

            float h = _asset.height;
            if (_tool == Tool.Prop)
            {
                // 道具预览按占地画：放不下的位置直接标红，不等点下去才发现没反应
                int propId = SelectedPropId;
                _asset.PropSize(propId, out int pw, out int ph);
                var r = new Rect(
                    area.x + _hover.x * cellPx,
                    area.y + (h - _hover.y - ph) * cellPx,
                    pw * cellPx,
                    ph * cellPx);
                bool fits = propId > 0 && _asset.CanPlaceProp(propId, _hover.x, _hover.y);
                MapEditorGUI.Outline(r, fits
                    ? new Color(1f, 1f, 1f, 0.75f)
                    : new Color(1f, 0.35f, 0.30f, 0.95f), 1f);
            }
            else if (_tool == Tool.Brush || _tool == Tool.Erase)
            {
                int half = (_brushSize - 1) / 2;
                int bx = _hover.x - half;
                int by = _hover.y - half;
                var r = new Rect(
                    area.x + bx * cellPx,
                    area.y + (h - by - _brushSize) * cellPx,
                    _brushSize * cellPx,
                    _brushSize * cellPx);
                MapEditorGUI.Outline(r, new Color(1f, 1f, 1f, 0.75f), 1f);
            }
            else
            {
                var r = new Rect(area.x + _hover.x * cellPx, area.y + (h - 1 - _hover.y) * cellPx, cellPx, cellPx);
                MapEditorGUI.Outline(r, new Color(1f, 1f, 1f, 0.55f), 1f);
            }
        }

        // ---------------- 画布输入 ----------------

        private void HandleCanvasInput(Rect area, float cellPx, Event e)
        {
            bool inside = area.Contains(e.mousePosition);
            int cx = Mathf.FloorToInt((e.mousePosition.x - area.x) / cellPx);
            int cy = _asset.height - 1 - Mathf.FloorToInt((e.mousePosition.y - area.y) / cellPx);

            if (inside && (cx != _hover.x || cy != _hover.y))
            {
                _hover = new Vector2Int(cx, cy);
                if (e.type != EventType.Repaint) Repaint();
            }
            else if (!inside && e.type == EventType.MouseLeaveWindow && _hover.x >= 0)
            {
                _hover = new Vector2Int(-1, -1);
                Repaint();
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!inside || e.button > 1 || _asset.tileSet == null) break;
                    e.Use();
                    if (_tool == Tool.Fill)
                    {
                        FloodFill(cx, cy, e.button == 1 ? (byte)0 : SelectedValue);
                        break;
                    }
                    if (_tool == Tool.Rect)
                    {
                        _rectDragging = true;
                        _rectErase = e.button == 1;
                        _rectAnchor = new Vector2Int(cx, cy);
                        _rectCur = _rectAnchor;
                        RebuildRectPreview();
                        Repaint();
                        break;
                    }
                    if (_tool == Tool.Prop)
                    {
                        BeginStroke(e.button == 1);
                        _lastPaintCell = new Vector2Int(cx, cy);
                        PlacePropAt(cx, cy, _paintingErase);
                        Repaint();
                        break;
                    }
                    BeginStroke(e.button == 1 || _tool == Tool.Erase);
                    _lastPaintCell = new Vector2Int(cx, cy);
                    PaintAt(cx, cy, _paintingErase);
                    Repaint();
                    break;

                case EventType.MouseDrag:
                    if (_painting && e.button <= 1)
                    {
                        var cur = new Vector2Int(cx, cy);
                        if (_lastPaintCell.x == int.MinValue)
                        {
                            if (_paintingProp) PlacePropAt(cx, cy, _paintingErase);
                            else PaintAt(cx, cy, _paintingErase);
                        }
                        else if (cur != _lastPaintCell)
                        {
                            if (_paintingProp) PropLine(_lastPaintCell, cur, _paintingErase);
                            else PaintLine(_lastPaintCell, cur, _paintingErase);
                        }
                        _lastPaintCell = cur;
                        Repaint();
                        e.Use();
                    }
                    else if (_rectDragging && e.button <= 1)
                    {
                        var cur = new Vector2Int(
                            Mathf.Clamp(cx, 0, _asset.width - 1),
                            Mathf.Clamp(cy, 0, _asset.height - 1));
                        if (cur != _rectCur)
                        {
                            _rectCur = cur;
                            RebuildRectPreview();
                            Repaint();
                        }
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (_painting)
                    {
                        EndStroke();
                        e.Use();
                    }
                    else if (_rectDragging)
                    {
                        _rectDragging = false;
                        if (inside) CommitRect();   // 松手才落地
                        else CancelRect();          // 拖出画布 = 放弃，不写数据
                        e.Use();
                    }
                    break;

                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape)
                    {
                        if (_rectDragging) CancelRect();
                        else if (_painting) EndStroke();
                        e.Use();
                    }
                    break;

                case EventType.MouseLeaveWindow:
                    if (_painting) EndStroke();
                    if (_rectDragging) CancelRect();
                    break;
            }
        }
    }
}
