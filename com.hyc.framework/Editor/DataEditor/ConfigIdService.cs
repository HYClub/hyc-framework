using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// ID 构造器插件接口：由独立局域网 ID 服务器项目（Unity 插件包）实现。
    /// 数据编辑器检测到有实现类后，才显示"在 Unity 内启动 ID 服务器"。
    /// </summary>
    public interface IQkIdServerLauncher
    {
        /// <summary>启动服务器。返回空串表示成功（out 参数为实际监听的 IP/端口），否则返回错误信息。</summary>
        string StartServer(out string ip, out int port);

        /// <summary>停止服务器。</summary>
        void StopServer();
    }

    /// <summary>
    /// ID 构造器管理：按设置创建 本地/局域网 提供者，统一入口。
    /// 负责创建资产发号、删除释放、导出前查重（重复可一键重新派发）、右下角状态点。
    /// </summary>
    public static class ConfigIdService
    {
        private static IConfigIdProvider sProvider;
        private static bool sProbing;

        public static IConfigIdProvider Provider
        {
            get
            {
                if (sProvider == null)
                {
                    sProvider = ConfigDataSettings.IdProviderType == 1
                        ? (IConfigIdProvider)new NetworkConfigIdProvider(ConfigDataSettings.IdServerIp, ConfigDataSettings.IdServerPort)
                        : new LocalConfigIdProvider();
                    (sProvider as LocalConfigIdProvider)?.LoadExistingIds();
                }
                return sProvider;
            }
        }

        /// <summary>设置变化后重置提供者（下次访问按新设置重建）。</summary>
        public static void ResetProvider() => sProvider = null;

        public static ConfigIdState State => Provider.State;
        public static string ServerInfo => Provider.ServerInfo;

        /// <summary>创建资产时同步发号（局域网模式会等待连接/超时）。失败抛异常。</summary>
        public static ConfigId RequestIdSync()
        {
            return Provider.RequestIdAsync().GetAwaiter().GetResult();
        }

        public static void ReleaseIdSync(long id)
        {
            try
            {
                Provider.ReleaseIdAsync(id).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConfigId] 释放 ID {id} 失败: {e.Message}");
            }
        }

        /// <summary>后台探测连接状态（仅局域网有意义），用于状态点心跳。不阻塞编辑器线程。</summary>
        public static async void ProbeAsync(EditorWindow repaintTarget)
        {
            if (sProbing)
                return;
            sProbing = true;
            try
            {
                var p = Provider;
                if (p is LocalConfigIdProvider)
                    return;
                if (p is NetworkConfigIdProvider net)
                {
                    if (p.State != ConfigIdState.Connected)
                        await p.ConnectAsync();
                    else
                        await net.PingAsync();
                }
            }
            catch
            {
                // 状态由 provider 内部维护，这里忽略
            }
            finally
            {
                sProbing = false;
            }
            repaintTarget?.Repaint();
        }

        /// <summary>同步探测（"检测"按钮用），返回当前是否可用。</summary>
        public static bool Ping()
        {
            return Provider.Ping();
        }

        // ---------- 反射读写配置资产 ID/GUID ----------

        private const string FieldId = "ID";
        private const string FieldGuid = "GUID";

        private static readonly BindingFlags FieldFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        public static bool TryReadId(ScriptableObject asset, out long id, out string guid)
        {
            id = 0;
            guid = null;
            if (asset == null)
                return false;
            var fi = asset.GetType().GetField(FieldId, FieldFlags);
            var fg = asset.GetType().GetField(FieldGuid, FieldFlags);
            if (fi == null || fg == null)
                return false;
            id = (long)fi.GetValue(asset);
            guid = (string)fg.GetValue(asset);
            return true;
        }

        public static bool TryWriteId(ScriptableObject asset, ConfigId cid)
        {
            if (asset == null)
                return false;
            var fi = asset.GetType().GetField(FieldId, FieldFlags);
            var fg = asset.GetType().GetField(FieldGuid, FieldFlags);
            if (fi == null || fg == null)
                return false;
            fi.SetValue(asset, cid.id);
            fg.SetValue(asset, cid.guid);
            EditorUtility.SetDirty(asset);
            return true;
        }

        /// <summary>收集配置根目录/子目录下所有生成的配置资产（排除模板）。</summary>
        public static List<ScriptableObject> CollectConfigAssets(string folder)
        {
            var result = new List<ScriptableObject>();
            var ns = ConfigDataSettings.Namespace;
            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null)
                    continue;
                var t = asset.GetType();
                if (t == typeof(ConfigTemplate))
                    continue;
                if (t.Namespace == null || !t.Namespace.StartsWith(ns))
                    continue;
                result.Add(asset);
            }
            return result;
        }

        // ---------- 导出前查重 ----------

        public class DuplicateIssue
        {
            public ScriptableObject Asset;
            public string AssetName;
            public string Reason;
        }

        /// <summary>扫描资产列表，返回重复 ID/GUID 与未分配项。</summary>
        public static List<DuplicateIssue> FindIssues(List<ScriptableObject> assets)
        {
            var issues = new List<DuplicateIssue>();
            var seenId = new Dictionary<long, ScriptableObject>();
            var seenGuid = new Dictionary<string, ScriptableObject>();

            foreach (var a in assets)
            {
                if (a == null || !TryReadId(a, out var id, out var guid))
                    continue;
                if (id == 0 || string.IsNullOrEmpty(guid))
                {
                    issues.Add(new DuplicateIssue
                    {
                        Asset = a,
                        AssetName = a.name,
                        Reason = "未分配 ID/GUID（请重新派发）",
                    });
                    continue;
                }

                if (seenId.TryGetValue(id, out var prev))
                    issues.Add(new DuplicateIssue
                    {
                        Asset = a,
                        AssetName = a.name,
                        Reason = $"ID {id} 与 \"{prev.name}\" 重复",
                    });
                else
                    seenId[id] = a;

                if (seenGuid.TryGetValue(guid, out var prevG))
                    issues.Add(new DuplicateIssue
                    {
                        Asset = a,
                        AssetName = a.name,
                        Reason = $"GUID 与 \"{prevG.name}\" 重复",
                    });
                else
                    seenGuid[guid] = a;
            }
            return issues;
        }

        /// <summary>为问题资产重新派发 ID/GUID（优先走当前构造器，失败时用统一规则兜底）。</summary>
        public static int Reissue(List<DuplicateIssue> issues)
        {
            var count = 0;
            foreach (var issue in issues)
            {
                ConfigId cid;
                try
                {
                    cid = RequestIdSync();
                }
                catch
                {
                    cid = ConfigId.Generate();
                }
                if (TryWriteId(issue.Asset, cid))
                {
                    issue.Reason = "已重新派发";
                    count++;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return count;
        }

        /// <summary>导出前查重。发现重复/未分配 → 弹窗，可重新派发。返回 true 表示可继续导出。</summary>
        public static bool EnsureUniqueBeforeExport(List<ScriptableObject> assets, string context)
        {
            if (assets == null || assets.Count == 0)
                return true;
            var issues = FindIssues(assets);
            if (issues.Count == 0)
                return true;

            var lines = issues.Take(8).Select(i => $"  - {i.AssetName}：{i.Reason}").ToList();
            if (issues.Count > 8)
                lines.Add($"  ... 共 {issues.Count} 处问题");
            var msg = $"导出前检查发现 {issues.Count} 处 ID/GUID 问题（{context}）：\n\n"
                      + string.Join("\n", lines)
                      + "\n\n是否重新派发？（ID/GUID 必须全局唯一）";

            var choice = EditorUtility.DisplayDialogComplex("ID 冲突", msg, "重新派发并继续", "取消导出", "仍然导出");
            if (choice == 0)
            {
                var n = Reissue(issues);
                Debug.Log($"[ConfigId] 已重新派发 {n} 个资产的 ID/GUID");
                return true;
            }
            return choice == 2; // 仍然导出（用户坚持）
        }

        // ---------- 右下角状态点 ----------

        private static Texture2D sDotGreen;
        private static Texture2D sDotYellow;
        private static Texture2D sDotRed;

        /// <summary>在窗口右下角绘制 ID 构造器状态点（绿/黄/红）+ 悬停提示。</summary>
        public static void DrawStatusDot(Rect rect)
        {
            var tex = State == ConfigIdState.Connected
                ? (sDotGreen ?? (sDotGreen = MakeDotTexture(Color.green)))
                : State == ConfigIdState.Connecting
                    ? (sDotYellow ?? (sDotYellow = MakeDotTexture(Color.yellow)))
                    : (sDotRed ?? (sDotRed = MakeDotTexture(Color.red)));

            var stateText = State == ConfigIdState.Connected
                ? "已连接"
                : State == ConfigIdState.Connecting ? "连接中" : "不可用";
            GUI.Label(rect, new GUIContent(tex, $"{ServerInfo} - {stateText}（点击设置可切换/检测）"));
        }

        private static Texture2D MakeDotTexture(Color color)
        {
            const int size = 12;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var col = (Color32)color;
            var radius = size / 2f - 0.5f;
            var center = (size - 1) / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    pixels[y * size + x] = dx * dx + dy * dy <= radius * radius
                        ? col
                        : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // ---------- 在 Unity 内启动 ID 服务器（插件，未安装则无此能力） ----------

        public static bool HasServerLauncherPlugin =>
            TypeCache.GetTypesDerivedFrom<IQkIdServerLauncher>().Any(t => !t.IsAbstract);

        /// <summary>启动插件提供的 ID 服务器，成功后自动把地址写回设置。</summary>
        public static void LaunchServerPlugin()
        {
            var t = TypeCache.GetTypesDerivedFrom<IQkIdServerLauncher>().FirstOrDefault(x => !x.IsAbstract);
            if (t == null)
            {
                EditorUtility.DisplayDialog("未安装", "未找到 ID 服务器插件，请先安装独立局域网 ID 服务器包。", "确定");
                return;
            }

            var launcher = (IQkIdServerLauncher)Activator.CreateInstance(t);
            var err = launcher.StartServer(out var ip, out var port);
            if (!string.IsNullOrEmpty(err))
            {
                EditorUtility.DisplayDialog("启动失败", err, "确定");
                return;
            }

            ConfigDataSettings.IdServerIp = ip;
            ConfigDataSettings.IdServerPort = port;
            ConfigDataSettings.IdProviderType = 1;
            ResetProvider();
            Debug.Log($"[ConfigId] ID 服务器已启动：{ip}:{port}");
        }
    }
}
