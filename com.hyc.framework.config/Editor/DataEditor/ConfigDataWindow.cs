using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// QK 数据编辑器主窗口：左侧配置树 + 右侧属性编辑/预览。
    /// 支持搜索、右键创建配置/目录/重命名/复制/删除、拖拽移动。
    /// </summary>
    public class ConfigDataWindow : EditorWindow
    {
        private static float mTreeWidth = 280;

        private ConfigDataTree mTreeView;
        private ConfigDataContainer mPropertyView;
        private SearchField mSearchField;
        private bool mLastDirtyState;
        private double mLastProbe;

        private static Texture mRootFolderIcon => EditorGUIUtility.IconContent("Folder Icon").image;

        [MenuItem("HYC Framework/Config/Data Editor")]
        public static void Open()
        {
            var window = GetWindow<ConfigDataWindow>(false, "数据配置", true);
            window.titleContent = new GUIContent("数据配置", mRootFolderIcon);

            if (!window.docked)
            {
                var width = Mathf.Max(window.position.width, 168);
                var height = Mathf.Max(window.position.height, 168);
                var x = (Screen.width - width) * 0.5f;
                var y = (Screen.height - height) * 0.5f;
                window.position = new Rect(x, y, width, height);
            }
        }

        private void OnEnable()
        {
            ConfigDataSettings.EnsureRootFolder();

            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.projectChanged += OnProjectChanged;

            // ID 构造器状态心跳（仅局域网有意义）
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            if (mPropertyView != null)
            {
                mPropertyView.Dispose();
                mPropertyView = null;
            }

            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorApplication.timeSinceStartup - mLastProbe < 5.0)
                return;
            mLastProbe = EditorApplication.timeSinceStartup;
            ConfigIdService.ProbeAsync(this);
        }

        private void OnUndoRedoPerformed()
        {
            mTreeView?.Reload();
            mPropertyView?.Reload();
            Repaint();
        }

        private void OnProjectChanged()
        {
            mTreeView?.Reload();
            Repaint();
        }

        private void OnGUI()
        {
            var viewWidth = EditorGUIUtility.currentViewWidth;

            // Ctrl+S 保存全部
            OnKeyDown(Event.current);

            // 检测资产 dirty 状态变化（编辑后显示 * 标记），仅重绘不重建树
            var dirtyNow = ComputeAnyDirty();
            if (dirtyNow != mLastDirtyState)
            {
                mLastDirtyState = dirtyNow;
                Repaint();
            }

            DrawToolbar();

            if (mTreeView == null)
            {
                mTreeView = ConfigDataTree.Create();
                mTreeView.Reload();
                mTreeView.searchString = string.Empty;
                mSearchField = new SearchField();
                mSearchField.downOrUpArrowKeyPressed += mTreeView.SetFocusAndEnsureSelectedItem;
            }

            if (mPropertyView == null)
                mPropertyView = new ConfigDataContainer();

            if (mTreeWidth < 128)
                mTreeWidth = 128;

            // 搜索栏
            GUILayout.BeginArea(new Rect(2, 22, mTreeWidth - 8, 26));
            GUILayout.BeginHorizontal();
            var oldSearch = mTreeView.searchString;
            var newSearch = mSearchField.OnToolbarGUI(oldSearch);
            if (!string.Equals(oldSearch, newSearch))
            {
                mTreeView.searchString = newSearch;
                if (string.IsNullOrEmpty(newSearch))
                    mTreeView.SetFocusAndEnsureSelectedItem();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // 树
            var treeRect = new Rect(0, 48, mTreeWidth, position.height - 48);
            mTreeView.OnGUI(treeRect);

            // 分隔线
            var oldColor = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.3f);
            GUI.DrawTexture(new Rect(0, 48, mTreeWidth, 1), EditorGUIUtility.whiteTexture);
            GUI.DrawTexture(new Rect(treeRect.xMax, 0, 1, position.height), EditorGUIUtility.whiteTexture);
            GUI.color = oldColor;

            // 拖动条
            var splitLine = new Rect(treeRect.xMax - 4, 0, 8, position.height);
            EditorGUIUtility.AddCursorRect(splitLine, MouseCursor.ResizeHorizontal);
            mTreeWidth += GUIDrawer.SlideRect(splitLine, MouseCursor.ResizeHorizontal).x;

            // 属性视图
            var propRect = new Rect(treeRect.xMax, 0, viewWidth - treeRect.xMax, position.height);
            if (propRect.width > 0 && propRect.height > 0)
                mPropertyView.OnGUI(propRect, this, mTreeView, mTreeView.SelectedItem);

            // 工具栏线
            var color = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.3f);
            GUI.DrawTexture(new Rect(treeRect.xMax, 22, viewWidth - treeRect.xMax, 1), Texture2D.whiteTexture);
            GUI.color = color;

            // 右下角 ID 构造器状态点（绿/黄/红）
            ConfigIdService.DrawStatusDot(new Rect(position.width - 18, position.height - 18, 12, 12));
        }

        private void DrawToolbar()
        {
            var viewWidth = EditorGUIUtility.currentViewWidth;

            GUILayout.BeginArea(new Rect(0, 3, viewWidth, 20));
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("配置根目录", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                var picked = EditorUtility.OpenFolderPanel("选择配置根目录", ConfigDataSettings.RootFolder, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    var rel = picked.Replace('\\', '/');
                    if (rel.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                    {
                        rel = "Assets" + rel.Substring(Application.dataPath.Length);
                        ConfigDataSettings.RootFolder = rel;
                        ConfigDataSettings.EnsureRootFolder();
                        mTreeView?.Reload();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("错误", "配置根目录必须在 Assets 目录内!", "确定");
                    }
                }
            }

            if (GUILayout.Button(ConfigDataSettings.RootFolder, EditorStyles.toolbarButton, GUILayout.MaxWidth(160)))
            {
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ConfigDataSettings.RootFolder));
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("新建配置", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                ConfigCreateWindow.ShowWindow(GetTargetFolder());
            }

            if (GUILayout.Button("创建模板", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                var tpl = ConfigTemplateCodeGen.CreateTemplateAsset(GetTargetFolder());
                mTreeView?.Reload();
                mTreeView?.SelectAsset(tpl);
                Repaint();
            }

            if (GUILayout.Button("模板▾", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                ShowTemplatesMenu();
            }

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                mTreeView?.Reload();
                Repaint();
            }

            // 保存所有未保存资产（Ctrl+S）
            if (GUILayout.Button(mLastDirtyState ? "保存*" : "保存", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                SaveAll();
            }

            if (GUILayout.Button("设置", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                ConfigDataSettingsWindow.Open();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>Target folder for create actions: selected folder, or the selected file's parent, or the root.</summary>
        private string GetTargetFolder()
        {
            var target = mTreeView?.SelectedItem;
            if (target is ConfigDataTreeFolderNode f)
                return AssetDatabase.GUIDToAssetPath(f.guid);
            if (target is ConfigDataTreeFileNode file)
                return System.IO.Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(file.guid));
            return ConfigDataSettings.RootFolder;
        }

        /// <summary>当前是否有任一配置资产处于未保存（dirty）状态。</summary>
        private bool ComputeAnyDirty()
        {
            return mTreeView != null && mTreeView.HasDirtyAssets();
        }

        /// <summary>保存所有未保存的配置资产（配置/模板/枚举），并刷新树。</summary>
        public void SaveAll()
        {
            var any = false;
            foreach (var asset in ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder))
            {
                if (EditorUtility.IsDirty(asset))
                {
                    EditorUtility.SetDirty(asset);
                    any = true;
                }
            }
            // 模板与枚举不在 CollectConfigAssets（命名空间过滤）内，单独扫
            foreach (var guid in AssetDatabase.FindAssets("t:ConfigTemplate t:ConfigEnumDefinition", new[] { ConfigDataSettings.RootFolder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && EditorUtility.IsDirty(asset))
                {
                    EditorUtility.SetDirty(asset);
                    any = true;
                }
            }
            if (any)
                AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            mLastDirtyState = false;
            mTreeView?.Reload();
            Repaint();
        }

        /// <summary>Ctrl+S 保存全部。</summary>
        private void OnKeyDown(Event evt)
        {
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.S && (evt.modifiers & EventModifiers.Control) != 0)
            {
                SaveAll();
                evt.Use();
            }
        }

        public void RefreshTree()
        {
            mTreeView?.Reload();
            Repaint();
        }

        /// <summary>定位到指定资产并高亮其字段（检查错误跳转）。</summary>
        public void FocusField(UnityEngine.Object asset, string fieldName)
        {
            if (asset == null)
                return;

            mTreeView?.SelectAsset(asset);
            ConfigDataContainer.RequestHighlight(asset, fieldName);
            Repaint();
        }

        private void ShowTemplatesMenu()
        {
            var templates = ConfigTemplateCodeGen.LoadAllTemplates();
            var menu = new GenericMenu();
            foreach (var template in templates)
            {
                var tpl = template;
                var label = string.IsNullOrEmpty(tpl.displayName) ? tpl.className : tpl.displayName;
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    mTreeView?.SelectAsset(tpl);
                    Selection.activeObject = tpl;
                    Repaint();
                });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("打开配置根目录"), false, () =>
            {
                ConfigDataSettings.EnsureRootFolder();
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ConfigDataSettings.RootFolder));
            });

            if (templates.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("（暂无模板）"));
            }

            menu.ShowAsContext();
        }
    }
}
