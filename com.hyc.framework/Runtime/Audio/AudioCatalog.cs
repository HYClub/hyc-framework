// ============================================================
// HYC Framework - 音频总览数据结构
// 文件: Runtime/Audio/AudioCatalog.cs
// 说明: 音频清单的 Cfg 数据类型。这是框架自带的一个音频数据结构，
//       与其他配置类型完全同构（[CfgAsset] 注册 → 配置树可见可编辑 → 导出 Blob）。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using HYC.Framework.Config;
using HYC.Framework.IO;
namespace HYC.Framework.Audio
{
    /// <summary>音频总览里的一条记录。框架内置类型，用户不需要自己写。</summary>
    [Serializable]
    public class AudioEntry
    {
        /// <summary>条目标识。配置表字段如果存 id 型引用，用的就是它。</summary>
        public long id;

        /// <summary>显示名（界面与下拉里看到的名字）。</summary>
        public string displayName = "";

        /// <summary>备注 / 用途说明。</summary>
        public string description = "";

        /// <summary>是否启用。禁用后门面播放会跳过（由 <see cref="AudioManagerWindow"/> 导出时过滤）。</summary>
        public bool enabled = true;

        /// <summary>音量分组 id（0 = Master 固定全局，其余见 <see cref="HYC.Framework.Runtime.FrameworkSettings.audioGroups"/>）。</summary>
        public int channel = 2; // 默认 Sfx（id 2）

        /// <summary>Addressables 地址。拖 AudioClip 时由框架自动填地址。</summary>
        public string addressableKey = "";

        /// <summary>Wwise 事件名。由扫描 SoundBanksInfo.json 填充。</summary>
        public string wwiseEvent = "";

        /// <summary>框架默认（Addressables）模式下拖入的 AudioClip 引用。地址不手填——由它从 AA 设置自动解析进 <see cref="addressableKey"/>；编辑器只读列展示该地址。</summary>
        public AudioClip unityClip;

        /// <summary>来源物理文件夹（Assets 相对路径，如 "Assets/Audio/music"）。列表据此把条目归到对应文件夹节点下；为空表示未分组。</summary>
        public string sourceFolder = "";
    }

    /// <summary>音频总览里的一个「物理文件夹」节点。列表按此做树形分组——拖文件夹 / 扫描 clip 会自动建节点，也可手动新建空节点。它是实体（即使无子条目也存在），承载整组批量操作与 Addressables 登记。</summary>
    [Serializable]
    public class AudioFolderNode
    {
        /// <summary>节点标识。</summary>
        public long id;

        /// <summary>物理文件夹路径（Assets 相对，如 "Assets/Audio/music"）。</summary>
        public string folderPath = "";

        /// <summary>展示名（路径末段，如 "music"）。</summary>
        public string displayName = "";

        /// <summary>该组默认音量组 id，供「批量改音量组」使用。</summary>
        public int channel = 2; // 默认 Sfx（id 2）
    }

    /// <summary>
    /// 音频总览配置资产。挂在配置树「音频/音频总览」下，
    /// 编辑方式与其他配置类型一致（列表 + 字段编辑）。
    ///
    /// <c>Unique = true</c>：全工程只允许一个音频总览。配置表的「框架/音频」字段
    /// 下拉要枚举全局条目，多份总览会让同一个引用在不同表里指向不同东西。
    /// </summary>
    [CfgAsset("音频/音频总览", 60, true, Hide = true)]
    public class AudioCatalogAsset : ScriptableObject
    {
        /// <summary>音频条目列表。</summary>
        public List<AudioEntry> entries = new List<AudioEntry>();

        /// <summary>物理文件夹节点（树形分组）。即使没有子条目也存在（实体节点）。</summary>
        public List<AudioFolderNode> folders = new List<AudioFolderNode>();

        /// <summary>按 id 查条目，未找到返回 null。</summary>
        public AudioEntry FindById(long id)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].id == id) return entries[i];
            return null;
        }

        /// <summary>按显示名查条目，未找到返回 null。</summary>
        public AudioEntry FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].displayName == name) return entries[i];
            return null;
        }

        /// <summary>下一个可用 id（当前最大值 +1）。条目与文件夹节点共用一套 id 空间，避免查找时撞号。</summary>
        public long NextId()
        {
            long max = 0;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].id > max) max = entries[i].id;
            for (int i = 0; i < folders.Count; i++)
                if (folders[i] != null && folders[i].id > max) max = folders[i].id;
            return max + 1;
        }

        /// <summary>按物理文件夹路径取节点；不存在则新建（不登记 Addressables，登记由编辑器负责）。返回 null 表示路径为空。</summary>
        public AudioFolderNode EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            for (int i = 0; i < folders.Count; i++)
                if (folders[i] != null && folders[i].folderPath == path) return folders[i];
            var node = new AudioFolderNode
            {
                id = NextId(),
                folderPath = path,
                displayName = Path.GetFileName(path.TrimEnd('/', '\\')),
            };
            folders.Add(node);
            return node;
        }
    }

    /// <summary>Blob 里的音频条目（blittable，进 Blob 无字符串）。</summary>
    public struct AudioCatalogRow
    {
        /// <summary>条目标识。</summary>
        public long Id;

        /// <summary>对应哪一路地址：0 = Addressables，1 = Wwise。</summary>
        public int Target;

        /// <summary>音量分组。</summary>
        public int Channel;

        /// <summary>地址（Addressables key）或 Wwise 事件名。</summary>
        public BlobString Ref;
    }

    /// <summary>
    /// 音频总览 Blob 根。
    ///
    /// <b>不要在这里加 <c>where</c> 约束</b>：本类型不是泛型声明，C# 不允许
    /// （CS0080: Constraints are not allowed on non-generic declarations）。
    /// 框架的 <c>BlobRoot&lt;TRow&gt;</c> 能加是因为它<b>是泛型</b>。
    ///
    /// unmanaged 校验不靠约束，靠 <see cref="BlobArray{T}"/> 自身：
    /// 它要求 T 满足 unmanaged，所以 <c>AudioCatalogRow</c> 一旦含托管引用
    /// （如 AudioClip / string）就会在编译期直接报错，不必额外声明。
    /// </summary>
    public struct AudioCatalogBlob
    {
        /// <summary>全部条目。</summary>
        public BlobArray<AudioCatalogRow> Rows;
    }

    /// <summary>
    /// 音频总览的<b>托管</b>查询结果。
    ///
    /// 为什么要有这个 DTO、而不是直接返回 <see cref="AudioCatalogRow"/>：
    /// 行类型 <c>AudioCatalogRow</c> 住在 blob 存储里且含 <see cref="BlobString"/>，
    /// 分析器禁止把这种类型<b>按值作为 out 参数 / 返回值</b>传出（<b>EA0009</b>：
    /// "You may only access AudioCatalogRow by (non-)readonly ref"）。
    /// 框架的 <c>ConfigBlobTable&lt;TRow&gt;</c> 之所以能 <c>out TRow</c>，是因为它带
    /// <c>where TRow : unmanaged</c> 约束 —— 含 BlobString 的行不在那个覆盖范围内。
    ///
    /// 所以：Blob → 托管的转换<b>在 Snapshot 里一次做完</b>，BlobString 就地转成
    /// <see cref="string"/>，之后全程只有托管类型，Blob 类型不越过 API 边界。
    /// </summary>
    public struct AudioCatalogEntry
    {
        /// <summary>条目标识。</summary>
        public long Id;

        /// <summary>对应哪一路：0 = Addressables，1 = Wwise。</summary>
        public int Target;

        /// <summary>音量分组。</summary>
        public int Channel;

        /// <summary>地址（Addressables key）或 Wwise 事件名，已是托管字符串。</summary>
        public string Ref;
    }

    /// <summary>
    /// 音频总览运行期查询。门面按引用字符串路由时用它把引用换成实际地址。
    /// 数据来自 <see cref="AudioCatalogBlobSystem"/> 加载的 Blob。
    /// </summary>
    public static class AudioCatalog
    {
        private static BlobAssetReference<AudioCatalogBlob> _ref;
        private static bool _loaded;

        /// <summary>是否已加载到 Blob 数据。</summary>
        public static bool IsLoaded => _loaded && _ref.IsCreated;

        /// <summary>条目数，未加载时为 0。</summary>
        public static unsafe int Count
        {
            get
            {
                if (!IsLoaded) return 0;
                ref var rows = ref _ref.Value.Rows;
                return rows.Length;
            }
        }

        /// <summary>
        /// Blob 版本写在 <see cref="AudioCatalogBlobSystem.Version"/>（唯一定义处，
        /// 导出窗口也用它）。这里不另设一份常量，避免两个真相源走偏。
        /// </summary>

        /// <summary>直接接管一份 Blob（测试与自定义加载路径用）。</summary>
        public static void Adopt(BlobAssetReference<AudioCatalogBlob> blobRef)
        {
            Release();
            _ref = blobRef;
            _loaded = blobRef.IsCreated;
        }

        /// <summary>
        /// 把 Blob 里的全部行**转成托管 DTO 数组**（BlobString 在此就地转成 string）。
        ///
        /// 为什么必须在 unsafe 里做：Blob 数组只能按 ref 访问（EA0001），
        /// 而 <c>AudioCatalogRow</c> 住在 blob 里、含 <see cref="BlobString"/>，
        /// 禁止按值传出（EA0009）。转换必须<b>就地</b>完成，一旦离开 blob 就说不清了。
        ///
        /// 代价是每次查询一次数组分配 + 一次字符串分配。调用方应缓存结果，
        /// 不要在每帧播放路径上反复调 —— 门面只在"引用未直接可用"时才查它。
        /// </summary>
        private static unsafe AudioCatalogEntry[] Snapshot()
        {
            if (!IsLoaded) return Array.Empty<AudioCatalogEntry>();

            ref var rows = ref _ref.Value.Rows;
            var copy = new AudioCatalogEntry[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                ref var src = ref rows[i];
                copy[i] = new AudioCatalogEntry
                {
                    Id = src.Id,
                    Target = src.Target,
                    Channel = src.Channel,
                    Ref = src.Ref.Length <= 0 ? string.Empty : src.Ref.ToString(),
                };
            }
            return copy;
        }

        /// <summary>
        /// 按 id 查条目。找不到返回 false。
        /// </summary>
        /// <param name="key">条目 id。</param>
        /// <param name="entry">输出：命中的条目（托管类型）。</param>
        public static bool TryGet(long key, out AudioCatalogEntry entry)
        {
            entry = default;
            var all = Snapshot();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Id != key) continue;
                entry = all[i];
                return true;
            }
            return false;
        }

        /// <summary>
        /// 按引用字符串查（走 <see cref="AudioRefPrefix"/> 决定匹配哪一路）。
        /// 找不到返回 false —— 门面据此直接用手上的字符串播，不强制要求已登记。
        /// </summary>
        public static bool TryGetByRef(string audioRef, out AudioCatalogEntry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(audioRef)) return false;

            var isClip = AudioRefPrefix.TryParse(audioRef, out var target);
            var wantTarget = isClip ? 0 : 1;
            if (string.IsNullOrEmpty(target)) return false;

            var all = Snapshot();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Target != wantTarget) continue;
                if (all[i].Ref != target) continue;
                entry = all[i];
                return true;
            }
            return false;
        }

        /// <summary>遍历全部条目（窗口列列表 / 批量预检用）。</summary>
        public static IEnumerable<AudioCatalogEntry> All() => Snapshot();

        /// <summary>释放 Blob。</summary>
        public static void Release()
        {
            if (_ref.IsCreated) _ref.Dispose();
            _ref = default;
            _loaded = false;
        }
    }

    /// <summary>
    /// 音频总览 Blob 加载器。从 <c>StreamingAssets/AudioBlob</c> 读
    /// <c>AudioCatalog.blob</c>，与 Loc / BT 的 Blob 管线同构。
    /// 播放前不需要它 —— 门面拿到引用字符串会直接用；但窗口与批量预检会读它。
    /// </summary>
    public static class AudioCatalogBlobSystem
    {
        /// <summary>StreamingAssets 子目录。</summary>
        public const string DefaultFolderName = "AudioBlob";

        /// <summary>Blob 文件名。</summary>
        public const string BlobFileName = "AudioCatalog.blob";

        /// <summary>
        /// Blob 版本号。<b>必须与导出端一致</b>（窗口用
        /// <c>BlobAssetReference&lt;AudioCatalogBlob&gt;.Write(builder, path, AudioCatalogBlobSystem.Version)</c>
        /// 写入）——版本不一致 <see cref="BlobAssetReference{T}.TryRead"/> 会失败而不是读到错位数据。
        /// </summary>
        public const int Version = 1;

        /// <summary>
        /// 从 StreamingAssets 加载音频总览 Blob。与 <c>LocalizationBlobSystem</c> 同一套
        /// 自动加载模式：进 Play 前先试一次，已加载则跳过（避免重复注册）。
        ///
        /// 没导出过 Blob 时只警告不报错 —— 门面在总览缺失时仍能播
        /// （回退到直接用引用里的地址），所以这不是致命错误。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoLoadAtPlay()
        {
            if (!AudioCatalog.IsLoaded) Load();
        }

        /// <summary>从 StreamingAssets 加载音频总览 Blob。</summary>
        /// <returns>是否加载成功。</returns>
        public static bool Load()
        {
            var relative = $"{DefaultFolderName}/{BlobFileName}";
            var bytes = StreamingAssets.ReadBytes(relative);
            if (bytes == null)
            {
                Debug.LogWarning($"[HYC.Audio] 未找到音频总览 Blob: {relative}。" +
                                 "请在编辑器执行 HYC Framework/Audio/Export Catalog Blob。");
                return false;
            }

            // 与 BTBlobLoader 同一套 API：TryRead(ptr, length, version, out blob)。
            // 不用 CreateFromMemory —— 那个 API 不存在（Entities 1.0）。
            unsafe
            {
                fixed (byte* ptr = bytes)
                {
                    using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                    if (!BlobAssetReference<AudioCatalogBlob>.TryRead(reader, Version, out var blobRef))
                    {
                        Debug.LogError($"[HYC.Audio] 音频总览 Blob 读取失败（文件损坏或版本不一致）: {relative}");
                        return false;
                    }
                    AudioCatalog.Adopt(blobRef);
                }
            }
            return true;
        }
    }
}
