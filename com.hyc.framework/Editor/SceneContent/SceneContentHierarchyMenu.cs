using UnityEditor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// Hierarchy 右键 / 顶部 GameObject 菜单里的「Scene Content 类型列表」。
    /// <para>
    /// Unity 的 <c>[MenuItem]</c> 是静态特性，工程里动态生成的类型没法注册成静态子项；
    /// 所以菜单项只有一个，点开时用 <see cref="GenericMenu"/> <b>现场列出全部</b>
    /// <c>SceneContentTypeDef</c>：已编译的能点，未生成 / 未编译的置灰并附原因。
    /// </para>
    /// <para>
    /// 同一份菜单也由 <c>SceneContentWindow</c> 工具栏按钮调出（那里一定有 GUI 事件上下文），
    /// 两条入口共用 <see cref="BuildMenu"/>，避免各写一份判定。
    /// </para>
    /// </summary>
    public static class SceneContentHierarchyMenu
    {
        private const string MenuPath = "GameObject/Scene Content/Add Component...";

        [MenuItem(MenuPath, false, 20)]
        private static void ShowFromGameObjectMenu()
        {
            BuildMenu().ShowAsContext();
        }

        /// <summary>构建类型列表菜单；调用方负责 <c>ShowAsContext()</c>。</summary>
        public static GenericMenu BuildMenu()
        {
            var menu = new GenericMenu();
            var targets = Selection.gameObjects;

            menu.AddDisabledItem(new GUIContent(targets != null && targets.Length > 0
                ? $"目标：当前选中的 {targets.Length} 个 GameObject"
                : "目标：新建一个 GameObject 并挂载"));
            menu.AddSeparator("");

            var defs = SceneContentTypeCodeGen.LoadAllTypes();
            if (defs.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("（暂无场景内容类型，先到 Scene Content Window 新建）"));
            }
            else
            {
                foreach (var def in defs)
                {
                    var d = def;
                    // displayName 允许「分类/名称」，半角 / 会被 GenericMenu 当子菜单分隔符
                    var label = (string.IsNullOrEmpty(d.displayName) ? d.className : d.displayName).Replace('/', '·');
                    var path = $"{label} ({d.className})";
                    var hint = SceneContentComponentResolver.FailureHint(d);
                    if (hint != null)
                        menu.AddDisabledItem(new GUIContent(path + " — " + hint));
                    else
                        menu.AddItem(new GUIContent(path), false, () => Add(d));
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("打开 Scene Content Window"), false, SceneContentWindow.Open);
            return menu;
        }

        private static void Add(SceneContentTypeDef def)
        {
            var type = SceneContentComponentResolver.Resolve(def);
            if (type == null)
            {
                EditorUtility.DisplayDialog("添加场景内容组件",
                    SceneContentComponentResolver.FailureHint(def) ?? "无法解析生成组件类型", "确定");
                return;
            }

            var targets = Selection.gameObjects;
            if (targets == null || targets.Length == 0)
            {
                CreateNewGo(def, type);
                return;
            }

            var added = 0;
            foreach (var go in targets)
            {
                if (go == null || PrefabUtility.IsPartOfPrefabAsset(go))
                    continue;
                Undo.AddComponent(go, type);
                added++;
            }
            if (added == 0)
                EditorUtility.DisplayDialog("添加场景内容组件",
                    "选中的是 Prefab 资产本体，请双击进入 Prefab 编辑模式后再添加。", "确定");
        }

        private static void CreateNewGo(SceneContentTypeDef def, System.Type type)
        {
            var label = (string.IsNullOrEmpty(def.displayName) ? def.className : def.displayName).Replace('/', '·');
            var go = new GameObject(label);
            Undo.RegisterCreatedObjectUndo(go, "Create Scene Content GameObject");
            Undo.AddComponent(go, type);
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }
    }
}
