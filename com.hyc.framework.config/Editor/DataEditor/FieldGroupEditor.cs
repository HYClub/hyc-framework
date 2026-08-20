using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 字段组（可自定义渲染逻辑的编辑区块）。实现者可通过
    /// <see cref="FieldGroupEditor{T}.CreateGroups"/> 提供任意组布局。
    /// </summary>
    public interface FieldGroup
    {
        void Reload();
        void Cleanup();
        string Name { get; }
        void OnGUI(Rect rect);
    }

    /// <summary>单字段组（通常用于特殊控件容器）。</summary>
    public class SingleFieldGroup : FieldGroup
    {
        private string mName;
        public virtual string Name => mName;
        public virtual void Cleanup() { }
        public virtual void OnGUI(Rect rect) { }
        public virtual void Reload() { }

        public SingleFieldGroup(string name)
        {
            mName = name;
        }
    }

    /// <summary>
    /// 基础字段组：滚动显示一组字段 + 底部可折叠预览（Sprite/Texture2D/GameObject）。
    /// </summary>
    public class BasicFieldGroup : FieldGroup
    {
        private string mName;
        private SerializedObject mTarget;
        private GUITypeField[] mFields;
        private Vector2 mScroll = Vector2.zero;
        private ConfigDataPreview mPreview;
        private GUITypeField[] mPreviews;
        private int mPreviewIndex;
        private float mPreviewH = 180;
        private bool mPreviewOpened;
        private bool mDragStarted;

        public BasicFieldGroup(string name, SerializedObject target, GUITypeField[] fields)
        {
            mName = name;
            mTarget = target;
            mFields = fields;
            mPreviews = fields.Where(r =>
                r.Type == typeof(Sprite) || r.Type == typeof(Texture2D) || r.Type == typeof(GameObject)).ToArray();
        }

        public virtual string Name => mName;

        public virtual void Reload() { }

        public void Cleanup()
        {
            if (mPreview != null)
                mPreview.Cleanup();
            mPreview = null;
        }

        /// <summary>预览字段选择菜单。</summary>
        protected virtual void OnPreviewMenu(GenericMenu menu) { }

        public virtual void OnGUI(Rect rect)
        {
            var headerH = mPreviews.Length > 0 ? 20.0f : 0.0f;
            var previewH = mPreviewOpened ? mPreviewH : headerH;
            if (mPreviews.Length <= 0)
                previewH = 0;

            // 字段区
            GUILayout.BeginArea(new Rect(rect.x + 6, rect.y + 6, rect.width - 12, rect.height - 12 - previewH));
            mScroll = EditorGUILayout.BeginScrollView(mScroll);

            OnDrawFieldPre(rect);
            foreach (var field in mFields)
                GUIDrawer.DrawField(mTarget, field);
            OnDrawFieldPost(rect);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            // 预览区
            if (mPreviews.Length > 0)
            {
                GUILayout.BeginArea(new Rect(rect.x + 1, rect.y + rect.height - previewH, rect.width - 2, previewH));
                EditorGUILayout.BeginHorizontal();
                if (EditorGUILayout.DropdownButton(new GUIContent(mPreviews[mPreviewIndex].Label), FocusType.Passive, EditorStyles.toolbarDropDown))
                {
                    var menu = new GenericMenu();
                    OnPreviewMenu(menu);
                    for (var i = 0; i < mPreviews.Length; i++)
                    {
                        var index = i;
                        var preview = mPreviews[i];
                        menu.AddItem(new GUIContent(preview.Label), i == mPreviewIndex, () => mPreviewIndex = index);
                    }
                    menu.ShowAsContext();
                }
                var prevW = GUILayoutUtility.GetLastRect().width;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (mPreview == null)
                    mPreview = new ConfigDataPreview(null);

                var previewRect = new Rect(rect.x, rect.y + rect.height - previewH + headerH, rect.width, previewH - headerH);
                if (previewRect.width > 1 && previewRect.height > 1)
                {
                    var field = mPreviews[mPreviewIndex];
                    var prop = mTarget.FindProperty(field.Name);
                    if (field.Type == typeof(Sprite))
                        mPreview.Draw(previewRect, prop?.objectReferenceValue as Sprite);
                    else if (field.Type == typeof(Texture2D))
                        mPreview.Draw(previewRect, prop?.objectReferenceValue as Texture2D);
                    else if (field.Type == typeof(GameObject))
                        mPreview.Draw(previewRect, prop?.objectReferenceValue as GameObject);
                }
                GUILayout.EndArea();

                // 分隔线 + 拖拽
                GUIDrawer.FillRect(new Rect(rect.x + 1, rect.y + rect.height - previewH, rect.width - 2, 1));
                GUIDrawer.FillRect(new Rect(rect.x + 1, rect.y + rect.height - previewH + headerH, rect.width - 2, 1));

                var dragableRect = new Rect(rect.x + prevW + 1, rect.y + rect.height - previewH, rect.width - 2 - prevW, headerH);
                var delta = SlideRect(dragableRect, MouseCursor.ResizeVertical).y;
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
                if (mPreviewH > rect.height)
                    mPreviewH = rect.height;
            }
        }

        protected virtual void OnDrawFieldPre(Rect rect) { }
        protected virtual void OnDrawFieldPost(Rect rect) { }

        private Vector2 SlideRect(Rect rect, MouseCursor cursor)
        {
            EditorGUIUtility.AddCursorRect(rect, cursor);

            var controlID = GUIUtility.GetControlID(FocusType.Passive);
            if (GUI.enabled && Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            {
                mDragStarted = false;
                GUIUtility.hotControl = controlID;
                EditorGUIUtility.SetWantsMouseJumping(1);
                Event.current.Use();
            }
            else if (GUIUtility.hotControl == controlID)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    mDragStarted = true;
                    Event.current.Use();
                    GUI.changed = true;
                    return Event.current.delta;
                }

                if (Event.current.type == EventType.MouseUp)
                {
                    if (!mDragStarted)
                        mPreviewOpened = !mPreviewOpened;

                    mDragStarted = false;
                    GUIUtility.hotControl = 0;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                    Event.current.Use();
                }
            }

            return Vector2.zero;
        }
    }

    /// <summary>
    /// 字段组编辑器基类：与 <see cref="BaseConfigEditor{T}"/> 等价但通过
    /// <see cref="FieldGroup"/> 抽象，允许完全自定义组渲染。
    /// </summary>
    public class FieldGroupEditor<T> : ConfigDataEditor where T : ScriptableObject
    {
        private FieldGroup[] mGroups;
        private static readonly Dictionary<GUIType, bool[]> AllPageStates = new Dictionary<GUIType, bool[]>();

        protected virtual FieldGroup[] CreateGroups(GUIType type)
        {
            return type.Groups.Select(r => new BasicFieldGroup(r, mTarget, type.GetGroupBy(r).ToArray())).ToArray();
        }

        public override void Reload()
        {
            base.Reload();
            if (mGroups != null)
                foreach (var g in mGroups)
                    g.Reload();
        }

        public override void Dispose()
        {
            if (mGroups != null)
                foreach (var g in mGroups)
                    g.Cleanup();
            base.Dispose();
        }

        public override void OnGUI(float viewW, float viewH)
        {
            if (mTarget == null)
                return;

            mTarget.Update();

            var guiType = GUIType.Get(typeof(T));
            if (mGroups == null)
                mGroups = CreateGroups(guiType);

            if (!AllPageStates.TryGetValue(guiType, out var groupVisibles))
            {
                groupVisibles = mGroups.Select(r => true).ToArray();
                if (groupVisibles.Length > 0)
                    groupVisibles[0] = true;
                AllPageStates.Add(guiType, groupVisibles);
            }

            if (mGroups.Length > 1)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

                for (var i = 0; i < mGroups.Length; i++)
                {
                    groupVisibles[i] = GUILayout.Toggle(groupVisibles[i], $" {mGroups[i].Name} ", EditorStyles.toolbarButton);
                    if (groupVisibles[i] && mGroups[i] is SingleFieldGroup)
                    {
                        for (var j = 0; j < groupVisibles.Length; j++)
                        {
                            if (j != i)
                                groupVisibles[j] = false;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            var count = groupVisibles.Count(r => r);
            var left = 0.0f;
            var top = 28;
            var width = viewW / count;
            var height = viewH - top;

            for (var i = 0; i < groupVisibles.Length; i++)
            {
                if (!groupVisibles[i])
                    continue;

                if (width > 12 && height > 12)
                    mGroups[i].OnGUI(new Rect(left, top, width, height));

                left += width;

                var oldColor = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.3f);
                GUI.DrawTexture(new Rect(left - 1, top, 1, height), Texture2D.whiteTexture);
                GUI.color = oldColor;
            }

            mTarget.ApplyModifiedProperties();
        }
    }
}
