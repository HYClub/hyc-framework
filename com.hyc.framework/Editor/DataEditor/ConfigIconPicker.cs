using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 内置图标浏览器：全部图标平铺网格 + 顶部搜索框过滤。
    /// 点击选中并回调返回图标名。也支持选择自定义 Texture2D（浏览按钮走 ObjectPicker）。
    /// </summary>
    public class ConfigIconPicker : EditorWindow
    {
        private Action<string> mOnPickBuiltIn;
        private Action<Texture2D> mOnPickCustom;

        private string mSearch = "";
        private SearchField mSearchField;
        private Vector2 mScroll;
        private List<string> mFiltered;
        private List<string> mAll;

        private const float ItemSize = 56;
        private const int Columns = 6;

        public static void Open(Action<string> onPickBuiltIn, Action<Texture2D> onPickCustom)
        {
            var window = CreateInstance<ConfigIconPicker>();
            window.mOnPickBuiltIn = onPickBuiltIn;
            window.mOnPickCustom = onPickCustom;
            window.titleContent = new GUIContent("选择图标");
            window.minSize = new Vector2(400, 480);
            window.ShowUtility();
            window.Focus();
        }

        private void OnEnable()
        {
            mSearchField = new SearchField();
            mAll = ConfigTemplateIcon.GetBuiltInNames();
            mFiltered = mAll;
        }

        private void OnGUI()
        {
            // ObjectPicker 关闭回调
            if (Event.current != null && Event.current.commandName == "ObjectSelectorClosed")
            {
                HandleObjectPickerClosed();
                return;
            }

            EditorGUILayout.Space();
            var searchRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4);
            var newSearch = mSearchField.OnToolbarGUI(searchRect, mSearch);
            if (newSearch != mSearch)
            {
                mSearch = newSearch;
                ApplyFilter();
            }

            // 自定义图标浏览按钮
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("或选择项目内图标:", EditorStyles.miniLabel);
            if (GUILayout.Button("浏览项目图标…", GUILayout.Width(120)))
            {
                PickCustom();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            mScroll = EditorGUILayout.BeginScrollView(mScroll);

            var rowCount = Mathf.CeilToInt(mFiltered.Count / (float)Columns);
            var startY = 8f;
            var cellSize = (position.width - 24f) / Columns;
            for (var row = 0; row < rowCount; row++)
            {
                for (var col = 0; col < Columns; col++)
                {
                    var index = row * Columns + col;
                    if (index >= mFiltered.Count)
                        continue;

                    var name = mFiltered[index];
                    var cellRect = new Rect(4 + col * cellSize, startY + row * (ItemSize + 6), cellSize - 4, ItemSize);

                    var tex = ConfigTemplateIcon.GetBuiltIn(name);
                    var bg = GUI.backgroundColor;
                    if (tex != null)
                        GUI.DrawTexture(new Rect(cellRect.x + cellSize * 0.5f - 14, cellRect.y + 4, 28, 28), tex);
                    GUI.Label(new Rect(cellRect.x, cellRect.y + 36, cellRect.width, 16), ShortName(name), EditorStyles.miniLabel);

                    if (GUI.Button(cellRect, "", GUIStyle.none))
                    {
                        mOnPickBuiltIn?.Invoke(name);
                        Close();
                    }
                }
            }

            if (mFiltered.Count == 0)
                GUILayout.Label("没有匹配的图标", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndScrollView();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
            }
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrEmpty(mSearch))
            {
                mFiltered = mAll;
            }
            else
            {
                mFiltered = mAll.Where(n => n.IndexOf(mSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
        }

        private static string ShortName(string full)
        {
            // 去掉 d_ 前缀和 " Icon" 后缀，显示更友好
            var s = full;
            if (s.StartsWith("d_"))
                s = s.Substring(2);
            s = s.Replace(" Icon", "");
            return s.Length > 12 ? s.Substring(0, 12) : s;
        }

        private void PickCustom()
        {
            // 用 Unity 原生 ObjectPicker 选项目内 Texture2D
            mPickerControlId = EditorGUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.ShowObjectPicker<Texture2D>(null, false, "", mPickerControlId);
        }

        private void HandleObjectPickerClosed()
        {
            var picked = EditorGUIUtility.GetObjectPickerObject();
            if (picked is Texture2D tex)
            {
                mOnPickCustom?.Invoke(tex);
                Close();
            }
        }

        private int mPickerControlId = -1;
    }
}
