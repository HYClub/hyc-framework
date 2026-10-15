using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// HYC 数据编辑器主窗口：左侧配置树 + 右侧属性编辑/预览。
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

        // priority=2：Config 档内，Config Window(0) → Validate(1) → 本项，组内差1 ⇒ 不画线。
        [MenuItem("HYC Framework/Config/Data Editor", false, 2)]
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

        // priority=4：Config 档末位（Config Window 0 / Validate 1 / Data Editor 2 / Reissue 3 → 本项），
        // 档内相邻差 1 ⇒ 不画线；4 → BT(10) 差 6 < 11 仍不画线，tier①→② 分隔线位置不变。
        // 一键重生成全部配置代码：模板（主类/侧类型/Check/Editor/Export/Blob 三件套）+ 枚举，
        // 并清理命名空间 root 切换后残留的旧命名空间生成文件。覆盖写，保留各 .cs 的 .meta GUID，
        // 配置资产不掉脚本；框架级基类（generateCode=false，如 ConfigBase）自动跳过。
        [MenuItem("HYC Framework/Config/Regenerate All Config Code", false, 4)]
        public static void RegenerateAllConfigCode()
        {
            var templates = ConfigTemplateCodeGen.LoadAllTemplates() ?? new ConfigTemplate[0];
            var enumDefs = ConfigEnumCodeGen.LoadAllEnums() ?? new List<ConfigEnumDefinition>();
            if ((templates == null || templates.Length == 0) && (enumDefs == null || enumDefs.Count == 0))
            {
                EditorUtility.DisplayDialog("Regenerate All Config Code", "未找到任何配置模板。", "OK");
                return;
            }

            int ok = 0, skipped = 0, failed = 0, enumOk = 0, enumFailed = 0;
            var errors = new StringBuilder();
            try
            {
                for (int i = 0; i < templates.Length; i++)
                {
                    var tpl = templates[i];
                    var name = (tpl != null && !string.IsNullOrEmpty(tpl.className)) ? tpl.className : "(unnamed)";
                    EditorUtility.DisplayProgressBar("Regenerate All Config Code",
                        $"({i + 1}/{templates.Length}) {name}", templates.Length == 0 ? 0f : (float)i / templates.Length);
                    try
                    {
                        // 框架级基类（如 ConfigBase）随包预编译，不往宿主生成
                        if (tpl != null && !tpl.generateCode)
                        {
                            skipped++;
                            continue;
                        }
                        if (ConfigTemplateCodeGen.WriteFile(tpl, out var err))
                            ok++;
                        else
                        {
                            failed++;
                            errors.AppendLine($"· {name}: {err}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        failed++;
                        errors.AppendLine($"· {name}: {ex.Message}");
                    }
                }

                // 枚举不在模板链里，但同样是「重新生成全部」的应有内容：root 切换后枚举文件
                // 不会被模板重写覆盖，永远停在旧命名空间（CS0246 批量来源）。
                foreach (var def in enumDefs)
                {
                    if (def == null)
                        continue;
                    try
                    {
                        if (ConfigEnumCodeGen.WriteFile(def, out var enumErr))
                            enumOk++;
                        else
                        {
                            enumFailed++;
                            errors.AppendLine($"· 枚举 {def.className}: {enumErr}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        enumFailed++;
                        errors.AppendLine($"· 枚举 {def.className}: {ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            // 清理 root 切换等场景下的旧命名空间残留生成文件（只删带自动生成头、命名空间不在
            // 当前 root 派生范围内的 .cs；手写文件不碰）
            var cleaned = ConfigTemplateCodeGen.CleanStaleNamespaceFiles(true);
            if (cleaned > 0)
                AssetDatabase.Refresh();

            failed += enumFailed;
            var summary = $"Done — generated {ok}, skipped {skipped} (framework base classes), failed {failed}.";
            if (enumDefs.Count > 0)
                summary += $"\nEnums regenerated: {enumOk}, failed: {enumFailed}.";
            if (cleaned > 0)
                summary += $"\nStale-namespace generated files removed: {cleaned}.";
            if (failed > 0)
                summary += "\n\nFailures:\n" + errors.ToString();
            EditorUtility.DisplayDialog("Regenerate All Config Code", summary, "OK");
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
            var skip = mTreeView != null && mTreeView.SkipNextProjectChangedReload;
            Debug.Log($"[ConfigDataWindow] OnProjectChanged skipReload={skip}");
            if (skip)
            {
                mTreeView.SkipNextProjectChangedReload = false;
                Repaint();
                return;
            }
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
                // 不传目标文件夹：自动归置到「配置模板」类型文件夹（见 ConfigAssetLayout）
                var tpl = ConfigTemplateCodeGen.CreateTemplateAsset();
                mTreeView?.Reload();
                mTreeView?.SelectAsset(tpl);
                Repaint();
            }

            if (GUILayout.Button("模板▾", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                ShowTemplatesMenu();
            }

            if (GUILayout.Button("全量导出", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                mTreeView?.CleanAndExportAll();
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
