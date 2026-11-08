namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>
    /// full catalog 与 catalogs.bin 的读写。官方写 catalog 的 CreateRemoteContentLocationData 是 private，
    /// 因此这里用 public 的 BlobBuilder / BlobAssetReference&lt;T&gt;.Write 复刻同一格式（blob version 1）。
    /// </summary>
    public static class ContentCatalogIO
    {
        public const int CatalogBlobVersion = 1;
        public const string CatalogPointerFileName = "catalogs.bin";
        public const string CatalogPointerSetName = "catalogs";

        public static CatalogSnapshot ReadCatalog(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return null;

            if (!Unity.Entities.BlobAssetReference<HycRemoteContentCatalogData>.TryRead(
                    path, CatalogBlobVersion, out var blob))
                return null;

            try
            {
                ref var root = ref blob.Value;
                var snapshot = new CatalogSnapshot { SourcePath = path };

                var entryCount = root.RemoteContentLocations.Length;
                for (var i = 0; i < entryCount; i++)
                {
                    ref var item = ref root.RemoteContentLocations[i];
                    var entry = new CatalogEntry
                    {
                        IdName = ContentExportText.ToManaged(item.identifier.Name),
                        IdHashBytes = item.identifier.Hash,
                        LocationPath = ContentExportText.ToManaged(item.location.Path),
                        LocationHashBytes = item.location.Hash,
                        LocationCrc = item.location.Crc,
                        LocationSize = item.location.Size
                    };
                    snapshot.Entries.Add(entry);
                }

                var setCount = root.ContentSets.Length;
                for (var s = 0; s < setCount; s++)
                {
                    ref var set = ref root.ContentSets[s];
                    var def = new CatalogSetDef { Name = set.Name };
                    var setName = ContentExportText.ToManaged(set.Name);
                    var idCount = set.Ids.Length;
                    for (var j = 0; j < idCount; j++)
                    {
                        var index = set.Ids[j];
                        def.Ids.Add(index);
                        if (index >= 0 && index < snapshot.Entries.Count)
                            snapshot.Entries[index].Sets.Add(setName);
                    }
                    snapshot.Sets.Add(def);
                }

                return snapshot;
            }
            finally
            {
                blob.Dispose();
            }
        }

        public static bool WriteCatalog(string path, CatalogSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(path))
                return false;

            try
            {
                var folder = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                    System.IO.Directory.CreateDirectory(folder);

                var setMap = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>(
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
                        if (!setMap.TryGetValue(name, out list))
                        {
                            list = new System.Collections.Generic.List<int>();
                            setMap.Add(name, list);
                        }
                        list.Add(i);
                    }
                }

                var names = new System.Collections.Generic.List<string>(setMap.Keys);
                using (var builder = new Unity.Entities.BlobBuilder(Unity.Collections.Allocator.Temp))
                {
                    ref var root = ref builder.ConstructRoot<HycRemoteContentCatalogData>();
                    var entries = builder.Allocate(ref root.RemoteContentLocations, snapshot.Entries.Count);
                    for (var i = 0; i < snapshot.Entries.Count; i++)
                    {
                        var source = snapshot.Entries[i];
                        entries[i] = new HycRemoteContentLocationData
                        {
                            identifier = new HycRemoteContentId
                            {
                                Name = new Unity.Collections.FixedString512Bytes(source.IdName),
                                Hash = source.IdHashBytes
                            },
                            location = new HycRemoteContentLocation
                            {
                                Type = HycLocationType.RemoteURL,
                                Path = new Unity.Collections.FixedString512Bytes(source.LocationPath),
                                Hash = source.LocationHashBytes,
                                Crc = source.LocationCrc,
                                Size = source.LocationSize
                            }
                        };
                    }

                    if (names.Count > 0)
                    {
                        var sets = builder.Allocate(ref root.ContentSets, names.Count);
                        for (var s = 0; s < names.Count; s++)
                        {
                            sets[s] = new HycRemoteContentSetData
                            {
                                Name = new Unity.Collections.FixedString512Bytes(names[s])
                            };
                            var indices = setMap[names[s]];
                            var ids = builder.Allocate(ref sets[s].Ids, indices.Count);
                            for (var j = 0; j < indices.Count; j++)
                                ids[j] = indices[j];
                        }
                    }

                    Unity.Entities.BlobAssetReference<HycRemoteContentCatalogData>.Write(
                        builder, path, CatalogBlobVersion);
                }
                return true;
            }
            catch (System.Exception e)
            {
                ContentExportLog.Add(ContentExportLogLevel.Error, "写 catalog 失败：" + e.Message);
                return false;
            }
        }

        /// <summary>catalogs.bin：固定名指针 catalog，只含 1 条指向 full catalog 的记录。</summary>
        public static bool WriteCatalogPointer(string targetFolder, string relativePath,
            UnityEngine.Hash128 hash, long size)
        {
            var snapshot = new CatalogSnapshot();
            var entry = new CatalogEntry();

            // 指针 Id 用官方 RemoteContentId(name) 构造器算出同官方一致的 Hash，再落到托管镜像字段。
            var id = new Unity.Entities.Content.RemoteContentId(
                new Unity.Collections.FixedString512Bytes(CatalogPointerSetName));
            entry.IdName = ContentExportText.ToManaged(id.Name);
            entry.IdHashBytes = ContentExportHash.ToHycHash128(id.Hash);

            var location = new Unity.Entities.Content.RemoteContentLocation();
            location.Type = Unity.Entities.Content.RemoteContentLocation.LocationType.RemoteURL;
            location.Path = new Unity.Collections.FixedString512Bytes(relativePath.Replace('\\', '/'));
            location.Hash = hash;
            location.Size = size;
            location.Crc = 0;
            entry.LocationPath = ContentExportText.ToManaged(location.Path);
            entry.LocationHashBytes = ContentExportHash.ToHycHash128(location.Hash);
            entry.LocationCrc = location.Crc;
            entry.LocationSize = location.Size;
            entry.Sets.Add(CatalogPointerSetName);
            snapshot.Entries.Add(entry);

            return WriteCatalog(System.IO.Path.Combine(targetFolder, CatalogPointerFileName), snapshot);
        }

        /// <summary>由 catalogs.bin 的指针解析出 full catalog 的磁盘路径。</summary>
        public static string ResolveFullCatalogPath(string targetFolder)
        {
            var pointerPath = System.IO.Path.Combine(targetFolder, CatalogPointerFileName);
            var pointer = ReadCatalog(pointerPath);
            if (pointer == null || pointer.Entries.Count == 0)
                return null;

            var relative = pointer.Entries[0].LocationPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
            var full = System.IO.Path.Combine(targetFolder, relative);
            return System.IO.File.Exists(full) ? full : null;
        }

        /// <summary>内容寻址相对路径：hash[0]hash[1]/hash，与官方 PublishContent 一致。</summary>
        public static string HashRelativePath(byte[] bytes)
        {
            var hash = UnityEngine.Hash128.Compute(bytes).ToString();
            return hash.Substring(0, 2) + "/" + hash;
        }

        /// <summary>CDN 上存在但 catalog 没有登记的文件（孤儿）。运行期不会被请求，但必须保留供回滚。</summary>
        public static System.Collections.Generic.List<string> FindOrphanFiles(
            string targetFolder, CatalogSnapshot snapshot)
        {
            var result = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(targetFolder) || !System.IO.Directory.Exists(targetFolder))
                return result;

            var used = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (snapshot != null)
            {
                for (var i = 0; i < snapshot.Entries.Count; i++)
                    used.Add(snapshot.Entries[i].LocationPath.Replace('\\', '/'));
            }
            used.Add(CatalogPointerFileName);

            var files = System.IO.Directory.GetFiles(targetFolder, "*", System.IO.SearchOption.AllDirectories);
            for (var i = 0; i < files.Length; i++)
            {
                if (files[i].Length <= targetFolder.Length + 1)
                    continue;
                var relative = files[i].Substring(targetFolder.Length + 1).Replace('\\', '/');
                if (!used.Contains(relative))
                    result.Add(relative);
            }
            return result;
        }
    }
}
