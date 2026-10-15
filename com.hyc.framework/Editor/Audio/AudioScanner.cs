// ============================================================
// HYC Framework - 音频扫描器
// 文件: Editor/Audio/AudioScanner.cs
// 说明: 两个数据源 —— Unity 工程内的 AudioClip，Wwise 的 SoundBanksInfo.json。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using HYC.Framework.Audio;
using HYC.Framework.Runtime;
// 不 using HYC.Framework.Config.Editor —— 里面的 GUIDrawer 与 UnityEditor.GUIDrawer 同名，
// 裸写 GUIDrawer 会报 CS0104 二义错误。需要时写全名，见 GetAddressableAddress 的调用处。

namespace HYC.Framework.AudioEditor
{
    /// <summary>扫描结果。</summary>
    public class AudioScanResult
    {
        /// <summary>工程内扫到的 AudioClip（路径 → 资源）。</summary>
        public List<AudioClip> Clips = new List<AudioClip>();

        /// <summary>从 SoundBanksInfo.json 读到的 Wwise 事件名。</summary>
        public List<string> WwiseEvents = new List<string>();

        /// <summary>Wwise 元数据文件路径（没找到为空）。</summary>
        public string WwiseMetaPath = "";

        /// <summary>Wwise 部署目录是否存在（装了 Wwise Unity Integration）。</summary>
        public bool WwiseInstalled;

        /// <summary>扫描过程中的提示（错误/警告），逐条显示给用户。</summary>
        public List<string> Messages = new List<string>();
    }

    /// <summary>音频扫描器。Unity 侧用 AssetDatabase，Wwise 侧解析官方 JSON 元数据。</summary>
    public static class AudioScanner
    {
        /// <summary>Wwise Unity Integration 的默认部署目录（相对工程根）。</summary>
        private static readonly string[] WwiseDeploymentDirs =
        {
            "Assets/Wwise/Deployment",
            "Assets/WwiseData/Banks",
        };

        /// <summary>
        /// 全量扫描。Unity AudioClip 永远能扫；Wwise 事件仅在找到 SoundBanksInfo.json 时才有。
        /// </summary>
        /// <param name="wwiseMetaOverridePath">
        /// 手动指定的 SoundBanksInfo.json 路径。空串表示自动探测。
        /// 新版 Wwise 是 .unitypackage（无 package.json），部署目录可自定义，所以留这个口子。
        /// </param>
        public static AudioScanResult Scan(string wwiseMetaOverridePath = "")
        {
            var result = new AudioScanResult();
            ScanClips(result);
            ScanWwise(result, wwiseMetaOverridePath);
            return result;
        }

        private static void ScanClips(AudioScanResult result)
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) result.Clips.Add(clip);
            }
            result.Clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        private static void ScanWwise(AudioScanResult result, string overridePath)
        {
            // 是否装了 Wwise：探测部署目录或常见的 Ak 脚本。
            foreach (var dir in WwiseDeploymentDirs)
            {
                if (Directory.Exists(dir)) { result.WwiseInstalled = true; break; }
            }
            if (!result.WwiseInstalled && AssetDatabase.FindAssets("AkSoundEngine").Length > 0)
                result.WwiseInstalled = true;

            if (!string.IsNullOrEmpty(overridePath))
            {
                if (File.Exists(overridePath))
                    ParseWwiseMeta(overridePath, result);
                else
                    result.Messages.Add($"指定的 Wwise 元数据文件不存在: {overridePath}");
                return;
            }

            if (!result.WwiseInstalled)
            {
                result.Messages.Add("未检测到 Wwise（Assets/Wwise/Deployment 不存在）。Wwise 相关列留空，不影响 clip 侧。");
                return;
            }

            // 优先找工程内（Unity 侧拷贝过来的），其次找 Wwise 工程侧的 GeneratedSoundBanks。
            var found = FindFirstExisting(WwiseMetaCandidates());
            if (found == null)
            {
                result.Messages.Add(
                    "检测到 Wwise，但找不到 SoundBanksInfo.json。请在 Wwise Authoring → " +
                    "Project Settings → SoundBanks 勾选 Generate JSON Metadata 后重新 Generate SoundBanks，" +
                    "或在本窗口手动指定该文件路径。");
                return;
            }

            ParseWwiseMeta(found, result);
        }

        private static IEnumerable<string> WwiseMetaCandidates()
        {
            // Unity 工程内（Addressables / Picker 拷过来的）
            foreach (var dir in WwiseDeploymentDirs)
            {
                if (!Directory.Exists(dir)) continue;
                var direct = Path.Combine(dir, "SoundBanksInfo.json");
                if (File.Exists(direct)) yield return direct;
                var nested = Directory.GetFiles(dir, "SoundBanksInfo.json", SearchOption.AllDirectories);
                foreach (var f in nested) yield return f;
            }

            // Wwise Authoring 工程侧：从工程内 .wwise 文件反推同级 GeneratedSoundBanks
            // Wwise Authoring 工程侧：直接扫磁盘找 .wwise 工程文件。
            // 注意不能用 AssetDatabase.FindAssets —— .wwise 是纯文本工程文件，
            // 既不是 Object 也不是 MonoScript，FindAssets 查不到（踩过一次）。
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null) yield break;

            foreach (var wwiseProj in EnumerateWwiseProjects(projectRoot.FullName))
            {
                var root = Path.GetDirectoryName(wwiseProj);
                if (root == null) continue;
                var gen = Path.Combine(root, "GeneratedSoundBanks");
                if (!Directory.Exists(gen)) continue;
                var files = Directory.GetFiles(gen, "SoundBanksInfo.json", SearchOption.AllDirectories);
                foreach (var f in files) yield return f;
            }
        }

        /// <summary>
        /// 递归找 .wwise 工程文件，跳过 Library / Temp / obj / .git 等重目录。
        /// 不排除的话 Library 动辄上万文件，扫一遍能把编辑器卡住。
        /// </summary>
        private static IEnumerable<string> EnumerateWwiseProjects(string rootDir)
        {
            var skip = new[] { "Library", "Temp", "obj", "Logs", ".git", ".vs", "Build", "UserSettings" };
            var stack = new Stack<string>();
            stack.Push(rootDir);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch { continue; } // 无权限目录直接跳过，不中断整体扫描

                foreach (var sub in subdirs)
                {
                    var name = Path.GetFileName(sub);
                    if (skip.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    stack.Push(sub);
                }

                string[] files;
                try { files = Directory.GetFiles(dir, "*.wwise"); }
                catch { continue; }
                foreach (var f in files) yield return f;
            }
        }

        private static string FindFirstExisting(IEnumerable<string> candidates)
        {
            foreach (var c in candidates) if (File.Exists(c)) return c;
            return null;
        }

        /// <summary>
        /// 解析 SoundBanksInfo.json，取出全部事件名。
        /// 结构：SoundBanksInfo.SoundBanks[].IncludedEvents[].Name
        /// 用 Newtonsoft（框架已有依赖）解析，缺字段时静默跳过而不是抛异常 ——
        /// 不同 Wwise 版本的元数据字段有增删。
        /// </summary>
        private static void ParseWwiseMeta(string path, AudioScanResult result)
        {
            result.WwiseMetaPath = path;
            try
            {
                var json = File.ReadAllText(path);
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                var banks = root["SoundBanks"] as Newtonsoft.Json.Linq.JArray;
                if (banks == null)
                {
                    result.Messages.Add($"SoundBanksInfo.json 结构异常（缺 SoundBanks 数组）: {path}");
                    return;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var bank in banks)
                {
                    var events = bank?["IncludedEvents"] as Newtonsoft.Json.Linq.JArray;
                    if (events == null) continue;
                    foreach (var ev in events)
                    {
                        var name = ev?["Name"]?.ToString();
                        if (string.IsNullOrEmpty(name)) continue;
                        if (seen.Add(name)) result.WwiseEvents.Add(name);
                    }
                }

                result.WwiseEvents.Sort(StringComparer.Ordinal);
                if (result.WwiseEvents.Count == 0)
                    result.Messages.Add("SoundBanksInfo.json 里没有事件（SoundBanks 是否为空？）。");
            }
            catch (Exception e)
            {
                result.Messages.Add($"解析 SoundBanksInfo.json 失败: {e.Message}");
            }
        }

        /// <summary>
        /// 用扫描结果里的 AudioClip 往总览里补条目。已存在同名/同地址的跳过，不覆盖用户已有配置。
        /// </summary>
        /// <returns>新增的条目数。</returns>
        public static int ImportClips(AudioCatalogAsset catalog, List<AudioClip> clips)
        {
            if (catalog == null || clips == null) return 0;

            var added = 0;
            foreach (var clip in clips)
            {
                if (clip == null) continue;

                // 写全名：UnityEditor 里也有 GUIDrawer，using 两个命名空间会二义（CS0104）。
                var address = HYC.Framework.Config.Editor.GUIDrawer.GetAddressableAddress(clip);
                if (string.IsNullOrEmpty(address)) address = clip.name;

                // 已经登记过同一资源就跳过，避免反复扫描灌一堆重复条目。
                if (catalog.FindByName(clip.name) != null) continue;
                bool dup = false;
                for (int i = 0; i < catalog.entries.Count; i++)
                {
                    var e = catalog.entries[i];
                    if (e != null && e.unityClip == clip) { dup = true; break; }
                }
                if (dup) continue;

                // 按物理文件夹分组：扫描进来的 clip 落到它所在的物理目录节点下，
                // 保证列表「显示的都是物理文件夹」。注册 AA 由用户在窗口里点「登记 AA」触发（扫描不自动改 AA 设置）。
                var clipPath = AssetDatabase.GetAssetPath(clip);
                var folderPath = string.IsNullOrEmpty(clipPath) ? "" : Path.GetDirectoryName(clipPath).Replace('\\', '/');
                catalog.EnsureFolder(folderPath);

                catalog.entries.Add(new AudioEntry
                {
                    id = catalog.NextId(),
                    displayName = clip.name,
                    description = clip.length.ToString("F2") + "s",
                    enabled = true,
                    channel = InferChannel(clip),
                    addressableKey = address,
                    unityClip = clip,
                    sourceFolder = folderPath,
                });
                added++;
            }
            return added;
        }

        /// <summary>
        /// 猜分组：按 FrameworkSettings 里可配置的通道推断规则匹配资源路径/名称（首命中优先），
        /// 无命中回退 Sfx。规则在 HYC Settings 的 Audio/Inference 编辑。只是初值，用户可在列表里改。
        /// </summary>
        internal static int InferChannel(AudioClip clip)
        {
            if (clip == null) return AudioChannelId.Sfx;
            var path = AssetDatabase.GetAssetPath(clip);
            // 路径 + 文件名一起当匹配文本（"Assets/Audio/Music/Click" + "/" + "Click"），
            // 这样目录名（music/ui/voice）和文件名前缀（ui_）都能命中。
            var haystack = (string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/') + "/") + clip.name;
            haystack = haystack.ToLowerInvariant();

            var rules = FrameworkSettings.Instance.audioChannelRules;
            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    if (rule == null || string.IsNullOrEmpty(rule.keyword)) continue;
                    if (haystack.Contains(rule.keyword.ToLowerInvariant()))
                        return rule.channel;
                }
            }
            return AudioChannelId.Sfx;
        }
    }
}
