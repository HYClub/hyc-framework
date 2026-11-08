using System.Text;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Map.Editor
{
    /// <summary>
    /// 地图编辑器第三半：瓦片池 / 规则集 / 规则组 / 规则 / 3×3 图案编辑 / 过渡向导。
    /// <para>
    /// <b>为什么规则要能一层层看</b>：一套地图几十条规则，出问题时「哪条没命中」比「规则写错了」更难查。
    /// 所以组可以整组停用、画布可以只看一层、光标信息会逐层报出实际选中的瓦片下标——
    /// 三件事合起来才能把「为什么这格长这样」定位到具体一条规则。
    /// </para>
    /// <para>
    /// <b>过渡向导</b>照搬 LDtk Rules Wizard 的思路：填 16 张图，自动生成 16 条规则。
    /// 那 16 条正好覆盖「本材质 A 与邻接材质 B」的四种边全组合，也就是业界所说的 16 张 Wang / 过渡集，
    /// 每张过渡图都是<b>与 A、B 同尺寸的一整格</b>（不是半格窄条）。
    /// </para>
    /// </summary>
    public partial class MapEditorWindow
    {
        private Vector2 _candScroll;

        [SerializeField] private int _wizMaterialA;
        [SerializeField] private int _wizMaterialB = 1;
        [SerializeField] private string _wizGroupName = "过渡";
        [SerializeField] private Sprite[] _wizTiles = new Sprite[WizardTileCount];

        /// <summary>过渡集张数：四种边各自「邻格是不是 B」，2⁴ = 16。</summary>
        private const int WizardTileCount = 16;

        // ---------------- 面板入口 ----------------

        private void DrawRulesPanel()
        {
            var set = _asset != null ? _asset.tileSet : null;
            if (set == null) return;

            // 瓦片集可能在别处被改过（Inspector/撤销），每次进面板都夹一遍下标，避免越界
            ClampSelection();

            EditorGUILayout.Space(8f);
            DrawTilePoolSection(set);
            DrawRuleSetSection(set);
            DrawGroupSection(set);
            DrawRuleListSection(set);
            DrawSelectedRuleSection(set);
            DrawWizardSection(set);
        }

        // ---------------- 瓦片池 ----------------

        private void DrawTilePoolSection(MapTileSet set)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"瓦片池（{set.tiles.Count} 张）", EditorStyles.boldLabel);
            if (GUILayout.Button("＋选中", EditorStyles.miniButton, GUILayout.Width(52f)))
            {
                int added = MapEditorUtil.AddSelectedSprites(set);
                if (added > 0)
                {
                    EditorUtility.SetDirty(set);
                    Repaint();
                }
                else
                {
                    Debug.Log("[Map] 没有可加的 Sprite：先在 Project 窗口选中 Sprite 或 Sprite 子资产。");
                }
            }
            if (GUILayout.Button("＋空位", EditorStyles.miniButton, GUILayout.Width(52f)))
            {
                Undo.RecordObject(set, "Add Tile Slot");
                set.tiles.Add(null);
                EditorUtility.SetDirty(set);
            }
            if (GUILayout.Button("去空槽", EditorStyles.miniButton, GUILayout.Width(56f)))
                RemoveEmptyTileSlots(set);
            EditorGUILayout.EndHorizontal();

            if (set.tiles.Count == 0)
            {
                EditorGUILayout.HelpBox("瓦片池是空的：画布只会显示材质底色，规则也没有瓦片可引用。", MessageType.Warning);
                return;
            }

            _poolScroll = EditorGUILayout.BeginScrollView(_poolScroll, GUILayout.Height(108f));
            int removeAt = -1;
            for (int i = 0; i < set.tiles.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(i.ToString(), EditorStyles.miniLabel, GUILayout.Width(26f));

                EditorGUI.BeginChangeCheck();
                var sprite = (Sprite)EditorGUILayout.ObjectField(set.tiles[i], typeof(Sprite), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(set, "Edit Tile Pool");
                    set.tiles[i] = sprite;
                    EditorUtility.SetDirty(set);
                }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20f)))
                    removeAt = i;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            // 删除延到循环之后：循环中改集合会让本帧控件数量与列表长度错位
            if (removeAt >= 0 && removeAt < set.tiles.Count)
            {
                Undo.RecordObject(set, "Remove Tile");
                set.tiles.RemoveAt(removeAt);
                EditorUtility.SetDirty(set);
                Repaint();
            }
        }

        /// <summary>
        /// 去掉空槽。<b>会改动所有引用它的下标</b>，所以必须同步重映射规则里的瓦片下标，
        /// 否则「清一下空槽」会把整张地图的图案换掉。
        /// </summary>
        private static void RemoveEmptyTileSlots(MapTileSet set)
        {
            if (set == null || set.tiles == null || set.tiles.Count == 0) return;

            var remap = new int[set.tiles.Count];
            var kept = new System.Collections.Generic.List<Sprite>(set.tiles.Count);
            for (int i = 0; i < set.tiles.Count; i++)
            {
                if (set.tiles[i] == null)
                {
                    remap[i] = -1;
                    continue;
                }
                remap[i] = kept.Count;
                kept.Add(set.tiles[i]);
            }
            if (kept.Count == set.tiles.Count) return;   // 没有空槽

            Undo.RecordObject(set, "Compact Tile Pool");
            set.tiles.Clear();
            set.tiles.AddRange(kept);

            if (set.ruleSets == null) return;
            for (int s = 0; s < set.ruleSets.Count; s++)
            {
                var ruleSet = set.ruleSets[s];
                if (ruleSet == null || ruleSet.groups == null) continue;
                for (int g = 0; g < ruleSet.groups.Count; g++)
                {
                    var group = ruleSet.groups[g];
                    if (group == null || group.rules == null) continue;
                    for (int r = 0; r < group.rules.Count; r++)
                    {
                        var rule = group.rules[r];
                        if (rule == null || rule.tiles == null) continue;
                        for (int t = 0; t < rule.tiles.Length; t++)
                        {
                            int old = rule.tiles[t];
                            rule.tiles[t] = (uint)old < (uint)remap.Length ? remap[old] : -1;
                        }
                    }
                }
            }
            EditorUtility.SetDirty(set);
        }

        // ---------------- 规则集（图层） ----------------

        private void DrawRuleSetSection(MapTileSet set)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"规则集 · 图层（{set.ruleSets.Count}）", EditorStyles.boldLabel);
            if (GUILayout.Button("＋", EditorStyles.miniButton, GUILayout.Width(24f)))
            {
                Undo.RecordObject(set, "Add Rule Set");
                var ruleSet = new MapAutoRuleSet { name = $"层{set.ruleSets.Count}" };
                ruleSet.groups.Add(new MapAutoRuleGroup { name = "基础" });
                set.ruleSets.Add(ruleSet);
                _ruleSetIndex = set.ruleSets.Count - 1;
                _groupIndex = 0;
                _ruleIndex = 0;
                EditorUtility.SetDirty(set);
            }
            if (GUILayout.Button("－", EditorStyles.miniButton, GUILayout.Width(24f)) && set.ruleSets.Count > 0)
            {
                if (EditorUtility.DisplayDialog("删除规则集",
                    $"删掉图层「{set.ruleSets[_ruleSetIndex].name}」？这一层的所有规则都会丢失。", "删除", "取消"))
                {
                    Undo.RecordObject(set, "Remove Rule Set");
                    set.ruleSets.RemoveAt(_ruleSetIndex);
                    ClampSelection();
                    EditorUtility.SetDirty(set);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (set.ruleSets.Count == 0)
            {
                EditorGUILayout.HelpBox("没有规则集：地图加载后啥也不铺。点「＋」建一层。", MessageType.Warning);
                return;
            }

            var ruleSets = set.ruleSets;
            var labels = new string[ruleSets.Count];
            for (int i = 0; i < ruleSets.Count; i++)
                labels[i] = ruleSets[i] != null ? $"{i}. {ruleSets[i].name}" : $"{i}. (空槽)";

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup("当前图层", _ruleSetIndex, labels);
            if (EditorGUI.EndChangeCheck())
            {
                _ruleSetIndex = picked;
                _groupIndex = 0;
                _ruleIndex = 0;
                ClampSelection();
            }

            var current = ruleSets[_ruleSetIndex];
            if (current == null) return;

            var gridLabels = new string[set.GridCount];
            for (int g = 0; g < gridLabels.Length; g++) gridLabels[g] = $"{g}. {set.GridName(g)}";

            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("层名", current.name);
            bool collider = EditorGUILayout.Toggle(
                new GUIContent("生成碰撞", "只应有一层勾上。碰撞只挂在不可走的格子上。"),
                current.contributesCollider);
            int sourceGrid = EditorGUILayout.Popup(
                new GUIContent("读哪一层", "这一层的规则看哪份网格数据。多个层读同一层是常规用法——" +
                                          "一个网格可以铺好几层视觉（河流压在草地上）。"),
                Mathf.Clamp(current.sourceGrid, 0, set.GridCount - 1), gridLabels);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(set, "Edit Rule Set");
                current.name = name;
                current.contributesCollider = collider;
                current.sourceGrid = sourceGrid;
                EditorUtility.SetDirty(set);
                Repaint();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("只看本层", EditorStyles.miniButton, GUILayout.Width(72f)))
            {
                _soloLayer = true;
                _soloLayerIndex = _ruleSetIndex;
                Repaint();
            }
            if (GUILayout.Button("看全部层", EditorStyles.miniButton, GUILayout.Width(72f)))
            {
                _soloLayer = false;
                Repaint();
            }
            if (GUILayout.Button(new GUIContent("排序本层", "所有组内的规则都按约束位数降序重排（稳定）。"),
                EditorStyles.miniButton, GUILayout.Width(64f)))
                SortRuleSet(set, current);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{current.RuleCount()} 条", EditorStyles.miniLabel, GUILayout.Width(48f));
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 把某一层<b>所有组内</b>的规则按特化度重排。
        /// <para>
        /// 只排组内、不动组顺序——组顺序本身是「一套过渡压另一套」的语义，
        /// 打乱它会把「先铺过渡再铺底色」这种依赖关系破坏掉。
        /// </para>
        /// </summary>
        private void SortRuleSet(MapTileSet set, MapAutoRuleSet ruleSet)
        {
            if (ruleSet == null || ruleSet.groups == null) return;

            Undo.RecordObject(set, "Sort Rules");
            int moved = 0;
            for (int g = 0; g < ruleSet.groups.Count; g++)
            {
                var group = ruleSet.groups[g];
                if (group == null || group.rules == null) continue;
                moved += MapAutoRules.SortBySpecificity(group.rules);
            }
            EditorUtility.SetDirty(set);
            Repaint();
            Debug.Log(moved == 0
                ? $"[Map] 图层「{ruleSet.name}」的规则本来就有序，未移动。"
                : $"[Map] 图层「{ruleSet.name}」按特化度重排：{moved} 条规则换了位置。");
        }

        // ---------------- 规则组 ----------------

        private void DrawGroupSection(MapTileSet set)
        {
            if (set.ruleSets.Count == 0) return;
            var ruleSet = set.ruleSets[_ruleSetIndex];
            if (ruleSet == null) return;

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"规则组（{ruleSet.groups.Count}）", EditorStyles.boldLabel);
            if (GUILayout.Button("＋", EditorStyles.miniButton, GUILayout.Width(24f)))
            {
                Undo.RecordObject(set, "Add Rule Group");
                ruleSet.groups.Add(new MapAutoRuleGroup { name = $"组{ruleSet.groups.Count}" });
                _groupIndex = ruleSet.groups.Count - 1;
                _ruleIndex = 0;
                EditorUtility.SetDirty(set);
            }
            if (GUILayout.Button("－", EditorStyles.miniButton, GUILayout.Width(24f)) && ruleSet.groups.Count > 0)
            {
                Undo.RecordObject(set, "Remove Rule Group");
                ruleSet.groups.RemoveAt(_groupIndex);
                ClampSelection();
                EditorUtility.SetDirty(set);
            }
            EditorGUILayout.EndHorizontal();

            if (ruleSet.groups.Count == 0)
            {
                EditorGUILayout.HelpBox("没有规则组。点「＋」建一组，规则都放在组里。", MessageType.Info);
                return;
            }

            // 组行：勾选 = 整组生效；点名字 = 选中
            for (int g = 0; g < ruleSet.groups.Count; g++)
            {
                var group = ruleSet.groups[g];
                if (group == null) continue;
                bool selected = _groupIndex == g;

                var row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                    MapEditorGUI.Fill(row, selected
                        ? new Color(0.24f, 0.49f, 0.90f, 0.35f)
                        : new Color(0f, 0f, 0f, 0f));

                var toggleRect = new Rect(row.x + 2f, row.y + 4f, 16f, 16f);
                EditorGUI.BeginChangeCheck();
                bool active = EditorGUI.Toggle(toggleRect, group.active);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(set, "Toggle Rule Group");
                    group.active = active;
                    EditorUtility.SetDirty(set);
                    Repaint();
                }

                EditorGUI.LabelField(new Rect(row.x + 22f, row.y + 3f, row.width - 100f, 16f),
                    string.IsNullOrEmpty(group.name) ? "(未命名组)" : group.name);
                EditorGUI.LabelField(new Rect(row.xMax - 74f, row.y + 3f, 72f, 16f),
                    $"{group.rules.Count} 条", EditorStyles.miniLabel);

                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition)
                    && !toggleRect.Contains(Event.current.mousePosition))
                {
                    _groupIndex = g;
                    _ruleIndex = 0;
                    ClampSelection();
                    GUI.changed = true;
                    Event.current.Use();
                }
            }

            var cur = ruleSet.groups[_groupIndex];
            if (cur == null) return;
            EditorGUI.BeginChangeCheck();
            string groupName = EditorGUILayout.TextField("组名", cur.name);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(set, "Rename Rule Group");
                cur.name = groupName;
                EditorUtility.SetDirty(set);
            }
        }

        // ---------------- 规则列表 ----------------

        private void DrawRuleListSection(MapTileSet set)
        {
            if (set.ruleSets.Count == 0) return;
            var ruleSet = set.ruleSets[_ruleSetIndex];
            if (ruleSet == null || ruleSet.groups.Count == 0) return;
            var group = ruleSet.groups[_groupIndex];
            if (group == null) return;

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"规则（{group.rules.Count}）", EditorStyles.boldLabel);
            if (GUILayout.Button("＋ 新规则", EditorStyles.miniButton, GUILayout.Width(76f)))
            {
                Undo.RecordObject(set, "Add Rule");
                var rule = new MapAutoRule();
                // 新规则默认「中心 = 当前选中值」，这是九成情况下的起点
                rule.pattern[MapRulePattern.Center] = (sbyte)CurrentValueIn(set, ruleSet.sourceGrid);
                group.rules.Add(rule);
                _ruleIndex = group.rules.Count - 1;
                EditorUtility.SetDirty(set);
            }
            if (GUILayout.Button(new GUIContent("按特化度排序", "约束位数多的排前面：越具体的先命中，通配兜底自然落到最后。"),
                EditorStyles.miniButton, GUILayout.Width(96f)))
            {
                Undo.RecordObject(set, "Sort Group Rules");
                int sorted = MapAutoRules.SortBySpecificity(group.rules);
                EditorUtility.SetDirty(set);
                Repaint();
                Debug.Log(sorted == 0
                    ? $"[Map] 组「{group.name}」的规则本来就有序，未移动。"
                    : $"[Map] 组「{group.name}」按特化度重排：{sorted} 条规则换了位置。");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("顺序即优先级：靠前的先命中，兜底规则放最后。", EditorStyles.miniLabel);

            int moveFrom = -1, moveTo = -1, removeAt = -1;
            for (int r = 0; r < group.rules.Count; r++)
            {
                var rule = group.rules[r];
                if (rule == null) continue;
                bool selected = _ruleIndex == r;

                var row = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
                var thumb = new Rect(row.x + 2f, row.y + 3f, 22f, 22f);

                if (Event.current.type == EventType.Repaint)
                {
                    MapEditorGUI.Fill(row, selected
                        ? new Color(0.24f, 0.49f, 0.90f, 0.35f)
                        : new Color(0f, 0f, 0f, 0f));
                    DrawMiniPattern(thumb, rule.pattern, set, ruleSet.sourceGrid);
                }

                int tileCount = rule.tiles != null ? rule.tiles.Length : 0;
                string label = $"#{r} · {tileCount} 张 · {rule.ConstraintCount()} 位";
                if (rule.chance < 1f) label += $" · {rule.chance * 100f:0}%";
                EditorGUI.LabelField(new Rect(row.x + 28f, row.y + 6f, row.width - 118f, 16f),
                    label, EditorStyles.miniLabel);

                if (GUI.Button(new Rect(row.xMax - 86f, row.y + 4f, 18f, 18f), "↑", EditorStyles.miniButton))
                {
                    moveFrom = r;
                    moveTo = r - 1;
                }
                if (GUI.Button(new Rect(row.xMax - 66f, row.y + 4f, 18f, 18f), "↓", EditorStyles.miniButton))
                {
                    moveFrom = r;
                    moveTo = r + 1;
                }
                if (GUI.Button(new Rect(row.xMax - 46f, row.y + 4f, 18f, 18f), "×", EditorStyles.miniButton))
                    removeAt = r;

                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition)
                    && Event.current.mousePosition.x < row.xMax - 90f)
                {
                    _ruleIndex = r;
                    GUI.changed = true;
                    Event.current.Use();
                }
            }

            if (moveFrom >= 0 && moveTo >= 0 && moveTo < group.rules.Count && moveTo != moveFrom)
            {
                Undo.RecordObject(set, "Reorder Rule");
                var moved = group.rules[moveFrom];
                group.rules.RemoveAt(moveFrom);
                group.rules.Insert(moveTo, moved);
                _ruleIndex = moveTo;
                EditorUtility.SetDirty(set);
            }
            else if (removeAt >= 0)
            {
                Undo.RecordObject(set, "Remove Rule");
                group.rules.RemoveAt(removeAt);
                ClampSelection();
                EditorUtility.SetDirty(set);
            }
        }

        /// <summary>规则行里的 3×3 缩略图：格色即要求的格子值色，深色 = 必须为空，近底色 = 通配。</summary>
        private static void DrawMiniPattern(Rect area, sbyte[] pattern, MapTileSet set, int grid)
        {
            float cs = area.width / 3f;
            for (int i = 0; i < MapRulePattern.Length; i++)
            {
                int col = i % 3, row = i / 3;
                var cell = new Rect(area.x + col * cs, area.y + row * cs, cs - 0.5f, cs - 0.5f);
                sbyte v = pattern != null && i < pattern.Length ? pattern[i] : MapRulePattern.Any;

                Color c;
                if (v == MapRulePattern.Any) c = new Color(0.20f, 0.20f, 0.21f, 1f);
                else if (v == 0) c = new Color(0.05f, 0.05f, 0.06f, 1f);
                else c = MapEditorUtil.ColorOf(set, grid, v - 1);

                MapEditorGUI.Fill(cell, c);
            }
            MapEditorGUI.Outline(area, new Color(0f, 0f, 0f, 0.6f), 1f);
        }

        // ---------------- 选中规则 ----------------

        private void DrawSelectedRuleSection(MapTileSet set)
        {
            if (set.ruleSets.Count == 0) return;
            var ruleSet = set.ruleSets[_ruleSetIndex];
            if (ruleSet == null || ruleSet.groups.Count == 0) return;
            var group = ruleSet.groups[_groupIndex];
            if (group == null || group.rules.Count == 0) return;
            var rule = group.rules[_ruleIndex];
            if (rule == null) return;

            int grid = ruleSet.sourceGrid;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"规则 #{_ruleIndex}（读「{set.GridName(grid)}」）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("3×3 邻域：左键下一档 / 右键上一档", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("值循环：通配 → 空 → 该层各值", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            var pattern = rule.pattern;
            if (pattern == null || pattern.Length != MapRulePattern.Length)
            {
                pattern = MapRulePattern.NewPattern();
                rule.pattern = pattern;
            }

            bool changed = DrawPatternGrid(pattern, set, grid, 38f);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(
                $"中心 = {DescribePatternValue(set, grid, pattern[MapRulePattern.Center])}", EditorStyles.miniLabel);
            if (GUILayout.Button("中心 = 当前值", EditorStyles.miniButton))
            {
                Undo.RecordObject(set, "Set Rule Center");
                pattern[MapRulePattern.Center] = (sbyte)CurrentValueIn(set, grid);
                changed = true;
            }
            if (GUILayout.Button("全清为通配", EditorStyles.miniButton))
            {
                Undo.RecordObject(set, "Clear Rule Pattern");
                for (int i = 0; i < pattern.Length; i++) pattern[i] = MapRulePattern.Any;
                changed = true;
            }
            if (GUILayout.Button("四边 = 中心", EditorStyles.miniButton))
            {
                // 常见起点：正交四邻都要和自己同材质（「一片连续区域」的兜底瓦片）
                Undo.RecordObject(set, "Fill Rule Edges");
                sbyte c = pattern[MapRulePattern.Center];
                pattern[MapRulePattern.Index(0, 1)] = c;
                pattern[MapRulePattern.Index(1, 0)] = c;
                pattern[MapRulePattern.Index(0, -1)] = c;
                pattern[MapRulePattern.Index(-1, 0)] = c;
                changed = true;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (changed)
            {
                EditorUtility.SetDirty(set);
                Repaint();
            }

            EditorGUILayout.Space(4f);
            int candidateCount = rule.tiles != null ? rule.tiles.Length : 0;
            EditorGUILayout.LabelField($"命中瓦片（{candidateCount} 张，多张按坐标散列稳定挑）", EditorStyles.miniLabel);
            if (DrawTileCandidates(set, rule)) EditorUtility.SetDirty(set);

            EditorGUI.BeginChangeCheck();
            float chance = EditorGUILayout.Slider(
                new GUIContent("命中概率", "小于 1 时按格坐标散列判定，每次加载结果一致，不会随机漂移。"),
                rule.chance, 0f, 1f);
            bool breakOnMatch = EditorGUILayout.Toggle(
                new GUIContent("命中即停", "关掉则继续往后匹配，后一条会覆盖这一条的结果。"),
                rule.breakOnMatch);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(set, "Edit Rule");
                rule.chance = chance;
                rule.breakOnMatch = breakOnMatch;
                EditorUtility.SetDirty(set);
            }
        }

        /// <summary>3×3 图案编辑器。返回是否被改过。</summary>
        private bool DrawPatternGrid(sbyte[] pattern, MapTileSet set, int grid, float cs)
        {
            bool changed = false;
            var block = GUILayoutUtility.GetRect(cs * 3f, cs * 3f,
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            for (int i = 0; i < MapRulePattern.Length; i++)
            {
                int col = i % 3, row = i / 3;
                var cell = new Rect(block.x + col * cs, block.y + row * cs, cs - 2f, cs - 2f);
                sbyte v = pattern[i];
                bool isCenter = i == MapRulePattern.Center;

                if (Event.current.type == EventType.Repaint)
                {
                    MapEditorGUI.Fill(cell, isCenter
                        ? new Color(0.28f, 0.28f, 0.30f, 1f)
                        : new Color(0.19f, 0.19f, 0.21f, 1f));

                    if (v == MapRulePattern.Any)
                    {
                        MapEditorGUI.Fill(
                            new Rect(cell.center.x - 1.5f, cell.center.y - 1.5f, 3f, 3f),
                            new Color(1f, 1f, 1f, 0.35f));
                    }
                    else if (v == 0)
                    {
                        MapEditorGUI.Fill(cell, new Color(0.07f, 0.07f, 0.08f, 1f));
                    }
                    else
                    {
                        MapEditorGUI.Fill(cell, MapEditorUtil.ColorOf(set, grid, v - 1));
                    }

                    MapEditorGUI.Outline(cell,
                        isCenter ? new Color(1f, 0.85f, 0.25f, 0.95f) : new Color(0f, 0f, 0f, 0.55f),
                        isCenter ? 2f : 1f);
                }

                if (Event.current.type == EventType.MouseDown && Event.current.button <= 1
                    && cell.Contains(Event.current.mousePosition))
                {
                    pattern[i] = CyclePatternValue(v, Event.current.button == 0 ? 1 : -1, set, grid);
                    changed = true;
                    Event.current.Use();
                }
            }
            return changed;
        }

        /// <summary>在「通配 / 空 / 该层各值」之间循环。</summary>
        private static sbyte CyclePatternValue(sbyte current, int step, MapTileSet set, int grid)
        {
            int valueCount = set != null ? set.ValueCount(grid) : 0;
            int length = valueCount + 2;                    // -1 通配 + 0 空 + 值 1..N
            int index = Mathf.Clamp(current + 1, 0, length - 1);
            index = (index + step + length) % length;
            return (sbyte)(index - 1);
        }

        private static string DescribePatternValue(MapTileSet set, int grid, sbyte v)
        {
            if (v == MapRulePattern.Any) return "通配";
            if (v == 0) return "空";
            var value = set != null ? set.ValueOf(grid, (byte)v) : null;
            return value != null ? $"{value.displayName}（{value.key}）" : $"值 {v}";
        }

        /// <summary>
        /// 某个规则集读的那一层里，「当前选中序号」对应的格子值。
        /// <para>
        /// 序号跨层复用：地形层选中第 3 个值，切到通行层的规则上就是通行层的第 3 个值。
        /// 越界时夹到该层末尾——宁可给一个真实存在的值，也不要写个 0（空）进图案，
        /// 那会让「中心 = 当前值」这个按钮看起来点坏了。
        /// </para>
        /// </summary>
        private byte CurrentValueIn(MapTileSet set, int grid)
        {
            if (set == null) return 0;
            int count = set.ValueCount(grid);
            if (count <= 0) return 0;
            return set.CellValueOf(grid, Mathf.Clamp(_valueIndex, 0, count - 1));
        }

        /// <summary>瓦片池横条：点一下把这张瓦片加入/移出本规则的候选池。返回是否改过。</summary>
        private bool DrawTileCandidates(MapTileSet set, MapAutoRule rule)
        {
            if (set.tiles.Count == 0)
            {
                EditorGUILayout.HelpBox("瓦片池是空的，先去上面加瓦片。", MessageType.Info);
                return false;
            }

            bool changed = false;
            const float cell = 46f;
            _candScroll = EditorGUILayout.BeginScrollView(_candScroll, GUILayout.Height(cell + 18f));
            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < set.tiles.Count; i++)
            {
                var rect = GUILayoutUtility.GetRect(cell, cell, GUILayout.Width(cell), GUILayout.Height(cell));
                bool inPool = Contains(rule.tiles, i);

                if (Event.current.type == EventType.Repaint)
                {
                    MapEditorGUI.DrawThumb(rect, set.tiles[i], new Color(0.45f, 0.45f, 0.47f, 1f));
                    if (inPool)
                    {
                        MapEditorGUI.Outline(rect, new Color(0.30f, 0.75f, 1f, 1f), 2f);
                        MapEditorGUI.Fill(new Rect(rect.xMax - 12f, rect.y, 12f, 12f),
                            new Color(0.30f, 0.75f, 1f, 0.95f));
                    }
                    GUI.Label(new Rect(rect.x + 2f, rect.yMax - 13f, rect.width - 4f, 12f),
                        i.ToString(), EditorStyles.miniLabel);
                }

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    Undo.RecordObject(set, "Edit Rule Tiles");
                    rule.tiles = ToggleTile(rule.tiles, i);
                    changed = true;
                    Event.current.Use();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
            return changed;
        }

        private static bool Contains(int[] arr, int value)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == value) return true;
            return false;
        }

        /// <summary>候选池增删。<b>保持升序</b>，这样无论点的顺序，最终数组都一样（diff 友好）。</summary>
        private static int[] ToggleTile(int[] tiles, int index)
        {
            if (tiles == null) tiles = new int[0];
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] != index) continue;
                var shorter = new int[tiles.Length - 1];
                for (int j = 0, k = 0; j < tiles.Length; j++)
                    if (j != i) shorter[k++] = tiles[j];
                return shorter;
            }
            var longer = new int[tiles.Length + 1];
            longer[0] = index;
            for (int i = 0; i < tiles.Length; i++) longer[i + 1] = tiles[i];
            System.Array.Sort(longer);
            return longer;
        }

        // ---------------- 过渡向导 ----------------

        private void DrawWizardSection(MapTileSet set)
        {
            EditorGUILayout.Space(8f);
            _showWizard = EditorGUILayout.Foldout(_showWizard, "过渡向导（A 邻接 B → 16 张过渡）", true);
            if (!_showWizard) return;

            EditorGUILayout.HelpBox(
                "填 16 张过渡瓦片，自动生成 16 条规则：中心固定是 A，四条边各自「是 B 还是 A」。\n" +
                "位序 N=1 / E=2 / S=4 / W=8。生成的组会插在最前面（过渡优先于底色规则）。",
                MessageType.None);

            if (set.ruleSets.Count == 0)
            {
                EditorGUILayout.HelpBox("先建一个规则集（图层），向导把规则生成到当前图层。", MessageType.Warning);
                return;
            }

            // 过渡规则会落进「当前规则集」，所以 A/B 必须是那一层读的网格里的值，
            // 不能固定取地形层——否则在通行层上生成的规则里全是错的格子值，一条都不会命中。
            var target = set.ruleSets[_ruleSetIndex];
            int grid = target != null ? target.sourceGrid : 0;

            if (set.ValueCount(grid) < 2)
            {
                EditorGUILayout.HelpBox($"「{set.GridName(grid)}」少于两个值，做不了 A↔B 过渡。", MessageType.Warning);
                return;
            }

            var labels = MapEditorUtil.ValueLabels(set, grid, false);
            EditorGUI.BeginChangeCheck();
            int a = EditorGUILayout.Popup("值 A（本层）", Mathf.Clamp(_wizMaterialA, 0, labels.Length - 1), labels);
            int b = EditorGUILayout.Popup("值 B（邻接）", Mathf.Clamp(_wizMaterialB, 0, labels.Length - 1), labels);
            string groupName = EditorGUILayout.TextField("新组名", _wizGroupName);
            if (EditorGUI.EndChangeCheck())
            {
                _wizMaterialA = a;
                _wizMaterialB = b;
                _wizGroupName = groupName;
            }

            if (_wizTiles == null || _wizTiles.Length != WizardTileCount)
                _wizTiles = new Sprite[WizardTileCount];

            EditorGUILayout.LabelField("16 张过渡瓦片（缺的槽不生成规则）", EditorStyles.miniLabel);

            const float slotW = 58f;
            const float slotH = 86f;
            const float thumb = 54f;
            var block = GUILayoutUtility.GetRect(slotW * 4f, slotH * 4f,
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            for (int m = 0; m < WizardTileCount; m++)
            {
                int col = m % 4, row = m / 4;
                var origin = new Rect(block.x + col * slotW, block.y + row * slotH, thumb, thumb);

                if (Event.current.type == EventType.Repaint)
                    MapEditorGUI.DrawThumb(origin, _wizTiles[m], new Color(0.42f, 0.42f, 0.44f, 1f));

                EditorGUI.BeginChangeCheck();
                var picked = (Sprite)EditorGUI.ObjectField(
                    new Rect(origin.x, origin.yMax + 1f, thumb, 16f), _wizTiles[m], typeof(Sprite), false);
                if (EditorGUI.EndChangeCheck()) _wizTiles[m] = picked;

                EditorGUI.LabelField(new Rect(origin.x, origin.yMax + 18f, thumb, 12f),
                    MaskLabel(m), EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(set.ruleSets.Count == 0))
            {
                if (GUILayout.Button("生成 16 条过渡规则"))
                    GenerateTransitionRules(set);
            }
        }

        private static string MaskLabel(int mask)
        {
            if (mask == 0) return "无";
            var sb = new StringBuilder(4);
            if ((mask & 1) != 0) sb.Append('N');
            if ((mask & 2) != 0) sb.Append('E');
            if ((mask & 4) != 0) sb.Append('S');
            if ((mask & 8) != 0) sb.Append('W');
            return sb.ToString();
        }

        /// <summary>
        /// 按 16 张图生成 16 条规则。
        /// <para>
        /// 每条规则：中心 = A；四条边按位取出「B 还是 A」；对角留通配（16 张集不含内凹角修正，
        /// 要处理内凹角得再补规则或换成 47 张集）。
        /// </para>
        /// </summary>
        private void GenerateTransitionRules(MapTileSet set)
        {
            if (set == null || set.ruleSets.Count == 0) return;
            int grid = set.ruleSets[_ruleSetIndex] != null ? set.ruleSets[_ruleSetIndex].sourceGrid : 0;

            if (_wizMaterialA == _wizMaterialB)
            {
                EditorUtility.DisplayDialog("过渡向导", "值 A 与 B 不能相同。", "知道了");
                return;
            }

            sbyte a = (sbyte)set.CellValueOf(grid, _wizMaterialA);
            sbyte b = (sbyte)set.CellValueOf(grid, _wizMaterialB);
            if (a == 0 || b == 0)
            {
                EditorUtility.DisplayDialog("过渡向导",
                    $"值下标越界，检查「{set.GridName(grid)}」的值表。", "知道了");
                return;
            }

            Undo.RecordObject(set, "Generate Transition Rules");

            var matA = set.ValueOf(grid, (byte)a);
            var matB = set.ValueOf(grid, (byte)b);
            string fallbackName =
                $"{(matA != null ? matA.displayName : "#" + _wizMaterialA)}↔" +
                $"{(matB != null ? matB.displayName : "#" + _wizMaterialB)}";

            var group = new MapAutoRuleGroup
            {
                name = string.IsNullOrWhiteSpace(_wizGroupName) ? fallbackName : _wizGroupName,
                active = true,
            };

            int generated = 0;
            for (int m = 0; m < WizardTileCount; m++)
            {
                int tileIndex = MapEditorUtil.EnsureTile(set, _wizTiles[m]);
                if (tileIndex < 0) continue;      // 这个位没填图，跳过

                var rule = new MapAutoRule();
                var pattern = rule.pattern;
                pattern[MapRulePattern.Center] = a;
                pattern[MapRulePattern.Index(0, 1)] = (m & 1) != 0 ? b : a;    // N
                pattern[MapRulePattern.Index(1, 0)] = (m & 2) != 0 ? b : a;    // E
                pattern[MapRulePattern.Index(0, -1)] = (m & 4) != 0 ? b : a;   // S
                pattern[MapRulePattern.Index(-1, 0)] = (m & 8) != 0 ? b : a;   // W
                rule.tiles = new[] { tileIndex };
                rule.breakOnMatch = true;
                rule.chance = 1f;
                group.rules.Add(rule);
                generated++;
            }

            if (generated == 0)
            {
                EditorUtility.DisplayDialog("过渡向导", "16 个槽位都没填图，没有生成任何规则。", "知道了");
                return;
            }

            set.ruleSets[_ruleSetIndex].groups.Insert(0, group);
            _groupIndex = 0;
            _ruleIndex = 0;
            ClampSelection();
            EditorUtility.SetDirty(set);
            Repaint();

            Debug.Log($"[Map] 过渡向导：在图层「{set.ruleSets[_ruleSetIndex].name}」最前面生成了 " +
                      $"{generated} 条规则（组「{group.name}」）。");
        }
    }
}
