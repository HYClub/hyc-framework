using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Right-side pane host of the data editor. Resolves the editor class for
    /// the selected asset type (via <see cref="CfgEditorAttribute"/>), creates
    /// it once per asset, and forwards <see cref="OnGUI"/>/reload events.
    /// </summary>
    public class ConfigDataContainer
    {
        private int mCurrentId;
        private SerializedObject mAssetSO;
        private ConfigDataEditor mEditor;
        private FolderConfigEditor mFolderEditor;

        private static Dictionary<Type, Type> mEditorMap;

        /// <summary>待高亮字段：资产 + 字段名 + 高亮开始时间（短暂闪烁）。</summary>
        public static UnityEngine.Object HighlightAsset;
        public static string HighlightFieldName;
        public static double HighlightStartTime;

        /// <summary>请求高亮指定资产的字段（持续约 1.5 秒）。</summary>
        public static void RequestHighlight(UnityEngine.Object asset, string fieldName)
        {
            HighlightAsset = asset;
            HighlightFieldName = fieldName;
            HighlightStartTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>当前是否正处于高亮期间。</summary>
        public static bool IsHighlighting(UnityEngine.Object asset, string fieldName)
        {
            if (HighlightAsset == null || HighlightFieldName == null)
                return false;
            if (HighlightAsset != asset || HighlightFieldName != fieldName)
                return false;
            return EditorApplication.timeSinceStartup - HighlightStartTime < 1.5;
        }

        public void Reload()
        {
            mEditor?.Reload();
            mFolderEditor?.Reload();
        }

        public void OnGUI(Rect rect, ConfigDataWindow window, TreeView tree, ConfigDataTreeNode treeNode)
        {
            GUILayout.BeginArea(rect);

            if (treeNode == null)
            {
                DrawCenteredHint("在左侧选择一个配置，或右键目录创建新配置。\n"
                                 + "也可把外部资产（如 .inputactions）从 Project 窗口拖到左侧树的空白区纳入「外部配置」。");
                GUILayout.EndArea();
                return;
            }

            // 虚拟文件夹本身不是真实资产，右栏显示用法说明即可
            if (treeNode is ConfigDataTreeVirtualFolderNode)
            {
                DrawCenteredHint("「外部配置」是一个虚拟文件夹：成员是外部资产的引用，\n"
                                 + "资产本体不会被移动或复制，仍保留在它原来的位置。\n\n"
                                 + "加入方式：从 Project 窗口把资产拖到左侧树的空白区，松手后确认即可；\n"
                                 + "右键成员节点「移除」只删这里的引用，不会删除资产文件。");
                GUILayout.EndArea();
                return;
            }

            if (mCurrentId != treeNode.id)
            {
                mCurrentId = treeNode.id;

                if (treeNode is ConfigDataTreeFolderNode)
                {
                    // 文件夹视图：释放资产编辑器，按需创建 FolderConfigEditor
                    if (mEditor != null) { mEditor.Dispose(); mEditor = null; mAssetSO = null; }
                    if (mFolderEditor == null)
                        mFolderEditor = new FolderConfigEditor();
                    mFolderEditor.Open(window, tree, treeNode, null);
                }
                else if (treeNode is ConfigDataTreeExternalNode)
                {
                    // 外部资产：整体嵌入原生 Inspector（释放文件夹视图与其他资产编辑器）
                    if (mFolderEditor != null) { mFolderEditor.Dispose(); mFolderEditor = null; }
                    if (mEditor != null) { mEditor.Dispose(); mEditor = null; }

                    var assetData = treeNode.GetAsset();
                    // .inputactions 的原生 Inspector 被 Input System 刻意留空（编辑在独立窗口），
                    // 直接嵌入会一片空白 ⇒ 走专门的只读结构视图编辑器（InputActionsAssetEditor）。
                    mEditor = assetData is InputActionAsset
                        ? (ConfigDataEditor)new InputActionsAssetEditor()
                        : new UnityAssetEditor();

                    mAssetSO = assetData != null ? new SerializedObject(assetData) : null;
                    mEditor.Open(window, tree, treeNode, mAssetSO);
                }
                else
                {
                    if (mFolderEditor != null) { mFolderEditor.Dispose(); mFolderEditor = null; }

                    var assetData = treeNode.GetAsset();
                    var assetType = assetData != null ? assetData.GetType() : null;

                    var editorType = GetEditor(assetType);
                    if (editorType != null && typeof(ConfigDataEditor).IsAssignableFrom(editorType))
                    {
                        if (mEditor != null) { mEditor.Dispose(); mEditor = null; }
                        if (mEditor == null)
                            mEditor = (ConfigDataEditor)Activator.CreateInstance(editorType);
                        mAssetSO = assetData != null ? new SerializedObject(assetData) : null;
                        mEditor.Open(window, tree, treeNode, mAssetSO);
                    }
                    else if (mEditor != null)
                    {
                        mEditor.Dispose();
                        mEditor = null;
                    }
                }
            }

            if (treeNode is ConfigDataTreeFolderNode)
            {
                if (mFolderEditor != null)
                    mFolderEditor.OnGUI(rect.width, rect.height);
            }
            else if (mEditor != null && mAssetSO != null)
            {
                mEditor.OnGUI(rect.width, rect.height);
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// 在右栏正中间（水平 + 垂直都居中）画一段说明文字。
        /// <para>必须已在 <c>GUILayout.BeginArea(rect)</c> 内调用：用局部坐标布局，否则会二次偏移
        /// （文字被推到右上或被右边缘裁切）。靠上下各一个 FlexibleSpace 撑出垂直居中，
        /// 单靠 <c>centeredGreyMiniLabel</c> 只能水平居中、会贴在顶部。</para>
        /// </summary>
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

        private static Type GetEditor(Type assetType)
        {
            if (assetType == null)
                return typeof(NormalConfigEditor);

            if (mEditorMap == null)
            {
                mEditorMap = new Dictionary<Type, Type>();
                foreach (var type in TypeCache.GetTypesWithAttribute<CfgEditorAttribute>())
                {
                    foreach (var attr in type.GetCustomAttributes<CfgEditorAttribute>())
                    {
                        if (attr.Type != null)
                            mEditorMap[attr.Type] = type;
                    }
                }
            }

            return mEditorMap.TryGetValue(assetType, out var editorType)
                ? editorType
                : typeof(NormalConfigEditor);
        }

        public void Dispose()
        {
            if (mEditor != null)
            {
                mEditor.Dispose();
                mEditor = null;
            }
            if (mFolderEditor != null)
            {
                mFolderEditor.Dispose();
                mFolderEditor = null;
            }
            mAssetSO = null;
        }
    }
}
