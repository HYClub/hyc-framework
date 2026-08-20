using System;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 带确定/取消和校验的输入框弹窗。返回 true 表示用户确认，输入值在
    /// <paramref name="input"/> 中；校验失败时显示错误并保持弹窗。
    /// </summary>
    public class EditorInputDialog : EditorWindow
    {
        private string mTitle;
        private string mLabel;
        private string mValue;
        private Func<string, bool> mValidator;
        private string mError;
        private bool mDone;
        private bool mResult;

        public static bool Show(string title, string label, ref string input,
            Func<string, bool> validator, string errorMessage)
        {
            var window = CreateInstance<EditorInputDialog>();
            window.titleContent = new GUIContent(title);
            window.mTitle = title;
            window.mLabel = label;
            window.mValue = input;
            window.mValidator = validator;
            window.mError = errorMessage;
            window.minSize = new Vector2(360, 150);
            window.position = new Rect(
                Screen.width / 2 - 180, Screen.height / 2 - 75, 360, 150);
            window.ShowModalUtility();
            input = window.mValue;
            return window.mResult;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(mTitle, EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(mLabel, EditorStyles.wordWrappedLabel);
            GUI.SetNextControlName("InputField");
            mValue = EditorGUILayout.TextField(mValue);
            if (Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != "InputField")
            {
                GUI.FocusControl("InputField");
                mValue = mValue; // keep
            }

            EditorGUILayout.Space();
            if (!string.IsNullOrEmpty(mError) && !string.IsNullOrEmpty(mValue) && !mValidator(mValue))
            {
                EditorGUILayout.HelpBox(mError, MessageType.Error);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("确定", GUILayout.Width(80)))
            {
                if (mValidator(mValue))
                {
                    mResult = true;
                    mDone = true;
                    Close();
                }
            }
            if (GUILayout.Button("取消", GUILayout.Width(80)))
            {
                mResult = false;
                mDone = true;
                Close();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    if (mValidator(mValue))
                    {
                        mResult = true;
                        mDone = true;
                        Close();
                    }
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Escape)
                {
                    mResult = false;
                    mDone = true;
                    Close();
                    Event.current.Use();
                }
            }
        }
    }
}
