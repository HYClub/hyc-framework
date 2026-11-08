namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>
    /// 两代 full catalog 的合并与差异。官方 PublishContent 每次只重建本次构建目录的全量快照，
    /// 没有合并上一版 catalog 的逻辑；「只导一部分」的正确性由这里按 RemoteContentId 归并来保证。
    /// </summary>
    public static class ContentCatalogMerger
    {
        public static ContentExportMergeResult Merge(CatalogSnapshot baseline, CatalogSnapshot current,
            System.Collections.Generic.HashSet<string> removedKeys)
        {
            var baseSnapshot = baseline ?? new CatalogSnapshot();
            var currentSnapshot = current ?? new CatalogSnapshot();

            var result = new ContentExportMergeResult
            {
                Merged = new CatalogSnapshot(),
                Diff = new CatalogDiffReport()
            };

            var index = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);
            for (var i = 0; i < baseSnapshot.Entries.Count; i++)
            {
                var entry = baseSnapshot.Entries[i];
                if (removedKeys != null && removedKeys.Contains(entry.Key))
                {
                    result.Removed++;
                    continue;
                }
                index[entry.Key] = result.Merged.Entries.Count;
                result.Merged.Entries.Add(entry.Clone());
            }

            for (var i = 0; i < currentSnapshot.Entries.Count; i++)
            {
                var entry = currentSnapshot.Entries[i];
                int at;
                if (index.TryGetValue(entry.Key, out at))
                {
                    result.Merged.Entries[at] = entry.Clone();
                    result.Overwritten++;
                }
                else
                {
                    index[entry.Key] = result.Merged.Entries.Count;
                    result.Merged.Entries.Add(entry.Clone());
                    result.Added++;
                }
            }

            result.Kept = result.Merged.Entries.Count - result.Added - result.Overwritten;
            result.ReindexedSets = RebuildSets(result.Merged);
            result.Diff = Compare(baseSnapshot, result.Merged);
            return result;
        }

        /// <summary>条目集合变化后 ContentSets[].Ids 存的是下标，必须整体重算。</summary>
        public static int RebuildSets(CatalogSnapshot snapshot)
        {
            if (snapshot == null)
                return 0;

            snapshot.Sets.Clear();
            var map = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>(
                System.StringComparer.Ordinal);
            for (var i = 0; i < snapshot.Entries.Count; i++)
            {
                var sets = snapshot.Entries[i].Sets;
                for (var s = 0; s < sets.Count; s++)
                {
                    var name = sets[s];
                    if (string.IsNullOrEmpty(name))
                        continue;
                    System.Collections.Generic.List<int> list;
                    if (!map.TryGetValue(name, out list))
                    {
                        list = new System.Collections.Generic.List<int>();
                        map.Add(name, list);
                    }
                    list.Add(i);
                }
            }

            var names = new System.Collections.Generic.List<string>(map.Keys);
            names.Sort(System.StringComparer.Ordinal);
            for (var i = 0; i < names.Count; i++)
            {
                snapshot.Sets.Add(new CatalogSetDef
                {
                    Name = new Unity.Collections.FixedString512Bytes(names[i]),
                    Ids = map[names[i]]
                });
            }
            return snapshot.Sets.Count;
        }

        /// <summary>四分类 diff：join key 是 RemoteContentId.Hash，location hash 变化即 Changed。</summary>
        public static CatalogDiffReport Compare(CatalogSnapshot baseline, CatalogSnapshot current)
        {
            var baseSnapshot = baseline ?? new CatalogSnapshot();
            var currentSnapshot = current ?? new CatalogSnapshot();
            var report = new CatalogDiffReport();

            var baseMap = new System.Collections.Generic.Dictionary<string, CatalogEntry>(System.StringComparer.Ordinal);
            for (var i = 0; i < baseSnapshot.Entries.Count; i++)
                baseMap[baseSnapshot.Entries[i].Key] = baseSnapshot.Entries[i];

            var currentMap = new System.Collections.Generic.Dictionary<string, CatalogEntry>(System.StringComparer.Ordinal);
            for (var i = 0; i < currentSnapshot.Entries.Count; i++)
                currentMap[currentSnapshot.Entries[i].Key] = currentSnapshot.Entries[i];

            var keys = new System.Collections.Generic.List<string>(baseMap.Keys);
            foreach (var key in currentMap.Keys)
            {
                if (!baseMap.ContainsKey(key))
                    keys.Add(key);
            }
            keys.Sort(System.StringComparer.Ordinal);

            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                CatalogEntry before;
                CatalogEntry after;
                var hasBefore = baseMap.TryGetValue(key, out before);
                var hasAfter = currentMap.TryGetValue(key, out after);

                var diff = new CatalogDiffEntry
                {
                    Key = key,
                    Name = hasAfter ? after.Name : before.Name,
                    BaselineHash = hasBefore ? before.LocationHash : "",
                    CurrentHash = hasAfter ? after.LocationHash : "",
                    SetName = hasAfter && after.Sets.Count > 0 ? after.Sets[0] :
                              (hasBefore && before.Sets.Count > 0 ? before.Sets[0] : "")
                };

                if (!hasBefore)
                {
                    diff.Kind = ContentExportDiffKind.Added;
                    report.AddedCount++;
                }
                else if (!hasAfter)
                {
                    diff.Kind = ContentExportDiffKind.Removed;
                    report.RemovedCount++;
                }
                else if (!string.Equals(before.LocationHash, after.LocationHash, System.StringComparison.Ordinal))
                {
                    diff.Kind = ContentExportDiffKind.Changed;
                    report.ChangedCount++;
                }
                else
                {
                    diff.Kind = ContentExportDiffKind.Unchanged;
                    report.UnchangedCount++;
                }
                report.Entries.Add(diff);
            }
            return report;
        }
    }
}
