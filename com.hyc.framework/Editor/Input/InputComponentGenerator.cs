using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.InputSystem;
using UnityEditor;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// 输入组件生成器：读 <see cref="InputComponentMapping"/>，按每个 action map 生成一份
    /// 「富状态组件 + 写入器 + AddTo 便捷挂载」的 .cs 到目标文件夹，并在目标文件夹缺 asmdef 时
    /// 顺手补一个最小 asmdef（引用 HYC.Framework.Runtime / Unity.Entities / Unity.Collections / Unity.Mathematics / Unity.InputSystem）。
    /// 走统一写盘入口 <see cref="GeneratedCodeFile"/>（套禁改头 + 未变不重写）。
    /// </summary>
    public static class InputComponentGenerator
    {
        /// <summary>生成所有 map。返回 null = 成功；否则返回错误信息（多 map 错误用换行拼接）。</summary>
        public static string Generate(InputComponentMapping mapping)
        {
            if (mapping == null) return "InputComponentMapping 为空。";
            if (mapping.asset == null) return "未绑定 InputActionAsset。";
            if (mapping.maps == null || mapping.maps.Count == 0) return "没有任何 map 目标配置。";

            var sb = new StringBuilder();
            foreach (var t in mapping.maps)
            {
                var err = GenerateMap(mapping.asset, t);
                if (err != null) sb.AppendLine(err);
            }
            if (sb.Length > 0) return sb.ToString();
            AssetDatabase.Refresh();
            return null;
        }

        static string GenerateMap(InputActionAsset asset, InputMapTarget t)
        {
            if (string.IsNullOrEmpty(t.mapName)) return "mapName 为空，跳过。";
            var map = asset.FindActionMap(t.mapName);
            if (map == null) return $"资产中找不到 action map: {t.mapName}，跳过。";

            var compName = string.IsNullOrEmpty(t.componentName)
                ? SanitizeIdent(t.mapName) + "InputComponent"
                : SanitizeIdent(t.componentName);
            if (string.IsNullOrEmpty(compName)) return $"组件名清洗后为空（map={t.mapName}），跳过。";
            var ns = string.IsNullOrEmpty(t.ns) ? "HYC.Framework.Input" : t.ns;
            if (string.IsNullOrEmpty(t.outputFolder)) return $"输出文件夹为空（map={t.mapName}），跳过。";

            if (!Directory.Exists(t.outputFolder)) Directory.CreateDirectory(t.outputFolder);

            // 收集字段：去重 fieldName（防 action 名清洗后撞名）；保留原始 actionName 供 FindAction
            var fields = new List<(string type, string field, string action, string read)>();
            var used = new HashSet<string>();
            foreach (var action in map.actions)
            {
                var rawField = SanitizeIdent(action.name);
                if (string.IsNullOrEmpty(rawField)) rawField = "Action" + fields.Count;
                var field = rawField;
                while (!used.Add(field)) field += "_";
                var (ftype, read) = MapControlType(action.expectedControlType);
                fields.Add((ftype, field, action.name, read));
            }

            var code = BuildFile(ns, compName, t.mapName, fields);
            var csPath = Path.Combine(t.outputFolder, compName + ".cs");
            GeneratedCodeFile.WriteAndImport(csPath, code);

            EnsureAsmdef(t.outputFolder, ns);
            return null;
        }

        static (string ftype, string read) MapControlType(string ctl)
        {
            switch (ctl)
            {
                case "Button":
                    return ("InputButton", "InputRead.ReadButton");
                case "Axis":
                    return ("InputAxis", "InputRead.ReadAxis");
                case "Vector2":
                case "Stick":
                    return ("InputVector2", "InputRead.ReadVector2");
                case "Vector3":
                    return ("InputVector3", "InputRead.ReadVector3");
                case "Quaternion":
                    return ("InputQuaternion", "InputRead.ReadQuaternion");
                case "Integer":
                case "Discrete":
                    return ("InputInteger", "InputRead.ReadInteger");
                default:
                    // 未设 / 未知控制类型：兜底按按钮（press）处理
                    return ("InputButton", "InputRead.ReadButton");
            }
        }

        static string BuildFile(string ns, string compName, string mapName,
            List<(string type, string field, string action, string read)> fields)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using Unity.Entities;");
            sb.AppendLine("using HYC.Framework.Input;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine.InputSystem;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            // —— 组件 ——
            sb.AppendLine("    /// <summary>输入组件（由 HYC InputComponentGenerator 生成，勿手改）：action map = " + mapName + "。每玩家实体一份。</summary>");
            sb.AppendLine("    public struct " + compName + " : IComponentData");
            sb.AppendLine("    {");
            foreach (var f in fields)
                sb.AppendLine("        public " + f.type + " " + f.field + ";");
            sb.AppendLine("        /// <summary>便捷挂载：把本组件加到指定玩家实体上。</summary>");
            sb.AppendLine("        public static void AddTo(EntityManager em, Entity player) => em.AddComponent<" + compName + ">(player);");
            sb.AppendLine("    }");
            sb.AppendLine();
            // —— 写入器 ——
            sb.AppendLine("    /// <summary>把 " + mapName + " map 的当前输入写进 " + compName + "。自注册进 InputMapWriterRegistry。</summary>");
            sb.AppendLine("    public sealed class " + compName + "Writer : IInputMapWriter");
            sb.AppendLine("    {");
            sb.AppendLine("        public string MapName => \"" + mapName + "\";");
            sb.AppendLine("        public void Write(EntityManager em, Entity player, InputActionMap map, Dictionary<string, float> prev)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!em.HasComponent<" + compName + ">(player)) return;");
            sb.AppendLine("            var c = new " + compName + "();");
            foreach (var f in fields)
            {
                var key = mapName + ":" + f.field;
                sb.AppendLine("            c." + f.field + " = " + f.read + "(map.FindAction(\"" + f.action + "\"), prev, \"" + key + "\");");
            }
            sb.AppendLine("            em.SetComponentData(player, c);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]");
            sb.AppendLine("        static void RegisterSelf() => InputMapWriterRegistry.Register(new " + compName + "Writer());");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        static void EnsureAsmdef(string folder, string ns)
        {
            var existing = Directory.GetFiles(folder, "*.asmdef");
            if (existing.Length > 0) return; // 已有 asmdef（宿主自建或上次生成），不覆盖
            var asmName = SanitizeIdent(ns.Replace('.', '_'));
            if (string.IsNullOrEmpty(asmName)) asmName = "GeneratedInput";
            var path = Path.Combine(folder, asmName + ".asmdef");
            var json = "{\n" +
                "  \"name\": \"" + asmName + "\",\n" +
                "  \"rootNamespace\": \"" + ns + "\",\n" +
                "  \"references\": [\n" +
                "    \"HYC.Framework.Runtime\",\n" +
                "    \"Unity.Entities\",\n" +
                "    \"Unity.Collections\",\n" +
                "    \"Unity.Mathematics\",\n" +
                "    \"Unity.InputSystem\"\n" +
                "  ],\n" +
                "  \"includePlatforms\": [],\n" +
                "  \"excludePlatforms\": [],\n" +
                "  \"allowUnsafeCode\": false,\n" +
                "  \"overrideReferences\": false,\n" +
                "  \"precompiledReferences\": [],\n" +
                "  \"autoReferenced\": true,\n" +
                "  \"defineConstraints\": [],\n" +
                "  \"versionDefines\": [],\n" +
                "  \"noEngineReferences\": false\n" +
                "}";
            GeneratedCodeFile.WriteIfChanged(path, json);
        }

        /// <summary>清洗成合法 C# 标识符：非字母/下划线/数字 → 下划线；去首尾下划线；数字开头则截掉前缀数字。</summary>
        public static string SanitizeIdent(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder();
            foreach (var ch in raw)
            {
                if (char.IsLetter(ch) || ch == '_' || (sb.Length > 0 && char.IsDigit(ch)))
                    sb.Append(ch);
                else if (sb.Length > 0)
                    sb.Append('_');
            }
            var s = sb.ToString().Trim('_');
            while (s.Length > 0 && char.IsDigit(s[0])) s = s.Substring(1);
            return s;
        }
    }
}
