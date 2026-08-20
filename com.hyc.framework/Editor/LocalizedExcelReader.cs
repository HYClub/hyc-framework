using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// Localization Excel importer. Merges every <c>.xls/.xlsx</c> under
    /// <see cref="LocalizationSettings.ExcelFolder"/> (first row = headers,
    /// <c>id</c> column = keys, remaining columns = languages) into binary
    /// blob files under <see cref="LocalizationSettings.OutputFolder"/>:
    /// <c>id</c>, one <c>{lang}.lang</c> per language, <c>lang</c> (default
    /// language) and <c>filter</c> (sensitive words). Ported from the source
    /// StarDeep <c>LocalizedExcelReader</c>.
    /// </summary>
    public static class LocalizedExcelReader
    {
        private const string IdColumn = "id";

        [MenuItem("HYC Framework/Localization/Import Excel")]
        public static void ImportFromMenu() => ImportAll();

        /// <summary>Imports all Excels, writes blob files and reloads the manager.</summary>
        public static void ImportAll()
        {
            var errors = ImportExcels();
            if (errors.Count > 0)
            {
                foreach (var error in errors) Debug.LogError(error);
                EditorUtility.DisplayDialog("Localization", "Import failed, see Console for details.", "OK");
                return;
            }

            var output = Path.GetFullPath(LocalizationSettings.OutputFolder);
            LocalizationManager.Reload(output);

            var keyCount = LocalizationManager.IDs?.Length ?? 0;
            var langCount = LocalizationManager.Langs?.Length ?? 0;
            Debug.Log($"Localization imported: {keyCount} keys, {langCount} languages -> {output}");
        }

        private static List<string> ImportExcels()
        {
            var errors = new List<string>();
            var idConflictTable = new Dictionary<string, List<string>>();
            var idQueue = new List<string>();
            var idIndexQueue = new List<int>();
            var txtQueue = new Dictionary<string, List<string>>();

            var excelDir = Path.GetFullPath(LocalizationSettings.ExcelFolder);
            if (!Directory.Exists(excelDir))
            {
                errors.Add($"Excel folder {excelDir} does not exist. Configure it under HYC Framework/Localization/Settings.");
            }
            else
            {
                var files = Directory.GetFiles(excelDir, "*.xls", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(excelDir, "*.xlsx", SearchOption.AllDirectories))
                    .Where(r => !Path.GetFileName(r).StartsWith("~"))
                    .ToList();
                files.Sort((a, b) => Path.GetFileName(a).CompareTo(Path.GetFileName(b)));

                var fileNames = new List<string>();

                foreach (var excel in files)
                {
                    if (!File.Exists(excel))
                    {
                        errors.Add($"File not found! ({excel})");
                        continue;
                    }

                    fileNames.Add(Path.GetFileName(excel));

                    using var stream = new FileStream(excel, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    IWorkbook workbook = excel.ToLowerInvariant().EndsWith(".xls")
                        ? (IWorkbook)new HSSFWorkbook(stream)
                        : new XSSFWorkbook(stream);

                    for (var i = 0; i < workbook.NumberOfSheets; i++)
                    {
                        var sheet = workbook.GetSheetAt(i);
                        var sheetName = workbook.GetSheetName(i);
                        var titleRow = sheet.GetRow(sheet.FirstRowNum);
                        if (titleRow == null) continue;

                        var sheetTitles = new Dictionary<string, int>();
                        for (var j = titleRow.FirstCellNum; j <= titleRow.LastCellNum; j++)
                        {
                            var cellValue = GetCellText(titleRow.GetCell(j));
                            if (string.IsNullOrWhiteSpace(cellValue)) continue;
                            sheetTitles[LocaleUtil.NormalizeLangHeader(cellValue)] = j;
                        }

                        if (!sheetTitles.ContainsKey(IdColumn))
                        {
                            errors.Add($"Header row of {excel}:{sheetName} has no '{IdColumn}' column.");
                            continue;
                        }

                        for (var j = sheet.FirstRowNum + 1; j <= sheet.LastRowNum; j++)
                        {
                            var row = sheet.GetRow(j);
                            if (row == null) continue;

                            var firstCell = GetCellText(row.GetCell(row.FirstCellNum));
                            if (!string.IsNullOrEmpty(firstCell) && firstCell.StartsWith("#")) continue;
                            if (sheetTitles.Keys.All(key => GetCellText(row.GetCell(sheetTitles[key])) == string.Empty)) continue;

                            var idValue = GetCellText(row.GetCell(sheetTitles[IdColumn]));
                            if (string.IsNullOrEmpty(idValue)) continue;

                            if (!idConflictTable.TryGetValue(idValue, out var locations))
                            {
                                locations = new List<string>(1) { $"{excel}:{sheetName} {(j + 1)}:{sheetTitles[IdColumn]}" };
                                idConflictTable.Add(idValue, locations);
                            }
                            else
                            {
                                locations.Add($"{excel}:{sheetName} {(j + 1)}:{sheetTitles[IdColumn]}");
                            }
                            if (locations.Count > 1) continue;

                            foreach (var key in sheetTitles.Keys)
                            {
                                if (string.Equals(key, IdColumn)) continue;
                                if (!txtQueue.TryGetValue(key, out var queue))
                                {
                                    queue = new List<string>();
                                    txtQueue.Add(key, queue);
                                }
                                while (queue.Count < idQueue.Count) queue.Add(string.Empty);
                                queue.Add(GetCellText(row.GetCell(sheetTitles[key])));
                            }

                            idQueue.Add(idValue);
                            idIndexQueue.Add(fileNames.Count - 1);
                        }
                    }
                }

                foreach (var queue in txtQueue.Values)
                {
                    while (queue.Count < idQueue.Count) queue.Add(string.Empty);
                }

                foreach (var id in idConflictTable.Keys)
                {
                    if (idConflictTable[id].Count <= 1) continue;
                    errors.Add($"Key conflict '{id}':");
                    foreach (var location in idConflictTable[id]) errors.Add($"    {location}");
                }

                if (errors.Count == 0)
                {
                    LocalizationManager.ConfigLanguage = string.IsNullOrWhiteSpace(LocalizationSettings.DefaultLanguage)
                        ? LocaleUtil.DefaultCode
                        : LocalizationSettings.DefaultLanguage.Trim();

                    Save(LocalizationManager.DEBUG_ID_FILE_NAME, idIndexQueue.ToArray());
                    Save(LocalizationManager.DEBUG_NAME_FILE_NAME, fileNames.ToArray());
                    Save(LocalizationManager.ID_FILE_NAME, idQueue.ToArray());
                    Save(LocalizationManager.DEF_LANG_FILE_NAME, LocalizationManager.ConfigLanguage);
                    foreach (var lang in txtQueue.Keys)
                    {
                        Save($"{lang}{LocalizationManager.LANG_FILE_EXT}", txtQueue[lang].ToArray());
                    }
                }
            }

            var sensitiveFile = Path.GetFullPath(LocalizationSettings.SensitiveWordsFile);
            if (File.Exists(sensitiveFile))
            {
                var words = File.ReadAllText(sensitiveFile)
                    .Split(new[] { ',', '，', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim()).Where(r => r.Length > 0).Distinct().ToArray();
                Save(LocalizationManager.SENSITIVE_WORD_FILE_NAME, words);
            }
            else if (errors.Count == 0)
            {
                Debug.LogWarning($"Sensitive-word file {sensitiveFile} not found; skipping filter import.");
            }

            return errors;
        }

        /// <summary>Reads a cell as display text (numbers, booleans and cached
        /// formula results included) via NPOI DataFormatter.</summary>
        private static string GetCellText(ICell cell)
        {
            if (cell == null) return string.Empty;
            var text = new DataFormatter().FormatCellValue(cell);
            return text == null ? string.Empty : text.Trim();
        }

        private static string OutputDir()
        {
            var folder = Path.GetFullPath(LocalizationSettings.OutputFolder);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return folder;
        }

        private static void Save(string name, string text)
            => File.WriteAllText(Path.Combine(OutputDir(), name), text);

        private static void Save(string name, int[] values)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CfgLocalizationIndex>();
            var array = builder.Allocate(ref root.values, values.Length);
            for (var i = 0; i < values.Length; i++) array[i] = values[i];

            BlobAssetReference<CfgLocalizationIndex>.Write(builder, Path.Combine(OutputDir(), name), 3);
            builder.Dispose();
        }

        private static void Save(string name, string[] values)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CfgLocalization>();
            var array = builder.Allocate(ref root.values, values.Length);
            for (var i = 0; i < values.Length; i++)
                AllocateString(ref builder, ref array[i], values[i]);

            BlobAssetReference<CfgLocalization>.Write(builder, Path.Combine(OutputDir(), name), 3);
            builder.Dispose();
        }

        /// <summary>
        /// Writes a UTF-8 string (exact byte length + null terminator) into a
        /// <see cref="BlobString"/>. The package's built-in
        /// <c>BlobStringExtensions.AllocateString</c> reserves
        /// <c>Length * 2 + 1</c> bytes, which truncates CJK text (3 bytes per
        /// char); sizing from the real UTF-8 byte count avoids that. The
        /// runtime reader reads via <see cref="BlobString.ToString"/> which
        /// expects the null-terminated layout.
        /// </summary>
        private static unsafe void AllocateString(ref BlobBuilder builder, ref BlobString blobStr, string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
            var blobData = (BlobArray<byte>*)UnsafeUtility.AddressOf(ref blobStr);
            var res = builder.Allocate(ref *blobData, bytes.Length + 1);
            fixed (byte* ptr = bytes)
            {
                UnsafeUtility.MemCpy(res.GetUnsafePtr(), ptr, bytes.Length);
            }
            ((byte*)res.GetUnsafePtr())[bytes.Length] = 0;
        }
    }
}
