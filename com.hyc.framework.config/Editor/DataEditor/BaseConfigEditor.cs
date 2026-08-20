using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 分组页签编辑器基类：按 <see cref="GroupAttribute"/> 分组，
    /// 每个分组一个页签（可多开并排），每组独立滚动 + 底部可选预览条。
    /// 自定义配置编辑器继承本类（配 [CfgEditor]）即可获得完整分组编辑能力。
    /// </summary>
    public class BaseConfigEditor<T> : ConfigDataEditor where T : ScriptableObject
    {
        private static readonly Dictionary<GUIType, bool[]> AllPageStates = new Dictionary<GUIType, bool[]>();
        private Vector2[] mPageScrolls;

        public override void OnGUI(float viewW, float viewH)
        {
            if (mTarget == null)
                return;

            mTarget.Update();

            var guiType = GUIType.Get(typeof(T));
            if (!AllPageStates.TryGetValue(guiType, out var guiStates))
            {
                guiStates = guiType.Groups.Select(r => true).ToArray();
                if (guiStates.Length > 0)
                    guiStates[0] = true;
                AllPageStates.Add(guiType, guiStates);
            }

            // 顶部分隔线
            var oldColor = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.3f);
            GUI.DrawTexture(new Rect(0, 28, viewW, 1), Texture2D.whiteTexture);
            GUI.color = oldColor;

            if (guiType.Groups.Count > 1)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

                for (var i = 0; i < guiType.Groups.Count; i++)
                {
                    var idx = i;
                    guiStates[i] = GUILayout.Toggle(guiStates[i], $" {guiType.Groups[i]} ", EditorStyles.toolbarButton);
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            if (mPageScrolls == null)
                mPageScrolls = guiType.Groups.Select(r => Vector2.zero).ToArray();

            var count = guiStates.Count(r => r);
            var left = 0.0f;
            var top = 28;
            var width = viewW / count;
            var height = viewH - top;

            for (var i = 0; i < guiStates.Length; i++)
            {
                if (!guiStates[i])
                    continue;

                if (width > 12 && height > 12)
                    OnPageGUI(guiType, i, new Rect(left, top, width, height));

                left += width;

                GUI.color = new Color(0, 0, 0, 0.3f);
                GUI.DrawTexture(new Rect(left - 1, top, 1, height), Texture2D.whiteTexture);
                GUI.color = oldColor;
            }

            // 右下角：保存 + 导出按钮
            const float bottomBarH = 26;
            if (GUI.Button(new Rect(viewW - 154, viewH - bottomBarH, 68, 20), "保存"))
            {
                mTarget.ApplyModifiedProperties();
                var obj = mTarget.targetObject;
                if (obj != null)
                {
                    EditorUtility.SetDirty(obj);
                    AssetDatabase.SaveAssetIfDirty(obj);
                }
            }
            if (GUI.Button(new Rect(viewW - 80, viewH - bottomBarH, 74, 20), "导出"))
            {
                mTarget.ApplyModifiedProperties();
                var obj = mTarget.targetObject;
                if (obj != null)
                {
                    if (ConfigExportService.ExportSingle(obj, true, true))
                        mWindow?.ShowNotification(new GUIContent("已导出"));
                }
            }

            mTarget.ApplyModifiedProperties();
        }

        private float mPreviewH = 80;

        /// <summary>单页绘制：滚动字段区 + 底部预览条（可拖动）。</summary>
        protected virtual void OnPageGUI(GUIType type, int page, Rect rect)
        {
            var pageTop = 6;
            var scrollH = rect.height - 12 - mPreviewH;
            if (scrollH < 0) scrollH = 0;

            GUILayout.BeginArea(new Rect(rect.x + 6, rect.y + pageTop, rect.width - 12, scrollH));
            mPageScrolls[page] = EditorGUILayout.BeginScrollView(mPageScrolls[page]);

            foreach (var field in type.GetGroupBy(type.Groups[page]))
                GUIDrawer.DrawField(mTarget, field);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            // 预览条区域
            var previewTop = rect.y + rect.height - mPreviewH;
            GUILayout.BeginArea(new Rect(rect.x + 1, previewTop, rect.width - 2, mPreviewH));
            EditorGUILayout.BeginHorizontal();
            var dropdownW = 40f;
            GUILayout.Label("预览", EditorStyles.miniLabel);
            var prevW = GUILayoutUtility.GetLastRect().width;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUIDrawer.FillRect(new Rect(rect.x + 1, previewTop, rect.width - 2, 1));
            var dragableRect = new Rect(rect.x + 1 + prevW, previewTop, rect.width - 2 - prevW, 20);
            var delta = GUIDrawer.SlideRect(dragableRect, MouseCursor.ResizeVertical).y;
            mPreviewH -= delta;

            if (mPreviewH < 30)
                mPreviewH = 30;
            if (mPreviewH > rect.height)
                mPreviewH = rect.height;
        }
    }
}
