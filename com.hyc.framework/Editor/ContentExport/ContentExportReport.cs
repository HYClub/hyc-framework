namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>CLI 调用结果。AI / CI 读 last-report.json 判定成败，不依赖解析 Unity Console 文本。</summary>
    [System.Serializable]
    public sealed class ContentExportReport
    {
        public string Command = "";
        public bool Success;
        public string Message = "";
        public string Timestamp = "";
        public string LayoutFingerprint = "";
        public string LayoutMessage = "";
        public string CatalogPath = "";
        public string FullCatalogRelativePath = "";
        public int EntryCount;
        public int Added;
        public int Removed;
        public int Changed;
        public int Unchanged;
        public int NewHashFileCount;
        public System.Collections.Generic.List<string> Steps = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> NewHashFiles = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> OrphanFiles = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> Errors = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> Log = new System.Collections.Generic.List<string>();

        // 真实 Diff / 合并明细（DryRun / 正式导出后填充，供窗口页签展示）
        public System.Collections.Generic.List<string> DiffAdded = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> DiffRemoved = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> DiffChanged = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> DiffUnchanged = new System.Collections.Generic.List<string>();
        public int MergeOverwritten;
        public int MergeAdded;
        public int MergeKept;
        public int MergeRemoved;
        public int MergeReindexed;
    }

    public static class ContentExportReportWriter
    {
        public const string FileName = "last-report.json";

        public static string FilePath
        {
            get
            {
                return System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    ContentExportGroups.RelativeFolder.Replace('/', System.IO.Path.DirectorySeparatorChar), FileName);
            }
        }

        public static string Write(ContentExportReport report)
        {
            if (report == null)
                return "";
            var lines = ContentExportLog.Snapshot();
            for (var i = 0; i < lines.Count; i++)
                report.Log.Add(lines[i].Time + " [" + lines[i].Label + "] " + lines[i].Message);

            var path = FilePath;
            var folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                System.IO.Directory.CreateDirectory(folder);
            var text = Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            return path;
        }

        public static void LogToConsole(ContentExportReport report)
        {
            if (report == null)
                return;
            var summary = "[ContentExport] " + report.Command + " → " +
                          (report.Success ? "OK" : "FAILED") + " · " + report.Message +
                          " · report=" + FilePath;
            if (report.Success)
                UnityEngine.Debug.Log(summary);
            else
                UnityEngine.Debug.LogError(summary);
        }
    }
}
