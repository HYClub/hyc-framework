using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Default data editor: draws all serialized fields in a scroll view and,
    /// when the asset exposes Sprite/Texture2D/GameObject fields, shows a
    /// collapsible preview panel at the bottom with a field picker.
    /// </summary>
    public class NormalConfigEditor : ConfigDataEditor
    {
        private Vector2 mScroll;

        // Preview
        private class PreviewField
        {
            public string Name;
            public string Label;
            public System.Type Type;
        }

        private List<PreviewField> mPreviewFields;
        private int mPreviewIndex;
        private ConfigDataPreview mPreview;
        private float mPreviewH = 160;
        private bool mPreviewOpened;
        private bool mDragStarted;

        protected override void Init()
        {
            mPreviewFields = null;
            mPreviewIndex = 0;
            mPreviewOpened = false;
            mPreviewH = 160;
        }

        public override void OnGUI(float viewW, float viewH)
        {
            if (mTarget == null)
                return;

            mTarget.Update();

            if (mPreviewFields == null)
                mPreviewFields = CollectPreviewFields();

            // 顶部标题栏：显示名-类名
            var title = ConfigTypeDisplay.GetShortName(mTarget.targetObject.GetType()) + "-" + mTarget.targetObject.GetType().Name;
            GUI.Label(new Rect(6, 4, viewW - 12, 20), title, EditorStyles.boldLabel);
            GUIDrawer.FillRect(new Rect(1, 26, viewW - 2, 1));

            const float top = 32;
            const float bottomBarH = 30;
            var headerH = mPreviewFields.Count > 0 ? 20f : 0f;
            var previewH = mPreviewOpened ? mPreviewH : headerH;
            if (mPreviewFields.Count <= 0)
                previewH = 0;
            if (previewH > viewH - top - bottomBarH)
                previewH = viewH - top - bottomBarH;

            // Fields area
            GUILayout.BeginArea(new Rect(6, top, viewW - 12, viewH - top - bottomBarH - previewH));
            mScroll = EditorGUILayout.BeginScrollView(mScroll);
            GUIDrawer.DrawFields(mTarget);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            // Preview panel
            if (mPreviewFields.Count > 0)
            {
                GUILayout.BeginArea(new Rect(1, viewH - bottomBarH - previewH, viewW - 2, previewH));

                EditorGUILayout.BeginHorizontal();
                if (EditorGUILayout.DropdownButton(new GUIContent(mPreviewFields[mPreviewIndex].Label), FocusType.Passive, EditorStyles.toolbarDropDown))
                {
                    var menu = new GenericMenu();
                    for (var i = 0; i < mPreviewFields.Count; i++)
                    {
                        var index = i;
                        var field = mPreviewFields[i];
                        menu.AddItem(new GUIContent(field.Label), i == mPreviewIndex, () => mPreviewIndex = index);
                    }
                    menu.ShowAsContext();
                }
                var prevW = GUILayoutUtility.GetLastRect().width;
                // 模型预览窗口按钮(AA 地址或 GameObject 字段)
                if (GUILayout.Button("模型预览", EditorStyles.toolbarButton, GUILayout.Width(72)))
                {
                    var field = mPreviewFields[mPreviewIndex];
                    OpenModelPreview(field);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (mPreview == null)
                    mPreview = new ConfigDataPreview(null);

                var previewRect = new Rect(0, headerH, viewW, previewH - headerH);
                if (previewRect.width > 1 && previewRect.height > 1)
                {
                    var field = mPreviewFields[mPreviewIndex];
                    var prop = mTarget.FindProperty(field.Name);
                    if (field.Type == typeof(Sprite))
                        mPreview.Draw(previewRect, prop?.objectReferenceValue as Sprite);
                    else if (field.Type == typeof(Texture2D))
                        mPreview.Draw(previewRect, prop?.objectReferenceValue as Texture2D);
                    else if (field.Type == typeof(GameObject))
                        mPreview.Draw(previewRect, prop?.objectReferenceValue as GameObject);
                }

                GUILayout.EndArea();

                // Separators + drag handle（预览区顶部在 viewH - bottomBarH - previewH）
                var previewTop = viewH - bottomBarH - previewH;
                GUIDrawer.FillRect(new Rect(1, previewTop, viewW - 2, 1));
                GUIDrawer.FillRect(new Rect(1, previewTop + headerH, viewW - 2, 1));

                var dragableRect = new Rect(prevW + 2, previewTop, viewW - 2 - prevW, headerH);
                var delta = GUIDrawer.SlideRect(dragableRect, MouseCursor.ResizeVertical).y;

                if (mPreviewOpened)
                {
                    mPreviewH -= delta;
                }
                else if (delta < 0)
                {
                    mPreviewH = -delta;
                    mPreviewOpened = true;
                }

                if (mPreviewH < headerH)
                    mPreviewH = headerH;
                if (mPreviewH > viewH)
                    mPreviewH = viewH;
            }

            // 底部右下角：保存按钮（固定在窗口底部）
            if (GUI.Button(new Rect(viewW - 74, viewH - bottomBarH + 4, 68, 20), "保存"))
            {
                mTarget.ApplyModifiedProperties();
                EditorUtility.SetDirty(mTarget.targetObject);
                AssetDatabase.SaveAssetIfDirty(mTarget.targetObject);
                mWindow?.ShowNotification(new GUIContent("已保存"));
            }

            mTarget.ApplyModifiedProperties();
        }

        /// <summary>打开模型预览窗口: 支持 Addressable(string) 和 GameObject 字段。</summary>
        private void OpenModelPreview(PreviewField field)
        {
            var prop = mTarget.FindProperty(field.Name);
            string address = null;

            // Addressable 字段(string) 或 GameObject 字段
            if (prop.propertyType == SerializedPropertyType.String)
            {
                address = prop.stringValue;
                if (string.IsNullOrEmpty(address))
                {
                    EditorUtility.DisplayDialog("预览", $"字段 {field.Name} 地址为空", "确定");
                    return;
                }
                // 收集动画字段映射(字段名 → 动画名)供预览标注
                var mapping = CollectAnimFieldMapping();
                var win = ConfigModelPreviewWindow.OpenWindow(address);
                win.SetAnimMapping(mapping);
                return;
            }

            if (prop.propertyType == SerializedPropertyType.ObjectReference)
            {
                var go = prop.objectReferenceValue as GameObject;
                if (go == null)
                {
                    EditorUtility.DisplayDialog("预览", $"字段 {field.Name} 未选择模型", "确定");
                    return;
                }
                // GameObject 直接预览
                var mapping = CollectAnimFieldMapping();
                var win = ConfigModelPreviewWindow.OpenWindow(AssetDatabase.GetAssetPath(go));
                win.SetAnimMapping(mapping);
                return;
            }
        }

        /// <summary>收集配置里的动画字段映射(字段名 → 动画名), 供预览标注"已用于"。</summary>
        private Dictionary<string, string> CollectAnimFieldMapping()
        {
            var mapping = new Dictionary<string, string>();
            var so = new SerializedObject(mTarget.targetObject);
            var prop = so.GetIterator();
            while (prop.NextVisible(true))
            {
                if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue is AnimationClip clip)
                {
                    mapping[prop.displayName] = clip.name;
                }
            }
            return mapping;
        }

        private List<PreviewField> CollectPreviewFields()
        {
            var fields = new List<PreviewField>();

            var prop = mTarget.GetIterator();
            var enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyPath == "m_Script")
                    continue;

                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var type = prop.objectReferenceValue != null
                        ? prop.objectReferenceValue.GetType()
                        : GetFieldType(mTarget.targetObject.GetType(), prop.propertyPath);

                    if (type != null &&
                        (type == typeof(Sprite) || type == typeof(Texture2D) || type == typeof(GameObject) ||
                         typeof(Sprite).IsAssignableFrom(type) || typeof(Texture2D).IsAssignableFrom(type) ||
                         typeof(GameObject).IsAssignableFrom(type)))
                    {
                        fields.Add(new PreviewField
                        {
                            Name = prop.propertyPath,
                            Label = prop.displayName,
                            Type = type,
                        });
                    }
                }
            }

            return fields;
        }

        private static System.Type GetFieldType(System.Type type, string path)
        {
            var field = type;
            var parts = path.Split('.');
            for (var i = 0; i < parts.Length; i++)
            {
                if (field == null)
                    return null;
                var fi = field.GetField(parts[i], System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (fi == null)
                    return null;
                field = fi.FieldType;
            }
            return field;
        }

        public override void Dispose()
        {
            if (mPreview != null)
                mPreview.Cleanup();
            mPreview = null;
            base.Dispose();
        }
    }
}
