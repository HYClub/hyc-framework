using System.Collections.Generic;
using System.IO;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HYC.Framework.Config.Editor
{
    /// <summary>一个工作表解析后的内容（表头 3 行 + 数据行，全部取文本）。</summary>
    public sealed class ExcelSheetData
    {
        /// <summary>工作表名（Excel 原名）。</summary>
        public string SheetName = "";

        /// <summary>第 1 行：中文显示名（可能为空，回落英文名）。</summary>
        public List<string> ChineseHeaders = new List<string>();

        /// <summary>第 2 行：英文字段名。</summary>
        public List<string> EnglishHeaders = new List<string>();

        /// <summary>第 3 行：类型串（无类型行的格式下为空串）。</summary>
        public List<string> TypeHeaders = new List<string>();

        /// <summary>数据行（第 4 行起；无类型行的格式下从第 2/3 行起）。</summary>
        public List<List<string>> Rows = new List<List<string>>();

        /// <summary>列数（三行表头与数据行的最大列数）。</summary>
        public int ColumnCount;

        /// <summary>表头格式：<see cref="ConfigExcelSchema.FormatTyped3"/> / <see cref="ConfigExcelSchema.FormatEn2"/> / <see cref="ConfigExcelSchema.FormatCn1"/>。</summary>
        public string FormatMode = ConfigExcelSchema.FormatCn1;
    }

    /// <summary>
    /// Excel 读取与表头识别（导入侧，与 <see cref="ConfigTemplateExcelExporter"/> 的写出格式互为逆操作）。
    /// <para>
    /// 读法沿用 <see cref="LocalizedExcelReader"/>：NPOI + <see cref="DataFormatter"/> 全取文本，
    /// 数值单元格格式化为 "123" / "123.5"，不区分单元格存储类型。
    /// 表头三种格式自动识别（优先级 typed3 → en2 → cn1），与网页原型 v6 的判定一致。
    /// </para>
    /// </summary>
    public static class ConfigExcelReader
    {
        /// <summary>读取 .xlsx / .xls 的全部工作表。</summary>
        public static List<ExcelSheetData> ReadFile(string path)
        {
            var result = new List<ExcelSheetData>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return result;

            IWorkbook workbook;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                workbook = path.ToLowerInvariant().EndsWith(".xls")
                    ? (IWorkbook)new HSSFWorkbook(stream)
                    : new XSSFWorkbook(stream);
            }

            for (var i = 0; i < workbook.NumberOfSheets; i++)
            {
                var sheet = workbook.GetSheetAt(i);
                var data = ReadSheet(sheet, workbook.GetSheetName(i));
                if (data != null)
                    result.Add(data);
            }
            return result;
        }

        /// <summary>读取单个工作表；全空表返回 null。</summary>
        public static ExcelSheetData ReadSheet(ISheet sheet, string sheetName)
        {
            // 先全量取文本行，跳过整行全空的（与 LocalizedExcelReader 同款空行策略）
            var raw = new List<List<string>>();
            var colCount = 0;
            for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null)
                    continue;
                var cells = new List<string>();
                var any = false;
                for (var c = row.FirstCellNum; c < row.LastCellNum; c++)
                {
                    var text = GetCellText(row.GetCell(c));
                    cells.Add(text);
                    if (text.Length > 0)
                        any = true;
                }
                if (!any)
                    continue;
                if (cells.Count > colCount)
                    colCount = cells.Count;
                raw.Add(cells);
            }

            if (raw.Count == 0)
                return null;

            var data = new ExcelSheetData { SheetName = sheetName ?? "", ColumnCount = colCount };

            // 表头识别：typed3（第 2 行标识符 + 第 3 行类型）→ en2（第 1 行标识符）→ cn1
            var format = ConfigExcelSchema.FormatCn1;
            var dataStart = 1;
            if (raw.Count >= 3 && IsIdentifierRow(raw[1]) && IsTypeRow(raw[2]))
            {
                format = ConfigExcelSchema.FormatTyped3;
                dataStart = 3;
            }
            else if (raw.Count >= 2 && IsIdentifierRow(raw[0]))
            {
                format = ConfigExcelSchema.FormatEn2;
                dataStart = 1;
            }
            data.FormatMode = format;

            for (var c = 0; c < colCount; c++)
            {
                // typed3/en2：第 1 行中文、第 2 行英文；cn1 只有第 1 行（中文兼作英文名）
                var cn = CellAt(raw[0], c);
                var en = format == ConfigExcelSchema.FormatCn1 ? cn : CellAt(raw[1], c);
                var type = format == ConfigExcelSchema.FormatTyped3 ? CellAt(raw[2], c) : "";
                data.ChineseHeaders.Add(cn);
                data.EnglishHeaders.Add(en);
                data.TypeHeaders.Add(type);
            }

            for (var r = dataStart; r < raw.Count; r++)
            {
                var cells = new List<string>(colCount);
                for (var c = 0; c < colCount; c++)
                    cells.Add(CellAt(raw[r], c));
                data.Rows.Add(cells);
            }
            return data;
        }

        private static string CellAt(List<string> row, int index)
            => index >= 0 && index < row.Count ? row[index] : "";

        /// <summary>整行非空单元格都是合法 C# 标识符 → 英文字段名行。</summary>
        private static bool IsIdentifierRow(List<string> row)
        {
            var any = false;
            foreach (var cell in row)
            {
                var s = cell.Trim();
                if (s.Length == 0)
                    continue;
                any = true;
                if (!char.IsLetter(s[0]) && s[0] != '_')
                    return false;
                foreach (var ch in s)
                {
                    if (!char.IsLetterOrDigit(ch) && ch != '_')
                        return false;
                }
            }
            return any;
        }

        /// <summary>整行非空单元格都是已知类型串 → 类型行。</summary>
        private static bool IsTypeRow(List<string> row)
        {
            var any = false;
            foreach (var cell in row)
            {
                var s = cell.Trim();
                if (s.Length == 0)
                    continue;
                any = true;
                if (!ConfigExcelSchema.IsKnownTypeName(s))
                    return false;
            }
            return any;
        }

        private static string GetCellText(ICell cell)
        {
            if (cell == null)
                return string.Empty;
            var text = new DataFormatter().FormatCellValue(cell);
            return text == null ? string.Empty : text.Trim();
        }
    }
}
