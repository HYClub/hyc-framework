using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Config asset creation popup: category list on the left, asset-type grid
    /// on the right, optional search, and single/multi-file creation.
    /// Framework port of the source <c>ConfigCreateWindow</c> with a local ID
    /// provider instead of the server round-trip.
    /// </summary>
    public class ConfigCreateWindow : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(720, 400);

        private Dictionary<string, List<CategoryItem>> mCategoryGroups;
        private List<string> mCategoryNames;
        private int mSelectedCategoryIndex;
        private Vector2 mLeftScroll;
        private Vector2 mRightScroll;
        private string mSearchString = "";
        private List<CategoryItem> mFilteredItems;
        private SearchField mSearchField;

        private CategoryItem mSelectedItem;
        private string mTargetCreateFolder;

        private readonly GUIContent[] mCreateMode = { new GUIContent("单文件"), new GUIContent("多文件") };
        private int mCreateModeIndex;
        private string mFileNameForSingle;
        private string mFileNameForMultiple;
        private int mFileCount = 2;

        private GUIStyle mNormalStyle;
        private GUIStyle mLabelStyle;
        private bool mClosedByAction;

        private class CategoryItem
        {
            public Type Type;
            public string DisplayName;
            public string FullPath;
            public Texture2D Icon;
            public int Order;
            public bool Unique;
        }

        public static void ShowWindow(string targetFolder)
        {
            var window = CreateInstance<ConfigCreateWindow>();
            window.titleContent = new GUIContent("配置创建器");
            window.minSize = WindowSize;
            window.maxSize = WindowSize;
            window.mTargetCreateFolder = targetFolder;

            var mainWindow = EditorWindow.focusedWindow;
            Vector2 center;
            if (mainWindow != null)
            {
                var r = mainWindow.position;
                center = new Vector2(r.x + (r.width - WindowSize.x) / 2, r.y + (r.height - WindowSize.y) / 2);
            }
            else
            {
                center = new Vector2(
                    (Screen.currentResolution.width - WindowSize.x) / 2,
                    (Screen.currentResolution.height - WindowSize.y) / 2);
            }

            window.position = new Rect(center.x, center.y, WindowSize.x, WindowSize.y);
            window.ShowUtility();
            window.Focus();
        }

        private void OnEnable()
        {
            mSearchField = new SearchField();
            InitializeCategoryData();
        }

        private void OnDisable()
        {
            if (!mClosedByAction)
                Debug.Log("ConfigCreateWindow closed without action.");
        }

        private void InitializeCategoryData()
        {
            mCategoryGroups = new Dictionary<string, List<CategoryItem>>();

            var types = TypeCache.GetTypesWithAttribute<CfgAssetAttribute>()
                .Where(r => !r.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(r))
                .Where(r => r != typeof(ConfigEnumDefinition)) // 枚举定义在数据树右键创建，不走配置创建器
                .ToList();

            foreach (var type in types)
            {
                var attr = type.GetCustomAttributes(typeof(CfgAssetAttribute), true).FirstOrDefault() as CfgAssetAttribute;
                if (attr == null)
                    continue;

                var categoryName = "常规";
                var displayName = type.Name;

                if (!string.IsNullOrEmpty(attr.Name))
                {
                    var parts = attr.Name.Split('/', '\\');
                    if (parts.Length == 1)
                    {
                        displayName = parts[0];
                    }
                    else if (parts.Length > 1)
                    {
                        categoryName = parts[0];
                        displayName = parts[parts.Length - 1];
                    }
                }

                if (!mCategoryGroups.TryGetValue(categoryName, out var list))
                {
                    list = new List<CategoryItem>();
                    mCategoryGroups[categoryName] = list;
                }

                list.Add(new CategoryItem
                {
                    Type = type,
                    DisplayName = displayName,
                    FullPath = attr.Name,
                    Icon = GetIconForType(type),
                    Order = attr.Order,
                    Unique = attr.Unique,
                });
            }

            foreach (var category in mCategoryGroups.Values)
                category.Sort((a, b) => a.Order.CompareTo(b.Order));

            mCategoryNames = mCategoryGroups.Keys.OrderBy(r => r).ToList();

            UpdateFilteredItems();
        }

        private void UpdateFilteredItems()
        {
            if (string.IsNullOrEmpty(mSearchString))
            {
                mFilteredItems = null;
            }
            else
            {
                mFilteredItems = mCategoryGroups.Values
                    .SelectMany(r => r)
                    .Where(r =>
                        r.DisplayName.ToLower().Contains(mSearchString.ToLower()) ||
                        r.FullPath.ToLower().Contains(mSearchString.ToLower()) ||
                        r.Type.Name.ToLower().Contains(mSearchString.ToLower()))
                    .OrderBy(r => r.Order)
                    .ToList();
            }
        }

        private static Texture2D GetIconForType(Type type)
        {
            // 模板生成的类型：从对应 className 的模板读图标
            var template = ConfigTemplateCodeGen.LoadAllTemplates()
                .FirstOrDefault(t => t.className == type.Name);
            if (template != null)
            {
                var tplIcon = ConfigTemplateIcon.Resolve(template);
                if (tplIcon != null)
                    return tplIcon as Texture2D;
            }

            try
            {
                var instance = ScriptableObject.CreateInstance(type);
                var script = MonoScript.FromScriptableObject(instance);
                UnityEngine.Object.DestroyImmediate(instance);
                return EditorGUIUtility.ObjectContent(script, type).image as Texture2D;
            }
            catch
            {
                return null;
            }
        }

        private void OnGUI()
        {
            if (mCategoryGroups == null || mCategoryNames == null)
            {
                InitializeCategoryData();
                return;
            }

            var searchRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 6);
            var newSearch = mSearchField.OnToolbarGUI(new Rect(searchRect.x + 3, searchRect.y + 5, searchRect.width - 6, searchRect.height - 6), mSearchString);
            if (newSearch != mSearchString)
            {
                mSearchString = newSearch;
                UpdateFilteredItems();
            }

            DrawLine(EditorGUILayout.GetControlRect(false, 1));

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            GUILayout.Space(6);
            DrawCategoryList();
            var lastRect = GUILayoutUtility.GetLastRect();
            DrawLine(new Rect(lastRect.xMax + 1, lastRect.y, 1, lastRect.height));
            GUILayout.Space(6);
            DrawItemList();
            GUILayout.Space(3);
            EditorGUILayout.EndHorizontal();

            DrawLine(EditorGUILayout.GetControlRect(false, 1));

            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6);
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            mCreateModeIndex = EditorGUILayout.Popup(new GUIContent("创建模式"), mCreateModeIndex, mCreateMode);
            if (mCreateModeIndex == 0)
            {
                mFileNameForSingle = EditorGUILayout.TextField("文件名称", mFileNameForSingle);
            }
            else
            {
                mFileNameForMultiple = EditorGUILayout.TextField("文件名称", mFileNameForMultiple);
                mFileCount = EditorGUILayout.IntField("文件数量", mFileCount);
                if (mFileCount < 2)
                    mFileCount = 2;
            }
            GUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(" 确 定 ", GUILayout.ExpandWidth(false), GUILayout.Width(68), GUILayout.Height(26)))
            {
                if (mSelectedItem != null)
                    CreateConfigAsset(mSelectedItem);
            }
            if (GUILayout.Button(" 取 消 ", GUILayout.ExpandWidth(false), GUILayout.Width(68), GUILayout.Height(26)))
            {
                mClosedByAction = true;
                Close();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(12);

            DrawLine(new Rect(0, 0, 1, position.height));
            DrawLine(new Rect(position.width - 1, 0, 1, position.height));
            DrawLine(new Rect(0, 0, position.width, 1));
            DrawLine(new Rect(0, position.height - 1, position.width, 1));

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                mClosedByAction = true;
                Close();
                Event.current.Use();
            }
        }

        private void DrawLine(Rect rect)
        {
            GUIDrawer.FillRect(rect);
        }

        private void DrawCategoryList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(188), GUILayout.ExpandWidth(false));

            mLeftScroll = EditorGUILayout.BeginScrollView(mLeftScroll);

            for (var i = 0; i < mCategoryNames.Count; i++)
            {
                var categoryName = mCategoryNames[i];
                var style = mSelectedCategoryIndex == i ? EditorStyles.selectionRect : EditorStyles.label;

                if (GUILayout.Button(categoryName, style, GUILayout.Height(20), GUILayout.ExpandWidth(true)))
                {
                    mSelectedCategoryIndex = i;
                    mSelectedItem = null;
                    mFileNameForSingle = string.Empty;
                    mFileNameForMultiple = string.Empty;
                    GUIUtility.keyboardControl = 0;
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawItemList()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            var items = !string.IsNullOrEmpty(mSearchString) && mFilteredItems != null
                ? mFilteredItems
                : (mSelectedCategoryIndex >= 0 && mSelectedCategoryIndex < mCategoryNames.Count
                    ? mCategoryGroups[mCategoryNames[mSelectedCategoryIndex]]
                    : null);

            if (items == null || items.Count <= 0)
            {
                GUILayout.Label("请选择左侧分类或使用搜索", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            mRightScroll = EditorGUILayout.BeginScrollView(mRightScroll);

            const int itemsPerRow = 4;
            const float itemWidth = 120f;
            const float itemHeight = 80f;

            for (var i = 0; i < items.Count; i += itemsPerRow)
            {
                EditorGUILayout.BeginHorizontal();
                for (var j = 0; j < itemsPerRow && i + j < items.Count; j++)
                {
                    DrawConfigItem(items[i + j], itemWidth, itemHeight);
                    if (j < itemsPerRow - 1 && i + j + 1 < items.Count)
                        GUILayout.Space(5);
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(5);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawConfigItem(CategoryItem item, float width, float height)
        {
            var isSelected = mSelectedItem == item;

            var controlRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));

            if (mNormalStyle == null)
            {
                mNormalStyle = new GUIStyle
                {
                    padding = GUI.skin.box.padding,
                    margin = GUI.skin.box.margin,
                };
            }

            GUI.Box(controlRect, "", isSelected ? EditorStyles.selectionRect : mNormalStyle);

            var contentRect = new Rect(controlRect.x + 5, controlRect.y + 10, controlRect.width - 10, controlRect.height - 15);

            if (item.Icon != null)
            {
                var iconRect = new Rect(contentRect.x + (contentRect.width - 32) / 2, contentRect.y + 5, 32, 32);
                GUI.DrawTexture(iconRect, item.Icon, ScaleMode.ScaleToFit);
            }

            var displayText = !string.IsNullOrEmpty(mSearchString) ? item.FullPath : item.DisplayName;
            var textRect = new Rect(contentRect.x, contentRect.y + 40, contentRect.width, contentRect.height - 40);

            if (mLabelStyle == null)
            {
                mLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    wordWrap = true,
                    alignment = TextAnchor.UpperCenter,
                };
                mLabelStyle.normal.textColor = Color.white;
                mLabelStyle.hover.textColor = Color.white;
            }

            GUI.Label(textRect, displayText, mLabelStyle);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && controlRect.Contains(Event.current.mousePosition))
            {
                if (mSelectedItem != item)
                {
                    mSelectedItem = item;
                    UpdateBatchCreateNamePattern();
                }

                GUIUtility.keyboardControl = 0;
                Event.current.Use();
                Repaint();
            }
        }

        private void UpdateBatchCreateNamePattern()
        {
            if (mSelectedItem == null)
                return;

            if (mCreateModeIndex == 0)
                mFileNameForSingle = mSelectedItem.DisplayName;
            else
                mFileNameForMultiple = mSelectedItem.DisplayName;
        }

        private void CreateConfigAsset(CategoryItem item)
        {
            if (item.Unique)
            {
                var type = item.Type;
                var guids = AssetDatabase.FindAssets($"t:{type.Name}", new[] { ConfigDataSettings.RootFolder });
                if (guids.Any(g => type.IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GUIDToAssetPath(g)))))
                {
                    EditorUtility.DisplayDialog("错误", "此类配置只允许有一个，项目中已经有了!", "确定");
                    return;
                }
            }

            if (string.IsNullOrEmpty(mTargetCreateFolder))
                mTargetCreateFolder = ConfigDataSettings.RootFolder;

            ConfigDataSettings.EnsureRootFolder();

            if (mCreateModeIndex == 0)
                CreateSingleAsset(item.Type);
            else
                CreateMultiAsset(item.Type);
        }

        private void CreateSingleAsset(Type type)
        {
            if (string.IsNullOrEmpty(mFileNameForSingle))
            {
                EditorUtility.DisplayDialog("错误", "文件名称不能为空", "确定");
                return;
            }

            var fileName = $"{mFileNameForSingle}.asset";
            var fullPath = Path.Combine(mTargetCreateFolder, fileName);

            var index = 1;
            while (File.Exists(fullPath))
            {
                fileName = $"{mFileNameForSingle} {index}.asset";
                fullPath = Path.Combine(mTargetCreateFolder, fileName);
                index++;
            }

            var asset = ScriptableObject.CreateInstance(type);

            AssetDatabase.CreateAsset(asset, fullPath);
            if (!AssignNewId(asset))
            {
                AssetDatabase.DeleteAsset(fullPath);
                return;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);

            mClosedByAction = true;
            EditorUtility.DisplayDialog("创建成功", $"已在 {mTargetCreateFolder} 下成功创建了 {Path.GetFileName(fullPath)}", "确定");
            Close();
        }

        /// <summary>从当前 ID 构造器获取 {id,guid} 并写入资产。失败时弹窗并返回 false。</summary>
        private static bool AssignNewId(ScriptableObject asset)
        {
            ConfigId cid;
            try
            {
                cid = ConfigIdService.RequestIdSync();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("ID 获取失败",
                    $"无法从 ID 构造器获取 ID：{e.Message}\n\n局域网模式请确认服务器在线（可检测），或切换到本地构造器。", "确定");
                return false;
            }

            if (!ConfigIdService.TryWriteId(asset, cid))
            {
                EditorUtility.DisplayDialog("警告", "该配置类型没有 ID/GUID 字段（基类未挂 ConfigBase？），未分配 ID。", "确定");
                return false;
            }
            return true;
        }

        private void CreateMultiAsset(Type type)
        {
            var paths = new List<string>();

            // 多文件模式：文件名作为前缀，自动追加 _序号（位数按数量算）
            var prefix = mFileNameForMultiple;
            if (string.IsNullOrEmpty(prefix))
                prefix = mSelectedItem != null ? mSelectedItem.DisplayName : type.Name;

            // 位数：1~9 → 1位, 10~99 → 2位, 100~999 → 3位 ...
            var digits = Math.Max(1, mFileCount.ToString().Length);

            for (var i = 1; i <= mFileCount; i++)
            {
                var fileName = $"{prefix}_{i.ToString("D" + digits)}.asset";
                paths.Add(Path.Combine(mTargetCreateFolder, fileName));
            }

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    EditorUtility.DisplayDialog("错误", $"文件已经存在! ({path})", "确定");
                    return;
                }
            }

            var createdAssets = new List<UnityEngine.Object>();
            for (var i = 0; i < paths.Count; i++)
            {
                var asset = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(asset, paths[i]);
                createdAssets.Add(asset);
            }

            // 逐个分配 ID/GUID；失败则回滚已创建资产
            foreach (var asset in createdAssets)
            {
                if (!AssignNewId(asset as ScriptableObject))
                {
                    foreach (var created in createdAssets)
                    {
                        var p = AssetDatabase.GetAssetPath(created);
                        if (!string.IsNullOrEmpty(p))
                            AssetDatabase.DeleteAsset(p);
                    }
                    return;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.objects = createdAssets.ToArray();

            mClosedByAction = true;
            EditorUtility.DisplayDialog("创建成功",
                $"已在 {mTargetCreateFolder} 下成功创建了 {string.Join(", ", paths.Select(Path.GetFileName))}", "确定");
            Close();
        }
    }
}
