using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

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
        }

        public void OnGUI(Rect rect, ConfigDataWindow window, TreeView tree, ConfigDataTreeNode treeNode)
        {
            if (treeNode == null)
            {
                DrawEmptyHint(rect);
                return;
            }

            GUILayout.BeginArea(rect);

            if (mCurrentId != treeNode.id)
            {
                mCurrentId = treeNode.id;

                var assetData = treeNode.GetAsset();
                var assetType = assetData != null ? assetData.GetType() : null;

                var editorType = GetEditor(assetType);
                if (editorType != null && typeof(ConfigDataEditor).IsAssignableFrom(editorType))
                {
                    if (mEditor != null)
                    {
                        mEditor.Dispose();
                        mEditor = null;
                    }

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

            if (mEditor != null && mAssetSO != null)
                mEditor.OnGUI(rect.width, rect.height);

            GUILayout.EndArea();
        }

        private static void DrawEmptyHint(Rect rect)
        {
            GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height),
                "在左侧选择一个配置，或右键目录创建新配置",
                EditorStyles.centeredGreyMiniLabel);
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
            mAssetSO = null;
        }
    }
}
