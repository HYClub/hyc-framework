using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置导入的三种模式（对应设计文档 Mode A/B/C）。
    /// </summary>
    public enum ConfigExcelImportMode
    {
        /// <summary>新建模板 + 新建配置（全新表）。</summary>
        NewTemplate,
        /// <summary>更新模板（按字段名合并）+ 新建/更新配置。</summary>
        UpdateTemplate,
        /// <summary>仅更新配置（按主键 ID 对账，不动模板）。</summary>
        ConfigOnly,
    }

    /// <summary>
    /// 字段草拟项：从 Excel 表头解析出来、用户可改的一行。
    /// <see cref="IsKey"/> 列（ID）与 <see cref="IsIgnored"/> 列（GUID）是框架列，不生成字段。
    /// </summary>
    public sealed class DraftField
    {
        public string Chinese = "";
        public string English = "field";
        public ConfigFieldType Type = ConfigFieldType.String;
        /// <summary><see cref="ConfigFieldType.Enum"/> 的枚举类名 / <see cref="ConfigFieldType.Reference"/> 的配置类短名。</summary>
        public string TypeArg = "";
        public bool IsList;
        /// <summary>主键列（英文字段名为 ID）。模式③按它对账。</summary>
        public bool IsKey;
        /// <summary>框架列（GUID），导入时忽略。</summary>
        public bool IsIgnored;
        /// <summary>类型串解析失败（表头第 3 行认不出）——用户必须手动指定类型。</summary>
        public bool ParseFailed;
        /// <summary>来源列下标（预览与草拟表对齐用）。</summary>
        public int SourceColumn;
    }

    /// <summary>
    /// Excel → 配置导入窗口（「拖入 Excel → 生成/更新模板与配置」功能族的交互本体）。
    /// <para>
    /// 四步动线：① 选/拖入 .xlsx（支持多 sheet）→ ② 选 sheet + 看预览
    /// → ③ 定模式 + 命名 + 字段草拟（类型/列表/主键可改）→ ④ 执行。
    /// 表头三种格式（3 行中文/英文/类型、2 行英文、1 行中文）自动识别，见 <see cref="ConfigExcelReader"/>。
    /// </para>
    /// <para>
    /// 执行逻辑按模式拆到独立文件（NewTemplate / UpdateTemplate / ConfigOnly），
    /// 本窗口只负责「解析 + 预览 + 草拟 + 校验」，与写入策略解耦。
    /// </para>
    /// </summary>
    public class ConfigExcelImportWindow : EditorWindow
    {
        private const float LetterColWidth = 28f;
        private const float MinDataColWidth = 90f;
        private const float ScrollBarAllowance = 15f;
        private const float ViewPadding = 12f;
        private const int MaxPreviewRows = 200;

        private string _filePath = "";
        private List<ExcelSheetData> _sheets = new List<ExcelSheetData>();
        private int _sheetIndex;
        private List<DraftField> _fields = new List<DraftField>();
        private ConfigExcelImportMode _mode = ConfigExcelImportMode.NewTemplate;
        private string _displayName = "";
        private string _className = "";
        private string _loadError = "";
        /// <summary>删除孤儿配置资产（Excel 中不存在的存量资产）。默认关，仅模式②③有意义。</summary>
        private bool _deleteOrphans;
        /// <summary>最近一次校验的错误数。DrawValidation 每帧先于 DrawExecuteBar 执行，取缓存避免重复扫资产库。</summary>
        private int _lastErrorCount = 1;

        private Vector2 _previewScroll;
        private Vector2 _draftScroll;

        // 样式缓存（static：窗口重开不重建）
        private static GUIStyle s_headerCellStyle;
        private static GUIStyle s_cellStyle;
        private static GUIStyle s_errorStyle;
        private static GUIStyle s_dimStyle;
        private static readonly string[] s_TypeLabels;

        static ConfigExcelImportWindow()
        {
            s_TypeLabels = ConfigExcelSchema.DraftTypes
                .Select(t => ConfigExcelSchema.DraftTypeLabel(t, "", ""))
                .ToArray();
        }

        [MenuItem("HYC Framework/Config/Import Excel...", false, 7)]
        public static void OpenFromMenu()
        {
            var win = GetWindow<ConfigExcelImportWindow>(false, "Import Config Excel", true);
            win.minSize = new Vector2(880f, 560f);
        }

        // ---------- 每帧 UI ----------

        private void OnGUI()
        {
            HandleDragAndDrop();
            EnsureStyles();

            EditorGUILayout.Space(4);
            DrawFileBar();
            EditorGUILayout.Space(2);

            if (_sheets.Count > 0)
            {
                DrawSheetBar();
                EditorGUILayout.Space(2);
                DrawNamingBar();
                DrawModeBar();
                EditorGUILayout.Space(4);
                DrawValidation();
                EditorGUILayout.Space(4);
                DrawExecuteBar();
                EditorGUILayout.Space(4);
                DrawDraftTable();
                EditorGUILayout.Space(4);
                DrawPreview();
            }
            else if (_loadError.Length > 0)
            {
                EditorGUILayout.HelpBox(_loadError, MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Drop an .xlsx/.xls file here, or use the button above to pick one.\n" +
                    "Header formats auto-detected: 3 rows (Chinese / English / Type), 2 rows (English), 1 row (Chinese).",
                    MessageType.Info);
            }
        }

        private void EnsureStyles()
        {
            if (s_headerCellStyle == null)
            {
                s_headerCellStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true,
                    fontSize = 10,
                    alignment = TextAnchor.UpperLeft,
                };
                s_cellStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = false,
                    alignment = TextAnchor.MiddleLeft,
                };
                s_errorStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true,
                    fontSize = 11,
                };
                s_errorStyle.normal.textColor = new Color(0.85f, 0.2f, 0.2f);
                s_dimStyle = new GUIStyle(EditorStyles.label);
                s_dimStyle.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
            }
        }

        private void DrawFileBar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Excel...", GUILayout.Width(120)))
                PickFile();
            if (GUILayout.Button("Reload", GUILayout.Width(70)) && _filePath.Length > 0)
                LoadFile(_filePath);
            var label = _filePath.Length > 0 ? _filePath : "(no file)";
            GUILayout.Label(new GUIContent(label, label), s_dimStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSheetBar()
        {
            var sheet = CurrentSheet;
            if (sheet == null)
                return;
            var names = _sheets.Select(s => s.SheetName).ToArray();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Sheet", GUILayout.Width(44));
            var next = _sheets.Count > 1
                ? GUILayout.Toolbar(Mathf.Clamp(_sheetIndex, 0, names.Length - 1), names)
                : _sheetIndex;
            if (next != _sheetIndex)
            {
                _sheetIndex = next;
                RebuildDraftFields();
            }
            GUILayout.Label($"{sheet.ColumnCount} cols × {sheet.Rows.Count} data rows · {FormatModeText(sheet.FormatMode)}",
                s_dimStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawNamingBar()
        {
            // 模式③不动模板：Display Name 置灰（控件集合不变，仅禁用）；
            // Class Name 三种模式都可编辑——模式②③要靠它定位目标模板。
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Display Name", GUILayout.Width(90));
            EditorGUI.BeginDisabledGroup(_mode == ConfigExcelImportMode.ConfigOnly);
            _displayName = EditorGUILayout.TextField(_displayName);
            EditorGUI.EndDisabledGroup();
            GUILayout.Label("Class Name", GUILayout.Width(80));
            _className = EditorGUILayout.TextField(_className);
            if (GUILayout.Button("From Sheet", GUILayout.Width(90)))
                ApplySheetNaming();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawModeBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Mode", GUILayout.Width(44));
            var next = (ConfigExcelImportMode)GUILayout.Toolbar((int)_mode, new[]
            {
                "New Template + Configs",
                "Update Template + Configs",
                "Configs Only (by ID)",
            });
            if (next != _mode)
                _mode = next;
            EditorGUILayout.EndHorizontal();

            var hint = _mode == ConfigExcelImportMode.NewTemplate
                ? "Create a new template asset + one config asset per data row."
                : _mode == ConfigExcelImportMode.UpdateTemplate
                    ? "Merge fields into the existing template by field name (add/remove/rename), then create/update configs."
                    : "Do not touch the template; reconcile config rows by primary key (ID).";
            EditorGUILayout.LabelField(hint, s_dimStyle);
        }

        private void DrawValidation()
        {
            var errors = Validate();
            if (errors.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"Plan looks good: {_fields.Count(f => !f.IsKey && !f.IsIgnored)} field(s), " +
                    $"{CurrentSheet?.Rows.Count ?? 0} data row(s)." +
                    (_mode == ConfigExcelImportMode.NewTemplate
                        ? " Ready to execute."
                        : _mode == ConfigExcelImportMode.UpdateTemplate
                            ? " Ready to execute (template merge + row reconcile)."
                            : " Ready to execute (row reconcile by ID)."),
                    MessageType.Info);
                _lastErrorCount = 0;
                return;
            }
            _lastErrorCount = errors.Count;
            var shown = string.Join("\n", errors.Take(6));
            if (errors.Count > 6)
                shown += $"\n(+{errors.Count - 6} more, see Console)";
            foreach (var e in errors.Skip(6))
                Debug.LogWarning("[ConfigExcelImport] " + e);
            EditorGUILayout.HelpBox(shown, MessageType.Error);
        }

        // ---------- 执行 ----------

        /// <summary>
        /// 执行栏：三种模式都可点（校验全过才启用——IMGUI 铁律：控件集合不变，仅禁用）。
        /// 孤儿删除开关常画（模式①置灰）：删除 Excel 中不存在的存量配置资产，默认关。
        /// </summary>
        private void DrawExecuteBar()
        {
            EditorGUILayout.BeginHorizontal();
            var canExecute = _lastErrorCount == 0 && CurrentSheet != null;
            var label = _mode == ConfigExcelImportMode.NewTemplate
                ? "Execute: Create Template + Configs"
                : _mode == ConfigExcelImportMode.UpdateTemplate
                    ? "Execute: Update Template + Configs"
                    : "Execute: Reconcile Configs by ID";
            EditorGUI.BeginDisabledGroup(_mode == ConfigExcelImportMode.NewTemplate);
            _deleteOrphans = GUILayout.Toggle(_deleteOrphans,
                new GUIContent("Delete orphans", "Delete config assets whose ID is absent from the Excel (C4).\nOff = keep them and report in Console."),
                GUILayout.Width(110));
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!canExecute);
            if (GUILayout.Button(label, GUILayout.Height(24)))
                Execute();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void Execute()
        {
            var plan = BuildPlan();
            plan.DeleteOrphans = _deleteOrphans;
            bool ok;
            string error;
            try
            {
                switch (plan.Mode)
                {
                    case ConfigExcelImportMode.UpdateTemplate:
                        ok = ConfigExcelImporter.ExecuteUpdateTemplate(plan, out error);
                        break;
                    case ConfigExcelImportMode.ConfigOnly:
                        ok = ConfigExcelImporter.ExecuteConfigOnly(plan, out error);
                        break;
                    default:
                        ok = ConfigExcelImporter.ExecuteNewTemplate(plan, out error);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[ConfigExcelImport] Execute failed:\n" + e);
                EditorUtility.DisplayDialog("Import Failed", e.Message, "OK");
                return;
            }
            if (!ok)
            {
                EditorUtility.DisplayDialog("Import Failed", error ?? "Unknown error.", "OK");
                return;
            }
            if (plan.Mode == ConfigExcelImportMode.UpdateTemplate)
            {
                EditorUtility.DisplayDialog("Import Queued",
                    $"Template '{plan.ClassName}' merged with the Excel fields.\n\n" +
                    "If the template changed, configs are reconciled automatically once Unity finishes compiling — " +
                    "watch the Console for the result.\n\n" +
                    "(Orphan configs are " + (_deleteOrphans ? "DELETED" : "kept and reported") + ".)",
                    "OK");
            }
            else if (plan.Mode == ConfigExcelImportMode.ConfigOnly)
            {
                EditorUtility.DisplayDialog("Import Queued",
                    $"Configs of '{plan.ClassName}' reconciled by ID.\n\n" +
                    "Watch the Console for the created / updated / skipped / orphan counts.\n\n" +
                    "(Orphan configs are " + (_deleteOrphans ? "DELETED" : "kept and reported") + ".)",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Import Queued",
                    $"Template '{plan.ClassName}' created and code generated.\n\n" +
                    "The config assets are created automatically once Unity finishes compiling — " +
                    "watch the Console for the result.\n\n" +
                    "(The template asset is in the Config data root folder.)",
                    "OK");
            }
        }

        private ImportPlan BuildPlan()
        {
            return new ImportPlan
            {
                Mode = _mode,
                DisplayName = _displayName ?? "",
                ClassName = _className ?? "",
                Fields = _fields,
                Sheet = CurrentSheet,
            };
        }

        private void DrawDraftTable()
        {
            EditorGUILayout.LabelField("Fields (draft)", EditorStyles.boldLabel);
            _draftScroll = EditorGUILayout.BeginScrollView(_draftScroll, GUILayout.Height(180));

            // 表头
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Col", GUILayout.Width(28));
            GUILayout.Label("Chinese", GUILayout.Width(140));
            GUILayout.Label("English (field name)", GUILayout.Width(160));
            GUILayout.Label("Type", GUILayout.Width(110));
            GUILayout.Label("Class / Enum", GUILayout.Width(120));
            GUILayout.Label("List", GUILayout.Width(36));
            GUILayout.Label("Key", GUILayout.Width(36));
            GUILayout.Space(ScrollBarAllowance);
            EditorGUILayout.EndHorizontal();

            for (var i = 0; i < _fields.Count; i++)
                DrawDraftRow(i);

            EditorGUILayout.EndScrollView();
        }

        private void DrawDraftRow(int i)
        {
            var f = _fields[i];
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label(ColumnLetter(f.SourceColumn), GUILayout.Width(28));
            f.Chinese = EditorGUILayout.TextField(f.Chinese, GUILayout.Width(140));
            f.English = EditorGUILayout.TextField(f.English, GUILayout.Width(160));

            var typeIndex = Array.IndexOf(ConfigExcelSchema.DraftTypes, f.Type);
            var newIndex = EditorGUILayout.Popup(Mathf.Max(typeIndex, 0), s_TypeLabels, GUILayout.Width(110));
            if (newIndex != typeIndex && newIndex >= 0 && newIndex < ConfigExcelSchema.DraftTypes.Length)
                f.Type = ConfigExcelSchema.DraftTypes[newIndex];

            // Enum / Reference 需要类名参数；解析失败时也允许手填
            if (f.Type == ConfigFieldType.Enum || f.Type == ConfigFieldType.Reference || f.ParseFailed)
            {
                f.TypeArg = EditorGUILayout.TextField(f.TypeArg, GUILayout.Width(120));
                if (f.Type == ConfigFieldType.Enum && f.TypeArg.Length > 0
                    && ConfigEnumCodeGen.FindEnum(f.TypeArg) == null)
                    GUILayout.Label("enum?", s_errorStyle, GUILayout.Width(40));
                else if (f.Type == ConfigFieldType.Reference && f.TypeArg.Length > 0
                         && !IsKnownConfigClass(f.TypeArg))
                    GUILayout.Label("cfg?", s_errorStyle, GUILayout.Width(40));
                else
                    GUILayout.Label("", GUILayout.Width(40));
            }
            else
            {
                GUILayout.Label("", GUILayout.Width(120));
                GUILayout.Label("", GUILayout.Width(40));
            }

            f.IsList = EditorGUILayout.Toggle(f.IsList, GUILayout.Width(36));
            f.IsKey = EditorGUILayout.Toggle(f.IsKey, GUILayout.Width(36));

            if (f.ParseFailed)
                GUILayout.Label("!", s_errorStyle, GUILayout.Width(18));
            else if (f.IsKey)
                GUILayout.Label("K", s_dimStyle, GUILayout.Width(18));
            else if (f.IsIgnored)
                GUILayout.Label("G", s_dimStyle, GUILayout.Width(18));
            else
                GUILayout.Label("", GUILayout.Width(18));

            GUILayout.Space(ScrollBarAllowance);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreview()
        {
            var sheet = CurrentSheet;
            if (sheet == null)
                return;

            EditorGUILayout.LabelField("Preview (data rows)", EditorStyles.boldLabel);
            var rowCount = Mathf.Min(sheet.Rows.Count, MaxPreviewRows);
            if (sheet.Rows.Count > MaxPreviewRows)
                EditorGUILayout.LabelField($"showing first {MaxPreviewRows} of {sheet.Rows.Count} rows", s_dimStyle);

            var dataColWidth = Mathf.Max(MinDataColWidth,
                (position.width - ViewPadding - LetterColWidth - ScrollBarAllowance)
                / Mathf.Max(1, sheet.ColumnCount));

            // 表头画在滚动区外：列字母 / 中文 / 英文 / 类型四行堆叠，不随数据滚动
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(LetterColWidth));
            for (var c = 0; c < sheet.ColumnCount; c++)
            {
                var text = $"{ColumnLetter(c)}\n{sheet.ChineseHeaders[c]}\n{sheet.EnglishHeaders[c]}\n{sheet.TypeHeaders[c]}";
                GUILayout.Label(new GUIContent(text, text), s_headerCellStyle, GUILayout.Width(dataColWidth));
            }
            GUILayout.Space(ScrollBarAllowance);
            EditorGUILayout.EndHorizontal();

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(200));
            for (var r = 0; r < rowCount; r++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label((r + 1).ToString(), s_dimStyle, GUILayout.Width(LetterColWidth));
                var row = sheet.Rows[r];
                for (var c = 0; c < sheet.ColumnCount; c++)
                {
                    var text = c < row.Count ? row[c] : "";
                    GUILayout.Label(new GUIContent(text, text), s_cellStyle, GUILayout.Width(dataColWidth));
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        // ---------- 文件 / 解析 ----------

        private ExcelSheetData CurrentSheet
            => _sheetIndex >= 0 && _sheetIndex < _sheets.Count ? _sheets[_sheetIndex] : null;

        private void PickFile()
        {
            var path = EditorUtility.OpenFilePanel("Select Excel File", "Assets", "xlsx");
            if (!string.IsNullOrEmpty(path))
                LoadFile(path);
        }

        private void LoadFile(string path)
        {
            _filePath = path;
            _sheets = new List<ExcelSheetData>();
            _fields = new List<DraftField>();
            _loadError = "";
            _sheetIndex = 0;

            try
            {
                _sheets = ConfigExcelReader.ReadFile(path);
            }
            catch (Exception e)
            {
                _loadError = $"Failed to read {path}: {e.Message}";
                Debug.LogError("[ConfigExcelImport] " + _loadError);
                return;
            }

            if (_sheets.Count == 0)
            {
                _loadError = "No sheets with data found in the file.";
                return;
            }

            ApplySheetNaming();
            RebuildDraftFields();
            Debug.Log($"[ConfigExcelImport] Loaded {path}: {_sheets.Count} sheet(s), using '{CurrentSheet?.SheetName}'");
        }

        private void ApplySheetNaming()
        {
            var sheet = CurrentSheet;
            if (sheet == null)
                return;
            _displayName = sheet.SheetName;
            _className = ConfigExcelSchema.SanitizeClassName(sheet.SheetName);
        }

        private void RebuildDraftFields()
        {
            var sheet = CurrentSheet;
            _fields.Clear();
            if (sheet == null)
                return;

            for (var c = 0; c < sheet.ColumnCount; c++)
            {
                var english = ConfigExcelSchema.SanitizeFieldName(
                    c < sheet.EnglishHeaders.Count ? sheet.EnglishHeaders[c] : "");
                var chinese = c < sheet.ChineseHeaders.Count ? sheet.ChineseHeaders[c] : "";
                var f = new DraftField
                {
                    Chinese = chinese.Length > 0 ? chinese : english,
                    English = english,
                    SourceColumn = c,
                };

                if (string.Equals(f.English, ConfigExcelSchema.IdColumn, StringComparison.OrdinalIgnoreCase))
                    f.IsKey = true;
                else if (string.Equals(f.English, ConfigExcelSchema.GuidColumn, StringComparison.OrdinalIgnoreCase))
                    f.IsIgnored = true;
                else
                    ResolveFieldType(f, sheet, c);

                _fields.Add(f);
            }
        }

        private void ResolveFieldType(DraftField f, ExcelSheetData sheet, int column)
        {
            var typeText = column < sheet.TypeHeaders.Count ? sheet.TypeHeaders[column] : "";
            if (typeText.Length == 0)
            {
                f.Type = InferType(sheet, column);
                return;
            }
            if (!ConfigExcelSchema.TryParseTypeName(typeText, out var t))
            {
                // 认不出的类型串：标红留待用户手改（不静默按 String 吞掉）
                f.ParseFailed = true;
                f.Type = ConfigFieldType.String;
                Debug.LogWarning($"[ConfigExcelImport] column {ColumnLetter(column)}: unknown type '{typeText}'");
                return;
            }
            f.Type = t;
            if (t == ConfigFieldType.Enum)
                f.TypeArg = typeText;
            else if (t == ConfigFieldType.Reference)
                f.TypeArg = ConfigTemplateCodeGen.RefTypeShortName(typeText) ?? typeText;
        }

        /// <summary>无类型行时按首个非空数据值推断（兜底，用户可在草拟表改）。</summary>
        private ConfigFieldType InferType(ExcelSheetData sheet, int column)
        {
            foreach (var row in sheet.Rows)
            {
                if (column >= row.Count)
                    continue;
                var s = row[column].Trim();
                if (s.Length == 0)
                    continue;
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
                    return ConfigFieldType.Bool;
                if (long.TryParse(s, out var l))
                    return l > int.MaxValue || l < int.MinValue ? ConfigFieldType.Long : ConfigFieldType.Int;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    return ConfigFieldType.Float;
                return ConfigFieldType.String;
            }
            return ConfigFieldType.String;
        }

        // ---------- 校验 ----------

        private List<string> Validate()
        {
            var errors = new List<string>();
            var sheet = CurrentSheet;
            if (sheet == null)
            {
                errors.Add("No sheet loaded.");
                return errors;
            }

            // 字段名重复 / 空
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _fields)
            {
                if (f.IsKey || f.IsIgnored)
                    continue;
                if (f.English.Trim().Length == 0)
                {
                    errors.Add($"Column {ColumnLetter(f.SourceColumn)}: empty field name.");
                    continue;
                }
                if (!seen.Add(f.English))
                    errors.Add($"Duplicate field name '{f.English}'.");
            }

            // Enum / Reference 类名可解析
            foreach (var f in _fields)
            {
                if (f.IsKey || f.IsIgnored)
                    continue;
                if (f.ParseFailed)
                    errors.Add($"Field '{f.English}': type '{sheet.TypeHeaders[f.SourceColumn]}' unrecognized — pick a type in the draft table.");
                if (f.Type == ConfigFieldType.Enum && ConfigEnumCodeGen.FindEnum(f.TypeArg) == null)
                    errors.Add($"Field '{f.English}': enum '{f.TypeArg}' not found (create it in the Enum editor first).");
                if (f.Type == ConfigFieldType.Reference && !IsKnownConfigClass(f.TypeArg))
                    errors.Add($"Field '{f.English}': config class '{f.TypeArg}' not found.");
            }

            // 主键列重复值（模式②③按 ID 对账，重复会导致同一资产被写多次）
            var keyField = _fields.FirstOrDefault(f => f.IsKey);
            if (keyField != null && _mode != ConfigExcelImportMode.NewTemplate)
            {
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in sheet.Rows)
                {
                    if (keyField.SourceColumn < 0 || keyField.SourceColumn >= row.Count)
                        continue;
                    var v = (row[keyField.SourceColumn] ?? "").Trim();
                    if (v.Length == 0)
                        continue;
                    if (!seenIds.Add(v))
                        errors.Add($"Duplicate ID '{v}' in the key column — each config row must have a unique ID.");
                }
            }

            // 模式相关
            if (_mode == ConfigExcelImportMode.NewTemplate)
            {
                if (_className.Trim().Length == 0)
                    errors.Add("Class name is empty.");
                else if (IsKnownConfigClass(_className))
                    errors.Add($"Class name '{_className}' is already taken — use Update mode or pick another name.");
            }
            else
            {
                if (_className.Trim().Length == 0)
                    errors.Add("Class name is empty.");
                else if (!IsKnownConfigClass(_className))
                    errors.Add($"Class '{_className}' not found — check the name or use New Template mode.");
                if (_mode == ConfigExcelImportMode.ConfigOnly && !_fields.Any(f => f.IsKey))
                    errors.Add("Configs Only mode needs an ID key column (a column named 'ID').");
            }
            return errors;
        }

        private static bool IsKnownConfigClass(string className)
        {
            if (string.IsNullOrEmpty(className))
                return false;
            foreach (var t in ConfigTemplateCodeGen.LoadAllTemplates(false))
            {
                if (t != null && t.generateCode && t.className == className)
                    return true;
            }
            return false;
        }

        // ---------- 拖拽 ----------

        private void HandleDragAndDrop()
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
                return;
            var excel = DragAndDrop.paths.FirstOrDefault(p =>
                p.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".xls", StringComparison.OrdinalIgnoreCase));
            if (excel == null)
                return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                LoadFile(excel);
            }
            e.Use();
        }

        // ---------- 工具 ----------

        private static string FormatModeText(string mode)
        {
            switch (mode)
            {
                case ConfigExcelSchema.FormatTyped3: return "3-row header (Chinese / English / Type)";
                case ConfigExcelSchema.FormatEn2: return "2-row header (English)";
                default: return "1-row header (Chinese)";
            }
        }

        /// <summary>0→A, 25→Z, 26→AA（Excel 列字母）。</summary>
        private static string ColumnLetter(int index)
        {
            var n = index;
            var s = "";
            n++;
            while (n > 0)
            {
                var rem = (n - 1) % 26;
                s = (char)('A' + rem) + s;
                n = (n - 1) / 26;
            }
            return s;
        }
    }
}
