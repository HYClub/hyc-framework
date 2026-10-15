using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 内置图标浏览器：全部图标平铺网格（列数随窗口宽度自适应）+ 顶部搜索框过滤。
    /// 点击选中并回调返回图标名。也支持选择自定义 Texture2D（浏览按钮走 ObjectPicker），
    /// 以及清除当前图标（回归默认）。
    /// </summary>
    public class ConfigIconPicker : EditorWindow
    {
        private Action<string> mOnPickBuiltIn;
        private Action<Texture2D> mOnPickCustom;
        private Action mOnClear;

        private string mSearch = "";
        private SearchField mSearchField;
        private Vector2 mScroll;
        private List<string> mFiltered;
        private List<string> mAll;
        private bool mClosing;
        private bool mPickerHandled;

        private const float ItemSize = 56;
        private const float MinCellWidth = 100f;

        public static void Open(Action<string> onPickBuiltIn, Action<Texture2D> onPickCustom, Action onClear)
        {
            var window = CreateInstance<ConfigIconPicker>();
            window.mOnPickBuiltIn = onPickBuiltIn;
            window.mOnPickCustom = onPickCustom;
            window.mOnClear = onClear;
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

            EditorGUILayout.Space(2);

            // 图标网格占满中部剩余空间；底部按钮栏为独立一行（helpBox 分栏）
            mScroll = EditorGUILayout.BeginScrollView(mScroll);

            // 可用宽度预留垂直滚动条位置：滚动条出现与否列数一致，不触发横向滚动条；
            // 列数按最小格宽分档，格子等分填满可用宽度（无剩余空间可拉伸）
            var avail = position.width - 27f;
            var columns = Mathf.Max(2, Mathf.FloorToInt(avail / MinCellWidth));
            var cellWidth = avail / columns;

            if (mFiltered.Count == 0)
            {
                GUILayout.Label("没有匹配的图标", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                var rowCount = Mathf.CeilToInt(mFiltered.Count / (float)columns);
                for (var row = 0; row < rowCount; row++)
                {
                    EditorGUILayout.BeginHorizontal();
                    var first = row * columns;
                    var last = Mathf.Min(first + columns, mFiltered.Count);
                    for (var i = first; i < last; i++)
                    {
                        var name = mFiltered[i];
                        // GUIStyle.none 占位向滚动视图贡献真实格区高度（否则滚动条不出现），
                        // 再取精确 rect 手动绘制，格区尺寸不随布局拉伸
                        GUILayout.Box(GUIContent.none, GUIStyle.none,
                            GUILayout.Width(cellWidth - 6), GUILayout.Height(ItemSize));
                        DrawIconCell(GUILayoutUtility.GetLastRect(), name);
                    }
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(2);

            // 底部固定栏：自定义图标浏览 + 清除（与顶部的搜索区上下分栏）
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("或选择项目内图标:", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("浏览项目图标…", GUILayout.Width(120), GUILayout.Height(22)))
            {
                PickCustom();
            }
            if (GUILayout.Button("清除", GUILayout.Width(50), GUILayout.Height(22)))
            {
                mOnClear?.Invoke();
                SafeClose();
            }
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                SafeClose();
                Event.current.Use();
            }
        }

        // 关闭幂等：窗口可能正处于 Unity 自身发起的关闭流程（如点窗口 ✕ 触发
        // InternalCloseWindow，其内部仍会 pump OnGUI），此时同步 Close() 会二次
        // 销毁同一对象；统一推迟到关闭流程外执行，窗口已销毁（Unity 伪 null）则跳过
        private void SafeClose()
        {
            if (mClosing)
                return;
            mClosing = true;
            EditorApplication.delayCall += TryClose;
        }

        private void TryClose()
        {
            if (this == null)
                return;
            Close();
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

        private void DrawIconCell(Rect cellRect, string name)
        {
            if (cellRect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(cellRect, new Color(1f, 1f, 1f, 0.06f));

            var tex = ConfigTemplateIcon.GetBuiltIn(name);
            if (tex != null)
                GUI.DrawTexture(new Rect(cellRect.x + cellRect.width * 0.5f - 14, cellRect.y + 4, 28, 28), tex);
            GUI.Label(new Rect(cellRect.x, cellRect.y + 36, cellRect.width, 16), ShortName(name), NameStyle);

            if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none))
            {
                mOnPickBuiltIn?.Invoke(name);
                SafeClose();
            }
        }

        private static GUIStyle mNameStyle;
        private static GUIStyle NameStyle
        {
            get
            {
                if (mNameStyle == null)
                    mNameStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                return mNameStyle;
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
            // 重置一次性标志：每次重新打开 picker 都允许处理下一次关闭事件
            mPickerHandled = false;
            // 用 Unity 原生 ObjectPicker 选项目内 Texture2D
            mPickerControlId = EditorGUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.ShowObjectPicker<Texture2D>(null, false, "", mPickerControlId);
        }

        private void HandleObjectPickerClosed()
        {
            // ObjectSelectorClosed 在同一关闭周期内可能重复送达，只处理第一次；
            // 取消选择同样置位（避免后续重复送达重复触发回调）
            if (mPickerHandled)
                return;
            mPickerHandled = true;

            var picked = EditorGUIUtility.GetObjectPickerObject();
            if (picked is Texture2D tex)
            {
                mOnPickCustom?.Invoke(tex);
                SafeClose();
            }
        }

        private int mPickerControlId = -1;
    }
}
