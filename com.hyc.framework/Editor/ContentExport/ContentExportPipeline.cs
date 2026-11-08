namespace HYC.Framework.ContentExport.Editor
{
    public sealed class ContentExportOptions
    {
        /// <summary>构建产物目录（BuildContent 的输出）。本工具从这里开始 publish，不负责生成产物。</summary>
        public string SourceFolder = "";
        /// <summary>发布根目录（CDN 镜像 / 本地缓存结构），内容 hash 文件只追加不覆盖。</summary>
        public string TargetFolder = "";
        public ContentExportScopeRequest Scope = new ContentExportScopeRequest();
        public string DefaultSetName = "all";
        public bool SwitchPointer = true;
        public string Version = "";
        public string Channel = "stable";
        public int RolloutPercent = 100;
        public string CompatibleSince = "";
        /// <summary>配置里已明确移除的逻辑 ID；只有这些才从 catalog 真删条目。</summary>
        public System.Collections.Generic.HashSet<string> RemovedKeys =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
    }

    /// <summary>
    /// 一次导出：布局自检 → 官方 PublishContent 到临时目录 → 与基线合并 → 追加新 hash 文件 →
    /// 写新 full catalog → 最后切 catalogs.bin。PublishContent 的私有 catalog 写入函数由
    /// ContentCatalogIO 的镜像 struct 复刻，故官方 catalogs.bin 在这里会被合并结果覆盖。
    /// </summary>
    public static class ContentExportPipeline
    {
        public const string TempFolderName = "__hyc_publish_tmp";
        public const string TempCatalogName = "__hyc_full_catalog.tmp";

        public static ContentExportResult Run(ContentExportOptions options)
        {
            ContentExportLog.Clear();
            if (options == null)
                return ContentExportResult.Fail("导出选项为空。");

            var source = NormalizeDir(options.SourceFolder);
            var target = NormalizeDir(options.TargetFolder);
            if (string.IsNullOrEmpty(source) || !System.IO.Directory.Exists(source))
                return ContentExportResult.Fail("构建产物目录不存在：" + options.SourceFolder);
            if (string.IsNullOrEmpty(target))
                return ContentExportResult.Fail("发布目录未配置。");
            System.IO.Directory.CreateDirectory(target);

            var layout = ContentExportLayoutCheck.EnsureLayout();
            if (!layout.Matched)
                return ContentExportResult.Fail("镜像布局校验未通过：" + layout.Message);

            var result = ContentExportResult.Ok("导出完成");
            result.Steps.Add("镜像布局自检通过 · " + layout.OfficialFingerprint);

            var baselinePath = ContentCatalogIO.ResolveFullCatalogPath(target);
            var baseline = string.IsNullOrEmpty(baselinePath)
                ? null
                : ContentCatalogIO.ReadCatalog(baselinePath);
            result.Steps.Add(baseline == null ? "首次发布，无基线 catalog" : "基线 catalog：" + baseline.Count + " 条");

            var temp = System.IO.Path.Combine(target, TempFolderName);
            DeleteTemp(temp);

            var document = ContentExportGroups.Load();
            var prefixes = ContentExportGroups.ResolveScope(document, options.Scope);
            var rules = CollectSetRules(document);
            System.Func<string, System.Collections.Generic.IEnumerable<string>> contentSetFunc = relative =>
            {
                var normalized = (relative ?? "").Replace('\\', '/');
                if (!ContentExportGroups.MatchesScope(normalized, prefixes))
                    return null;
                return new string[] { ResolveSetName(rules, normalized, options.DefaultSetName) };
            };

            if (!Unity.Entities.Content.RemoteContentCatalogBuildUtility.PublishContent(
                    source, temp, contentSetFunc))
            {
                DeleteTemp(temp);
                return ContentExportResult.Fail("PublishContent 失败，详见 Console。");
            }
            result.Steps.Add("PublishContent 完成 · Scope " +
                             (prefixes.Count == 0 ? "全量" : prefixes.Count + " 个前缀"));

            var currentPath = ContentCatalogIO.ResolveFullCatalogPath(temp);
            if (string.IsNullOrEmpty(currentPath))
            {
                DeleteTemp(temp);
                return ContentExportResult.Fail("本次 catalog 生成失败（catalogs.bin 无法解析）。");
            }
            var current = ContentCatalogIO.ReadCatalog(currentPath);
            if (current == null)
            {
                DeleteTemp(temp);
                return ContentExportResult.Fail("本次 full catalog 读取失败。");
            }

            var merge = ContentCatalogMerger.Merge(baseline, current, options.RemovedKeys);
            result.Merge = merge;
            result.Steps.Add("合并：覆盖 " + merge.Overwritten + " / 新增 " + merge.Added +
                             " / 保留 " + merge.Kept + " / 移除 " + merge.Removed +
                             "，重算 " + merge.ReindexedSets + " 个 content set");

            for (var i = 0; i < current.Entries.Count; i++)
            {
                var relative = current.Entries[i].LocationPath;
                var src = System.IO.Path.Combine(temp,
                    relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
                var dst = System.IO.Path.Combine(target,
                    relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (!System.IO.File.Exists(src) || System.IO.File.Exists(dst))
                    continue;
                var folder = System.IO.Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(folder))
                    System.IO.Directory.CreateDirectory(folder);
                System.IO.File.Copy(src, dst, false);
                result.NewHashFiles.Add(relative);
            }
            result.Steps.Add("追加 " + result.NewHashFiles.Count + " 个新 hash 文件，旧文件全部保留");

            var tempCatalog = System.IO.Path.Combine(target, TempCatalogName);
            if (!ContentCatalogIO.WriteCatalog(tempCatalog, merge.Merged))
            {
                DeleteTemp(temp);
                return ContentExportResult.Fail("写新 full catalog 失败。");
            }

            var bytes = System.IO.File.ReadAllBytes(tempCatalog);
            var hash = UnityEngine.Hash128.Compute(bytes);
            var relativeCatalogPath = ContentCatalogIO.HashRelativePath(bytes);
            var destination = System.IO.Path.Combine(target,
                relativeCatalogPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var destinationFolder = System.IO.Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationFolder))
                System.IO.Directory.CreateDirectory(destinationFolder);
            if (!System.IO.File.Exists(destination))
                System.IO.File.Copy(tempCatalog, destination, false);
            System.IO.File.Delete(tempCatalog);

            result.CatalogPath = destination;
            result.FullCatalogRelativePath = relativeCatalogPath;
            result.EntryCount = merge.Merged.Count;

            if (options.SwitchPointer)
            {
                if (!ContentCatalogIO.WriteCatalogPointer(target, relativeCatalogPath, hash, bytes.LongLength))
                {
                    DeleteTemp(temp);
                    return ContentExportResult.Fail("切换 catalogs.bin 失败。");
                }
                result.Steps.Add("catalogs.bin 已指向 " + relativeCatalogPath);

                if (!string.IsNullOrEmpty(options.Version))
                {
                    ContentExportReleases.Add(new ContentExportReleaseRecord
                    {
                        Version = options.Version,
                        Channel = options.Channel,
                        FullCatalogRelativePath = relativeCatalogPath,
                        FullCatalogHash = hash.ToString(),
                        EntryCount = merge.Merged.Count,
                        RolloutPercent = options.RolloutPercent,
                        CompatibleSince = options.CompatibleSince,
                        CreatedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
            }
            else
            {
                result.Steps.Add("未切换 catalogs.bin（由发布流程显式切换）");
            }

            merge.Diff.OrphanFiles = ContentCatalogIO.FindOrphanFiles(target, merge.Merged);
            var missing = VerifyLocalIntegrity(target, merge.Merged);
            if (missing.Count > 0)
                ContentExportLog.Add(ContentExportLogLevel.Warning,
                    "catalog 引用了 " + missing.Count + " 个本地缺失文件，切换指针前请确认 CDN 已上传。");

            DeleteTemp(temp);
            ContentExportLog.Add(ContentExportLogLevel.Success,
                "导出完成：" + merge.Merged.Count + " 条，full catalog " + relativeCatalogPath);
            return result;
        }

        /// <summary>发布流程显式切指针 / 回滚：把 catalogs.bin 重新指向指定的 full catalog，不动内容文件。</summary>
        public static bool SwitchPointerTo(string targetFolder, string relativeCatalogPath)
        {
            var target = NormalizeDir(targetFolder);
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(relativeCatalogPath))
                return false;
            var full = System.IO.Path.Combine(target,
                relativeCatalogPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(full))
            {
                ContentExportLog.Add(ContentExportLogLevel.Error, "回滚目标不存在：" + full);
                return false;
            }
            var bytes = System.IO.File.ReadAllBytes(full);
            return ContentCatalogIO.WriteCatalogPointer(target, relativeCatalogPath,
                UnityEngine.Hash128.Compute(bytes), bytes.LongLength);
        }

        /// <summary>本地完整性校验：catalog 引用的 hash 文件是否都在发布目录里。</summary>
        public static System.Collections.Generic.List<string> VerifyLocalIntegrity(
            string targetFolder, CatalogSnapshot snapshot)
        {
            var missing = new System.Collections.Generic.List<string>();
            if (snapshot == null || string.IsNullOrEmpty(targetFolder))
                return missing;
            for (var i = 0; i < snapshot.Entries.Count; i++)
            {
                var relative = snapshot.Entries[i].LocationPath;
                var full = System.IO.Path.Combine(targetFolder,
                    relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (!System.IO.File.Exists(full))
                    missing.Add(relative);
            }
            return missing;
        }

        private static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
            CollectSetRules(ContentExportGroupsDocument document)
        {
            var rules = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
            if (document == null)
                return rules;
            for (var i = 0; i < document.Groups.Count; i++)
                CollectNodeRules(document.Groups[i], rules);
            return rules;
        }

        private static void CollectNodeRules(ContentExportGroupNode node,
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> rules)
        {
            if (!string.IsNullOrEmpty(node.SetName))
                rules.Add(new System.Collections.Generic.KeyValuePair<string, string>("", node.SetName));
            for (var i = 0; i < node.Entries.Count; i++)
            {
                var entry = node.Entries[i];
                if (!string.IsNullOrEmpty(entry.SourceRelativePath) && !string.IsNullOrEmpty(entry.SetName))
                {
                    rules.Add(new System.Collections.Generic.KeyValuePair<string, string>(
                        entry.SourceRelativePath.Replace('\\', '/'), entry.SetName));
                }
            }
            for (var i = 0; i < node.Children.Count; i++)
                CollectNodeRules(node.Children[i], rules);
        }

        private static string ResolveSetName(
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> rules,
            string relative, string fallback)
        {
            var best = "";
            var bestLength = -1;
            for (var i = 0; i < rules.Count; i++)
            {
                var prefix = rules[i].Key;
                if (prefix.Length > bestLength &&
                    relative.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    best = rules[i].Value;
                    bestLength = prefix.Length;
                }
            }
            return string.IsNullOrEmpty(best) ? fallback : best;
        }

        private static string NormalizeDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";
            return System.IO.Path.GetFullPath(path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        private static void DeleteTemp(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
                    System.IO.Directory.Delete(path, true);
            }
            catch (System.Exception e)
            {
                ContentExportLog.Add(ContentExportLogLevel.Warning, "临时目录清理失败：" + e.Message);
            }
        }
    }
}
