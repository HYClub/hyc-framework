using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using HYC.Framework.IO;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 场景内容导出：扫描关卡资产（Prefab / Scene）上所有实现 <see cref="ISceneContentMarker"/> 的组件，
    /// 客户端写 <c>StreamingAssets/SceneContent/&lt;guid&gt;.blob</c>（唯一进客户端包体的产物，做 TryRead 往返自校验），
    /// 服务端写 <c>ServerExportDir/SceneContent/&lt;guid&gt;.json</c>（Assets 外、非 Blob，只交付服务端进程）。
    /// <para>
    /// 关卡身份 = 资产 .meta 的 GUID：导出时由 <see cref="AssetDatabase.AssetPathToGUID"/> 取得，
    /// 并以此命名产物。同时把「业务 key → GUID」登记进
    /// <c>StreamingAssets/SceneContent/index.json</c>，运行期关卡加载器用业务 key 调
    /// <c>SceneContentLoader.LoadByKey</c>（或仍按 GUID 调 <c>Load</c>）。
    /// 不依赖任何手填 id，也不使用关卡名。
    /// </para>
    /// <para>两侧差异只体现在 payload：标记组件按 <see cref="ISceneContentMarkerPayload"/> 收到 <see cref="SceneContentSide"/> 自行决定字段。</para>
    /// </summary>
    public static class SceneContentExporter
    {
        /// <summary>客户端 Blob 输出目录（必须位于 StreamingAssets 内，运行期才能读取）。</summary>
        public const string BlobDir = "Assets/StreamingAssets/SceneContent";

        /// <summary>服务端 JSON 产物在 ServerExportDir 下的子目录名。</summary>
        private const string ServerJsonSubDir = "SceneContent";

        /// <summary>服务端交付文件的结构版本（与客户端 Blob 版本互不相干）。</summary>
        private const int ServerDocVersion = 1;

        /// <summary>旧版服务端 Blob 子目录名；导出流程已不再使用，保留仅为兼容既有引用。</summary>
        [System.Obsolete("服务端产物已改为 ServerExportDir/SceneContent/<guid>.json，不再写入 StreamingAssets。")]
        public const string ServerSubDir = "server";

        private const string Ext = ".blob";

        /// <summary>从一个关卡根 GO 收集标记条目（指定导出侧，不做 Blob 写盘）。</summary>
        public static List<SceneContentEntryData> Collect(GameObject levelRoot, SceneContentSide side)
        {
            var all = new List<SceneContentEntryData>();
            if (levelRoot == null) return all;
            var markers = levelRoot.GetComponentsInChildren<Component>(true);
            foreach (var c in markers)
            {
                if (c == null || !(c is ISceneContentMarker marker)) continue;
                var t = c.transform;
                // 路径相对「标记自身所在的关卡根」算：prefab 即 prefab 根，scene 即其所在根 GO，
                // 比原先「单一 Authoring 根」更贴近实际层级。
                var levelRootForPath = t.root != null ? t.root.gameObject : levelRoot;
                var payload = c is ISceneContentMarkerPayload payloadProvider
                    ? payloadProvider.ToPayloadJson(levelRootForPath, side)
                    : JsonUtility.ToJson(c);
                all.Add(new SceneContentEntryData
                {
                    contentTypeId = marker.ContentTypeId,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                    payloadJson = payload ?? "",
                });
            }
            return all;
        }

        /// <summary>
        /// 导出选中资产（Prefab 或 Scene）。返回是否成功。
        /// <para>除了客户端 Blob 与服务端 JSON，还会把「业务 key → 资产 GUID」登记进
        /// <c>SceneContent/index.json</c> 供运行期 <c>SceneContentLoader.LoadByKey</c> 使用：
        /// <paramref name="alias"/> 显式传入时用它，否则按「Addressables 地址 > 资产名」自动推断。</para>
        /// </summary>
        /// <param name="levelAsset">关卡 Prefab / Scene 资产。</param>
        /// <param name="alias">业务 key（如 LevelConfig.MapAddress 的 "Level/001"）。传 null 则自动推断；不改现有调用。</param>
        public static bool Export(Object levelAsset, string alias = null)
        {
            if (levelAsset == null) return false;
            var path = AssetDatabase.GetAssetPath(levelAsset);
            if (string.IsNullOrEmpty(path)) return false;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"[SceneContent] 无法取得资产「{levelAsset.name}」的 GUID，跳过导出。");
                return false;
            }

            List<GameObject> roots;
            SceneLoadHandle handle = null;
            if (levelAsset is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go))
            {
                var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                roots = loaded != null ? new List<GameObject> { loaded } : new List<GameObject>();
            }
            else if (levelAsset is SceneAsset)
            {
                var scene = SceneManager.GetSceneByPath(path);
                var wasOpen = scene.IsValid() && scene.isLoaded;
                var opened = wasOpen ? scene : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                handle = new SceneLoadHandle(opened, wasOpen);
                roots = new List<GameObject>(opened.GetRootGameObjects());
            }
            else
            {
                Debug.LogWarning($"[SceneContent] 「{levelAsset.name}」既不是 Prefab 也不是 Scene，跳过导出。");
                return false;
            }

            try
            {
                if (roots.Count == 0) return false;
                WarnLegacyServerBlobs();
                var key = ResolveKey(path, guid, alias);
                var client = Collect(roots, SceneContentSide.Client);
                var server = Collect(roots, SceneContentSide.Server);

                // 清单只在客户端 Blob 写成功后登记，避免清单指向缺失 / 损坏的 blob；
                // 服务端 JSON 属另一条交付链，它失败不该让客户端产物失去可寻址的业务 key。
                // 清单是纯磁盘文件：不需要 AssetDatabase.ImportAsset，
                // WebGL / Android 构建期由 StreamingAssetsManifestBuilder 递归收进预取清单。
                var ok = WriteClientBlob(client, guid);
                if (ok) ok = WriteManifest(key, guid, path);
                var serverOk = WriteServerJson(server, guid, key, path);
                return ok && serverOk;
            }
            finally
            {
                handle?.Dispose();
            }
        }

        /// <summary>按「显式 alias &gt; Addressables 地址 &gt; 资产名」推断业务 key。</summary>
        /// <remarks>
        /// 关卡资产通常本身就是 Addressable（SnowBros 的 Level_001 → "Level/001"），
        /// 这样导出的 key 与运行期 LevelConfig.MapAddress 天然一致，无需调用方手填。
        /// 没装 / 没初始化 Addressables 时静默回退资产名，不让导出失败。
        /// </remarks>
        private static string ResolveKey(string assetPath, string guid, string alias)
        {
            if (!string.IsNullOrWhiteSpace(alias)) return alias.Trim();

            try
            {
                var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
                var entry = settings != null ? settings.FindAssetEntry(guid, true) : null;
                if (entry != null && !string.IsNullOrEmpty(entry.address))
                    return entry.address;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SceneContent] 读取 Addressables 地址失败，回退资产名：{e.Message}");
            }

            return Path.GetFileNameWithoutExtension(assetPath);
        }

        /// <summary>
        /// Upsert 一条「业务 key → 资产 GUID」到 <c>SceneContent/index.json</c>。
        /// <para>同一 key 后写覆盖：关卡 prefab 重建换了 GUID，重新导出即自愈；
        /// 不同 key 指向同一 GUID 允许共存（一个关卡可有多个业务别名）。</para>
        /// </summary>
        private static bool WriteManifest(string key, string guid, string assetPath)
        {
            var manifestPath = Path.Combine(BlobDir, "index.json");
            try
            {
                if (!Directory.Exists(BlobDir)) Directory.CreateDirectory(BlobDir);

                SceneContentManifest manifest = null;
                if (File.Exists(manifestPath))
                {
                    var json = File.ReadAllText(manifestPath);
                    if (!string.IsNullOrEmpty(json))
                        manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<SceneContentManifest>(json);
                }
                if (manifest == null) manifest = new SceneContentManifest();
                if (manifest.entries == null)
                    manifest.entries = new List<SceneContentManifestEntry>();

                // 同名资产（不同目录）用资产名兜底时会撞同一个 key：明确提示改用显式 alias
                for (int i = 0; i < manifest.entries.Count; i++)
                {
                    var e = manifest.entries[i];
                    if (e == null || !string.Equals(e.key, key, System.StringComparison.Ordinal)) continue;
                    if (!string.Equals(e.guid, guid, System.StringComparison.Ordinal))
                        Debug.LogWarning($"[SceneContent] 业务 key「{key}」被多个关卡资产共用" +
                                         $"（{e.assetPath} 与 {assetPath}）。请给 Export(asset, alias) 传不重复的 alias，" +
                                         "否则先导出者的映射会被覆盖。");
                    break;
                }

                manifest.entries.RemoveAll(
                    e => e == null || string.Equals(e.key, key, System.StringComparison.Ordinal));
                manifest.entries.Add(new SceneContentManifestEntry
                {
                    key = key,
                    guid = guid,
                    assetPath = assetPath,
                });
                manifest.version = SceneContentManifest.ManifestVersion;

                // 必须无 BOM：运行期 StreamingAssets.ReadText 用 Encoding.UTF8.GetString 不会剥离 U+FEFF，
                // 带 BOM 的 JSON 会让 Newtonsoft.Json 反序列化直接报错。
                File.WriteAllText(manifestPath,
                    Newtonsoft.Json.JsonConvert.SerializeObject(manifest, Newtonsoft.Json.Formatting.Indented),
                    new System.Text.UTF8Encoding(false));
                Debug.Log($"[SceneContent] 已登记业务 key「{key}」→ guid={guid}（{SceneContentManifest.RelativePath}）。");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SceneContent] 写映射清单失败（{manifestPath}）：{e.Message}");
                return false;
            }
        }

        private static List<SceneContentEntryData> Collect(List<GameObject> roots, SceneContentSide side)
        {
            var all = new List<SceneContentEntryData>();
            foreach (var r in roots)
                all.AddRange(Collect(r, side));
            return all;
        }

        /// <summary>
        /// 导出工程内全部含标记组件的 Prefab / Scene。返回成功数。
        /// <para>业务 key 按「Addressables 地址 &gt; 资产名」自动推断；同名资产（不同目录）
        /// 会撞 key，此时改用 <c>Export(asset, alias)</c> 逐资产传不重复的别名。</para>
        /// </summary>
        public static int ExportAll()
        {
            var ok = 0;
            var total = 0;
            foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null || go.GetComponentsInChildren<ISceneContentMarker>(true).Length == 0) continue;
                total++;
                if (Export(go)) ok++;
            }
            foreach (var g in AssetDatabase.FindAssets("t:SceneAsset"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(p);
                if (scene == null) continue;
                // 仅在场景确实含标记时才计入（避免无谓地加载空场景）
                var s = SceneManager.GetSceneByPath(p);
                var wasOpen = s.IsValid() && s.isLoaded;
                var opened = wasOpen ? s : EditorSceneManager.OpenScene(p, OpenSceneMode.Additive);
                var has = false;
                foreach (var root in opened.GetRootGameObjects())
                    if (root.GetComponentsInChildren<ISceneContentMarker>(true).Length > 0) { has = true; break; }
                if (!wasOpen) EditorSceneManager.CloseScene(opened, true);
                if (!has) continue;
                total++;
                if (Export(scene)) ok++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[SceneContent] 全量导出：{ok}/{total} 个关卡成功。");
            return ok;
        }

        /// <summary>场景附加加载的 RAII 句柄：仅在本次新开时才关，绝不关掉用户原本打开的场景。</summary>
        private sealed class SceneLoadHandle : System.IDisposable
        {
            private readonly Scene _scene;
            private readonly bool _wasOpen;
            public SceneLoadHandle(Scene scene, bool wasOpen) { _scene = scene; _wasOpen = wasOpen; }
            public void Dispose()
            {
                if (!_wasOpen && _scene.IsValid() && _scene.isLoaded)
                    EditorSceneManager.CloseScene(_scene, true);
            }
        }

        /// <summary>客户端产物：写 StreamingAssets 内的 Blob 并做 TryRead 往返自校验。</summary>
        private static bool WriteClientBlob(List<SceneContentEntryData> data, string guid)
        {
            if (!Directory.Exists(BlobDir)) Directory.CreateDirectory(BlobDir);
            var path = Path.Combine(BlobDir, guid + Ext);
            WriteBlob(path, guid, data);

            // 自校验：读回确认版本、guid 与条数一致
            var bytes = File.ReadAllBytes(path);
            if (!BlobReader.TryRead<SceneContentBlob>(bytes, SceneContentBlob.BlobVersion, out var check))
            {
                Debug.LogError($"[SceneContent] 导出后自校验失败（无法读回）：{path}");
                return false;
            }
            // 先取快照再 Dispose：BlobAssetReference 释放后再访问 .Value 属非法使用
            // ★EA0001：Blob 成员（BlobArray）必须按非只读 ref 访问，不能按值拷出
            ref var gbytes = ref check.Value.LevelGuid;
            var buf = new byte[gbytes.Length];
            for (int i = 0; i < gbytes.Length; i++) buf[i] = gbytes[i];
            var readBackGuid = Encoding.UTF8.GetString(buf);
            var readBackCount = check.Value.Entries.Length;
            check.Dispose();
            var ok = readBackCount == data.Count && readBackGuid == guid;
            if (ok)
                Debug.Log($"[SceneContent] 导出完成（客户端 Blob）：guid={guid}，{data.Count} 条 → {path}");
            else
                Debug.LogError($"[SceneContent] 导出自校验不一致（客户端）：期望 guid={guid}/{data.Count} 条，" +
                               $"实际 {readBackGuid}/{readBackCount} 条。");
            return ok;
        }

        /// <summary>
        /// 服务端产物：写 JSON 到 <c>ServerExportDir/SceneContent/&lt;guid&gt;.json</c>。
        /// <para>服务端不消费 Unity Blob，也不该把数据写进 StreamingAssets —— 那里的内容
        /// 在所有平台都会被打进客户端包体。目录落在 Assets 内时直接拒绝，避免误配把服务端数据带进包。</para>
        /// </summary>
        private static bool WriteServerJson(List<SceneContentEntryData> data, string guid,
            string key, string assetPath)
        {
            var root = HYC.Framework.Config.Editor.ConfigDataSettings.ServerExportDir;
            if (string.IsNullOrWhiteSpace(root))
            {
                Debug.LogError("[SceneContent] 服务器导出目录为空（HYC Settings → 配置 → 服务器导出目录），" +
                               "已跳过服务端产物。");
                return false;
            }
            if (HYC.Framework.Config.Editor.ConfigDataSettings.IsServerExportDirInsideAssets(root))
            {
                Debug.LogError($"[SceneContent] 服务器导出目录「{root}」位于 Assets 内，" +
                               "服务端产物会被打进客户端包体。请改到工程 Assets 外的目录（如 Exports/Server）。");
                return false;
            }

            var dir = Path.Combine(root, ServerJsonSubDir);
            var file = Path.Combine(dir, guid + ".json");
            try
            {
                Directory.CreateDirectory(dir);
                var doc = new ServerDoc
                {
                    version = ServerDocVersion,
                    key = key,
                    guid = guid,
                    assetPath = assetPath,
                    entries = new List<ServerEntry>(data.Count),
                };
                for (int i = 0; i < data.Count; i++)
                {
                    var d = data[i];
                    doc.entries.Add(new ServerEntry
                    {
                        contentTypeId = d.contentTypeId,
                        posX = d.position.x,
                        posY = d.position.y,
                        posZ = d.position.z,
                        rotX = d.rotation.x,
                        rotY = d.rotation.y,
                        rotZ = d.rotation.z,
                        rotW = d.rotation.w,
                        scaleX = d.scale.x,
                        scaleY = d.scale.y,
                        scaleZ = d.scale.z,
                        payloadJson = d.payloadJson ?? "",
                    });
                }
                // 无 BOM：服务端解析侧不必额外处理 U+FEFF。
                File.WriteAllText(file,
                    Newtonsoft.Json.JsonConvert.SerializeObject(doc, Newtonsoft.Json.Formatting.Indented),
                    new System.Text.UTF8Encoding(false));
                Debug.Log($"[SceneContent] 导出完成（服务端 JSON）：guid={guid}，{data.Count} 条 → {file}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SceneContent] 写服务端 JSON 失败（{file}）：{e.Message}");
                return false;
            }
        }

        /// <summary>服务端交付文件结构：纯 JSON，扁平 float，不含 Unity Blob 二进制。</summary>
        private sealed class ServerDoc
        {
            public int version;
            public string key;
            public string guid;
            public string assetPath;
            public List<ServerEntry> entries;
        }

        private sealed class ServerEntry
        {
            public int contentTypeId;
            public float posX, posY, posZ;
            public float rotX, rotY, rotZ, rotW;
            public float scaleX, scaleY, scaleZ;
            public string payloadJson;
        }

        private static bool sLegacyServerWarned;

        /// <summary>旧版把服务端 Blob 写进 StreamingAssets；残留目录仍会进客户端包体，明确提示清理。</summary>
        private static void WarnLegacyServerBlobs()
        {
            if (sLegacyServerWarned) return;
            var legacy = BlobDir + "/server";
            if (!Directory.Exists(legacy)) return;
            sLegacyServerWarned = true;
            Debug.LogError("[SceneContent] 检测到旧版服务端 Blob 目录 " + legacy +
                           "。服务端产物已改到 ServerExportDir/SceneContent/<guid>.json，" +
                           "请删除该目录及其 .meta —— 它仍在 StreamingAssets 内，会被打进客户端包体。");
        }

        private static void WriteBlob(string path, string guid, List<SceneContentEntryData> data)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref var root = ref builder.ConstructRoot<SceneContentBlob>();
                // 关卡身份（资产 GUID）写入 Blob，供运行期/自校验核对
                var guidBytes = Encoding.UTF8.GetBytes(guid ?? "");
                var g = builder.Allocate(ref root.LevelGuid, guidBytes.Length);
                for (int i = 0; i < guidBytes.Length; i++) g[i] = guidBytes[i];

                var arr = builder.Allocate(ref root.Entries, data.Count);
                for (int i = 0; i < data.Count; i++)
                {
                    var d = data[i];
                    var payloadBytes = Encoding.UTF8.GetBytes(d.payloadJson ?? "");
                    // 只写扁平 float（UnityEngine.Vector3/Quaternion 的 x/y/z/w），
                    // 不出现任何 Unity.Mathematics 类型——Editor 程序集（asmdef）没有该引用，
                    // 触碰 math 字段会 CS0012 / EA0003（见 SceneContentEntryBlob 注释）。
                    arr[i] = new SceneContentEntryBlob
                    {
                        ContentTypeId = d.contentTypeId,

                        PosX = d.position.x,
                        PosY = d.position.y,
                        PosZ = d.position.z,

                        RotX = d.rotation.x,
                        RotY = d.rotation.y,
                        RotZ = d.rotation.z,
                        RotW = d.rotation.w,

                        ScaleX = d.scale.x,
                        ScaleY = d.scale.y,
                        ScaleZ = d.scale.z,
                    };
                    var payloadArr = builder.Allocate(ref arr[i].Payload, payloadBytes.Length);
                    for (int k = 0; k < payloadBytes.Length; k++)
                        payloadArr[k] = payloadBytes[k];
                }
                BlobAssetReference<SceneContentBlob>.Write(builder, path, SceneContentBlob.BlobVersion);
            }
            finally
            {
                builder.Dispose();
            }
        }
    }
}
