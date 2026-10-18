// ============================================================
// HYC Framework - Cinemachine 相机装配 · 编辑器窗口
// 文件: Editor/Cinemachine/CinemachineRigWindow.cs
// 说明: 相机装配的唯一操作入口（菜单 pri 80/81）。
//       左栏机位列表（+/复制/删除/排序），右栏类型感知参数面板：
//       公共参数 → 类型配方参数 → 「更多 More」全参数/组件增删。
//       直接编辑 rig prefab 内容（LoadPrefabContents），Save 落盘；
//       关闭或切换 rig 时有未保存改动自动落盘，防丢。
//       参数渲染走 CM 组件自身的序列化字段——CM 升级新增字段自动出现。
// ============================================================

using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Cinemachine.Editor
{
    /// <summary>
    /// 相机装配编辑器：机位列表 + 类型感知参数面板 + 「更多」全参数展开。
    /// 菜单 <c>HYC Framework/Cinemachine/Rig Editor</c>（pri 80）。
    /// </summary>
    public class CinemachineRigWindow : EditorWindow
    {
        private const string LastPathKey = "HYC.CinemachineRig.LastPath";

        private string _rigPath;
        private GameObject _rigRoot;
        private int _selected = -1;
        private RigKind _selectedKind = RigKind.ThirdPerson;
        private bool _showMore;
        private bool _dirty;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;

        // 组件移除延后到 OnGUI 末尾执行：避免边遍历边销毁导致同帧控件集合错位
        private readonly List<Object> _pendingDestroy = new List<Object>();

        // ---------- 菜单 ----------

        [MenuItem("HYC Framework/Cinemachine/Rig Editor", false, 80)]
        public static void Open()
        {
            var window = GetWindow<CinemachineRigWindow>("Camera Rig Editor");
            window.minSize = new Vector2(640, 440);

            var path = SelectedRigPath();
            if (!string.IsNullOrEmpty(path))
                window.LoadRig(path);
            else
                window.LoadLastPath();
            window.Show();
        }

        [MenuItem("HYC Framework/Cinemachine/New Rig", false, 81)]
        private static void NewRigMenu()
        {
            const string dir = "Assets/HYC/Cinemachine";
            CinemachineRigFactory.EnsureFolder(dir);

            var path = EditorUtility.SaveFilePanelInProject(
                "新建相机装配 Rig", "PlayerRig", "prefab",
                "选择 rig prefab 的保存位置", dir);
            if (string.IsNullOrEmpty(path))
                return;

            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!CinemachineRigFactory.CreateRigPrefab(path, name, out var error))
            {
                Debug.LogError("[HYC.Cinemachine] 新建 rig 失败：" + error);
                return;
            }

            AssetDatabase.ImportAsset(path);
            Debug.Log("[HYC.Cinemachine] 已新建 rig：" + path);

            var window = GetWindow<CinemachineRigWindow>("Camera Rig Editor");
            window.LoadRig(path);
            window.Show();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }

        private static string SelectedRigPath()
        {
            var go = Selection.activeObject as GameObject;
            if (go == null)
                return null;
            var path = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return null;
            return path;
        }

        // ---------- 生命周期 ----------

        private void OnDestroy()
        {
            UnloadRig();
        }

        // ---------- rig 加载 / 保存 ----------

        private void LoadLastPath()
        {
            var last = EditorPrefs.GetString(LastPathKey, string.Empty);
            if (!string.IsNullOrEmpty(last))
                LoadRig(last);
        }

        private void LoadRig(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[HYC.Cinemachine] 只支持打开 .prefab 资产：" + path);
                return;
            }

            UnloadRig();
            _rigPath = path;
            EditorPrefs.SetString(LastPathKey, path);

            try
            {
                _rigRoot = PrefabUtility.LoadPrefabContents(path);
            }
            catch (Exception e)
            {
                Debug.LogError("[HYC.Cinemachine] 打开 rig 失败：" + path + "\n" + e.Message);
                _rigRoot = null;
            }

            if (_rigRoot == null)
            {
                _selected = -1;
                UpdateTitle();
                return;
            }

            var list = BuildCameraList();
            _selected = list.Count > 0 ? 0 : -1;
            _selectedKind = _selected >= 0 ? DetectKind(list[0]) : RigKind.ThirdPerson;
            _showMore = false;
            _dirty = false;
            UpdateTitle();
            Repaint();
        }

        private void UnloadRig()
        {
            if (_rigRoot != null)
            {
                // 域重载等场景下 AssetDatabase/PrefabUtility 可能不可用：失败只告警不抛出，
                // 保证 UnloadPrefabContents 仍有机会执行（否则预览场景会泄漏）。
                if (_dirty)
                {
                    try
                    {
                        SaveRig();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[HYC.Cinemachine] 关闭时自动保存失败，可能有未保存改动：" + e.Message);
                    }
                }
                try
                {
                    PrefabUtility.UnloadPrefabContents(_rigRoot);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[HYC.Cinemachine] 卸载 rig 预览内容失败：" + e.Message);
                }
                _rigRoot = null;
            }
            _selected = -1;
            _showMore = false;
            _dirty = false;
            UpdateTitle();
        }

        private void SaveRig()
        {
            if (_rigRoot == null || string.IsNullOrEmpty(_rigPath))
                return;
            PrefabUtility.SaveAsPrefabAsset(_rigRoot, _rigPath);
            _dirty = false;
            UpdateTitle();
        }

        private void MarkDirty()
        {
            _dirty = true;
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            if (this == null)
                return;
            titleContent = new GUIContent(
                "Camera Rig Editor" + (_dirty ? " *" : string.Empty),
                "HYC Framework / Cinemachine");
        }

        // ---------- 主界面 ----------

        private void OnGUI()
        {
            EditorGUIUtility.labelWidth = 150f;

            DrawToolbar();
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            DrawListPane();
            DrawDetailPane();
            EditorGUILayout.EndHorizontal();

            // 延后的组件销毁（同帧控件集合已画完再执行）
            if (_pendingDestroy.Count > 0)
            {
                for (int i = 0; i < _pendingDestroy.Count; i++)
                    if (_pendingDestroy[i] != null)
                        Undo.DestroyObjectImmediate(_pendingDestroy[i]);
                _pendingDestroy.Clear();
                MarkDirty();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var current = string.IsNullOrEmpty(_rigPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(_rigPath);
            var picked = (GameObject)EditorGUILayout.ObjectField(
                current, typeof(GameObject), false, GUILayout.Width(220));
            if (picked != current)
            {
                var path = picked != null ? AssetDatabase.GetAssetPath(picked) : null;
                if (!string.IsNullOrEmpty(path))
                    LoadRig(path);
            }

            GUI.enabled = !string.IsNullOrEmpty(_rigPath);
            if (GUILayout.Button("Open", EditorStyles.toolbarButton, GUILayout.Width(50)))
                LoadRig(_rigPath);
            if (GUILayout.Button(_dirty ? "Save *" : "Save", EditorStyles.toolbarButton, GUILayout.Width(60)))
                SaveRig();
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("New Rig...", EditorStyles.toolbarButton, GUILayout.Width(80)))
                NewRigMenu();

            EditorGUILayout.EndHorizontal();
        }

        // ---------- 左栏：机位列表 ----------

        private void DrawListPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(210));
            EditorGUILayout.LabelField("机位 Cameras", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Rig：" + (_rigRoot != null ? _rigRoot.name : "（未打开）"), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUI.enabled = _rigRoot != null;
            if (GUILayout.Button("+", GUILayout.Width(24)))
                AddCamera();
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;

            var list = BuildCameraList();
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            for (int i = 0; i < list.Count; i++)
            {
                var go = list[i];
                var recipe = RigKinds.Get(DetectKind(go));
                var label = recipe.DisplayName;
                var space = label.IndexOf(' ');
                if (space > 0)
                    label = label.Substring(0, space);

                var oldBg = GUI.backgroundColor;
                if (i == _selected)
                    GUI.backgroundColor = new Color(0.45f, 0.62f, 0.95f);
                if (GUILayout.Button(go.name + "\n" + label, GUILayout.Height(34)))
                {
                    _selected = i;
                    _selectedKind = DetectKind(go);
                    _showMore = false;
                }
                GUI.backgroundColor = oldBg;
            }
            EditorGUILayout.EndScrollView();

            GUI.enabled = _rigRoot != null && _selected >= 0 && _selected < list.Count;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("复制")) DuplicateSelected();
            if (GUILayout.Button("删除")) DeleteSelected();
            if (GUILayout.Button("↑")) MoveSelected(-1);
            if (GUILayout.Button("↓")) MoveSelected(1);
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;

            EditorGUILayout.EndVertical();
        }

        // ---------- 右栏：参数面板 ----------

        private void DrawDetailPane()
        {
            EditorGUILayout.BeginVertical();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            if (_rigRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "尚未打开 rig。用菜单 HYC Framework/Cinemachine/New Rig 新建，" +
                    "或在上方工具栏拖入 / 选择一个 rig prefab 后点 Open。",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            var go = Selected();
            if (go == null)
            {
                EditorGUILayout.HelpBox(
                    "当前 rig 没有机位。点左上角 + 添加一个虚拟相机。",
                    MessageType.Info);
            }
            else
            {
                DrawCameraSection(go);
            }

            EditorGUILayout.Space(10);
            DrawRigSection();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCameraSection(GameObject go)
        {
            EditorGUILayout.LabelField(go.name, EditorStyles.boldLabel);

            // 名称
            var newName = EditorGUILayout.DelayedTextField("名称 Name", go.name);
            if (!string.IsNullOrEmpty(newName) && newName != go.name)
            {
                Undo.RecordObject(go, "HYC Rename Rig Camera");
                go.name = newName;
                MarkDirty();
            }

            // 类型（配方）下拉：切换才应用，避免每帧重放配方
            EditorGUI.BeginChangeCheck();
            var popupKind = (RigKind)EditorGUILayout.EnumPopup(
                new GUIContent("类型 Type", "切换类型会按配方增删 Body/Aim 组件；同阶段的旧组件会被移除。"),
                _selectedKind);
            if (EditorGUI.EndChangeCheck() && popupKind != _selectedKind)
            {
                CinemachineRigFactory.ApplyKind(go, popupKind);
                _selectedKind = popupKind;
                MarkDirty();
            }

            // 公共参数（仅 CinemachineCamera 有 Target/Lens）
            if (go.TryGetComponent<CinemachineCamera>(out var cmCam))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("公共参数 Common", EditorStyles.miniBoldLabel);

                EditorGUI.BeginChangeCheck();
                var follow = (Transform)EditorGUILayout.ObjectField(
                    "跟随目标 Follow", cmCam.Follow, typeof(Transform), true);
                var lookAt = (Transform)EditorGUILayout.ObjectField(
                    "朝向目标 LookAt", cmCam.LookAt, typeof(Transform), true);
                var priority = EditorGUILayout.DelayedIntField("优先级 Priority", cmCam.Priority);
                var lens = cmCam.Lens;
                var fov = EditorGUILayout.Slider("视野 FOV", lens.FieldOfView, 1f, 120f);
                var near = EditorGUILayout.FloatField("近裁剪 Near", lens.NearClipPlane);
                var far = EditorGUILayout.FloatField("远裁剪 Far", lens.FarClipPlane);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(cmCam, "HYC Edit Rig Camera");
                    cmCam.Follow = follow;
                    cmCam.LookAt = lookAt;
                    cmCam.Priority = priority;
                    lens.FieldOfView = fov;
                    lens.NearClipPlane = near;
                    lens.FarClipPlane = far;
                    cmCam.Lens = lens;
                    MarkDirty();
                }
            }

            // 类型参数：配方组件的完整序列化字段
            var recipe = RigKinds.Get(_selectedKind);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(
                "类型参数 · " + recipe.DisplayName, EditorStyles.miniBoldLabel);
            var anyRecipeComp = false;
            for (int i = 0; i < recipe.ComponentNames.Length; i++)
            {
                var type = CmComponentCatalog.Find(recipe.ComponentNames[i]);
                var comp = type != null ? go.GetComponent(type) : null;
                if (comp == null)
                    continue;
                anyRecipeComp = true;
                DrawComponentProperties(comp, false);
            }
            if (!anyRecipeComp)
            {
                EditorGUILayout.HelpBox(
                    "该机位没有配方组件。切换上方 Type 应用配方，或展开下方「更多」手动添加。",
                    MessageType.None);
            }

            // 更多：全部组件 + 增删
            EditorGUILayout.Space(6);
            _showMore = EditorGUILayout.Foldout(_showMore, "更多 More · 全部参数与组件");
            if (_showMore)
            {
                EditorGUILayout.LabelField("管线组件（Body / Aim / Noise）", EditorStyles.miniBoldLabel);
                var comps = go.GetComponents<CinemachineComponentBase>();
                for (int i = 0; i < comps.Length; i++)
                    if (comps[i] != null)
                        DrawComponentProperties(comps[i], true);
                if (GUILayout.Button("+ 添加管线组件"))
                    ShowAddMenu(CmComponentCatalog.PipelineComponents, go);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("扩展组件（Extensions）", EditorStyles.miniBoldLabel);
                var exts = go.GetComponents<CinemachineExtension>();
                for (int i = 0; i < exts.Length; i++)
                    if (exts[i] != null)
                        DrawComponentProperties(exts[i], true);
                if (GUILayout.Button("+ 添加扩展组件"))
                    ShowAddMenu(CmComponentCatalog.Extensions, go);

                EditorGUILayout.HelpBox(
                    "以上参数直接渲染 CM 组件的序列化字段：CM 升级新增字段会自动出现，无需改框架代码。",
                    MessageType.Info);
            }
        }

        private void DrawRigSection()
        {
            EditorGUILayout.LabelField("Rig 级设置", EditorStyles.boldLabel);

            var brain = _rigRoot.GetComponent<CinemachineBrain>();
            if (brain == null)
            {
                EditorGUILayout.HelpBox("rig 根节点上没有 CinemachineBrain（大脑）。", MessageType.Warning);
                if (GUILayout.Button("添加 CinemachineBrain"))
                {
                    Undo.AddComponent(_rigRoot, typeof(CinemachineBrain));
                    MarkDirty();
                }
                return;
            }

            var so = new SerializedObject(brain);
            EditorGUILayout.PropertyField(so.FindPropertyRelative("UpdateMethod"));
            EditorGUILayout.PropertyField(so.FindPropertyRelative("DefaultBlend.Style"));
            EditorGUILayout.PropertyField(so.FindPropertyRelative("DefaultBlend.Time"));
            if (so.ApplyModifiedProperties())
                MarkDirty();
        }

        // ---------- 组件通用渲染 ----------

        /// <summary>
        /// 渲染单个 CM 组件的全部序列化字段（调用 CM 自带 PropertyDrawer）。
        /// </summary>
        private void DrawComponentProperties(Component comp, bool allowRemove)
        {
            var name = comp.GetType().Name;
            if (name.StartsWith("Cinemachine", StringComparison.Ordinal))
                name = name.Substring("Cinemachine".Length);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(name), EditorStyles.boldLabel);
            if (allowRemove)
            {
                if (GUILayout.Button("移除", GUILayout.Width(44)))
                {
                    _pendingDestroy.Add(comp);
                    EditorGUILayout.EndHorizontal();
                    return;
                }
            }
            EditorGUILayout.EndHorizontal();

            var so = new SerializedObject(comp);
            var it = so.GetIterator();
            if (it.NextVisible(true))
            {
                do
                {
                    if (it.name == "m_Script")
                        continue;
                    EditorGUILayout.PropertyField(it, true);
                }
                while (it.NextVisible(false));
            }
            if (so.ApplyModifiedProperties())
                MarkDirty();
        }

        private void ShowAddMenu(string[] catalog, GameObject go)
        {
            var menu = new GenericMenu();
            for (int i = 0; i < catalog.Length; i++)
            {
                var type = CmComponentCatalog.Find(catalog[i]);
                if (type == null || go.GetComponent(type) != null)
                    continue;
                menu.AddItem(new GUIContent(catalog[i]), false, () =>
                {
                    Undo.AddComponent(go, type);
                    MarkDirty();
                    Repaint();
                });
            }
            if (menu.GetItemCount() == 0)
                menu.AddDisabledItem(new GUIContent("（已全部添加）"));
            menu.ShowAsContext();
        }

        // ---------- 机位结构操作 ----------

        private void AddCamera()
        {
            if (_rigRoot == null)
                return;

            var go = new GameObject(
                GameObjectUtility.GetUniqueNameForSibling(_rigRoot.transform, "vcam_Camera"));
            go.transform.SetParent(_rigRoot.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "HYC New Rig Camera");

            var vcam = go.AddComponent<CinemachineCamera>();
            var rigTarget = _rigRoot.transform.Find("RigTarget");
            if (rigTarget != null)
            {
                vcam.Follow = rigTarget;
                vcam.LookAt = rigTarget;
            }
            vcam.Priority = NextPriority();

            var lens = LensSettings.Default;
            lens.FieldOfView = 40f;
            vcam.Lens = lens;

            CinemachineRigFactory.ApplyKind(go, RigKind.ThirdPerson);

            _selected = BuildCameraList().IndexOf(go);
            _selectedKind = RigKind.ThirdPerson;
            _showMore = false;
            MarkDirty();
        }

        private void DuplicateSelected()
        {
            var src = Selected();
            if (src == null)
                return;

            var copy = Object.Instantiate(src, _rigRoot.transform);
            copy.name = GameObjectUtility.GetUniqueNameForSibling(_rigRoot.transform, src.name);
            Undo.RegisterCreatedObjectUndo(copy, "HYC Duplicate Rig Camera");

            _selected = BuildCameraList().IndexOf(copy);
            _selectedKind = DetectKind(copy);
            _showMore = false;
            MarkDirty();
        }

        private void DeleteSelected()
        {
            var go = Selected();
            if (go == null)
                return;

            Undo.DestroyObjectImmediate(go);
            var list = BuildCameraList();
            _selected = Mathf.Min(_selected, list.Count - 1);
            if (_selected >= 0)
                _selectedKind = DetectKind(list[_selected]);
            MarkDirty();
        }

        private void MoveSelected(int delta)
        {
            var go = Selected();
            if (go == null)
                return;

            var idx = go.transform.GetSiblingIndex();
            var target = idx + delta;
            if (target < 0)
                return;
            go.transform.SetSiblingIndex(target);

            _selected = BuildCameraList().IndexOf(go);
            MarkDirty();
        }

        private int NextPriority()
        {
            var max = 0;
            var list = BuildCameraList();
            for (int i = 0; i < list.Count; i++)
            {
                var vcam = list[i].GetComponent<CinemachineVirtualCameraBase>();
                if (vcam != null)
                    max = Mathf.Max(max, vcam.Priority);
            }
            return max + 10;
        }

        // ---------- 查询辅助 ----------

        /// <summary>rig 根下挂虚拟相机组件的子节点（不含根本身）。</summary>
        private List<GameObject> BuildCameraList()
        {
            var list = new List<GameObject>();
            if (_rigRoot == null)
                return list;
            for (int i = 0; i < _rigRoot.transform.childCount; i++)
            {
                var child = _rigRoot.transform.GetChild(i);
                if (child != null && child.GetComponent<CinemachineVirtualCameraBase>() != null)
                    list.Add(child.gameObject);
            }
            return list;
        }

        private GameObject Selected()
        {
            var list = BuildCameraList();
            if (_selected < 0 || _selected >= list.Count)
                return null;
            return list[_selected];
        }

        /// <summary>按已有组件反查机位类型；非配方组合回落 <see cref="RigKind.ThirdPerson"/>（仅作下拉初值）。</summary>
        private static RigKind DetectKind(GameObject go)
        {
            if (go == null)
                return RigKind.ThirdPerson;
            for (int r = 0; r < RigKinds.All.Length; r++)
            {
                var recipe = RigKinds.All[r];
                var all = true;
                for (int i = 0; i < recipe.ComponentNames.Length; i++)
                {
                    var type = CmComponentCatalog.Find(recipe.ComponentNames[i]);
                    if (type == null || go.GetComponent(type) == null)
                    {
                        all = false;
                        break;
                    }
                }
                if (all)
                    return recipe.Kind;
            }
            return RigKind.ThirdPerson;
        }
    }
}
