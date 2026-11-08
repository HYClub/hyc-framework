namespace HYC.Framework.ContentExport.Editor
{
    public enum ContentExportScopeKind
    {
        All,
        Selected,
        Group,
        Entry
    }

    /// <summary>一条导出条目。SourceRelativePath 是构建产物目录内的相对路径，用作 Scope 与 contentSetFunc 的匹配键。</summary>
    [System.Serializable]
    public sealed class ContentExportGroupEntry
    {
        public string Id = "";
        public string Name = "";
        public string AssetPath = "";
        public string Address = "";
        public string SourceRelativePath = "";
        public string Type = "Prefab";
        public string SetName = "";
        public bool Included = true;
        public bool PinLogicalId = true;
        public string LastHash = "";
        public string NamingTemplate = "";
    }

    /// <summary>Groups 树节点：可嵌套文件夹，也带自己的条目。</summary>
    [System.Serializable]
    public sealed class ContentExportGroupNode
    {
        public string Id = "";
        public string Name = "";
        public string NamingTemplate = "";
        public string SetName = "";
        public System.Collections.Generic.List<ContentExportGroupNode> Children =
            new System.Collections.Generic.List<ContentExportGroupNode>();
        public System.Collections.Generic.List<ContentExportGroupEntry> Entries =
            new System.Collections.Generic.List<ContentExportGroupEntry>();
    }

    [System.Serializable]
    public sealed class ContentExportGroupsDocument
    {
        public int Version = 1;
        public System.Collections.Generic.List<ContentExportGroupNode> Groups =
            new System.Collections.Generic.List<ContentExportGroupNode>();
    }

    public sealed class ContentExportScopeRequest
    {
        public ContentExportScopeKind Kind = ContentExportScopeKind.Selected;
        public string NodeId = "";
        public string EntryId = "";
    }

    /// <summary>Groups 配置与导出范围解析。配置存工程内 Assets/HYC/ContentExport/groups.json，随工程提交。</summary>
    public static class ContentExportGroups
    {
        public const string RelativeFolder = "HYC/ContentExport";
        public const string FileName = "groups.json";

        public static string FilePath
        {
            get
            {
                return System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    RelativeFolder.Replace('/', System.IO.Path.DirectorySeparatorChar), FileName);
            }
        }

        public static ContentExportGroupsDocument Load()
        {
            var path = FilePath;
            if (!System.IO.File.Exists(path))
                return CreateDefault();

            try
            {
                var text = System.IO.File.ReadAllText(path).TrimStart('\uFEFF');
                var doc = Newtonsoft.Json.JsonConvert.DeserializeObject<ContentExportGroupsDocument>(text);
                return doc ?? CreateDefault();
            }
            catch (System.Exception e)
            {
                ContentExportLog.Add(ContentExportLogLevel.Warning, "Groups 配置读取失败，使用默认：" + e.Message);
                return CreateDefault();
            }
        }

        public static void Save(ContentExportGroupsDocument document)
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

        public static ContentExportGroupsDocument CreateDefault()
        {
            var doc = new ContentExportGroupsDocument();
            var levels = new ContentExportGroupNode
            {
                Id = "g-levels", Name = "Levels", NamingTemplate = "scene_<guid>.entities", SetName = "levels"
            };
            var chapter = new ContentExportGroupNode
            {
                Id = "g-ch1", Name = "Chapter_01", NamingTemplate = "scene_<guid>.entities", SetName = "levels"
            };
            chapter.Entries.Add(new ContentExportGroupEntry
            {
                Id = "e-l001", Name = "Level_001.prefab", Address = "Level/001",
                SourceRelativePath = "Levels/", Type = "Prefab", SetName = "levels"
            });
            levels.Children.Add(chapter);
            doc.Groups.Add(levels);
            doc.Groups.Add(new ContentExportGroupNode
            {
                Id = "g-prefabs", Name = "Prefabs", NamingTemplate = "prefab_<guid>.entities", SetName = "prefabs"
            });
            doc.Groups.Add(new ContentExportGroupNode
            {
                Id = "g-shaders", Name = "Shaders", NamingTemplate = "shader_<group>.entities", SetName = "shaders"
            });
            return doc;
        }

        /// <summary>解析 Scope → 构建产物相对路径前缀集合。空集合表示全量（不跳过任何文件）。</summary>
        public static System.Collections.Generic.HashSet<string> ResolveScope(
            ContentExportGroupsDocument document, ContentExportScopeRequest request)
        {
            var result = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (document == null)
                return result;
            var scope = request ?? new ContentExportScopeRequest();
            if (scope.Kind == ContentExportScopeKind.All)
                return result;

            var target = scope.Kind == ContentExportScopeKind.Group ? scope.NodeId : "";
            for (var i = 0; i < document.Groups.Count; i++)
                Collect(document.Groups[i], scope, target, result, false);
            return result;
        }

        private static void Collect(ContentExportGroupNode node, ContentExportScopeRequest scope,
            string targetId, System.Collections.Generic.HashSet<string> result, bool insideTarget)
        {
            var hit = insideTarget || (scope.Kind == ContentExportScopeKind.Group &&
                                       string.Equals(node.Id, targetId, System.StringComparison.Ordinal));

            var collectHere = scope.Kind == ContentExportScopeKind.Selected ||
                              (scope.Kind == ContentExportScopeKind.Group && hit);
            if (collectHere)
            {
                for (var i = 0; i < node.Entries.Count; i++)
                    Add(node.Entries[i], result);
            }

            if (scope.Kind == ContentExportScopeKind.Entry)
            {
                for (var i = 0; i < node.Entries.Count; i++)
                {
                    if (string.Equals(node.Entries[i].Id, scope.EntryId, System.StringComparison.Ordinal))
                        Add(node.Entries[i], result);
                }
            }

            for (var i = 0; i < node.Children.Count; i++)
                Collect(node.Children[i], scope, targetId, result, hit);
        }

        private static void Add(ContentExportGroupEntry entry,
            System.Collections.Generic.HashSet<string> result)
        {
            if (entry == null)
                return;
            if (string.IsNullOrEmpty(entry.SourceRelativePath))
                return;
            if (!entry.Included)
                return;
            result.Add(entry.SourceRelativePath.Replace('\\', '/'));
        }

        public static bool MatchesScope(string relativePath,
            System.Collections.Generic.HashSet<string> prefixes)
        {
            if (prefixes == null || prefixes.Count == 0)
                return true;
            var normalized = (relativePath ?? "").Replace('\\', '/');
            foreach (var prefix in prefixes)
            {
                if (normalized.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static ContentExportGroupNode FindNode(ContentExportGroupsDocument document, string id)
        {
            if (document == null)
                return null;
            for (var i = 0; i < document.Groups.Count; i++)
            {
                var hit = FindNodeRecursive(document.Groups[i], id);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        private static ContentExportGroupNode FindNodeRecursive(ContentExportGroupNode node, string id)
        {
            if (string.Equals(node.Id, id, System.StringComparison.Ordinal))
                return node;
            for (var i = 0; i < node.Children.Count; i++)
            {
                var hit = FindNodeRecursive(node.Children[i], id);
                if (hit != null)
                    return hit;
            }
            return null;
        }
    }
}
