// ============================================================
// HYC Framework - 音频总览资产
// 文件: Runtime/Audio/AudioCatalogAsset.cs
// 说明: 音频总览的 [CfgAsset] ScriptableObject。单独成文件是因为 Unity
//       只为「类名 == 文件名」的类型生成 MonoScript；原先它与 AudioCatalog
//       静态类同处 AudioCatalog.cs，创建资产时拿不到 MonoScript（m_Script = 0），
//       entries 无法序列化。其余音频数据类型见 AudioCatalog.cs。
// ============================================================

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using HYC.Framework.Config;

namespace HYC.Framework.Audio
{
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
}
