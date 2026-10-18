using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 导入计划：导入窗口收集的用户意图（模式 / 命名 / 字段草拟 / 表数据）。
    /// 执行器（模式①②③）只认这个结构，与窗口 UI 解耦。
    /// </summary>
    public sealed class ImportPlan
    {
        public ConfigExcelImportMode Mode = ConfigExcelImportMode.NewTemplate;
        /// <summary>模板 displayName（支持 分类/名称）。</summary>
        public string DisplayName = "";
        /// <summary>模板 className（生成的 C# 类名）。</summary>
        public string ClassName = "";
        /// <summary>字段草拟（已排除框架列 ID/GUID）。</summary>
        public List<DraftField> Fields = new List<DraftField>();
        /// <summary>表数据（含表头三行信息，行单元格按 <see cref="DraftField.SourceColumn"/> 对齐）。</summary>
        public ExcelSheetData Sheet;
        /// <summary>删除孤儿配置资产（Excel 中不存在的存量资产，C4）。仅模式②③有意义。</summary>
        public bool DeleteOrphans;
    }

    /// <summary>待处理导入的字段定义（随 <see cref="ConfigExcelPendingImport"/> 序列化）。</summary>
    [Serializable]
    public sealed class PendingFieldDef
    {
        public string name = "";
        public ConfigFieldType type = ConfigFieldType.String;
        public bool isList;
        /// <summary>Enum 的枚举类名 / Reference 的配置类短名。</summary>
        public string typeArg = "";
    }

    /// <summary>待处理导入的一行数据（单元格按 <see cref="PendingFieldDef"/> 顺序对齐）。</summary>
    [Serializable]
    public sealed class PendingRow
    {
        public List<string> cells = new List<string>();
        /// <summary>主键列（ID）原始文本；无主键列时为空串。模式②③按它对账。</summary>
        public string idText = "";
    }

    /// <summary>待处理导入的应用方式。</summary>
    public enum PendingKind
    {
        /// <summary>只创建（模式①）：已存在的资产跳过，绝不改存量。</summary>
        CreateOnly,
        /// <summary>按主键对账（模式②③）：命中更新、未命中创建、无主键列时按资产名对账。</summary>
        Reconcile,
    }

    /// <summary>
    /// 待处理导入载荷：模板已建、代码已生成，但<b>配置资产要等编译后才能创建</b>
    /// （生成的 C# 类在编译前不存在，<see cref="ConfigAssetFactory"/> 拿不到类型）。
    /// 编译完成后由 <see cref="ConfigExcelPendingApplier"/> 自动应用并删除本资产。
    /// </summary>
    public sealed class ConfigExcelPendingImport : ScriptableObject
    {
        /// <summary>目标配置类型名（生成的类名）。</summary>
        public string className = "";
        /// <summary>模板资产路径（日志/排查用）。</summary>
        public string templatePath = "";
        /// <summary>应用方式（创建 / 对账）。</summary>
        public PendingKind kind = PendingKind.CreateOnly;
        /// <summary>是否有主键列（ID）。无主键列时按资产名对账。</summary>
        public bool hasKeyColumn;
        /// <summary>是否删除孤儿配置资产（Excel 中不存在的存量资产，C4）。默认 false=保留并报告。</summary>
        public bool deleteOrphans;
        public List<PendingFieldDef> fields = new List<PendingFieldDef>();
        public List<PendingRow> rows = new List<PendingRow>();
    }

    /// <summary>
    /// Excel → 配置执行器。
    /// <para>
    /// 模式①（<see cref="ExecuteNewTemplate"/>）：新建模板 + 新建配置。两段式落地——本段建模板资产
    /// → 生成代码 → 挂 <see cref="ConfigExcelPendingImport"/> 载荷；编译完成后
    /// <see cref="ConfigExcelPendingApplier"/>（[InitializeOnLoad]）自动创建配置资产。
    /// </para>
    /// <para>
    /// 模式②（<see cref="ExecuteUpdateTemplate"/>）：按字段名合并进既有模板（增/删/改类型）→
    /// 模板有实质变更则重新生成代码并挂对账载荷等编译后应用；零变更则目标类已在域内、立即对账。
    /// 对账（<see cref="ReconcilePending"/>）按主键 ID：命中更新、未命中创建、主键无效跳过、
    /// 孤儿（存量但不在 Excel）默认保留并报告；无主键列时退化为按资产名对账。
    /// </para>
    /// <para>
    /// 模式③（<see cref="ExecuteConfigOnly"/>）：不碰模板，只按主键 ID 对账配置行。
    /// 目标类已在域内（模板未变、无需重编译）→ 立即对账；类还没编译出来（如全新检出）
    /// → 挂载荷等编译后由 applier 对账。
    /// </para>
    /// <para>
    /// ID 策略：**模式①的 ID 一律走发号服**（<see cref="ConfigAssetFactory"/>），
    /// Excel 的 ID 列只是框架列、不手填（AGENTS.md 铁律：禁止手填 ID）；
    /// 模式②③按 ID 对账时它才是查询键（新行仍走发号服发新 ID）。
    /// </para>
    /// <para>
    /// 单元格文本 → 字段值的解析（含列表/引用/资源名/结构体）在本类，各模式共用。
    /// </para>
    /// </summary>
    public static class ConfigExcelImporter
    {
        // ---------- 模式①：新建模板 + 新建配置 ----------

        /// <summary>
        /// 建模板资产 → 生成代码 → 挂待处理数据。返回 false 时 error 为原因
        /// （代码生成失败也保留模板与载荷：用户修好字段后点「生成代码」即可继续）。
        /// </summary>
        public static bool ExecuteNewTemplate(ImportPlan plan, out string error)
        {
            error = null;
            if (plan?.Sheet == null)
            {
                error = "No sheet loaded.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(plan.ClassName))
            {
                error = "Class name is empty.";
                return false;
            }
            if (IsKnownConfigClass(plan.ClassName))
            {
                error = $"Class name '{plan.ClassName}' is already taken — use Update mode or pick another name.";
                return false;
            }
            var fields = (plan.Fields ?? new List<DraftField>()).Where(f => f != null && !f.IsKey && !f.IsIgnored).ToList();
            if (fields.Count == 0)
            {
                error = "No fields to import (every column is a framework column?).";
                return false;
            }

            var tplFolder = ConfigAssetLayout.EnsureTypeFolder(typeof(ConfigTemplate)) ?? ConfigDataSettings.RootFolder;

            // 1. 模板资产（默认基类 ConfigBase，与 CreateTemplateAsset 同规）
            var tpl = ScriptableObject.CreateInstance<ConfigTemplate>();
            tpl.displayName = string.IsNullOrWhiteSpace(plan.DisplayName) ? plan.ClassName : plan.DisplayName;
            tpl.className = plan.ClassName;
            tpl.baseTemplate = ConfigTemplateCodeGen.GetConfigBaseTemplate();
            foreach (var f in fields)
                tpl.fields.Add(ToTemplateField(f));
            var tplName = ConfigTemplateCodeGen.DisplayNameToAssetName(tpl.displayName);
            var tplPath = UniqueAssetPath(tplFolder, tplName);
            AssetDatabase.CreateAsset(tpl, tplPath);
            EditorUtility.SetDirty(tpl);
            AssetDatabase.SaveAssets();

            // 2. 待处理数据（先挂载荷：即使代码生成失败，用户修好模板重生成后也能自动落数据）
            var pending = ScriptableObject.CreateInstance<ConfigExcelPendingImport>();
            pending.className = plan.ClassName;
            pending.templatePath = tplPath;
            foreach (var f in fields)
            {
                pending.fields.Add(new PendingFieldDef
                {
                    name = f.English,
                    type = f.Type,
                    isList = f.IsList,
                    typeArg = f.TypeArg ?? "",
                });
            }
            foreach (var row in plan.Sheet.Rows)
            {
                if (row == null)
                    continue;
                var pr = new PendingRow();
                foreach (var f in fields)
                    pr.cells.Add(f.SourceColumn >= 0 && f.SourceColumn < row.Count ? row[f.SourceColumn] : "");
                if (pr.cells.Any(c => !string.IsNullOrEmpty(c)))
                    pending.rows.Add(pr);
            }
            var pendingPath = UniqueAssetPath(tplFolder, "PendingImport_" + plan.ClassName);
            AssetDatabase.CreateAsset(pending, pendingPath);
            AssetDatabase.SaveAssets();

            // 3. 生成代码（失败不删模板/载荷：保留可修）
            if (!ConfigTemplateCodeGen.WriteFile(tpl, out error))
            {
                Debug.LogWarning($"[ConfigExcelImport] Template '{plan.ClassName}' created ({tplPath}) but code generation failed: {error}\n" +
                                 $"Fix the fields in the template editor and click Generate Code — {pending.rows.Count} queued row(s) will be applied automatically after compilation.");
                return false;
            }

            Debug.Log($"[ConfigExcelImport] Template '{plan.ClassName}' created ({tplPath}), code generated. " +
                      $"{pending.rows.Count} row(s) queued ({pendingPath}) — config assets are created automatically after compilation.");
            return true;
        }

        /// <summary>草拟项 → 模板字段（枚举/引用带类名参数）。</summary>
        private static ConfigTemplateField ToTemplateField(DraftField f)
        {
            var tf = new ConfigTemplateField
            {
                name = f.English,
                description = f.Chinese ?? "",
                type = f.Type,
                isList = f.IsList,
            };
            if (f.Type == ConfigFieldType.Enum)
                tf.enumRefClassName = f.TypeArg ?? "";
            if (f.Type == ConfigFieldType.Reference)
                tf.refTypeFullName = f.TypeArg ?? "";
            return tf;
        }

        // ---------- 模式②：更新模板 + 新建/更新配置 ----------

        /// <summary>
        /// 按字段名把 Excel 草拟合并进既有模板（增/删/改类型），再按主键对账配置行。
        /// <para>
        /// 模板有实质变更（增/删/改类型/改中文名）→ 重新生成代码 → 挂对账载荷等编译后应用；
        /// 模板零变更 → 目标类已在当前域，立即对账（无需等编译）。
        /// </para>
        /// </summary>
        public static bool ExecuteUpdateTemplate(ImportPlan plan, out string error)
        {
            error = null;
            if (plan?.Sheet == null)
            {
                error = "No sheet loaded.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(plan.ClassName))
            {
                error = "Class name is empty.";
                return false;
            }
            var tpl = FindTemplateByClassName(plan.ClassName);
            if (tpl == null)
            {
                error = $"Template '{plan.ClassName}' not found — check the name or use New Template mode.";
                return false;
            }
            if (!tpl.generateCode)
            {
                error = $"Template '{plan.ClassName}' is framework-level (generateCode=false) — nothing to merge.";
                return false;
            }
            var fields = (plan.Fields ?? new List<DraftField>()).Where(f => f != null && !f.IsKey && !f.IsIgnored).ToList();
            if (fields.Count == 0)
            {
                error = "No fields to import (every column is a framework column?).";
                return false;
            }

            // 1. 按字段名合并：存量字段原地更新（保序，避免无谓重排引发重编译），Excel 新增的追加，Excel 没有的移除
            var byName = new Dictionary<string, ConfigTemplateField>(StringComparer.Ordinal);
            foreach (var tf in tpl.fields)
            {
                if (tf != null && !byName.ContainsKey(tf.name))
                    byName.Add(tf.name, tf);
            }
            var added = new List<string>();
            var removed = new List<string>();
            var retyped = new List<string>();
            var relabeled = new List<string>();
            foreach (var f in fields)
            {
                ConfigTemplateField tf;
                if (byName.TryGetValue(f.English, out tf))
                {
                    byName.Remove(f.English);
                    if (tf.type != f.Type || tf.isList != f.IsList
                        || (f.Type == ConfigFieldType.Enum && tf.enumRefClassName != (f.TypeArg ?? ""))
                        || (f.Type == ConfigFieldType.Reference && tf.refTypeFullName != (f.TypeArg ?? "")))
                    {
                        retyped.Add($"{f.English} ({tf.type}{(tf.isList ? "[]" : "")} → {f.Type}{(f.IsList ? "[]" : "")})");
                    }
                    if (tf.description != (f.Chinese ?? ""))
                        relabeled.Add(f.English);
                    tf.description = f.Chinese ?? "";
                    tf.type = f.Type;
                    tf.isList = f.IsList;
                    // 类型切走时清掉残留参数（消费端虽按 type 守卫，留着会在模板编辑器里造成误导）
                    tf.enumRefClassName = f.Type == ConfigFieldType.Enum ? (f.TypeArg ?? "") : "";
                    tf.refTypeFullName = f.Type == ConfigFieldType.Reference ? (f.TypeArg ?? "") : "";
                }
                else
                {
                    tf = ToTemplateField(f);
                    tpl.fields.Add(tf);
                    added.Add(f.English);
                }
            }
            foreach (var kv in byName)
                removed.Add(kv.Key);
            if (removed.Count > 0)
                tpl.fields.RemoveAll(tf => tf != null && byName.ContainsKey(tf.name));

            var changed = added.Count > 0 || removed.Count > 0 || retyped.Count > 0 || relabeled.Count > 0;
            EditorUtility.SetDirty(tpl);
            AssetDatabase.SaveAssets();

            // 2. 先挂对账载荷（与模式①同序：代码生成失败也保留可修——修好模板点「生成代码」后自动对账）
            var pending = BuildReconcilePayload(plan, tpl, fields);
            var pendingPath = UniqueAssetPath(ConfigAssetLayout.EnsureTypeFolder(typeof(ConfigTemplate)) ?? ConfigDataSettings.RootFolder,
                "PendingImport_" + plan.ClassName);
            AssetDatabase.CreateAsset(pending, pendingPath);
            AssetDatabase.SaveAssets();

            // 3. 有实质变更才重新生成代码（零变更不写文件，避免无谓重编译）
            if (changed)
            {
                if (retyped.Count > 0)
                    Debug.LogWarning("[ConfigExcelImport] Field type(s) changed — existing asset data may not convert: " + string.Join(", ", retyped));
                if (removed.Count > 0)
                    Debug.LogWarning("[ConfigExcelImport] Field(s) removed from template (data stays orphaned in existing assets): " + string.Join(", ", removed));
                if (!ConfigTemplateCodeGen.WriteFile(tpl, out error))
                {
                    Debug.LogWarning($"[ConfigExcelImport] Template '{plan.ClassName}' updated ({AssetDatabase.GetAssetPath(tpl)}) but code generation failed: {error}\n" +
                                     "Fix the fields in the template editor and click Generate Code — queued rows are kept.");
                    return false;
                }
            }

            // 4. 模板有变更时域内还是旧版类（重编译还没发生），必须等编译后由 applier 用新类对账；
            //    零变更时目标类已是当前版，立即对账并删载荷。
            var type = changed ? null : FindGeneratedType(plan.ClassName);
            if (type != null)
            {
                ReconcilePending(pending, type);
                AssetDatabase.DeleteAsset(pendingPath);
            }
            else
            {
                Debug.Log($"[ConfigExcelImport] Template '{plan.ClassName}' updated" +
                          (changed ? ", code generated" : " (no field change)") + ". " +
                          $"{pending.rows.Count} row(s) queued ({pendingPath}) — configs are reconciled automatically after compilation.");
            }
            return true;
        }

        /// <summary>按 className 找 generateCode 模板（大小写敏感，与生成类名一致）。</summary>
        private static ConfigTemplate FindTemplateByClassName(string className)
        {
            foreach (var t in ConfigTemplateCodeGen.LoadAllTemplates(false))
            {
                if (t != null && t.generateCode && t.className == className)
                    return t;
            }
            return null;
        }

        // ---------- 模式③：仅更新配置（按主键对账，不动模板） ----------

        /// <summary>
        /// 不碰模板，只按主键 ID 对账配置行（C1 新增 / C2 更新 / C3 跳过 / C4 孤儿）。
        /// 目标类已在域内（模板未变、无需重编译）→ 立即对账；类还没编译出来
        /// （如全新检出）→ 挂载荷等编译后由 applier 对账。
        /// </summary>
        public static bool ExecuteConfigOnly(ImportPlan plan, out string error)
        {
            error = null;
            if (plan?.Sheet == null)
            {
                error = "No sheet loaded.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(plan.ClassName))
            {
                error = "Class name is empty.";
                return false;
            }
            var tpl = FindTemplateByClassName(plan.ClassName);
            if (tpl == null)
            {
                error = $"Config class '{plan.ClassName}' not found — check the name or use another mode.";
                return false;
            }
            var fields = (plan.Fields ?? new List<DraftField>()).Where(f => f != null && !f.IsKey && !f.IsIgnored).ToList();
            if (fields.Count == 0)
            {
                error = "No fields to import (every column is a framework column?).";
                return false;
            }
            if (!(plan.Fields ?? new List<DraftField>()).Any(f => f != null && f.IsKey))
            {
                error = "Configs Only mode needs an ID key column (a column named 'ID').";
                return false;
            }

            var pending = BuildReconcilePayload(plan, tpl, fields);
            var pendingPath = UniqueAssetPath(ConfigAssetLayout.EnsureTypeFolder(typeof(ConfigTemplate)) ?? ConfigDataSettings.RootFolder,
                "PendingImport_" + plan.ClassName);
            AssetDatabase.CreateAsset(pending, pendingPath);
            AssetDatabase.SaveAssets();

            var type = FindGeneratedType(plan.ClassName);
            if (type != null)
            {
                ReconcilePending(pending, type);
                AssetDatabase.DeleteAsset(pendingPath);
            }
            else
            {
                Debug.Log($"[ConfigExcelImport] '{plan.ClassName}': {pending.rows.Count} row(s) queued ({pendingPath}) — " +
                          "configs are reconciled automatically once the generated code compiles.");
            }
            return true;
        }

        /// <summary>组装对账载荷（字段 + 全量行 + 主键列文本 + 孤儿删除开关）。</summary>
        private static ConfigExcelPendingImport BuildReconcilePayload(ImportPlan plan, ConfigTemplate tpl, List<DraftField> fields)
        {
            var keyCol = -1;
            foreach (var f in plan.Fields ?? new List<DraftField>())
            {
                if (f != null && f.IsKey)
                {
                    keyCol = f.SourceColumn;
                    break;
                }
            }
            var pending = ScriptableObject.CreateInstance<ConfigExcelPendingImport>();
            pending.className = plan.ClassName;
            pending.templatePath = AssetDatabase.GetAssetPath(tpl);
            pending.kind = PendingKind.Reconcile;
            pending.hasKeyColumn = keyCol >= 0;
            pending.deleteOrphans = plan.DeleteOrphans;
            foreach (var f in fields)
            {
                pending.fields.Add(new PendingFieldDef
                {
                    name = f.English,
                    type = f.Type,
                    isList = f.IsList,
                    typeArg = f.TypeArg ?? "",
                });
            }
            foreach (var row in plan.Sheet.Rows)
            {
                if (row == null)
                    continue;
                var pr = new PendingRow
                {
                    idText = keyCol >= 0 && keyCol < row.Count ? row[keyCol] : "",
                };
                foreach (var f in fields)
                    pr.cells.Add(f.SourceColumn >= 0 && f.SourceColumn < row.Count ? row[f.SourceColumn] : "");
                if (pr.cells.Any(c => !string.IsNullOrEmpty(c)))
                    pending.rows.Add(pr);
            }
            return pending;
        }

        /// <summary>
        /// 按主键对账配置行：命中更新（C2）、未命中创建（C1）、主键缺失/无效跳过（C3）、
        /// 存量资产不在 Excel 中=孤儿（C4，默认保留并报告，opt-in 删除）。
        /// 无主键列时退化为按资产名对账（首个字符串字段值 / 类名_序号）。
        /// </summary>
        private static void ReconcilePending(ConfigExcelPendingImport pending, Type type)
        {
            var folder = ConfigAssetLayout.EnsureTypeFolder(type) ?? ConfigDataSettings.RootFolder;
            var warnings = new ImportWarnings();
            var byId = pending.hasKeyColumn ? BuildIdIndex(type, folder) : null;
            var usedIds = new HashSet<long>();
            var created = 0;
            var updated = 0;
            var skipped = 0;

            for (var r = 0; r < pending.rows.Count; r++)
            {
                var row = pending.rows[r];
                if (row?.cells == null)
                    continue;

                string existingPath = null;
                if (pending.hasKeyColumn)
                {
                    if (!long.TryParse((row.idText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id == 0)
                    {
                        warnings.Add($"row {r + 1}: missing or invalid ID '{row.idText}' — skipped.");
                        skipped++;
                        continue;
                    }
                    usedIds.Add(id);
                    byId.TryGetValue(id, out existingPath);
                }
                else
                {
                    var namePath = $"{folder}/{SanitizeFileName(AssetNameForRow(pending, row.cells, r))}.asset";
                    if (File.Exists(namePath))
                        existingPath = namePath;
                }

                if (existingPath != null)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(existingPath);
                    if (asset == null || !type.IsInstanceOfType(asset))
                    {
                        warnings.Add($"row {r + 1}: asset at {existingPath} is not a {type.Name} — skipped.");
                        skipped++;
                        continue;
                    }
                    FillRow(asset, type, pending.fields, row.cells, warnings);
                    updated++;
                }
                else
                {
                    var asset = ConfigAssetFactory.Create(type, AssetNameForRow(pending, row.cells, r), folder);
                    FillRow(asset, type, pending.fields, row.cells, warnings);
                    created++;
                }
            }

            // C4 孤儿：工程内存量资产中 ID 不在 Excel 的
            var orphans = pending.hasKeyColumn
                ? byId.Where(kv => !usedIds.Contains(kv.Key)).Select(kv => kv.Value).ToList()
                : new List<string>();
            if (orphans.Count > 0)
            {
                if (pending.deleteOrphans)
                {
                    foreach (var p in orphans)
                        AssetDatabase.DeleteAsset(p);
                    Debug.Log($"[ConfigExcelImport] '{pending.className}': deleted {orphans.Count} orphan config asset(s).");
                }
                else
                {
                    Debug.LogWarning($"[ConfigExcelImport] '{pending.className}': {orphans.Count} orphan config asset(s) not in the Excel (kept):\n  " +
                                     string.Join("\n  ", orphans));
                }
            }

            AssetDatabase.SaveAssets();
            warnings.Flush(pending.className);
            Debug.Log($"[ConfigExcelImport] '{pending.className}': {created} created, {updated} updated, {skipped} skipped" +
                      (orphans.Count > 0 ? $", {orphans.Count} orphan(s)" : "") + $" in {folder}");
        }

        /// <summary>建一次 ID → 资产路径索引（整表只扫一遍，避免逐行 FindAssets）。</summary>
        private static Dictionary<long, string> BuildIdIndex(Type type, string folder)
        {
            var index = new Dictionary<long, string>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{type.Name}", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset != null && asset.GetType() == type
                    && ConfigIdService.TryReadId(asset, out var id, out _) && !index.ContainsKey(id))
                    index.Add(id, path);
            }
            return index;
        }

        // ---------- 编译后自动应用 ----------

        /// <summary>
        /// 应用全部待处理导入（类已编译出来的）。由 <see cref="ConfigExcelPendingApplier"/> 在
        /// 编辑器域加载后调用；类还没编译出来的载荷继续等待下次加载。
        /// </summary>
        public static void ApplyPendingImports()
        {
            var paths = AssetDatabase.FindAssets("t:ConfigExcelPendingImport")
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
            if (paths.Count == 0)
                return;

            var applied = 0;
            var waiting = 0;
            foreach (var path in paths)
            {
                ConfigExcelPendingImport pending;
                try
                {
                    pending = AssetDatabase.LoadAssetAtPath<ConfigExcelPendingImport>(path);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ConfigExcelImport] Failed to load pending import {path}: {e.Message}");
                    continue;
                }
                if (pending == null)
                    continue;

                var type = FindGeneratedType(pending.className);
                if (type == null)
                {
                    waiting++;
                    continue;
                }
                try
                {
                    if (pending.kind == PendingKind.Reconcile)
                        ReconcilePending(pending, type);
                    else
                        ApplyPending(pending, type);
                    AssetDatabase.DeleteAsset(path);
                    applied++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ConfigExcelImport] Failed to apply pending import {path}: {e.Message}\n{e.StackTrace}");
                }
            }

            if (applied > 0)
                Debug.Log($"[ConfigExcelImport] Pending import(s) applied: {applied} table(s) — config assets created.");
            if (waiting > 0)
                Debug.Log($"[ConfigExcelImport] {waiting} pending import(s) still waiting for the generated code to compile.");
        }

        private static void ApplyPending(ConfigExcelPendingImport pending, Type type)
        {
            var folder = ConfigAssetLayout.EnsureTypeFolder(type) ?? ConfigDataSettings.RootFolder;
            var warnings = new ImportWarnings();
            var created = 0;
            var skipped = 0;

            for (var r = 0; r < pending.rows.Count; r++)
            {
                var row = pending.rows[r];
                if (row?.cells == null)
                    continue;

                var assetName = AssetNameForRow(pending, row.cells, r);
                var fullPath = $"{folder}/{SanitizeFileName(assetName)}.asset";
                if (File.Exists(fullPath))
                {
                    skipped++; // 上次部分应用过：幂等重试不重复创建
                    continue;
                }

                var asset = ConfigAssetFactory.Create(type, assetName, folder);
                FillRow(asset, type, pending.fields, row.cells, warnings);
                created++;
            }

            AssetDatabase.SaveAssets();
            warnings.Flush(pending.className);
            Debug.Log($"[ConfigExcelImport] '{pending.className}': {created} config asset(s) created" +
                      (skipped > 0 ? $", {skipped} skipped (already existed)" : "") + $" in {folder}");
        }

        /// <summary>配置资产名：优先取第一个字符串字段的值，否则 类名_序号。</summary>
        private static string AssetNameForRow(ConfigExcelPendingImport pending, List<string> cells, int rowIndex)
        {
            for (var i = 0; i < pending.fields.Count; i++)
            {
                var f = pending.fields[i];
                if (f != null && !f.isList && f.type == ConfigFieldType.String
                    && i < cells.Count && !string.IsNullOrWhiteSpace(cells[i]))
                    return cells[i].Trim();
            }
            return $"{pending.className}_{rowIndex + 1:D3}";
        }

        private static void FillRow(object asset, Type type, List<PendingFieldDef> fields, List<string> cells, ImportWarnings warnings)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (f == null || string.IsNullOrEmpty(f.name))
                    continue;
                var fi = type.GetField(f.name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (fi == null)
                {
                    warnings.Add($"field '{f.name}' not found on {type.Name} (template changed after queuing?)");
                    continue;
                }
                var cell = i < cells.Count ? cells[i] : "";
                try
                {
                    SetCell(asset, fi, f.type, f.isList, f.typeArg, cell, warnings);
                }
                catch (Exception e)
                {
                    warnings.Add($"field '{f.name}' value '{cell}' rejected: {e.Message}");
                }
            }
            EditorUtility.SetDirty((ScriptableObject)asset);
        }

        // ---------- 单元格 → 字段值 ----------

        /// <summary>写一个单元格的值到字段（列表按 <see cref="ConfigExcelSchema.ListSeparator"/> 拆分）。</summary>
        internal static void SetCell(object asset, System.Reflection.FieldInfo fi,
            ConfigFieldType type, bool isList, string typeArg, string cell, ImportWarnings warnings)
        {
            if (isList)
            {
                var list = (IList)Activator.CreateInstance(fi.FieldType);
                foreach (var part in SplitList(cell))
                {
                    object element;
                    if (TryParseElement(type, typeArg, part, fi.FieldType, warnings, out element))
                        list.Add(element);
                }
                fi.SetValue(asset, list);
                return;
            }
            object value;
            if (TryParseElement(type, typeArg, cell, fi.FieldType, warnings, out value))
                fi.SetValue(asset, value);
        }

        private static string[] SplitList(string cell)
        {
            if (string.IsNullOrEmpty(cell))
                return Array.Empty<string>();
            return cell.Split(ConfigExcelSchema.ListSeparator);
        }

        /// <summary>
        /// 解析单个元素文本为字段类型的值。<paramref name="ownerType"/> 是列表元素类型
        /// （标量时即字段类型），Enum/Reference/资源引用靠它解析。
        /// </summary>
        private static bool TryParseElement(ConfigFieldType type, string typeArg, string text, Type ownerType,
            ImportWarnings warnings, out object value)
        {
            value = null;
            var s = (text ?? "").Trim();

            switch (type)
            {
                case ConfigFieldType.String:
                case ConfigFieldType.LocalizedKey:
                case ConfigFieldType.Addressable:
                case ConfigFieldType.AudioEvent:
                    value = s;
                    return true;
                case ConfigFieldType.Bool:
                    if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
                        || s == "1" || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase))
                    {
                        value = true;
                        return true;
                    }
                    if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)
                        || s == "0" || string.Equals(s, "no", StringComparison.OrdinalIgnoreCase))
                    {
                        value = false;
                        return true;
                    }
                    return false;
                case ConfigFieldType.Char:
                    value = s.Length > 0 ? s[0] : default(char);
                    return true;
                case ConfigFieldType.Int:
                    return TryNum(s, v => Convert.ToInt32(v, CultureInfo.InvariantCulture), out value);
                case ConfigFieldType.Long:
                case ConfigFieldType.BehaviourTree:
                    return TryNum(s, v => Convert.ToInt64(v, CultureInfo.InvariantCulture), out value);
                case ConfigFieldType.Short:
                    return TryNum(s, v => Convert.ToInt16(v, CultureInfo.InvariantCulture), out value);
                case ConfigFieldType.Byte:
                    return TryNum(s, v => Convert.ToByte(v, CultureInfo.InvariantCulture), out value);
                case ConfigFieldType.UInt:
                    return TryNum(s, v => Convert.ToUInt32(v, CultureInfo.InvariantCulture), out value);
                case ConfigFieldType.Float:
                    return TryNum(s, v => Convert.ToSingle(v, CultureInfo.InvariantCulture), out value);
                case ConfigFieldType.Double:
                    return TryNum(s, v => Convert.ToDouble(v, CultureInfo.InvariantCulture), out value);
                case ConfigFieldType.Decimal:
                    return TryNum(s, v => Convert.ToDecimal(v, CultureInfo.InvariantCulture), out value);
                case ConfigFieldType.Enum:
                    try
                    {
                        value = Enum.Parse(ownerType, s, ignoreCase: true);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                case ConfigFieldType.Reference:
                    return TryResolveReference(typeArg, s, out value);
                case ConfigFieldType.LayerMask:
                    if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
                        return false;
                    value = (LayerMask)bits;
                    return true;
                default:
                    break;
            }

            if (ConfigFieldTypes.IsAssetRef(type))
                return TryResolveAsset(ownerType, s, out value);

            // 结构体：逐分量不变量文化解析（与导出侧 ElementText 的格式互为逆操作）
            switch (type)
            {
                case ConfigFieldType.Vector2:
                    return TryStruct(s, 2, v => new Vector2(v[0], v[1]), out value);
                case ConfigFieldType.Vector3:
                    return TryStruct(s, 3, v => new Vector3(v[0], v[1], v[2]), out value);
                case ConfigFieldType.Vector4:
                    return TryStruct(s, 4, v => new Vector4(v[0], v[1], v[2], v[3]), out value);
                case ConfigFieldType.Vector2Int:
                    return TryStructInt(s, 2, v => new Vector2Int((int)v[0], (int)v[1]), out value);
                case ConfigFieldType.Vector3Int:
                    return TryStructInt(s, 3, v => new Vector3Int((int)v[0], (int)v[1], (int)v[2]), out value);
                case ConfigFieldType.Quaternion:
                    return TryStruct(s, 4, v => new Quaternion(v[0], v[1], v[2], v[3]), out value);
                case ConfigFieldType.Color:
                    return TryStruct(s, 4, v => new Color(v[0], v[1], v[2], v[3]), out value);
                case ConfigFieldType.Color32:
                    return TryStructInt(s, 4, v => new Color32((byte)v[0], (byte)v[1], (byte)v[2], (byte)v[3]), out value);
                case ConfigFieldType.Rect:
                    return TryStruct(s, 4, v => new Rect(v[0], v[1], v[2], v[3]), out value);
                case ConfigFieldType.RectInt:
                    return TryStructInt(s, 4, v => new RectInt((int)v[0], (int)v[1], (int)v[2], (int)v[3]), out value);
                case ConfigFieldType.RectOffset:
                    return TryStructInt(s, 4, v => new RectOffset((int)v[0], (int)v[1], (int)v[2], (int)v[3]), out value);
                case ConfigFieldType.Bounds:
                    return TryStruct(s, 6, v => new Bounds(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5])), out value);
                case ConfigFieldType.BoundsInt:
                    return TryStructInt(s, 6, v => new BoundsInt(new Vector3Int((int)v[0], (int)v[1], (int)v[2]), new Vector3Int((int)v[3], (int)v[4], (int)v[5])), out value);
                case ConfigFieldType.Gradient:
                case ConfigFieldType.AnimationCurve:
                    warnings.AddOnce($"{type}", $"{type} cannot round-trip through Excel — fill it in the data editor.");
                    return false;
                default:
                    return false;
            }
        }

        private static bool TryNum<T>(string s, Func<string, T> convert, out object value)
        {
            value = null;
            if (s.Length == 0)
                return false;
            try
            {
                value = convert(s);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>解析 "x, y, z"（容忍外层括号）为 float 分量并构造结构体。</summary>
        private static bool TryStruct(string s, int count, Func<float[], object> build, out object value)
        {
            value = null;
            if (!TryParseComponents(s, count, out var comps))
                return false;
            value = build(comps);
            return true;
        }

        private static bool TryStructInt(string s, int count, Func<long[], object> build, out object value)
        {
            value = null;
            if (!TryParseIntComponents(s, count, out var comps))
                return false;
            value = build(comps);
            return true;
        }

        private static bool TryParseComponents(string s, int count, out float[] values)
        {
            values = null;
            var t = (s ?? "").Trim();
            if (t.StartsWith("(") && t.EndsWith(")"))
                t = t.Substring(1, t.Length - 2);
            var parts = t.Split(',');
            if (parts.Length < count)
                return false;
            values = new float[count];
            for (var i = 0; i < count; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return false;
            }
            return true;
        }

        private static bool TryParseIntComponents(string s, int count, out long[] values)
        {
            values = null;
            var t = (s ?? "").Trim();
            if (t.StartsWith("(") && t.EndsWith(")"))
                t = t.Substring(1, t.Length - 2);
            var parts = t.Split(',');
            if (parts.Length < count)
                return false;
            values = new long[count];
            for (var i = 0; i < count; i++)
            {
                if (!long.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                    return false;
            }
            return true;
        }

        // ---------- 引用 / 资源解析 ----------

        /// <summary>按 ID 找被引用的配置资产（编辑器态 Reference 字段存的是资产对象）。</summary>
        private static bool TryResolveReference(string refShortName, string idText, out object value)
        {
            value = null;
            if (string.IsNullOrEmpty(refShortName) || string.IsNullOrEmpty(idText))
                return false;
            if (!long.TryParse(idText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return false;

            var refType = FindGeneratedType(refShortName);
            if (refType == null)
                return false;

            foreach (var guid in AssetDatabase.FindAssets($"t:{refShortName}", new[] { ConfigDataSettings.RootFolder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.GetType() == refType
                    && ConfigIdService.TryReadId(asset, out var aid, out _) && aid == id)
                {
                    value = asset;
                    return true;
                }
            }
            return false;
        }

        /// <summary>按资产名找资源引用（导出侧写的是 name；同名取第一个精确类型匹配）。</summary>
        private static bool TryResolveAsset(Type fieldType, string name, out object value)
        {
            value = null;
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (var guid in AssetDatabase.FindAssets(name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(path);
                if (asset != null && fieldType.IsInstanceOfType(asset))
                {
                    value = asset;
                    return true;
                }
            }
            return false;
        }

        // ---------- 工具 ----------

        /// <summary>按类名找已编译的生成类型（命名空间 root 切换安全：按当前 ns 拼全名）。</summary>
        public static Type FindGeneratedType(string className)
        {
            if (string.IsNullOrEmpty(className))
                return null;
            var fullName = ConfigDataSettings.Namespace + "." + className;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    continue; // 含未加载依赖的程序集：跳过（生成类型所在程序集无此问题）
                }
                foreach (var t in types)
                {
                    if (t.FullName == fullName && typeof(ConfigBase).IsAssignableFrom(t))
                        return t;
                }
            }
            return null;
        }

        /// <summary>类名是否已被某 generateCode 模板占用。</summary>
        public static bool IsKnownConfigClass(string className)
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

        private static string UniqueAssetPath(string folder, string name)
        {
            var path = $"{folder}/{SanitizeFileName(name)}.asset";
            var index = 1;
            while (File.Exists(path))
            {
                path = $"{folder}/{SanitizeFileName(name)} {index}.asset";
                index++;
            }
            return path;
        }

        /// <summary>与 <see cref="ConfigAssetFactory"/> 的文件名净化保持一致（重复判定需要同一路径算法）。</summary>
        private static string SanitizeFileName(string name)
        {
            var s = (name ?? "").Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c.ToString(), "_");
            return string.IsNullOrEmpty(s) ? "Config" : s;
        }
    }

    /// <summary>导入告警收集（按 key 去重，结束统一落 Console，避免逐行刷屏）。</summary>
    internal sealed class ImportWarnings
    {
        private readonly Dictionary<string, string> _seen = new Dictionary<string, string>();

        public void Add(string message) => Add(message, message);

        public void AddOnce(string key, string message)
        {
            if (!_seen.ContainsKey(key))
                _seen.Add(key, message);
        }

        public void Flush(string context)
        {
            foreach (var kv in _seen)
                Debug.LogWarning($"[ConfigExcelImport] {context}: {kv.Value}");
        }
    }

    /// <summary>
    /// 待处理导入的自动应用器：编辑器域加载后（含编译完成后的域重载）尝试落数据。
    /// 生成类还没编译出来的载荷继续等待下一次域加载，不报错、不丢失。
    /// </summary>
    [InitializeOnLoad]
    internal static class ConfigExcelPendingApplier
    {
        static ConfigExcelPendingApplier() => ConfigExcelImporter.ApplyPendingImports();
    }
}
