namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>
    /// 镜像 struct 的布局自检。官方 RemoteContentCatalogData 是 internal，编译期不能按名字引用，
    /// 但反射可拿到字段树；Blob 按布局解释内存且 TryRead 不校验 sizeof，布局漂移会静默读错，故导出前必须比对。
    /// </summary>
    public static class ContentExportLayoutCheck
    {
        public sealed class LayoutCheckResult
        {
            public bool Matched;
            public string OfficialFingerprint = "";
            public string MirrorFingerprint = "";
            public string EntitiesVersion = "";
            public string Message = "";
            public string MirrorAssetPath = "";
        }

        public const string OfficialCatalogTypeName = "Unity.Entities.Content.RemoteContentCatalogData";
        public const string MirrorFileName = "ContentExportMirror.g.cs";

        private const System.Reflection.BindingFlags FieldFlags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        public static LayoutCheckResult Verify()
        {
            var result = new LayoutCheckResult();
            try
            {
                var official = typeof(Unity.Entities.Content.RemoteContentId).Assembly
                    .GetType(OfficialCatalogTypeName, false);
                if (official == null)
                {
                    result.Message = "找不到官方 " + OfficialCatalogTypeName + "，当前 Entities 版本可能已移除该类型。";
                    return result;
                }

                result.OfficialFingerprint = Signature(official, 0);
                result.MirrorFingerprint = Signature(typeof(HycRemoteContentCatalogData), 0);
                result.Matched = result.OfficialFingerprint == result.MirrorFingerprint;
                result.EntitiesVersion = typeof(Unity.Entities.Content.RemoteContentId).Assembly.GetName().Version.ToString();
                result.Message = result.Matched ? "布局一致" : "镜像布局与官方不一致，需更新镜像 struct";
            }
            catch (System.Exception e)
            {
                result.Message = "布局自检异常：" + e.Message;
            }
            return result;
        }

        /// <summary>导出前置校验。不一致时只有「提示 → 确定 → 更新」一条路径，没有忽略 / 取消分支。</summary>
        public static LayoutCheckResult EnsureLayout()
        {
            var result = Verify();
            if (result.Matched)
                return result;

            ContentExportLog.Add(ContentExportLogLevel.Warning, "镜像布局漂移：" + result.Message);
            if (!UnityEngine.Application.isBatchMode)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "布局校验未通过",
                    "官方 RemoteContentCatalogData 字段树与本地镜像不一致，继续导出会静默读错 catalog。\n\n" +
                    "点「确定」将按当前 Entities 版本更新镜像 struct 并刷新。",
                    "确定");
            }

            if (!RegenerateMirrorSource())
            {
                result.Message = "镜像 struct 自动更新失败，导出已终止；详见 Console。";
                return result;
            }

            // .g.cs 已重写并触发重编译，但当前 AppDomain 里的镜像类型仍是旧布局，
            // 再次 Verify 必然不等。本次必须终止，等 Unity 编译完成后重新执行才有效。
            result.Matched = false;
            result.Message = "镜像 struct 已按当前 Entities 版本更新，Unity 重编译后请重新执行本次导出。";
            ContentExportLog.Add(ContentExportLogLevel.Success, result.Message);
            return result;
        }

        /// <summary>按当前官方字段树重写 ContentExportMirror.g.cs，随后触发 Unity 重编译。</summary>
        public static bool RegenerateMirrorSource()
        {
            try
            {
                var official = typeof(Unity.Entities.Content.RemoteContentId).Assembly
                    .GetType(OfficialCatalogTypeName, false);
                if (official == null)
                    return false;

                var assetPath = FindMirrorAssetPath();
                if (string.IsNullOrEmpty(assetPath))
                {
                    ContentExportLog.Add(ContentExportLogLevel.Error, "找不到 " + MirrorFileName + " 的资产路径。");
                    return false;
                }

                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (info == null || string.IsNullOrEmpty(info.resolvedPath))
                {
                    ContentExportLog.Add(ContentExportLogLevel.Error, "无法解析包根目录，镜像更新失败。");
                    return false;
                }

                var absolute = System.IO.Path.Combine(info.resolvedPath, "Editor", "ContentExport", MirrorFileName);
                var source = BuildMirrorSource(official);
                System.IO.File.WriteAllText(absolute, source, new System.Text.UTF8Encoding(false));
                UnityEditor.AssetDatabase.ImportAsset(assetPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                UnityEditor.AssetDatabase.Refresh();
                ContentExportLog.Add(ContentExportLogLevel.Info, "已重写镜像 struct：" + assetPath);
                return true;
            }
            catch (System.Exception e)
            {
                ContentExportLog.Add(ContentExportLogLevel.Error, "镜像更新异常：" + e.Message);
                return false;
            }
        }

        private static string FindMirrorAssetPath()
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("ContentExportMirror t:MonoScript");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(MirrorFileName, System.StringComparison.Ordinal))
                    return path;
            }
            return null;
        }

        private static string BuildMirrorSource(System.Type official)
        {
            var asm = official.Assembly;
            var loc = official.GetNestedType("RemoteContentLocationData",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var set = official.GetNestedType("RemoteContentSetData",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var remoteId = asm.GetType("Unity.Entities.Content.RemoteContentId", false);
            var remoteLoc = asm.GetType("Unity.Entities.Content.RemoteContentLocation", false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// 镜像 struct：字段顺序与类型须与 com.unity.entities 的 RemoteContentCatalogData 一致。");
            sb.AppendLine("// 本文件由 ContentExportLayoutCheck.RegenerateMirrorSource() 重写，手改会被下次布局修复覆盖。");
            sb.AppendLine("// Hash128 必须替换成 HycHash128（4×uint），否则 BlobBuilder.ConstructRoot 报 EA0003。");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("namespace HYC.Framework.ContentExport.Editor");
            sb.AppendLine("{");

            // 收集所有源 struct 里出现的枚举，统一在顶部生成 Hyc* 枚举。
            var enums = new System.Collections.Generic.Dictionary<string, System.Type>();
            CollectEnums(enums, official);
            CollectEnums(enums, loc);
            CollectEnums(enums, set);
            CollectEnums(enums, remoteId);
            CollectEnums(enums, remoteLoc);
            foreach (var kv in enums)
                AppendEnum(sb, "Hyc" + kv.Key, kv.Value);

            AppendStruct(sb, "HycHash128", null);
            sb.AppendLine();
            if (remoteId != null)
            {
                AppendStruct(sb, "HycRemoteContentId", remoteId);
                sb.AppendLine();
            }
            if (remoteLoc != null)
            {
                AppendStruct(sb, "HycRemoteContentLocation", remoteLoc);
                sb.AppendLine();
            }
            AppendStruct(sb, "HycRemoteContentCatalogData", official);
            sb.AppendLine();
            if (loc != null)
            {
                AppendStruct(sb, "HycRemoteContentLocationData", loc);
                sb.AppendLine();
            }
            if (set != null)
                AppendStruct(sb, "HycRemoteContentSetData", set);
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendStruct(System.Text.StringBuilder sb, string name, System.Type source)
        {
            sb.AppendLine("    [System.Serializable]");
            sb.AppendLine("    public struct " + name);
            sb.AppendLine("    {");
            if (source == null)
            {
                // HycHash128：硬编码 4 个 uint，等价于官方 Hash128（16 字节）。
                sb.AppendLine("        public uint H0;");
                sb.AppendLine("        public uint H1;");
                sb.AppendLine("        public uint H2;");
                sb.AppendLine("        public uint H3;");
                sb.AppendLine("    }");
                return;
            }
            var fields = OrderedFields(source);
            for (var i = 0; i < fields.Length; i++)
                sb.AppendLine("        public " + MapTypeName(fields[i].FieldType) + " " + fields[i].Name + ";");
            sb.AppendLine("    }");
        }

        private static void AppendEnum(System.Text.StringBuilder sb, string name, System.Type enumType)
        {
            sb.AppendLine("    public enum " + name);
            sb.AppendLine("    {");
            foreach (var v in System.Enum.GetValues(enumType))
                sb.AppendLine("        " + v.ToString() + ",");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        private static void CollectEnums(System.Collections.Generic.Dictionary<string, System.Type> map, System.Type type)
        {
            if (type == null)
                return;
            foreach (var f in type.GetFields(FieldFlags))
            {
                var ft = f.FieldType;
                if (ft.IsEnum && !map.ContainsKey(ft.Name))
                    map.Add(ft.Name, ft);
            }
        }

        private static string MapTypeName(System.Type type)
        {
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(long)) return "long";
            if (type == typeof(Unity.Collections.FixedString512Bytes))
                return "Unity.Collections.FixedString512Bytes";
            if (type == typeof(UnityEngine.Hash128))
                return "HycHash128";
            if (type == typeof(Unity.Entities.Content.RemoteContentId))
                return "HycRemoteContentId";
            if (type == typeof(Unity.Entities.Content.RemoteContentLocation))
                return "HycRemoteContentLocation";
            if (type.IsEnum)
                return "Hyc" + type.Name;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Unity.Entities.BlobArray<>))
                return "Unity.Entities.BlobArray<" + MapTypeName(type.GetGenericArguments()[0]) + ">";

            var n = type.Name;
            if (n == "RemoteContentCatalogData") return "HycRemoteContentCatalogData";
            if (n == "RemoteContentLocationData") return "HycRemoteContentLocationData";
            if (n == "RemoteContentSetData") return "HycRemoteContentSetData";
            return string.IsNullOrEmpty(type.FullName) ? n : type.FullName;
        }

        private static System.Reflection.FieldInfo[] OrderedFields(System.Type type)
        {
            var fields = type.GetFields(FieldFlags);
            System.Array.Sort(fields, (a, b) =>
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.GetFieldOffset(a)
                    .CompareTo(Unity.Collections.LowLevel.Unsafe.UnsafeUtility.GetFieldOffset(b)));
            return fields;
        }

        private static string Signature(System.Type type, int depth)
        {
            var sb = new System.Text.StringBuilder();
            AppendSignature(sb, type, depth);
            return sb.ToString();
        }

        /// <summary>
        /// 指纹按「字节布局」归一化（偏移 + 大小 + 类型形状），不比较字段名以外的噪音，
        /// 并把 Hash128 与 HycHash128 都归一为 16 字节叶子、枚举按大小归一、FixedString512Bytes 按大小归一。
        /// 这样官方类型与镜像类型即便命名不同，只要逐字节布局一致就指纹相同；
        /// 一旦 Entities 改了内部布局（增删字段 / 调顺序 / 换 Hash128 大小），指纹立刻不等 → 触发重写。
        /// </summary>
        private static void AppendSignature(System.Text.StringBuilder sb, System.Type type, int depth)
        {
            var sizeOfType = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf(type);
            sb.Append('{').Append(sizeOfType);
            if (depth > 6)
            {
                sb.Append('}');
                return;
            }

            var fields = OrderedFields(type);
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                var fieldType = field.FieldType;
                // 只比较偏移 + 大小 + 类型形状，不比较字段名：官方自动属性的 backing field 名为
                // <Hash>k__BackingField，与镜像里的 Hash 名字不同但字节布局一致，带名字会误报漂移。
                // 位置由 GetFieldOffset 排序隐含，字段顺序变化会体现在偏移上。
                sb.Append('|').Append('@')
                    .Append(Unity.Collections.LowLevel.Unsafe.UnsafeUtility.GetFieldOffset(field))
                    .Append('/').Append(Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf(fieldType))
                    .Append(':');

                if (fieldType.IsGenericType &&
                    fieldType.GetGenericTypeDefinition() == typeof(Unity.Entities.BlobArray<>))
                {
                    sb.Append("BlobArray");
                    AppendSignature(sb, fieldType.GetGenericArguments()[0], depth + 1);
                }
                else if (IsBlittableLeaf(fieldType))
                {
                    sb.Append(LeafToken(fieldType));
                }
                else if (IsMirroredStruct(fieldType))
                {
                    AppendSignature(sb, fieldType, depth + 1);
                }
                else if (fieldType.IsEnum)
                {
                    sb.Append("enum").Append(Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf(fieldType));
                }
                else
                {
                    // 其余（理论上不应出现）兜底为全名，便于排查。
                    sb.Append(fieldType.FullName);
                }
            }
            sb.Append('}');
        }

        /// <summary>镜像里出现的、需要递归展开比对的结构体（官方与 Hyc* 同名都算）。</summary>
        private static bool IsMirroredStruct(System.Type type)
        {
            var n = type.Name;
            return n == "RemoteContentCatalogData" || n == "HycRemoteContentCatalogData" ||
                   n == "RemoteContentLocationData" || n == "HycRemoteContentLocationData" ||
                   n == "RemoteContentSetData" || n == "HycRemoteContentSetData" ||
                   n == "RemoteContentId" || n == "HycRemoteContentId" ||
                   n == "RemoteContentLocation" || n == "HycRemoteContentLocation";
        }

        /// <summary>
        /// blob 允许的非引用叶子：原始类型、FixedString512Bytes、Hash128 / HycHash128（两者均按 16 字节归一）。
        /// Hash128 内部含引用字段，绝不能递归展开（那正是 ConstructRoot 报错的根因），只能当不透明叶子。
        /// </summary>
        private static bool IsBlittableLeaf(System.Type type)
        {
            if (type.IsPrimitive)
                return true;
            if (type == typeof(Unity.Collections.FixedString512Bytes))
                return true;
            if (type == typeof(UnityEngine.Hash128) || type == typeof(HycHash128))
                return true;
            return false;
        }

        private static string LeafToken(System.Type type)
        {
            // Hash128 内部可能含引用字段导致运行时 SizeOf 偏大，但 blob 槽位固定 16 字节（4×uint），
            // 故两类型都归一成固定 h16，避免官方侧 SizeOf 偏大引发误报漂移。
            if (type == typeof(UnityEngine.Hash128) || type == typeof(HycHash128))
                return "h16";
            if (type == typeof(Unity.Collections.FixedString512Bytes))
                return "fs" + Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf(type);
            return "p" + Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf(type);
        }
    }
}
