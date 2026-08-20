using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 枚举定义 → C# 枚举代码。生成到 <see cref="ConfigDataSettings.OutputDir"/>/Enums/ 供编译，
    /// 导出时按导出目标复制 .cs 到客户端/服务器导出目录（用户需求：不导 JSON，全是 C# 代码）。
    /// </summary>
    public static class ConfigEnumCodeGen
    {
        /// <summary>所有枚举定义生成的代码目录（相对项目）。</summary>
        public static string EnumsOutputDir => $"{ConfigDataSettings.OutputDir}/Enums";

        /// <summary>加载配置根目录下所有枚举定义资产。</summary>
        public static List<ConfigEnumDefinition> LoadAllEnums()
        {
            var result = new List<ConfigEnumDefinition>();
            var root = ConfigDataSettings.RootFolder;
            if (!AssetDatabase.IsValidFolder(root))
                return result;
            foreach (var guid in AssetDatabase.FindAssets("t:ConfigEnumDefinition", new[] { root }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ConfigEnumDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                    result.Add(asset);
            }
            return result;
        }

        /// <summary>按类名查找枚举定义。</summary>
        public static ConfigEnumDefinition FindEnum(string className)
        {
            if (string.IsNullOrEmpty(className))
                return null;
            return LoadAllEnums().FirstOrDefault(e => e.className == className);
        }

        /// <summary>类名是否已被其他枚举使用。</summary>
        public static bool IsClassNameTaken(string className, ConfigEnumDefinition self)
        {
            return LoadAllEnums().Any(e => e != self && e.className == className);
        }

        /// <summary>校验枚举定义，失败返回 false 并输出错误。</summary>
        public static bool Validate(ConfigEnumDefinition def, out string error)
        {
            error = null;
            if (def == null)
            {
                error = "枚举定义为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(def.displayName))
            {
                error = "显示名不能为空";
                return false;
            }
            if (!ConfigTemplateCodeGen.IsValidIdentifier(def.className))
            {
                error = $"枚举名 {def.className} 不是合法的 C# 标识符";
                return false;
            }
            if (IsClassNameTaken(def.className, def))
            {
                error = $"枚举名 {def.className} 已被其他枚举定义使用";
                return false;
            }
            // 与模板类名冲突检查
            if (ConfigTemplateCodeGen.LoadAllTemplates().Any(t => t.className == def.className))
            {
                error = $"枚举名 {def.className} 与配置模板类名冲突";
                return false;
            }

            var names = new HashSet<string>();
            for (var i = 0; i < def.values.Count; i++)
            {
                var v = def.values[i];
                if (string.IsNullOrWhiteSpace(v.name))
                {
                    error = $"第 {i + 1} 个枚举值名称为空";
                    return false;
                }
                if (!ConfigTemplateCodeGen.IsValidIdentifier(v.name))
                {
                    error = $"枚举值名称 {v.name} 不是合法的 C# 标识符";
                    return false;
                }
                if (!names.Add(v.name))
                {
                    error = $"枚举值名称 {v.name} 重复";
                    return false;
                }
                if (def.isFlags && i >= 31)
                {
                    error = $"复合枚举最多 31 个值（1&lt;&lt;31 超出 int 范围），当前第 {i + 1} 个";
                    return false;
                }
            }
            return true;
        }

        /// <summary>生成 C# 枚举源码。</summary>
        public static string GenerateCode(ConfigEnumDefinition def)
        {
            var ns = ConfigDataSettings.Namespace;
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {Escape(def.displayName)}");
            sb.AppendLine("    /// </summary>");
            if (def.isFlags)
                sb.AppendLine("    [Flags]");
            sb.AppendLine($"    public enum {def.className}");
            sb.AppendLine("    {");
            for (var i = 0; i < def.values.Count; i++)
            {
                var v = def.values[i];
                if (!string.IsNullOrEmpty(v.description))
                {
                    sb.AppendLine("        /// <summary>");
                    sb.AppendLine($"        /// {Escape(v.description)}");
                    sb.AppendLine("        /// </summary>");
                }
                var value = ConfigEnumDefinition.ValueOf(def, i);
                sb.AppendLine($"        {v.name} = {value},");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>枚举代码文件是否已生成。</summary>
        public static bool IsGenerated(ConfigEnumDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.className))
                return false;
            var path = $"{EnumsOutputDir}/{def.className}.cs";
            return File.Exists(Path.Combine(Directory.GetCurrentDirectory(), path));
        }

        /// <summary>写入生成的枚举代码文件。</summary>
        public static bool WriteFile(ConfigEnumDefinition def, out string error)
        {
            if (!Validate(def, out error))
                return false;

            var code = GenerateCode(def);
            var relativePath = $"{EnumsOutputDir}/{def.className}.cs";
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, code, Encoding.UTF8);
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>按导出目标导出 .cs 到客户端/服务器目录。</summary>
        public static bool Export(ConfigEnumDefinition def, bool client, bool server)
        {
            if (!IsGenerated(def))
            {
                if (!WriteFile(def, out var err))
                {
                    Debug.LogWarning($"[枚举] 导出 {def.className} 失败：{err}");
                    return false;
                }
            }

            var code = GenerateCode(def);
            var ok = true;
            if (client && Matches(def.exportTarget, true))
                ok &= WriteExportFile(ConfigDataSettings.ClientExportDir, def, code);
            if (server && Matches(def.exportTarget, false))
                ok &= WriteExportFile(ConfigDataSettings.ServerExportDir, def, code);
            return ok;
        }

        private static bool Matches(ConfigExportTarget target, bool forClient)
        {
            return target == ConfigExportTarget.Both
                || (forClient && target == ConfigExportTarget.Client)
                || (!forClient && target == ConfigExportTarget.Server);
        }

        private static bool WriteExportFile(string exportDir, ConfigEnumDefinition def, string code)
        {
            if (string.IsNullOrEmpty(exportDir))
                return false;
            var dir = Path.GetFullPath(exportDir);
            Directory.CreateDirectory(Path.Combine(dir, "Enums"));
            var fullPath = Path.Combine(dir, "Enums", $"{def.className}.cs");
            File.WriteAllText(fullPath, code, Encoding.UTF8);
            return true;
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>创建枚举定义资产到指定目录（数据树右键入口）。</summary>
        public static ConfigEnumDefinition CreateEnumAsset(string targetFolder)
        {
            if (string.IsNullOrEmpty(targetFolder))
                targetFolder = ConfigDataSettings.RootFolder;
            ConfigDataSettings.EnsureRootFolder();

            var path = $"{targetFolder}/NewEnum.asset";
            var index = 1;
            while (AssetDatabase.LoadAssetAtPath<ConfigEnumDefinition>(path) != null)
            {
                path = $"{targetFolder}/NewEnum {index}.asset";
                index++;
            }

            var def = ScriptableObject.CreateInstance<ConfigEnumDefinition>();
            def.displayName = "分类/新枚举";
            def.className = "NewEnum" + index;
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return def;
        }
    }
}
