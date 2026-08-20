using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Minimal, dependency-free spreadsheet reader used by config tooling.
    /// Supports CSV now; an xlsx provider can be plugged in via
    /// <see cref="XlsxProvider"/> for .NET/XLSX without pulling NPOI into
    /// this package directly.
    /// </summary>
    public sealed class ExcelSheet
    {
        public string Name;
        public List<string[]> Rows = new List<string[]>();

        public int Height => Rows.Count;
        public int Width { get; private set; }

        public void AddRow(string[] cells)
        {
            Rows.Add(cells);
            if (cells.Length > Width) Width = cells.Length;
        }

        public static string[] ParseCsvLine(string line, char sep = '\t')
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuote)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') inQuote = false;
                    else sb.Append(c);
                }
                else if (c == '"') inQuote = true;
                else if (c == sep) { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }
    }

    /// <summary>Hook for .NET XLSX parsing; assign from the game project if NPOI is used.</summary>
    public static class XlsxProvider
    {
        /// <summary>Path delimiter used when returning multiple sheets.</summary>
        public static System.Func<string, List<ExcelSheet>>? Open;
    }

    public static class ExcelReader
    {
        public static List<ExcelSheet> Read(string path)
        {
            if (Path.GetExtension(path).ToLowerInvariant() == ".csv")
            {
                var sheet = new ExcelSheet();
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    sheet.AddRow(ExcelSheet.ParseCsvLine(line));
                }
                return new List<ExcelSheet> { sheet };
            }

            var provider = XlsxProvider.Open;
            if (provider != null) return provider(path);

            // No provider compiled in — fall back to tsv so tooling never hard-fails.
            var tsv = new ExcelSheet();
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                tsv.AddRow(line.Split('\t'));
            }
            return new List<ExcelSheet> { tsv };
        }
    }
}