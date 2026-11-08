namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>FixedString512Bytes → 托管字符串；写回 catalog 时不需要反向转换（复用读出的 Id 值）。</summary>
    public static class ContentExportText
    {
        public static string ToManaged(Unity.Collections.FixedString512Bytes value)
        {
            var copy = value;
            return Unity.Collections.FixedStringMethods.ConvertToString<Unity.Collections.FixedString512Bytes>(ref copy);
        }
    }

    /// <summary>
    /// Hash128（含引用字段，不能进 blob）与镜像 HycHash128（4×uint）的互转。
    /// Hash128 不公开其 4 个 uint 字段，故用 UnsafeUtility 直接读 16 字节（与 Hash128(uint*) 构造器一致），
    /// 不依赖 ToString() 格式；写回 catalog 时则走 Hash128(uint,uint,uint,uint) 构造器还原显示用字符串。
    /// </summary>
    public static class ContentExportHash
    {
        public static string ToHashString(HycHash128 h)
        {
            return new UnityEngine.Hash128(h.H0, h.H1, h.H2, h.H3).ToString();
        }

        public static unsafe HycHash128 ToHycHash128(UnityEngine.Hash128 h)
        {
            var local = h;
            void* p = Unity.Collections.LowLevel.Unsafe.UnsafeUtilityExtensions.AddressOf(in local);
            uint* u = (uint*)p;
            return new HycHash128 { H0 = u[0], H1 = u[1], H2 = u[2], H3 = u[3] };
        }
    }

    /// <summary>
    /// 一条 full catalog 记录（纯托管，不进 blob）。Hash 以 HycHash128 存储，避免依赖 Hash128 的私有字段。
    /// </summary>
    public sealed class CatalogEntry
    {
        public HycHash128 IdHashBytes;
        public string IdName = "";
        public string LocationPath = "";
        public HycHash128 LocationHashBytes;
        public uint LocationCrc;
        public long LocationSize;
        public System.Collections.Generic.List<string> Sets = new System.Collections.Generic.List<string>();

        public string Key => ContentExportHash.ToHashString(IdHashBytes);
        public string Name => IdName;
        public string LocationHash => ContentExportHash.ToHashString(LocationHashBytes);

        public CatalogEntry Clone()
        {
            var copy = new CatalogEntry
            {
                IdHashBytes = IdHashBytes,
                IdName = IdName,
                LocationPath = LocationPath,
                LocationHashBytes = LocationHashBytes,
                LocationCrc = LocationCrc,
                LocationSize = LocationSize
            };
            copy.Sets.AddRange(Sets);
            return copy;
        }
    }

    /// <summary>content set 及其下标集合。Ids 是 RemoteContentLocations 的下标，增删条目后必须重算。</summary>
    public sealed class CatalogSetDef
    {
        public Unity.Collections.FixedString512Bytes Name;
        public System.Collections.Generic.List<int> Ids = new System.Collections.Generic.List<int>();
        public string DisplayName => ContentExportText.ToManaged(Name);
    }

    /// <summary>一次 full catalog 的内存快照。Entries 顺序即 catalog 下标顺序。</summary>
    public sealed class CatalogSnapshot
    {
        public string SourcePath = "";
        public System.Collections.Generic.List<CatalogEntry> Entries = new System.Collections.Generic.List<CatalogEntry>();
        public System.Collections.Generic.List<CatalogSetDef> Sets = new System.Collections.Generic.List<CatalogSetDef>();

        public int Count => Entries.Count;
    }

    public enum ContentExportDiffKind
    {
        Added,
        Removed,
        Changed,
        Unchanged
    }

    public sealed class CatalogDiffEntry
    {
        public string Key = "";
        public string Name = "";
        public ContentExportDiffKind Kind;
        public string BaselineHash = "";
        public string CurrentHash = "";
        public string SetName = "";
    }

    public sealed class CatalogDiffReport
    {
        public System.Collections.Generic.List<CatalogDiffEntry> Entries = new System.Collections.Generic.List<CatalogDiffEntry>();
        public System.Collections.Generic.List<string> OrphanFiles = new System.Collections.Generic.List<string>();
        public int AddedCount;
        public int RemovedCount;
        public int ChangedCount;
        public int UnchangedCount;
    }

    public sealed class ContentExportMergeResult
    {
        public CatalogSnapshot Merged;
        public CatalogDiffReport Diff;
        public int Overwritten;
        public int Added;
        public int Kept;
        public int Removed;
        public int ReindexedSets;
    }

    public sealed class ContentExportResult
    {
        public bool Success;
        public string Message = "";
        public string CatalogPath = "";
        public string FullCatalogRelativePath = "";
        public int EntryCount;
        public System.Collections.Generic.List<string> Steps = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> NewHashFiles = new System.Collections.Generic.List<string>();
        public ContentExportMergeResult Merge;

        public static ContentExportResult Ok(string message)
        {
            return new ContentExportResult { Success = true, Message = message };
        }

        public static ContentExportResult Fail(string message)
        {
            return new ContentExportResult { Success = false, Message = message };
        }
    }

    public enum ContentExportLogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    public sealed class ContentExportLogLine
    {
        public string Time = "";
        public ContentExportLogLevel Level;
        public string Message = "";
        public string Label => Level.ToString().ToUpperInvariant();
    }

    /// <summary>窗口 Log 页与 CLI 报告共用的单次运行日志缓冲。</summary>
    public static class ContentExportLog
    {
        private static readonly System.Collections.Generic.List<ContentExportLogLine> Lines =
            new System.Collections.Generic.List<ContentExportLogLine>();

        public static void Clear()
        {
            Lines.Clear();
        }

        public static void Add(ContentExportLogLevel level, string message)
        {
            Lines.Add(new ContentExportLogLine
            {
                Time = System.DateTime.Now.ToString("HH:mm:ss"),
                Level = level,
                Message = message ?? ""
            });
            var text = "[ContentExport] " + (message ?? "");
            if (level == ContentExportLogLevel.Error)
                UnityEngine.Debug.LogError(text);
            else if (level == ContentExportLogLevel.Warning)
                UnityEngine.Debug.LogWarning(text);
            else
                UnityEngine.Debug.Log(text);
        }

        public static System.Collections.Generic.List<ContentExportLogLine> Snapshot()
        {
            return new System.Collections.Generic.List<ContentExportLogLine>(Lines);
        }
    }
}
