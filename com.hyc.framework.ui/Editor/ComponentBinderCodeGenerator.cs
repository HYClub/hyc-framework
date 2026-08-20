using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.UI.Editor
{
    /// <summary>
    /// Generates strongly-typed binder classes from <see cref="ComponentBinderTable"/>
    /// authoring components on prefabs. Produces files that implement
    /// <see cref="IComponentBinder"/>, one per binder, and cleans stale output so
    /// generation is idempotent.
    /// </summary>
    public static class ComponentBinderCodeGenerator
    {
        public const string GeneratedFileMarker = "// ComponentBinder";

        [MenuItem("HYC Framework/UI/Generate UI Binders")]
        public static void GenerateAllMenu() => GenerateAll();

        /// <summary>Validates a C# identifier (variable / type name).</summary>
        public static bool IsVariableName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.Trim();
            return name.Length > 0 && Regex.IsMatch(name, "^[a-zA-Z_][a-zA-Z0-9_]*$");
        }

        /// <summary>Validates a dotted namespace.</summary>
        public static bool IsPackageName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            name = name.Trim();
            if (string.IsNullOrEmpty(name)) return true;
            return Regex.IsMatch(name, "^[a-zA-Z_][a-zA-Z0-9_]*(\\.[a-zA-Z_][a-zA-Z0-9_]*)*$");
        }

        private static string GetHierarchy(GameObject go)
        {
            if (go.transform.parent != null)
                return GetHierarchy(go.transform.parent.gameObject) + "/" + go.name;
            return go.name;
        }

        /// <summary>Generate binders for every prefab in the configured search scope.</summary>
        public static void GenerateAll()
        {
            var setting = ComponentBinderSetting.instance;
            if (!CheckOutputSetting(setting)) return;

            var saveFolder = setting.CodeOutputMethod
                ? AssetDatabase.GetAssetPath(setting.CodeOutputFolder) : setting.CodeOutputFolderPath;
            if (saveFolder.StartsWith("..")) saveFolder = Path.Combine(Application.dataPath, saveFolder);
            CleanFolder(saveFolder);

            var guids = setting.FindAllPrefab
                ? AssetDatabase.FindAssets("t:Prefab")
                : AssetDatabase.FindAssets("t:Prefab", new[] { AssetDatabase.GetAssetPath(setting.PrefabFolder) });
            var prefabs = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path))
                .Where(p => p != null)
                .ToArray();

            var errors = new List<string>();
            var binders = new List<ComponentBinderTable>();
            foreach (var prefab in prefabs) CollectBinder(prefab.transform, binders);

            foreach (var binder in binders)
            {
                // Only generate for prefab roots / non-nested instances.
                var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(binder.gameObject);
                if (outermost == null) Generate(setting, binder, errors);
            }

            foreach (var error in errors) Debug.LogError(error);
        }

        /// <summary>Generate a single binder (from the inspector's "Generate" button).</summary>
        public static void GenerateOne(ComponentBinderTable binder)
        {
            var setting = ComponentBinderSetting.instance;
            if (!CheckOutputSetting(setting)) return;
            var errors = new List<string>();
            Generate(setting, binder, errors);
            foreach (var error in errors) Debug.LogError(error);
        }

        private static void CollectBinder(Transform transform, List<ComponentBinderTable> list)
        {
            var binder = transform.GetComponent<ComponentBinderTable>();
            if (binder != null && !list.Contains(binder)) list.Add(binder);
            for (int i = 0; i < transform.childCount; i++) CollectBinder(transform.GetChild(i), list);
        }

        private static void Generate(ComponentBinderSetting setting, ComponentBinderTable component, List<string> errors)
        {
            if (component.Items == null) return;

            var packName = component.CustomPackageName ? component.PackageName : setting.CodeOutputPackageName;
            var className = component.CustomClassName ? component.ClassName : component.gameObject.name;
            var prefix = setting.ClassNamePrefix?.Trim();
            var suffix = setting.ClassNameSuffix?.Trim();

            var allow = true;
            if (!string.IsNullOrEmpty(packName) && !IsPackageName(packName))
            { errors.Add($"Invalid namespace '{packName}' ({GetHierarchy(component.gameObject)})"); allow = false; }
            if (string.IsNullOrEmpty(className))
            { errors.Add($"Class name cannot be empty ({GetHierarchy(component.gameObject)})"); allow = false; }
            if (!IsVariableName(className))
            { errors.Add($"Invalid class name '{className}' ({GetHierarchy(component.gameObject)})"); allow = false; }
            if (!string.IsNullOrEmpty(prefix) && !IsVariableName(prefix))
            { errors.Add($"Invalid class-name prefix '{prefix}'"); allow = false; }
            if (!string.IsNullOrEmpty(suffix) && !IsVariableName(suffix))
            { errors.Add($"Invalid class-name suffix '{suffix}'"); allow = false; }
            if (!allow) return;

            className = (prefix ?? "") + className + (suffix ?? "");
            var pad = string.IsNullOrEmpty(packName) ? "" : "\t";

            var innerFields = new StringBuilder();
            var innerCtor = new StringBuilder();
            var binderFull = typeof(ComponentBinderTable).FullName!;

            for (int i = 0; i < component.Items.Length; i++)
            {
                var item = component.Items[i];
                if (item?.Component is ComponentBinderTable inner)
                {
                    var innerPack = inner.CustomPackageName ? inner.PackageName : setting.CodeOutputPackageName;
                    var innerClass = (inner.CustomClassName ? inner.ClassName : inner.gameObject.name).Trim();
                    var full = (string.IsNullOrEmpty(innerPack) ? "" : innerPack + ".") + (prefix ?? "") + innerClass + (suffix ?? "");
                    innerFields.AppendLine(pad + "\tprivate " + full + " m_" + item.Name + "__;");
                    innerCtor.AppendLine(pad + "\t\tm_" + item.Name + "__ = new " + full + "(m_table != null ? m_table.GetComponentAt(" + i + ") : null);");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(GeneratedFileMarker);
            sb.AppendLine("// AUTO-GENERATED. Do not edit.");
            sb.AppendLine("// Prefab: " + AssetDatabase.GetAssetPath(component));
            sb.AppendLine("// Hierarchy: /" + GetHierarchy(component.gameObject));

            if (!string.IsNullOrEmpty(packName)) { sb.AppendLine("namespace " + packName); sb.AppendLine("{"); }

            sb.AppendLine(pad + "public class " + className + " : " + typeof(IComponentBinder).FullName);
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "\tprivate " + binderFull + " m_table;");
            sb.Append(innerFields.ToString());

            sb.AppendLine(pad + "\tpublic " + className + "(" + binderFull + " component)");
            sb.AppendLine(pad + "\t{");
            sb.AppendLine(pad + "\t\tm_table = component;");
            sb.Append(innerCtor.ToString());
            sb.AppendLine(pad + "\t}");

            sb.AppendLine(pad + "\tpublic " + className + "(" + typeof(GameObject).FullName + " gameObject)");
            sb.AppendLine(pad + "\t{");
            sb.AppendLine(pad + "\t\tm_table = gameObject.GetComponent<" + binderFull + ">();");
            sb.Append(innerCtor.ToString());
            sb.AppendLine(pad + "\t}");

            sb.AppendLine(pad + "\tpublic " + className + "(" + typeof(Component).FullName + " component)");
            sb.AppendLine(pad + "\t{");
            sb.AppendLine(pad + "\t\tm_table = component.gameObject.GetComponent<" + binderFull + ">();");
            sb.Append(innerCtor.ToString());
            sb.AppendLine(pad + "\t}");

            sb.AppendLine(pad + "\tpublic void Reset(" + typeof(GameObject).FullName + " gameObject)");
            sb.AppendLine(pad + "\t{");
            sb.AppendLine(pad + "\t\tm_table = gameObject.GetComponent<" + binderFull + ">();");
            sb.Append(innerCtor.ToString());
            sb.AppendLine(pad + "\t}");

            sb.AppendLine(pad + "\tprivate " + typeof(Component).FullName + " GetComponentAt(int index) => m_table != null ? m_table.GetComponentAt(index) : null;");
            sb.AppendLine(pad + "\tpublic " + typeof(GameObject).FullName + " gameObject => m_table != null ? m_table.gameObject : null;");
            sb.AppendLine(pad + "\t");

            var names = new List<string>();
            for (int i = 0; i < component.Items.Length; i++)
            {
                var item = component.Items[i];
                var name = item.Name.Trim();
                if (!IsVariableName(item.Name))
                { errors.Add($"'{item.Name}' is not a valid field name ({GetHierarchy(component.gameObject)})"); continue; }
                if (names.Contains(name))
                { errors.Add($"Duplicate field name '{name}' ({GetHierarchy(component.gameObject)})"); continue; }
                if (item.Target == null)
                { errors.Add($"Empty node ({GetHierarchy(component.gameObject)})"); continue; }
                if (item.Component == null)
                { errors.Add($"Empty component ({GetHierarchy(component.gameObject)})"); continue; }
                if (item.Component.gameObject != item.Target)
                { errors.Add($"Component does not sit on target ({GetHierarchy(component.gameObject)})"); continue; }

                names.Add(name);

                if (!string.IsNullOrEmpty(item.Desc))
                {
                    sb.AppendLine(pad + "\t/// <summary>");
                    sb.AppendLine(pad + "\t/// " + item.Desc.Replace("\n", " "));
                    sb.AppendLine(pad + "\t/// </summary>");
                }

                if (item.Component is ComponentBinderTable inner2)
                {
                    var innerPack = inner2.CustomPackageName ? inner2.PackageName : setting.CodeOutputPackageName;
                    var innerClass = (inner2.CustomClassName ? inner2.ClassName : inner2.gameObject.name).Trim();
                    var full = (string.IsNullOrEmpty(innerPack) ? "" : innerPack + ".") + (prefix ?? "") + innerClass + (suffix ?? "");
                    sb.AppendLine(pad + "\tpublic " + full + " " + name + " => m_" + name + "__;");
                }
                else
                {
                    sb.AppendLine(pad + "\tpublic " + item.Component.GetType().FullName + " " + name + " => GetComponentAt(" + i + ") as " + item.Component.GetType().FullName + ";");
                }
                sb.AppendLine(pad + "\t");
            }

            sb.AppendLine(pad + "}");
            if (!string.IsNullOrEmpty(packName)) sb.AppendLine("}");

            var saveFolder = setting.CodeOutputMethod
                ? AssetDatabase.GetAssetPath(setting.CodeOutputFolder) : setting.CodeOutputFolderPath;
            if (saveFolder.StartsWith("..")) saveFolder = Path.Combine(Application.dataPath, saveFolder);

            File.WriteAllText(Path.Combine(saveFolder, className + ".cs"), sb.ToString());
            AssetDatabase.ImportAsset(ToAssetPath(saveFolder, className + ".cs"));
        }

        private static string ToAssetPath(string folder, string file)
        {
            var dataPath = Application.dataPath.Replace('\\', '/');
            var f = folder.Replace('\\', '/');
            if (f.StartsWith("Assets/")) return f + "/" + file;
            if (f.StartsWith(dataPath)) return "Assets/" + f.Substring(dataPath.Length).TrimStart('/') + "/" + file;
            return file;
        }

        private static bool CheckOutputSetting(ComponentBinderSetting setting)
        {
            if (!setting.FindAllPrefab && setting.PrefabFolder == null)
            { EditorUtility.DisplayDialog("Error", "'Search folder' not set", "OK"); return false; }
            if (setting.CodeOutputMethod)
            {
                if (setting.CodeOutputFolder == null)
                { EditorUtility.DisplayDialog("Error", "'Output folder' not set", "OK"); return false; }
            }
            else if (string.IsNullOrEmpty(setting.CodeOutputFolderPath))
            {
                EditorUtility.DisplayDialog("Error", "'Output folder' not set", "OK"); return false;
            }
            else
            {
                var path = setting.CodeOutputFolderPath;
                if (path.StartsWith("..")) path = Path.Combine(Application.dataPath, path);
                if (!Directory.Exists(path))
                { EditorUtility.DisplayDialog("Error", "'Output folder' does not exist: " + path, "OK"); return false; }
            }
            return true;
        }

        private static void CleanFolder(string saveFolder)
        {
            if (!Directory.Exists(saveFolder)) return;
            foreach (var file in Directory.GetFiles(saveFolder, "*.cs"))
            {
                try
                {
                    if (File.ReadAllText(file).StartsWith(GeneratedFileMarker)) File.Delete(file);
                }
                catch { /* ignore locked */ }
            }
        }
    }
}