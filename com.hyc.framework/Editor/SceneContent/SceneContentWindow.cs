using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 场景内容编辑器窗口（两个内层页签）。
    /// <para>
    /// ① <b>类型设计</b>：左树管「有哪些内容类型」，右侧<b>在本窗口内</b>设计字段 / 两侧导出方式，
    ///    点「生成代码」吐出 &lt;类名&gt;.cs + SceneContentTypes.g.cs。复用了
    ///    <see cref="SceneContentTypeDefEditor"/> 的绘制（已移除扫描反填）。
    /// </para>
    /// <para>
    /// ② <b>内容总览（只读）</b>：自动发现工程里场景 / 单独 Prefab 上挂了内容脚本的 GO，
    ///    点 GO 显示其用户填写值与内定参数。只发现、不改写——和已删的「扫描反填」不是一回事。
    /// </para>
    /// <para>
    /// 窗口骨架<b>照抄</b>数据配置窗口 <c>ConfigDataWindow</c>：一行工具条 + 左侧
    /// <c>SceneContentTreeView</c>（Unity TreeView，隔行底色铺满整块 rect）+ 分界线（可拖宽）+ 右侧内容区。
    /// </para>
    /// </summary>
    public class SceneContentWindow : EditorWindow
    {
        [MenuItem("HYC Framework/Scene Content/Scene Content Window", false, 70)]
        public static void Open()
        {
            GetWindow<SceneContentWindow>("Scene Content").Show();
        }

        /// <summary>
        /// 打开窗口、切到「类型设计」并选中指定定义（生成组件 Inspector 的回跳入口）。
        /// </summary>
        public static SceneContentWindow OpenAndSelect(SceneContentTypeDef def)
        {
            var win = GetWindow<SceneContentWindow>("Scene Content");
            win.Show();
            if (def == null)
                return win;
            win._tab = Tab.Design;
            win.RefreshTypeList();
            win.SelectDef(def);
            win.RefreshDesignTree();
            win.Repaint();
            return win;
        }

        [MenuItem("HYC Framework/Scene Content/Export All Blobs", false, 71)]
        public static void ExportAllBlobsMenu()
        {
            SceneContentExporter.ExportAll();
        }

        [MenuItem("HYC Framework/Scene Content/Export Blob (Selected)", false, 72)]
        public static void ExportSelectedMenu()
        {
            var obj = Selection.activeObject;
            if (obj is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go))
            {
                SceneContentExporter.Export(go);
                return;
            }
            if (obj is SceneAsset)
            {
                SceneContentExporter.Export(obj as SceneAsset);
                return;
            }
            Debug.LogWarning("[SceneContent] 请先在 Project 中选一个关卡 Prefab 或 Scene 资产再导出。");
        }

        /// <summary>新建场景内容类型定义资产并选中。</summary>
        [MenuItem("HYC Framework/Scene Content/New Scene Content Type", false, 73)]
        public static void NewTypeAssetMenu()
        {
            var def = SceneContentFactory.CreateType();
            if (def == null)
                return;
            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);
        }

        private enum Tab { Design, Overview }

        private Tab _tab = Tab.Design;

        // —— 窗口骨架（与 ConfigDataWindow 同一套坐标系）——
        private static float _listWidth = 280f;
        private SceneContentTreeView _designTree;
        private SceneContentTreeView _overviewTree;
        private SearchField _searchField;

        // —— 类型设计 ——
        private List<SceneContentTypeDef> _typeList = new List<SceneContentTypeDef>();
        private SceneContentTypeDef _selectedDef;
        private UnityEditor.Editor _defEditor;        // 命名空间同名，必须全限定
        private Vector2 _designDetailScroll;

        // —— 内容总览 ——
        private List<OverviewSource> _overviewSources = new List<OverviewSource>();
        private MarkerEntry _selectedMarker;
        private Vector2 _ovInstScroll;
        private string _overviewMessage = "";
        // 加过组件后内容总览已过期：切到该页签时自动重扫一次（不在设计页做全工程扫描）
        private bool _overviewDirty;

        private static readonly HashSet<string> SkipFieldNames = new HashSet<string>
        {
            "name", "hideFlags", "transform", "gameObject", "tag", "enabled",
            "isActiveAndEnabled", "useGUILayout", "runInEditMode",
        };

        // ---------------------------------------------------------------- 生命周期

        private void OnEnable()
        {
            RefreshTypeList();
        }

        private void OnDisable()
        {
            if (_defEditor != null)
            {
                DestroyImmediate(_defEditor);
                _defEditor = null;
            }
        }

        private void OnGUI()
        {
            var viewWidth = EditorGUIUtility.currentViewWidth;

            if (_tab == Tab.Overview && _overviewDirty)
            {
                _overviewDirty = false;
                RefreshOverview();
            }

            DrawToolbar(viewWidth);
            EnsureTrees();

            if (_listWidth < 128f) _listWidth = 128f;

            var tree = _tab == Tab.Design ? _designTree : _overviewTree;

            // 搜索栏：与配置窗口同一坐标系（工具条 0..22、搜索 22..48、树 48..底）
            GUILayout.BeginArea(new Rect(2, 22, _listWidth - 8, 26));
            GUILayout.BeginHorizontal();
            var oldSearch = tree.searchString;
            var newSearch = _searchField.OnToolbarGUI(oldSearch);
            if (!string.Equals(oldSearch, newSearch))
            {
                tree.searchString = newSearch;
                if (string.IsNullOrEmpty(newSearch))
                    tree.SetFocusAndEnsureSelectedItem();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            var treeRect = new Rect(0, 48, _listWidth, position.height - 48);
            tree.OnGUI(treeRect);

            // 分界线
            var oldColor = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.3f);
            GUI.DrawTexture(new Rect(0, 48, _listWidth, 1), EditorGUIUtility.whiteTexture);
            GUI.DrawTexture(new Rect(treeRect.xMax, 0, 1, position.height), EditorGUIUtility.whiteTexture);
            GUI.color = oldColor;

            var splitLine = new Rect(treeRect.xMax - 4, 0, 8, position.height);
            EditorGUIUtility.AddCursorRect(splitLine, MouseCursor.ResizeHorizontal);
            _listWidth += HYC.Framework.Config.Editor.GUIDrawer.SlideRect(splitLine, MouseCursor.ResizeHorizontal).x;

            var propRect = new Rect(treeRect.xMax, 0, viewWidth - treeRect.xMax, position.height);
            if (propRect.width > 0 && propRect.height > 0)
            {
                GUILayout.BeginArea(propRect);
                if (_tab == Tab.Design)
                    DrawDesignDetail();
                else
                    DrawOverviewDetail();
                GUILayout.EndArea();
            }

            GUI.color = new Color(0, 0, 0, 0.3f);
            GUI.DrawTexture(new Rect(treeRect.xMax, 22, viewWidth - treeRect.xMax, 1), Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        // ---------------------------------------------------------------- 工具条

        /// <summary>一行工具条：左侧页签、右侧动作按钮。控件集合每帧恒定，只换「刷新」的文案。</summary>
        private void DrawToolbar(float viewWidth)
        {
            GUILayout.BeginArea(new Rect(0, 3, viewWidth, 20));
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Toggle(_tab == Tab.Design, "类型设计", EditorStyles.toolbarButton, GUILayout.Width(76)))
                _tab = Tab.Design;
            if (GUILayout.Toggle(_tab == Tab.Overview, "内容总览", EditorStyles.toolbarButton, GUILayout.Width(76)))
                _tab = Tab.Overview;

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("新建类型", EditorStyles.toolbarButton, GUILayout.Width(70)))
                NewType();
            if (GUILayout.Button("生成全部代码", EditorStyles.toolbarButton, GUILayout.Width(90)))
                GenerateAllTypeCode();
            if (GUILayout.Button("全量导出", EditorStyles.toolbarButton, GUILayout.Width(70)))
                SceneContentExporter.ExportAll();
            if (GUILayout.Button("给 GO 加组件", EditorStyles.toolbarButton, GUILayout.Width(90)))
                SceneContentHierarchyMenu.BuildMenu().ShowAsContext();
            if (GUILayout.Button(_tab == Tab.Overview ? "刷新扫描" : "刷新", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                if (_tab == Tab.Overview)
                    RefreshOverview();
                else
                    RefreshTypeList();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>右侧空态：照抄 ConfigDataContainer.DrawCenteredHint。</summary>
        private static void DrawCenteredHint(string text)
        {
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, style, GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        // ---------------------------------------------------------------- 左树构建

        private void EnsureTrees()
        {
            if (_searchField == null)
                _searchField = new SearchField();

            if (_designTree == null)
            {
                _designTree = SceneContentTreeView.Create();
                _designTree.OnItemSelected += OnDesignItemSelected;
                _designTree.OnItemContextClicked += OnDesignItemContextClicked;
                _designTree.OnPlusClicked += OnDesignItemPlusClicked;
                _designTree.OnCanStartDrag += OnDesignCanStartDrag;
                _designTree.OnStartDrag += OnDesignStartDrag;
                _searchField.downOrUpArrowKeyPressed += _designTree.SetFocusAndEnsureSelectedItem;
                RefreshDesignTree();
            }
            if (_overviewTree == null)
            {
                _overviewTree = SceneContentTreeView.Create();
                _overviewTree.OnItemSelected += OnOverviewItemSelected;
                RefreshOverviewTree();
            }
        }

        private static Texture2D Icon(string name)
        {
            var c = EditorGUIUtility.IconContent(name);
            return c != null ? c.image as Texture2D : null;
        }

        private static SceneContentTreeNode Group(int id, string name)
        {
            return new SceneContentTreeNode
            {
                id = id,
                depth = 0,
                displayName = name,
                icon = Icon("Folder Icon"),
                isGroup = true,
            };
        }

        /// <summary>
        /// 设计页左树：按显示名里的「/」拆成分类层级（对齐数据配置的资源树，类型定义即「类型资源」）。
        /// 例如「场景内容/出生点」→ 场景内容（组）→ 出生点（叶子）；未生成的叶子末尾加「 (未生成)」标。
        /// </summary>
        private void RefreshDesignTree()
        {
            if (_designTree == null)
                return;
            var nodes = new List<SceneContentTreeNode>();
            var id = 1;
            // 分类路径（"场景内容" / "场景内容/子分类"）→ 组节点，复用避免重复建组
            var catNodes = new Dictionary<string, SceneContentTreeNode>();

            foreach (var def in _typeList)
            {
                var display = def.displayName ?? string.Empty;
                var parts = display.Split('/');
                var leafName = parts[parts.Length - 1];
                if (string.IsNullOrEmpty(leafName))
                    leafName = string.IsNullOrEmpty(display) ? def.className : display;

                // 逐级建分类组（parentId 指向上一层组）
                var parentId = 0;
                var pathSoFar = string.Empty;
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    var seg = parts[i].Trim();
                    if (string.IsNullOrEmpty(seg))
                        continue;
                    pathSoFar = string.IsNullOrEmpty(pathSoFar) ? seg : pathSoFar + "/" + seg;
                    if (!catNodes.TryGetValue(pathSoFar, out var cat))
                    {
                        cat = new SceneContentTreeNode
                        {
                            id = id++,
                            parentId = parentId,
                            displayName = seg,
                            icon = Icon("Folder Icon"),
                            isGroup = true,
                        };
                        catNodes[pathSoFar] = cat;
                        nodes.Add(cat);
                    }
                    parentId = cat.id;
                }

                // 没有任何分类段时，归入「（无分类）」组，避免叶子直接飘在根
                if (parentId == 0)
                {
                    const string uncat = "（无分类）";
                    if (!catNodes.TryGetValue(uncat, out var cat))
                    {
                        cat = new SceneContentTreeNode
                        {
                            id = id++,
                            parentId = 0,
                            displayName = uncat,
                            icon = Icon("Folder Icon"),
                            isGroup = true,
                        };
                        catNodes[uncat] = cat;
                        nodes.Add(cat);
                    }
                    parentId = cat.id;
                }

                var label = leafName;
                if (!SceneContentTypeCodeGen.IsGenerated(def))
                    label += " (未生成)";

                var leaf = new SceneContentTreeNode
                {
                    id = id++,
                    parentId = parentId,
                    displayName = label,
                    icon = (Texture2D)AssetDatabase.GetCachedIcon(AssetDatabase.GetAssetPath(def)),
                    payload = def,
                    showPlus = true,
                };
                nodes.Add(leaf);
            }

            _designTree.SetData(nodes);
            if (_selectedDef != null)
                _designTree.SelectPayload(_selectedDef);
        }

        /// <summary>总览页左树：场景 / Prefab → 资源 → 挂内容脚本的 GO。</summary>
        private void RefreshOverviewTree()
        {
            if (_overviewTree == null)
                return;
            var nodes = new List<SceneContentTreeNode>();
            var id = 1;
            foreach (var kind in new[] { "场景", "Prefab" })
            {
                var srcs = _overviewSources.FindAll(s => s.kind == kind && s.markers.Count > 0);
                if (srcs.Count == 0)
                    continue;
                var g = Group(id++, $"{kind}（{srcs.Count}）");
                nodes.Add(g);
                foreach (var s in srcs)
                {
                    var sNode = new SceneContentTreeNode
                    {
                        id = id++,
                        parentId = g.id,
                        depth = 1,
                        displayName = $"{s.name}（{s.markers.Count}）",
                        icon = Icon("Folder Icon"),
                        isGroup = true,
                    };
                    nodes.Add(sNode);
                    foreach (var m in s.markers)
                    {
                        nodes.Add(new SceneContentTreeNode
                        {
                            id = id++,
                            parentId = sNode.id,
                            depth = 2,
                            displayName = $"{m.goName}  ·  {m.typeName}",
                            icon = Icon("GameObject Icon"),
                            payload = m,
                        });
                    }
                }
            }
            _overviewTree.SetData(nodes);
        }

        private void OnDesignItemSelected(SceneContentTreeNode node)
        {
            SelectDef(node != null ? node.payload as SceneContentTypeDef : null);
        }

        private void OnOverviewItemSelected(SceneContentTreeNode node)
        {
            _selectedMarker = node != null ? node.payload as MarkerEntry : null;
            Repaint();
        }

        private void OnDesignItemContextClicked(SceneContentTreeNode node)
        {
            var def = node != null ? node.payload as SceneContentTypeDef : null;
            if (def != null) ShowTypeRowMenu(def);
        }

        private void OnDesignItemPlusClicked(SceneContentTreeNode node)
        {
            var def = node != null ? node.payload as SceneContentTypeDef : null;
            if (def != null) AddComponentToSelection(def);
        }

        private bool OnDesignCanStartDrag(SceneContentTreeNode node)
        {
            var def = node != null ? node.payload as SceneContentTypeDef : null;
            return def != null && SceneContentComponentResolver.FailureHint(def) == null;
        }

        private void OnDesignStartDrag(IList<int> ids)
        {
            if (ids == null || ids.Count == 0 || _designTree == null)
                return;
            var node = _designTree.GetNode(ids[0]);
            var def = node != null ? node.payload as SceneContentTypeDef : null;
            if (def == null)
                return;
            var hint = SceneContentComponentResolver.FailureHint(def);
            if (hint != null)
            {
                Debug.LogWarning($"[SceneContent] 类型「{def.displayName}」暂不能拖：{hint}");
                return;
            }
            var script = SceneContentComponentResolver.ResolveScript(def);
            if (script == null)
                return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new UnityEngine.Object[] { script };
            DragAndDrop.paths = new[] { SceneContentComponentResolver.ScriptPath(def) };
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            DragAndDrop.StartDrag("SceneContentComponent");
        }

        // ---------------------------------------------------------------- 右侧内容区

        private void DrawDesignDetail()
        {
            if (_selectedDef == null)
            {
                DrawCenteredHint("在左侧选择一个内容类型，或点右上「新建类型」设计字段。");
                return;
            }

            if (_defEditor == null || _defEditor.target != _selectedDef)
            {
                if (_defEditor != null) DestroyImmediate(_defEditor);
                _defEditor = CreateEmbeddedEditor(_selectedDef);
            }

            _designDetailScroll = EditorGUILayout.BeginScrollView(
                _designDetailScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.Space(4);
            if (_defEditor != null)
                _defEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }

        private void DrawOverviewDetail()
        {
            if (_selectedMarker == null)
            {
                DrawCenteredHint("在左侧选择一个 GO，这里显示它的填写值与内定参数（只读）。");
                return;
            }

            _ovInstScroll = EditorGUILayout.BeginScrollView(
                _ovInstScroll, GUILayout.ExpandHeight(true));
            var m = _selectedMarker;
            var pathLabel = string.IsNullOrEmpty(m.path) ? "(根)" : m.path;
            EditorGUILayout.LabelField($"{m.typeName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"GO：{m.goName}    来源：{m.sourceLabel}    内容 id：{m.contentTypeId}    路径：{pathLabel}",
                EditorStyles.miniLabel);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("用户填写", EditorStyles.boldLabel);
            var fields = m.component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
            var any = false;
            foreach (var fi in fields)
            {
                if (SkipFieldNames.Contains(fi.Name))
                    continue;
                var val = fi.GetValue(m.component);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(fi.Name, GUILayout.Width(160), GUILayout.MaxWidth(160));
                EditorGUILayout.LabelField(FormatValue(fi.FieldType, val), EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();
                any = true;
            }
            if (!any)
                EditorGUILayout.LabelField("（无用户字段）");

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("内定参数（只读 · 自动导出）", EditorStyles.boldLabel);
            var t = m.component.transform;
            EditorGUILayout.LabelField("localPos 相对", FormatVector(t.localPosition));
            EditorGUILayout.LabelField("worldPos 绝对", FormatVector(t.position));
            EditorGUILayout.LabelField("path", SceneContentRefPath.GetPath(m.root, t) ?? "(根)");
            EditorGUILayout.LabelField("localEuler", FormatVector(t.localEulerAngles));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("导出 payload 预览（客户端侧 · 生成代码产出）", EditorStyles.boldLabel);
            string payload;
            try
            {
                var p = m.component as ISceneContentMarkerPayload;
                payload = p != null
                    ? p.ToPayloadJson(m.root, SceneContentSide.Client)
                    : "(该组件未实现 ISceneContentMarkerPayload，导出时退回 JsonUtility.ToJson)";
            }
            catch (Exception e)
            {
                payload = "payload 生成失败：" + e.Message;
            }
            EditorGUILayout.SelectableLabel(payload, EditorStyles.textArea, GUILayout.MinHeight(110));

            EditorGUILayout.Space(8);
            if (GUILayout.Button(m.isScene ? "在 Project 中定位场景" : "在 Project 中定位 Prefab"))
                EditorGUIUtility.PingObject(m.locate);
            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- 类型操作

        private void ShowTypeRowMenu(SceneContentTypeDef def)
        {
            var menu = new GenericMenu();
            var hint = SceneContentComponentResolver.FailureHint(def);

            if (hint != null)
                menu.AddDisabledItem(new GUIContent($"添加到选中的 GameObject（{hint}）"));
            else
                menu.AddItem(new GUIContent("添加到选中的 GameObject"), false,
                    () => AddComponentToSelection(def));

            var scriptPath = SceneContentComponentResolver.ScriptPath(def);
            var generated = !string.IsNullOrEmpty(scriptPath)
                            && File.Exists(Path.Combine(Directory.GetCurrentDirectory(), scriptPath));
            if (generated)
                menu.AddItem(new GUIContent("定位生成脚本"), false, () => PingScript(scriptPath));
            else
                menu.AddDisabledItem(new GUIContent("定位生成脚本（尚未生成）"));

            menu.AddItem(new GUIContent("重新生成此类型代码"), false, () => RegenerateOne(def));
            menu.ShowAsContext();
        }

        /// <summary>给当前选中的 GameObject（可多选）挂组件；未编译 / 选了 Prefab 资产时给出明确原因。</summary>
        private void AddComponentToSelection(SceneContentTypeDef def)
        {
            var type = SceneContentComponentResolver.Resolve(def);
            if (type == null)
            {
                EditorUtility.DisplayDialog("添加场景内容组件",
                    SceneContentComponentResolver.FailureHint(def) ?? "无法解析生成组件类型", "确定");
                return;
            }

            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0)
            {
                EditorUtility.DisplayDialog("添加场景内容组件",
                    "请先在 Hierarchy / Scene 里选中要挂组件的 GameObject。", "确定");
                return;
            }

            var added = 0;
            foreach (var go in gos)
            {
                if (go == null || PrefabUtility.IsPartOfPrefabAsset(go))
                    continue;
                Undo.AddComponent(go, type);
                added++;
            }

            if (added == 0)
            {
                EditorUtility.DisplayDialog("添加场景内容组件",
                    "选中的是 Prefab 资产本体，请双击进入 Prefab 编辑模式后再添加。", "确定");
                return;
            }

            _overviewDirty = true;
            if (_tab == Tab.Overview)
            {
                _overviewDirty = false;
                RefreshOverview();
            }
        }

        private static void PingScript(string path)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script == null)
                return;
            Selection.activeObject = script;
            EditorGUIUtility.PingObject(script);
        }

        private static void RegenerateOne(SceneContentTypeDef def)
        {
            if (SceneContentTypeCodeGen.WriteFile(def, out var error))
                return;
            EditorUtility.DisplayDialog("场景内容代码生成", "生成失败：" + error, "确定");
        }

        private void RefreshTypeList()
        {
            _typeList = SceneContentTypeCodeGen.LoadAllTypes();
            // 选中的定义资产若已被删除，清掉选中态，避免内嵌 Inspector 指向空对象
            if (_selectedDef != null && !_typeList.Contains(_selectedDef))
                SelectDef(null);
            RefreshDesignTree();
        }

        private void SelectDef(SceneContentTypeDef def)
        {
            if (_defEditor != null)
            {
                DestroyImmediate(_defEditor);
                _defEditor = null;
            }
            _selectedDef = def;
            if (def != null)
                _defEditor = CreateEmbeddedEditor(def);
        }

        /// <summary>
        /// 窗口内嵌用：创建类型定义编辑器并立刻标记为内嵌。
        /// <para>不设 <see cref="SceneContentTypeDefEditor.IsEmbedded"/> 的话，窗口里也会按
        /// 「资产 Inspector」处理而留空；两处创建点统一走这里，避免漏设。</para>
        /// </summary>
        private static UnityEditor.Editor CreateEmbeddedEditor(SceneContentTypeDef def)
        {
            var editor = UnityEditor.Editor.CreateEditor(def);
            var typeEditor = editor as SceneContentTypeDefEditor;
            if (typeEditor != null)
                typeEditor.IsEmbedded = true;
            return editor;
        }

        private void NewType()
        {
            var def = SceneContentFactory.CreateType();
            if (def == null)
                return;
            RefreshTypeList();
            SelectDef(def);
            RefreshDesignTree();
        }

        private static void GenerateAllTypeCode()
        {
            var errors = SceneContentTypeCodeGen.WriteAll();
            if (errors.Count == 0)
            {
                Debug.Log("[SceneContent] 已生成全部场景内容类型代码。");
                return;
            }
            var message = string.Join("\n", errors);
            Debug.LogError("[SceneContent] 代码生成存在问题：\n" + message);
            EditorUtility.DisplayDialog("场景内容代码生成", "以下定义未能生成：\n\n" + message, "确定");
        }

        // ---------------------------------------------------------------- 扫描

        private void RefreshOverview()
        {
            _overviewSources = new List<OverviewSource>();
            _overviewMessage = "";
            try
            {
                // 单独 Prefab
                foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null)
                        continue;
                    var src = ScanRoot(root, root.name, "Prefab", root);
                    if (src != null)
                        _overviewSources.Add(src);
                }

                // 场景（未打开则临时附加加载，扫描完即关，不改动用户当前场景）
                foreach (var g in AssetDatabase.FindAssets("t:SceneAsset"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    ScanScene(path);
                }
            }
            catch (Exception e)
            {
                _overviewMessage = "扫描出错：" + e.Message;
                Debug.LogException(e);
            }
            _selectedMarker = null;
            RefreshOverviewTree();
        }

        private OverviewSource ScanRoot(GameObject root, string sourceName, string kind, UnityEngine.Object locate)
        {
            if (root == null)
                return null;
            var src = new OverviewSource { name = sourceName, kind = kind, locate = locate };
            foreach (var info in SceneContentPrefabScanner.Scan(root, -1))
            {
                src.markers.Add(new MarkerEntry
                {
                    component = info.Component,
                    typeName = info.TypeName,
                    contentTypeId = info.ContentTypeId,
                    goName = info.Component != null ? info.Component.gameObject.name : info.TypeName,
                    path = info.Path,
                    sourceLabel = sourceName,
                    root = root,
                    locate = locate,
                    isScene = kind == "场景",
                });
            }
            return src.markers.Count > 0 ? src : null;
        }

        private void ScanScene(string path)
        {
            var sceneName = Path.GetFileNameWithoutExtension(path);
            // 注意：GetSceneByPath 在 UnityEngine.SceneManagement.SceneManager 上，EditorSceneManager 没有该成员
            var existing = SceneManager.GetSceneByPath(path);
            var wasOpen = existing.IsValid() && existing.isLoaded;
            var scene = wasOpen ? existing : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var src = new OverviewSource { name = sceneName, kind = "场景", locate = AssetDatabase.LoadAssetAtPath<SceneAsset>(path) };
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var info in SceneContentPrefabScanner.Scan(root, -1))
                    {
                        src.markers.Add(new MarkerEntry
                        {
                            component = info.Component,
                            typeName = info.TypeName,
                            contentTypeId = info.ContentTypeId,
                            goName = info.Component != null ? info.Component.gameObject.name : info.TypeName,
                            path = info.Path,
                            sourceLabel = sceneName,
                            root = root,
                            locate = AssetDatabase.LoadAssetAtPath<SceneAsset>(path),
                            isScene = true,
                        });
                    }
                }
                if (src.markers.Count > 0)
                    _overviewSources.Add(src);
            }
            finally
            {
                // 仅当本次确实成功附加加载后才关闭，避免关掉用户原本打开的场景或非法场景
                if (!wasOpen && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---------------------------------------------------------------- 值格式化

        private static string FormatValue(Type t, object v)
        {
            if (v == null)
                return "(null)";
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (System.Collections.IList)v;
                if (list.Count == 0)
                    return $"List<{t.GetGenericArguments()[0].Name}> (0)";
                var sb = new StringBuilder();
                sb.Append($"List<{t.GetGenericArguments()[0].Name}> ({list.Count}) [");
                var max = Mathf.Min(list.Count, 5);
                for (var i = 0; i < max; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(list[i] != null ? list[i].ToString() : "null");
                }
                if (list.Count > max) sb.Append(", …");
                sb.Append("]");
                return sb.ToString();
            }
            if (t == typeof(GameObject))
                return (v as GameObject) != null ? (v as GameObject).name : "(none)";
            return v.ToString();
        }

        private static string FormatVector(Vector3 v)
        {
            return $"[{v.x:F2}, {v.y:F2}, {v.z:F2}]";
        }

        // ---------------------------------------------------------------- 数据结构

        private sealed class OverviewSource
        {
            public string name;
            public string kind;
            public UnityEngine.Object locate;
            public readonly List<MarkerEntry> markers = new List<MarkerEntry>();
        }

        private sealed class MarkerEntry
        {
            public Component component;
            public string typeName;
            public int contentTypeId;
            public string goName;
            public string path;
            public string sourceLabel;
            public GameObject root;
            public UnityEngine.Object locate;
            public bool isScene;
        }
    }
}
