namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>
    /// Unity CLI / -executeMethod 请求参数。AI 或 CI 先写 cli-request.json，再调无参静态方法。
    /// 字段全部可选：留空则回落到 HYC Settings 里的项目级配置；
    /// Scope 留空时使用所调命令自带的导出范围。
    /// </summary>
    [System.Serializable]
    public sealed class ContentExportCLIRequest
    {
        public string Scope = "";
        public string NodeId = "";
        public string EntryId = "";
        public string SourceFolder = "";
        public string TargetFolder = "";
        public string Version = "";
        public string Channel = "";
        public string DefaultSetName = "";
        public string SwitchPointer = "";
        public string FullCatalogRelativePath = "";
        public string RollbackVersion = "";
        public System.Collections.Generic.List<string> RemovedKeys =
            new System.Collections.Generic.List<string>();
    }

    /// <summary>
    /// 给 AI / CI 的静态入口。全部方法都是 public static void 无参，可直接用于
    /// -executeMethod HYC.Framework.ContentExport.Editor.ContentExportCLI.&lt;Method&gt;。
    /// 每次调用都把结构化结果写到 Assets/HYC/ContentExport/last-report.json。
    /// </summary>
    public static class ContentExportCLI
    {
        public const string RequestFileName = "cli-request.json";

        public static string RequestFilePath
        {
            get
            {
                return System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    ContentExportGroups.RelativeFolder.Replace('/', System.IO.Path.DirectorySeparatorChar),
                    RequestFileName);
            }
        }

        public static ContentExportCLIRequest LoadRequest()
        {
            var path = RequestFilePath;
            if (!System.IO.File.Exists(path))
                return new ContentExportCLIRequest();
            try
            {
                var text = System.IO.File.ReadAllText(path).TrimStart('\uFEFF');
                return Newtonsoft.Json.JsonConvert.DeserializeObject<ContentExportCLIRequest>(text)
                       ?? new ContentExportCLIRequest();
            }
            catch (System.Exception e)
            {
                ContentExportLog.Add(ContentExportLogLevel.Warning, "cli-request.json 解析失败，使用默认值：" + e.Message);
                return new ContentExportCLIRequest();
            }
        }

        public static void Help()
        {
            var report = NewReport("help");
            report.Success = true;
            report.Message = "可用命令（-executeMethod 全名前缀 HYC.Framework.ContentExport.Editor.ContentExportCLI.）";
            report.Steps.Add("Help · 列出命令");
            report.Steps.Add("VerifyLayout · 镜像布局自检，不一致时自动更新");
            report.Steps.Add("WriteStatus · 输出 Groups / 版本 / catalog 状态");
            report.Steps.Add("ExportAll · 全量导出");
            report.Steps.Add("ExportSelected · 只导勾选条目");
            report.Steps.Add("ExportGroup · 只导指定文件夹（request.NodeId）");
            report.Steps.Add("ExportEntry · 只导单个条目（request.EntryId）");
            report.Steps.Add("DryRunDiff · 只算差异并记录，不切换 catalogs.bin");
            report.Steps.Add("SwitchPointer · 把 catalogs.bin 指向 request.FullCatalogRelativePath（先上传后上线）");
            report.Steps.Add("Rollback · 把 catalogs.bin 指回 request.RollbackVersion");
            report.Steps.Add("请求文件：" + RequestFilePath);
            report.Steps.Add("结果文件：" + ContentExportReportWriter.FilePath);
            Finish(report);
        }

        public static void VerifyLayout()
        {
            var report = NewReport("verify-layout");
            try
            {
                var layout = ContentExportLayoutCheck.EnsureLayout();
                report.LayoutFingerprint = layout.OfficialFingerprint;
                report.LayoutMessage = layout.Message;
                report.Success = layout.Matched;
                report.Message = layout.Message;
                report.Steps.Add("官方指纹 " + layout.OfficialFingerprint);
                report.Steps.Add("镜像指纹 " + layout.MirrorFingerprint);
            }
            catch (System.Exception e)
            {
                Fail(report, e);
            }
            Finish(report);
        }

        public static void WriteStatus()
        {
            var report = NewReport("status");
            try
            {
                var doc = ContentExportGroups.Load();
                var releases = ContentExportReleases.Load();
                var layout = ContentExportLayoutCheck.Verify();
                var target = ContentExportSettings.ResolveProjectPath(ContentExportSettings.TargetFolder);
                var baselinePath = ContentCatalogIO.ResolveFullCatalogPath(target);
                var baseline = string.IsNullOrEmpty(baselinePath)
                    ? null
                    : ContentCatalogIO.ReadCatalog(baselinePath);

                report.LayoutFingerprint = layout.OfficialFingerprint;
                report.LayoutMessage = layout.Message;
                report.Success = true;
                report.Message = "状态读取完成";
                report.Steps.Add("顶层 Groups：" + doc.Groups.Count);
                report.Steps.Add("条目总数：" + CountEntries(doc));
                report.Steps.Add("当前版本：" + (string.IsNullOrEmpty(releases.CurrentVersion) ? "无" : releases.CurrentVersion));
                report.Steps.Add("发布记录数：" + releases.Records.Count);
                report.Steps.Add("发布目录：" + target);
                report.Steps.Add("基线 catalog：" + (baseline == null ? "无" : baseline.Count + " 条"));
                report.Steps.Add("镜像布局：" + (layout.Matched ? "一致" : "漂移"));
            }
            catch (System.Exception e)
            {
                Fail(report, e);
            }
            Finish(report);
        }

        public static void ExportAll()
        {
            RunExport("export-all", ContentExportScopeKind.All, false);
        }

        public static void ExportSelected()
        {
            RunExport("export-selected", ContentExportScopeKind.Selected, false);
        }

        public static void ExportGroup()
        {
            RunExport("export-group", ContentExportScopeKind.Group, false);
        }

        public static void ExportEntry()
        {
            RunExport("export-entry", ContentExportScopeKind.Entry, false);
        }

        public static void DryRunDiff()
        {
            RunExport("dry-run-diff", ContentExportScopeKind.Selected, true);
        }

        // ---- 窗口直连：只走当前 HYC Settings，不读 cli-request.json ----
        // CLI 的 ExportAll / ExportSelected / DryRunDiff 会用请求文件覆盖目录、版本等选项，
        // 窗口内点击不应受工程里残留的 CI 请求文件影响。

        public static void ExportAllNow()
        {
            RunExportScope("export-all", ContentExportScopeKind.All, "", "", false, null);
        }

        public static void ExportSelectedNow()
        {
            RunExportScope("export-selected", ContentExportScopeKind.Selected, "", "", false, null);
        }

        public static void DryRunDiffNow()
        {
            RunExportScope("dry-run-diff", ContentExportScopeKind.Selected, "", "", true, null);
        }

        /// <summary>分阶段发布的「上线」一步：CDN 上传完成后显式切 catalogs.bin，不改动内容文件。</summary>
        public static void SwitchPointer()
        {
            var report = NewReport("switch-pointer");
            try
            {
                var request = LoadRequest();
                if (string.IsNullOrWhiteSpace(request.FullCatalogRelativePath))
                    throw new System.Exception("request.FullCatalogRelativePath 未配置。");

                var target = string.IsNullOrEmpty(request.TargetFolder)
                    ? ContentExportSettings.ResolveProjectPath(ContentExportSettings.TargetFolder)
                    : ContentExportSettings.ResolveProjectPath(request.TargetFolder);
                var relative = request.FullCatalogRelativePath.Replace('\\', '/');
                var ok = ContentExportPipeline.SwitchPointerTo(target, relative);
                report.Success = ok;
                report.Message = ok ? "catalogs.bin 已指向 " + relative : "切换失败，详见 Console";
                report.FullCatalogRelativePath = relative;
                report.Steps.Add("catalogs.bin → " + relative);
            }
            catch (System.Exception e)
            {
                Fail(report, e);
            }
            Finish(report);
        }

        public static void Rollback()
        {
            RunRollback(LoadRequest());
        }

        /// <summary>窗口内「回滚至此版本」：直接按版本号回滚，不读写 cli-request.json。</summary>
        public static void RollbackTo(string version)
        {
            var request = new ContentExportCLIRequest { RollbackVersion = version };
            RunRollback(request);
        }

        /// <summary>窗口内「切换指针」：直接按相对路径切换，不读写 cli-request.json。</summary>
        public static void SwitchPointerToPath(string relativePath)
        {
            var report = NewReport("switch-pointer");
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    throw new System.Exception("relativePath 未配置。");
                var target = ContentExportSettings.ResolveProjectPath(ContentExportSettings.TargetFolder);
                var ok = ContentExportPipeline.SwitchPointerTo(target, relativePath.Replace('\\', '/'));
                report.Success = ok;
                report.Message = ok ? "catalogs.bin 已指向 " + relativePath : "切换失败，详见 Console";
                report.FullCatalogRelativePath = relativePath;
                report.Steps.Add("catalogs.bin → " + relativePath);
            }
            catch (System.Exception e)
            {
                Fail(report, e);
            }
            Finish(report);
        }

        private static void RunRollback(ContentExportCLIRequest request)
        {
            var report = NewReport("rollback");
            try
            {
                if (request == null || string.IsNullOrEmpty(request.RollbackVersion))
                    throw new System.Exception("request.RollbackVersion 未配置。");
                var record = ContentExportReleases.Find(request.RollbackVersion);
                if (record == null)
                    throw new System.Exception("找不到版本记录：" + request.RollbackVersion);

                var target = string.IsNullOrEmpty(request.TargetFolder)
                    ? ContentExportSettings.ResolveProjectPath(ContentExportSettings.TargetFolder)
                    : ContentExportSettings.ResolveProjectPath(request.TargetFolder);
                var ok = ContentExportPipeline.SwitchPointerTo(target, record.FullCatalogRelativePath);
                report.Success = ok;
                report.Message = ok ? "已回滚到 " + record.Version : "回滚失败，详见 Console";
                report.FullCatalogRelativePath = record.FullCatalogRelativePath;
                report.EntryCount = record.EntryCount;
                report.Steps.Add("catalogs.bin → " + record.FullCatalogRelativePath);
            }
            catch (System.Exception e)
            {
                Fail(report, e);
            }
            Finish(report);
        }

        /// <summary>窗口内右键「导出这个组」：直接按节点 ID 走当前 HYC Settings 导出，不读写 cli-request.json。</summary>
        public static void ExportGroupById(string nodeId)
        {
            RunExportScope("export-group", ContentExportScopeKind.Group, nodeId, "", false, null);
        }

        /// <summary>窗口内右键「导出这条」：直接按条目 ID 走当前 HYC Settings 导出，不读写 cli-request.json。</summary>
        public static void ExportEntryById(string entryId)
        {
            RunExportScope("export-entry", ContentExportScopeKind.Entry, "", entryId, false, null);
        }

        private static void RunExport(string command, ContentExportScopeKind kind, bool dryRun)
        {
            var request = LoadRequest();
            RunExportScope(command, kind, request.NodeId, request.EntryId, dryRun, request);
        }

        /// <summary>
        /// 统一导出入口。文件驱动的命令（ExportAll/Selected/Group/Entry）从 cli-request.json 读
        /// 范围与文件夹覆盖；窗口内右键触发的 ExportGroupById/ExportEntryById 传 null request，
        /// 直接走当前 HYC Settings，不做文件覆盖。
        /// </summary>
        private static void RunExportScope(string command, ContentExportScopeKind kind,
            string nodeId, string entryId, bool dryRun, ContentExportCLIRequest request)
        {
            var report = NewReport(command);
            try
            {
                var layout = ContentExportLayoutCheck.EnsureLayout();
                report.LayoutFingerprint = layout.OfficialFingerprint;
                report.LayoutMessage = layout.Message;
                if (!layout.Matched)
                    throw new System.Exception("镜像布局校验未通过：" + layout.Message);

                var scope = new ContentExportScopeRequest
                {
                    Kind = request != null ? ParseScope(request.Scope, kind) : kind,
                    NodeId = nodeId,
                    EntryId = entryId
                };
                var options = ContentExportSettings.CreateOptions(scope);
                if (request != null)
                    ApplyRequest(options, request);
                if (dryRun)
                {
                    options.SwitchPointer = false;
                    options.Version = "";
                }

                var result = ContentExportPipeline.Run(options);
                FillFromResult(report, result);
                if (dryRun)
                    report.Steps.Add("DryRun：未切换 catalogs.bin，未写发布记录");
            }
            catch (System.Exception e)
            {
                Fail(report, e);
            }
            Finish(report);
        }

        /// <summary>请求里的 Scope 可覆盖命令默认范围；非法值回落，不打断 CI。</summary>
        private static ContentExportScopeKind ParseScope(string value, ContentExportScopeKind fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            switch (value.Trim().ToLowerInvariant())
            {
                case "all": return ContentExportScopeKind.All;
                case "selected": return ContentExportScopeKind.Selected;
                case "group": return ContentExportScopeKind.Group;
                case "entry": return ContentExportScopeKind.Entry;
                default: return fallback;
            }
        }

        private static void ApplyRequest(ContentExportOptions options, ContentExportCLIRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.SourceFolder))
                options.SourceFolder = ContentExportSettings.ResolveProjectPath(request.SourceFolder);
            if (!string.IsNullOrWhiteSpace(request.TargetFolder))
                options.TargetFolder = ContentExportSettings.ResolveProjectPath(request.TargetFolder);
            if (!string.IsNullOrWhiteSpace(request.Version))
                options.Version = request.Version;
            if (!string.IsNullOrWhiteSpace(request.Channel))
                options.Channel = request.Channel;
            if (!string.IsNullOrWhiteSpace(request.DefaultSetName))
                options.DefaultSetName = request.DefaultSetName;
            if (!string.IsNullOrWhiteSpace(request.SwitchPointer))
            {
                bool switchPointer;
                if (bool.TryParse(request.SwitchPointer.Trim(), out switchPointer))
                    options.SwitchPointer = switchPointer;
            }
            if (request.RemovedKeys != null)
            {
                for (var i = 0; i < request.RemovedKeys.Count; i++)
                    options.RemovedKeys.Add(request.RemovedKeys[i]);
            }
        }

        private static void FillFromResult(ContentExportReport report, ContentExportResult result)
        {
            report.Success = result.Success;
            report.Message = result.Message;
            report.CatalogPath = result.CatalogPath;
            report.FullCatalogRelativePath = result.FullCatalogRelativePath;
            report.EntryCount = result.EntryCount;
            report.Steps.AddRange(result.Steps);
            report.NewHashFiles.AddRange(result.NewHashFiles);
            report.NewHashFileCount = result.NewHashFiles.Count;
            if (result.Merge == null)
                return;
            report.Added = result.Merge.Diff.AddedCount;
            report.Removed = result.Merge.Diff.RemovedCount;
            report.Changed = result.Merge.Diff.ChangedCount;
            report.Unchanged = result.Merge.Diff.UnchangedCount;
            report.OrphanFiles.AddRange(result.Merge.Diff.OrphanFiles);
            var diff = result.Merge.Diff;
            if (diff != null)
            {
                report.MergeOverwritten = result.Merge.Overwritten;
                report.MergeAdded = result.Merge.Added;
                report.MergeKept = result.Merge.Kept;
                report.MergeRemoved = result.Merge.Removed;
                report.MergeReindexed = result.Merge.ReindexedSets;
                for (var i = 0; i < diff.Entries.Count; i++)
                {
                    var e = diff.Entries[i];
                    switch (e.Kind)
                    {
                        case ContentExportDiffKind.Added: report.DiffAdded.Add(e.Name); break;
                        case ContentExportDiffKind.Removed: report.DiffRemoved.Add(e.Name); break;
                        case ContentExportDiffKind.Changed: report.DiffChanged.Add(e.Name); break;
                        default: report.DiffUnchanged.Add(e.Name); break;
                    }
                }
            }
        }

        private static ContentExportReport NewReport(string command)
        {
            return new ContentExportReport
            {
                Command = command,
                Timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private static void Fail(ContentExportReport report, System.Exception e)
        {
            report.Success = false;
            report.Message = e.Message;
            report.Errors.Add(e.ToString());
            ContentExportLog.Add(ContentExportLogLevel.Error, e.Message);
        }

        private static void Finish(ContentExportReport report)
        {
            ContentExportReportWriter.Write(report);
            ContentExportReportWriter.LogToConsole(report);
        }

        private static int CountEntries(ContentExportGroupsDocument document)
        {
            if (document == null)
                return 0;
            var total = 0;
            for (var i = 0; i < document.Groups.Count; i++)
                total += CountNodeEntries(document.Groups[i]);
            return total;
        }

        private static int CountNodeEntries(ContentExportGroupNode node)
        {
            var total = node.Entries.Count;
            for (var i = 0; i < node.Children.Count; i++)
                total += CountNodeEntries(node.Children[i]);
            return total;
        }
    }
}
