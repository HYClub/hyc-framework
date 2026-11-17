using System;
using System.IO;
using System.Text;
using UnityEngine;
using HYC.Framework.Runtime;

namespace HYC.Framework.Save
{
    /// <summary>
    /// 存档文件的外壳。真正的数据在 <see cref="Payload"/> 里，
    /// 外层只带格式版本与时间戳，用于以后改格式时还有退路。
    /// </summary>
    [Serializable]
    public sealed class SaveEnvelope
    {
        public int formatVersion;
        public string typeKey;
        public long timestamp;
        public string payload;
    }

    /// <summary>
    /// 存档行为配置。不调用 <see cref="SaveManager.Init"/> 时用这里的默认值。
    /// </summary>
    public sealed class SaveOptions
    {
        private string _rootPath;

        /// <summary>
        /// 存档根目录，默认 <c>Application.persistentDataPath/Save</c>。
        /// 延迟到首次访问才取 persistentDataPath，避免在类型加载期读到空值。
        /// 测试里可以指到临时目录做隔离。
        /// </summary>
        public string RootPath
        {
            get { return _rootPath ?? (_rootPath = Path.Combine(Application.persistentDataPath, "Save")); }
            set { _rootPath = value; }
        }

        /// <summary>序列化器，默认 Newtonsoft。要零依赖可换成 <see cref="JsonUtilitySaveSerializer"/>。</summary>
        public ISaveSerializer Serializer = new NewtonsoftSaveSerializer();
    }

    /// <summary>
    /// 存储层：把一个可序列化对象按<b>类型</b>写盘、按类型读回。就这么大。
    ///
    /// 它<b>不关心</b>这些事，因为它们是使用方的表现层/玩法层概念，不是存储概念：
    /// 有没有"存档槽"、列表界面长什么样、每档显示什么摘要、什么时候该自动保存。
    /// 想要多个存档就自己存列表（<c>Save(new List&lt;PlayerProgress&gt;{...})</c>）
    /// 或自己组合类型，框架不知道"槽"是什么。
    ///
    /// 三条属于存储层本分的保证：
    /// ① 原子写（先写 .tmp，再用 <see cref="File.Replace(string,string,string)"/> 顶替正式文件，
    ///    旧版自动备份成 <c>.bak</c>）；不支持该 API 的平台改为「先备份再覆盖写」。
    ///    <c>Get&lt;T&gt;</c> 在主档缺失 / 损坏时回退 <c>.bak</c>，保证至少有一份完整数据可读；
    /// ② 坏档改名 .corrupt 备份，绝不静默覆盖 —— 静默覆盖等于进度凭空消失且无从追查；
    /// ③ 格式版本写在文件里，以后改格式有退路；
    /// ④ 删除存档会连 .tmp / .bak / .corrupt_* 一起清，避免 <c>Get&lt;T&gt;</c> 从备份里复活已删的档。
    ///
    /// 注意：读写都是<b>即时落盘、无内存缓存</b>，<c>Get</c> 会读文件。
    /// 别放在每帧循环里，取一次存成局部变量。
    /// </summary>
    public static class SaveManager
    {
        /// <summary>存档文件格式版本。与文件里记录的不一致时视为坏档。</summary>
        public const int FormatVersion = 1;

        private const string Extension = ".dat";

        private static SaveOptions _options = new SaveOptions();

        /// <summary>当前生效的配置。</summary>
        public static SaveOptions Options => _options;

        /// <summary>初始化。不调用则使用默认配置。可重复调用。</summary>
        public static void Init(SaveOptions options = null)
        {
            _options = options ?? new SaveOptions();
        }

        /// <summary>把 <paramref name="data"/> 按 <typeparamref name="T"/> 写盘。</summary>
        public static void Save<T>(T data)
        {
            var type = typeof(T);
            var path = PathOf(type);
            try
            {
                string payload;
                try
                {
                    payload = _options.Serializer.Serialize(data) ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "SaveManager.Save 序列化失败: " + type.FullName);
                    return;
                }

                var envelope = new SaveEnvelope
                {
                    formatVersion = FormatVersion,
                    typeKey = type.FullName ?? type.Name,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    payload = payload
                };

                var fileText = _options.Serializer.Serialize(envelope);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 原子写：先落 .tmp，再整体顶替正式文件，旧版顺手备份成 .bak。
                // 崩溃窗口只存在于 File.WriteAllText(tmp) 内部——此时正式文件完好无损；
                // File.Replace 要么全成功要么全不动，不存在"新旧都丢"的中间态。
                var tmp = path + ".tmp";
                var bak = path + ".bak";
                File.WriteAllText(tmp, fileText);
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, bak);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // WebGL 等不支持 File.Replace 的平台：先把旧档完整复制成 .bak，
                        // 再覆盖写正式文件。绝不 Delete 正式文件——删除与写入之间崩溃会同时失去两份；
                        // 覆盖写中途崩溃时 .bak 完整，Get<T> 会回退它。
                        File.Copy(path, bak, true);
                        File.Copy(tmp, path, true);
                        try { File.Delete(tmp); }
                        catch (Exception e) { Log.Warn("SaveManager.Save 清理 .tmp 失败: " + e.Message); }
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SaveManager.Save 写盘失败: " + path);
            }
        }

        /// <summary>
        /// 按 <typeparamref name="T"/> 读回。主档不存在 / 读不出来时回退上一版 <c>.bak</c> 并写回主档，
        /// 避免覆盖写中途崩溃后用户看起来「存档凭空消失」；两份都不可用时返回 <c>default</c>。
        /// 读不出来会先把原文件改名为 <c>.corrupt_*</c> 备份，不覆盖。
        /// </summary>
        public static T Get<T>()
        {
            var path = PathOf(typeof(T));
            if (TryReadEnvelope<T>(path, out var value, true))
                return value;

            var bak = path + ".bak";
            // 备份本身坏了不要改名：那样既丢了备份，又会让下面的回写找不到源文件。
            if (File.Exists(bak) && TryReadEnvelope<T>(bak, out var backup, false))
            {
                Log.Warn("SaveManager.Get 主档不可用，已从 .bak 恢复上一版: " + path);
                try
                {
                    File.Copy(bak, path, true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "SaveManager.Get 回写主档失败: " + path);
                }
                return backup;
            }
            return default;
        }

        /// <summary>读单个存档文件；失败返回 false。坏档按 <paramref name="backupOnCorrupt"/> 决定是否改名备份。</summary>
        private static bool TryReadEnvelope<T>(string path, out T value, bool backupOnCorrupt)
        {
            value = default;
            if (!File.Exists(path)) return false;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SaveManager.Get 读取失败: " + path);
                return false;
            }

            try
            {
                var envelope = _options.Serializer.Deserialize<SaveEnvelope>(text);
                if (envelope == null)
                {
                    if (backupOnCorrupt) BackupCorrupt(path);
                    return false;
                }
                if (envelope.formatVersion != FormatVersion)
                {
                    Log.Warn("SaveManager.Get 格式版本不匹配(文件 " + envelope.formatVersion
                        + " vs 当前 " + FormatVersion + ")，按坏档处理: " + path);
                    if (backupOnCorrupt) BackupCorrupt(path);
                    return false;
                }
                // 空 payload 是合法的「空档」，必须按读取成功返回；
                // 判成坏档会让下面的 .bak 回退翻出更早的数据，等于存档回滚。
                if (!string.IsNullOrEmpty(envelope.payload))
                    value = _options.Serializer.Deserialize<T>(envelope.payload);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SaveManager.Get 反序列化失败: " + path);
                if (backupOnCorrupt) BackupCorrupt(path);
                return false;
            }
        }

        /// <summary>
        /// 读回；不存在时新建一个（<b>不自动写盘</b>，什么时候落盘仍由调用方决定）。
        /// 这样调用点不用到处写 <c>?? new T()</c>。
        /// </summary>
        public static T GetOrCreate<T>() where T : new()
        {
            var existing = Get<T>();
            // 值类型装箱后恒不为 null，这条对 class 与 struct 都成立
            return existing == null ? new T() : existing;
        }

        /// <summary>主档或上一版备份存在即视为有档（<c>Get&lt;T&gt;</c> 会回退备份）。不解析内容。</summary>
        public static bool Has<T>()
        {
            var path = PathOf(typeof(T));
            return File.Exists(path) || File.Exists(path + ".bak");
        }

        /// <summary>删除 <typeparamref name="T"/> 的存档文件及其全部副产物。不存在则什么都不做。</summary>
        public static void Delete<T>()
        {
            var path = PathOf(typeof(T));
            try
            {
                DeleteArtifacts(path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SaveManager.Delete 失败: " + path);
            }
        }

        /// <summary>删除存档目录下全部存档文件及其副产物。目录本身保留。</summary>
        public static void DeleteAll()
        {
            try
            {
                var dir = _options.RootPath;
                if (!Directory.Exists(dir))
                {
                    return;
                }
                // 副产物也必须清：否则清空存档后 Get<T> 仍能从 .bak 读回旧数据。
                var files = Directory.GetFiles(dir, "*" + Extension + "*");
                for (int i = 0; i < files.Length; i++)
                {
                    File.Delete(files[i]);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SaveManager.DeleteAll 失败: " + _options.RootPath);
            }
        }

        /// <summary>删除主档与它的 .tmp / .bak / .corrupt_* 副产物。</summary>
        private static void DeleteArtifacts(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            var bak = path + ".bak";
            if (File.Exists(bak)) File.Delete(bak);

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, Path.GetFileName(path) + ".corrupt_*"))
                File.Delete(f);
        }

        /// <summary>把坏档改名备份，腾出原路径。备份失败不影响主流程。</summary>
        private static void BackupCorrupt(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }
                var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var dst = path + ".corrupt_" + stamp;
                if (File.Exists(dst))
                {
                    File.Delete(dst);
                }
                File.Move(path, dst);
                Log.Warn("SaveManager 坏档已备份为: " + dst + "（原存档未覆盖）");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SaveManager.BackupCorrupt 失败: " + path);
            }
        }

        /// <summary>类型键 → 文件路径。类型全名里的非法文件名字符（泛型/嵌套类）替换为下划线。</summary>
        private static string PathOf(Type type)
        {
            var key = type.FullName ?? type.Name;
            return Path.Combine(_options.RootPath, Sanitize(key) + Extension);
        }

        private static string Sanitize(string key)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(key.Length);
            for (int i = 0; i < key.Length; i++)
            {
                var c = key[i];
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
