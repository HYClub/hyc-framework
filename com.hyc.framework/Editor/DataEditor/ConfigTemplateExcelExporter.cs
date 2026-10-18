using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置模板 → Excel 导出（Project 窗口右键 <see cref="ConfigTemplate"/> 资产）。
    /// <para>
    /// 产出 .xlsx，表头三行（与策划填表格式一致，导入侧按同一格式解析）：
    /// 第 1 行中文显示名（字段 description，空则回落字段名）、第 2 行英文字段名、
    /// 第 3 行类型；第 4 行起为数据。首列固定 <c>ID</c>（业务主键）、末列固定 <c>GUID</c>
    /// （框架内部字段，仅备份用，不参与对账）。
    /// </para>
    /// <para>
    /// 数据行取配置根目录下<b>精确匹配</b> <see cref="ConfigTemplate.className"/> 的配置资产
    /// （派生类型的资产归派生模板自己的表），按 ID 升序。字段值经反射读取，写入规则：
    /// 数值/ID/行为树 ID 写数值单元格，其余写文本；列表用 <c>|</c> 连接；
    /// 配置引用写目标资产 <see cref="ConfigBase.ID"/>；资源引用写资产名；
    /// Gradient / AnimationCurve 无法在 Excel 中表达，写空（已知限制）。
    /// </para>
    /// <para><b>维护约定</b>：本文件的 <see cref="ElementText"/> switch 覆盖全部
    /// <see cref="ConfigFieldType"/>——新增成员时必须同步（见 AGENTS.md「新增 ConfigFieldType 需同步」铁律）；
    /// 表头类型串的显示/解析统一走 <see cref="ConfigExcelSchema"/>，不在本文件维护第二套。</para>
    /// </summary>
    public static class ConfigTemplateExcelExporter
    {
        /// <summary>表头行数（中文 / 英文 / 类型）。数据从下一行开始。</summary>
        public const int HeaderRows = 3;

        /// <summary>列表单元格内多个值的连接符（与导入侧约定一致）。</summary>
        public const char ListSeparator = '|';

        // CONTEXT 菜单不在 HYC Framework 主菜单树内，priority 仅决定右键菜单内的位置；
        // 显式给出以避免落到默认 0 与 Unity 自带项混排。
        private const int ContextMenuPriority = 110;

        [MenuItem("CONTEXT/ConfigTemplate/Export Excel", false, ContextMenuPriority)]
        private static void ExportFromContextMenu(MenuCommand command)
        {
            Export(command.context as ConfigTemplate);
        }

        /// <summary>
        /// 导出模板对应的 Excel 文件（弹面板选落点，默认与模板资产同目录、按 className 命名）。
        /// </summary>
        public static void Export(ConfigTemplate template)
        {
            if (template == null)
                return;

            if (!template.generateCode)
            {
                EditorUtility.DisplayDialog("Export Excel",
                    "Framework-level template (generateCode = false) generates no config data. Nothing to export.",
                    "OK");
                return;
            }

            var className = (template.className ?? "").Trim();
            if (className.Length == 0)
            {
                EditorUtility.DisplayDialog("Export Excel", "Template className is empty. Generate code first.", "OK");
                return;
            }

            var assets = CollectDataAssets(className);
            assets.Sort((a, b) => a.ID.CompareTo(b.ID));

            var templatePath = AssetDatabase.GetAssetPath(template);
            var dir = string.IsNullOrEmpty(templatePath)
                ? "Assets"
                : Path.GetDirectoryName(templatePath).Replace('\\', '/');
            var outPath = EditorUtility.SaveFilePanel("Export Excel", dir,
                SanitizeSheetName(className) + ".xlsx", "xlsx");
            if (string.IsNullOrEmpty(outPath))
                return;

            // 先完整构建再落盘：构建失败不留半个文件（导入侧按 3 行表头解析，残缺文件比没有更坏）
            var sheetName = SanitizeSheetName(className);
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet(sheetName);
            BuildColumns(template, out var columns);
            WriteHeader(sheet, columns);
            WriteRows(sheet, columns, assets);

            using (var stream = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                workbook.Write(stream);

            // SaveFilePanel 返回的是磁盘绝对路径；落在工程 Assets 内才需要 Refresh 让 Unity 看见新文件
            var dataPath = Application.dataPath.Replace('\\', '/');
            if (outPath.Replace('\\', '/').StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                AssetDatabase.Refresh();

            Debug.Log($"[ConfigExcel] Exported {assets.Count} row(s) -> {outPath}");
            EditorUtility.DisplayDialog("Export Excel",
                $"Exported {assets.Count} row(s) of {className} to\n{outPath}", "OK");
        }

        // ---------- 数据资产收集 ----------

        /// <summary>
        /// 收集配置根目录下<b>精确匹配</b>类名的配置资产（派生类型的资产不归本模板——
        /// 它们有各自的模板与表）。找不到类型（未生成代码）时返回空表而非报错：
        /// 「无数据也生成一个模板」是既定行为。
        /// </summary>
        private static List<ConfigBase> CollectDataAssets(string className)
        {
            var result = new List<ConfigBase>();
            var root = (ConfigDataSettings.RootFolder ?? "").Replace('\\', '/').Trim('/');
            if (root.Length == 0)
                return result;

            foreach (var guid in AssetDatabase.FindAssets($"t:{className}", new[] { root }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ConfigBase>(AssetDatabase.GUIDToAssetPath(guid));
                // t: 过滤器按类型名匹配，但仍需精确比对：排除派生类型与同名非配置资产
                if (asset == null || asset.GetType().Name != className)
                    continue;
                result.Add(asset);
            }
            return result;
        }

        // ---------- 表头 ----------

        private readonly struct Column
        {
            public readonly string Chinese;
            public readonly string English;
            public readonly string Type;
            public readonly ConfigTemplateField Field; // ID/GUID 两列为 null

            public Column(string chinese, string english, string type, ConfigTemplateField field)
            {
                Chinese = chinese;
                English = english;
                Type = type;
                Field = field;
            }
        }

        private static void BuildColumns(ConfigTemplate template, out List<Column> columns)
        {
            columns = new List<Column>();
            columns.Add(new Column("ID", "ID", "long", null));
            foreach (var f in template.fields)
            {
                if (f == null || string.IsNullOrEmpty(f.name))
                    continue;
                var chinese = string.IsNullOrEmpty(f.description) ? f.name : f.description;
                var type = TypeDisplayName(f);
                columns.Add(new Column(chinese, f.name, f.isList ? type + "[]" : type, f));
            }
            columns.Add(new Column("GUID", "GUID", "string", null));
        }

        private static void WriteHeader(ISheet sheet, List<Column> columns)
        {
            var chineseRow = sheet.CreateRow(0);
            var englishRow = sheet.CreateRow(1);
            var typeRow = sheet.CreateRow(2);
            for (var i = 0; i < columns.Count; i++)
            {
                chineseRow.CreateCell(i).SetCellValue(columns[i].Chinese);
                englishRow.CreateCell(i).SetCellValue(columns[i].English);
                typeRow.CreateCell(i).SetCellValue(columns[i].Type);
            }
        }

        // ---------- 数据行 ----------

        private static void WriteRows(ISheet sheet, List<Column> columns, List<ConfigBase> assets)
        {
            for (var r = 0; r < assets.Count; r++)
            {
                var row = sheet.CreateRow(HeaderRows + r);
                var asset = assets[r];
                for (var c = 0; c < columns.Count; c++)
                {
                    var col = columns[c];
                    var cell = row.CreateCell(c);
                    if (col.Field == null)
                    {
                        // 框架列：ID 数值 / GUID 文本
                        if (col.English == "ID")
                            cell.SetCellValue(asset.ID);
                        else
                            cell.SetCellValue(asset.GUID ?? "");
                        continue;
                    }

                    object raw;
                    try
                    {
                        raw = asset.GetType().GetField(col.Field.name,
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                            ?.GetValue(asset);
                    }
                    catch (Exception e)
                    {
                        // 反射读取失败不中断整表导出：留空并在日志里点名，用户可手动补
                        Debug.LogWarning($"[ConfigExcel] {asset.name}: field {col.Field.name} read failed: {e.Message}");
                        raw = null;
                    }
                    WriteCell(cell, col.Field, raw);
                }
            }
        }

        private static void WriteCell(ICell cell, ConfigTemplateField f, object raw)
        {
            if (f.isList)
            {
                if (!(raw is IList list) || list.Count == 0)
                {
                    cell.SetCellValue(string.Empty);
                    return;
                }
                var sb = new StringBuilder();
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                        sb.Append(ListSeparator);
                    sb.Append(ElementText(f, list[i]));
                }
                cell.SetCellValue(sb.ToString());
                return;
            }

            // 数值族（含 ID / 行为树 ID）写数值单元格，其余写文本
            if (IsNumericCell(f.type))
            {
                if (TryToDouble(raw, out var d))
                    cell.SetCellValue(d);
                else
                    cell.SetCellValue(string.Empty);
                return;
            }

            cell.SetCellValue(ElementText(f, raw));
        }

        private static bool IsNumericCell(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.Int:
                case ConfigFieldType.Long:
                case ConfigFieldType.Float:
                case ConfigFieldType.Double:
                case ConfigFieldType.Short:
                case ConfigFieldType.Byte:
                case ConfigFieldType.UInt:
                case ConfigFieldType.Decimal:
                case ConfigFieldType.BehaviourTree:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryToDouble(object v, out double d)
        {
            d = 0d;
            if (v == null)
                return false;
            try
            {
                d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>单个元素（非列表）的文本表示。数值族走数值单元格，不经过本方法。</summary>
        private static string ElementText(ConfigTemplateField f, object v)
        {
            if (v == null)
                return string.Empty;

            switch (f.type)
            {
                case ConfigFieldType.String:
                case ConfigFieldType.LocalizedKey:
                case ConfigFieldType.Addressable:
                case ConfigFieldType.AudioEvent:
                    return (string)v;
                case ConfigFieldType.Bool:
                    return ((bool)v) ? "true" : "false";
                case ConfigFieldType.Char:
                    return ((char)v).ToString();
                case ConfigFieldType.Enum:
                    return v.ToString();
                case ConfigFieldType.Reference:
                    // 编辑器态存的是被引用配置资产本身 → 写它的 ID（运行期 ConfigRowRef 同款对账键）
                    return (v as ConfigBase)?.ID.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                case ConfigFieldType.Int:
                case ConfigFieldType.Short:
                case ConfigFieldType.Byte:
                case ConfigFieldType.UInt:
                    return Convert.ToInt64(v, CultureInfo.InvariantCulture)
                        .ToString(CultureInfo.InvariantCulture);
                case ConfigFieldType.Long:
                case ConfigFieldType.BehaviourTree:
                    return Convert.ToInt64(v, CultureInfo.InvariantCulture)
                        .ToString(CultureInfo.InvariantCulture);
                case ConfigFieldType.Float:
                    return ((float)v).ToString("R", CultureInfo.InvariantCulture);
                case ConfigFieldType.Double:
                    return ((double)v).ToString("R", CultureInfo.InvariantCulture);
                case ConfigFieldType.Decimal:
                    return ((decimal)v).ToString(CultureInfo.InvariantCulture);
                default:
                    break;
            }

            if (ConfigFieldTypes.IsAssetRef(f.type))
                return (v as UnityEngine.Object)?.name ?? string.Empty;

            // 结构体：逐分量不变量文化格式（避免逗号小数点混入分隔符语义）
            switch (f.type)
            {
                case ConfigFieldType.Vector2:
                    var v2 = (Vector2)v;
                    return Fmt(v2.x) + ", " + Fmt(v2.y);
                case ConfigFieldType.Vector3:
                    var v3 = (Vector3)v;
                    return Fmt(v3.x) + ", " + Fmt(v3.y) + ", " + Fmt(v3.z);
                case ConfigFieldType.Vector4:
                    var v4 = (Vector4)v;
                    return Fmt(v4.x) + ", " + Fmt(v4.y) + ", " + Fmt(v4.z) + ", " + Fmt(v4.w);
                case ConfigFieldType.Vector2Int:
                    var v2i = (Vector2Int)v;
                    return v2i.x + ", " + v2i.y;
                case ConfigFieldType.Vector3Int:
                    var v3i = (Vector3Int)v;
                    return v3i.x + ", " + v3i.y + ", " + v3i.z;
                case ConfigFieldType.Quaternion:
                    var q = (Quaternion)v;
                    return Fmt(q.x) + ", " + Fmt(q.y) + ", " + Fmt(q.z) + ", " + Fmt(q.w);
                case ConfigFieldType.Color:
                    var col32 = (Color)v;
                    return Fmt(col32.r) + ", " + Fmt(col32.g) + ", " + Fmt(col32.b) + ", " + Fmt(col32.a);
                case ConfigFieldType.Color32:
                    var c32 = (Color32)v;
                    return c32.r + ", " + c32.g + ", " + c32.b + ", " + c32.a;
                case ConfigFieldType.Rect:
                    var rect = (Rect)v;
                    return Fmt(rect.x) + ", " + Fmt(rect.y) + ", " + Fmt(rect.width) + ", " + Fmt(rect.height);
                case ConfigFieldType.RectInt:
                    var rectI = (RectInt)v;
                    return rectI.x + ", " + rectI.y + ", " + rectI.width + ", " + rectI.height;
                case ConfigFieldType.RectOffset:
                    var off = (RectOffset)v;
                    return off.left + ", " + off.right + ", " + off.top + ", " + off.bottom;
                case ConfigFieldType.Bounds:
                    var b = (Bounds)v;
                    return Fmt(b.center.x) + ", " + Fmt(b.center.y) + ", " + Fmt(b.center.z) + ", "
                           + Fmt(b.size.x) + ", " + Fmt(b.size.y) + ", " + Fmt(b.size.z);
                case ConfigFieldType.BoundsInt:
                    var bi = (BoundsInt)v;
                    var bc = bi.position;
                    var bs = bi.size;
                    return bc.x + ", " + bc.y + ", " + bc.z + ", " + bs.x + ", " + bs.y + ", " + bs.z;
                case ConfigFieldType.LayerMask:
                    return ((LayerMask)v).value.ToString(CultureInfo.InvariantCulture);
                case ConfigFieldType.Gradient:
                case ConfigFieldType.AnimationCurve:
                    // 无法在 Excel 中表达（见类注释「已知限制」）——留空，不写占位符误导导入
                    return string.Empty;
                default:
                    return v.ToString();
            }
        }

        private static string Fmt(float f) => f.ToString("R", CultureInfo.InvariantCulture);

        // ---------- 类型显示名（表头第 3 行） ----------

        /// <summary>
        /// <see cref="ConfigFieldType"/> → 表头第 3 行的类型串。与代码生成器使用的类型名
        /// 一致（枚举写类名、引用写短名、行为树写 BehaviourTree），导入侧按同一套解析。
        /// 单一真源在 <see cref="ConfigExcelSchema"/>（导入/导出共用，防两侧漂移）。
        /// </summary>
        private static string TypeDisplayName(ConfigTemplateField f)
            => ConfigExcelSchema.TypeDisplayName(f.type, f.enumRefClassName, f.refTypeFullName);

        // ---------- 工具 ----------

        /// <summary>Excel 工作表名：禁用 []:*?/\ ，最长 31 字符。</summary>
        private static string SanitizeSheetName(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name)
            {
                if (c == '[' || c == ']' || c == ':' || c == '*' || c == '?' || c == '/' || c == '\\')
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            var s = sb.ToString().Trim();
            return s.Length > 31 ? s.Substring(0, 31) : s;
        }
    }
}
