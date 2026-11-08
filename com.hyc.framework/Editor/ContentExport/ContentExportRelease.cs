namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>业务版本记录。官方 runtime 没有语义版本号，版本由 catalogs.bin 指向哪个 full catalog 决定。</summary>
    [System.Serializable]
    public sealed class ContentExportReleaseRecord
    {
        public string Version = "";
        public string Channel = "stable";
        public string FullCatalogRelativePath = "";
        public string FullCatalogHash = "";
        public int EntryCount;
        public int RolloutPercent = 100;
        public string CompatibleSince = "";
        public string CreatedAt = "";
        public string Note = "";
    }

    [System.Serializable]
    public sealed class ContentExportReleaseDocument
    {
        public int Version = 1;
        public string CurrentVersion = "";
        public System.Collections.Generic.List<ContentExportReleaseRecord> Records =
            new System.Collections.Generic.List<ContentExportReleaseRecord>();
    }

    public static class ContentExportReleases
    {
        public const string FileName = "releases.json";

        public static string FilePath
        {
            get
            {
                return System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    ContentExportGroups.RelativeFolder.Replace('/', System.IO.Path.DirectorySeparatorChar), FileName);
            }
        }

        public static ContentExportReleaseDocument Load()
        {
            var path = FilePath;
            if (!System.IO.File.Exists(path))
                return new ContentExportReleaseDocument();
            try
            {
                var text = System.IO.File.ReadAllText(path).TrimStart('\uFEFF');
                return Newtonsoft.Json.JsonConvert.DeserializeObject<ContentExportReleaseDocument>(text)
                       ?? new ContentExportReleaseDocument();
            }
            catch (System.Exception e)
            {
                ContentExportLog.Add(ContentExportLogLevel.Warning, "发布记录读取失败：" + e.Message);
                return new ContentExportReleaseDocument();
            }
        }

        public static void Save(ContentExportReleaseDocument document)
        {
            if (document == null)
                return;
            var path = FilePath;
            var folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                System.IO.Directory.CreateDirectory(folder);
            var text = Newtonsoft.Json.JsonConvert.SerializeObject(document, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        }

        public static void Add(ContentExportReleaseRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.Version))
                return;
            var doc = Load();
            ContentExportReleaseRecord existing = null;
            for (var i = 0; i < doc.Records.Count; i++)
            {
                if (string.Equals(doc.Records[i].Version, record.Version, System.StringComparison.Ordinal))
                {
                    existing = doc.Records[i];
                    break;
                }
            }
            if (existing != null)
            {
                existing.Channel = record.Channel;
                existing.FullCatalogRelativePath = record.FullCatalogRelativePath;
                existing.FullCatalogHash = record.FullCatalogHash;
                existing.EntryCount = record.EntryCount;
                existing.RolloutPercent = record.RolloutPercent;
                existing.CompatibleSince = record.CompatibleSince;
                existing.CreatedAt = record.CreatedAt;
                existing.Note = record.Note;
            }
            else
            {
                doc.Records.Add(record);
            }
            doc.CurrentVersion = record.Version;
            Save(doc);
        }

        public static ContentExportReleaseRecord Find(string version)
        {
            var doc = Load();
            for (var i = 0; i < doc.Records.Count; i++)
            {
                if (string.Equals(doc.Records[i].Version, version, System.StringComparison.Ordinal))
                    return doc.Records[i];
            }
            return null;
        }
    }
}
