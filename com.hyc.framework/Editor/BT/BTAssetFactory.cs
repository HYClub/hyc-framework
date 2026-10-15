// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTAssetFactory.cs
// 说明: 行为树资产唯一创建入口（"万能 AI 接口"，对应 ConfigAssetFactory）。
//       任何代码 / AI 创建行为树都必须走这里，确保 TreeId 一定由本厂统一派发，
//       禁止直接 ScriptableObject.CreateInstance<BTTreeAsset>() + 手填 TreeId
//       （time-ticks / 0 都会撞车，子树引用靠 TreeId 选树）。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    /// <summary>
    /// 行为树资产唯一创建入口。
    ///
    /// 与 <see cref="HYC.Framework.Config.Editor.ConfigAssetFactory"/> 平行：配置走发号服，
    /// 行为树的身份是 <c>long TreeId</c>（独立 long 空间，不接 ConfigIdService）。
    /// TreeId 由本厂以<b>本机指纹（MAC）派生</b>派发：高 48 位取机器 MAC、低 15 位为本机段内序号，
    /// 跨机天然隔离、无需局域网发号服也不会撞；同一工程内旧的小号 TreeId 由扫描兜底。
    ///
    /// 用法：
    /// <code>
    /// var t  = BTAssetFactory.Create("EnemyAI");               // 单棵，自动派发 TreeId
    /// var list = BTAssetFactory.CreateMany("Mob", 10);          // 批量
    /// var t2 = BTAssetFactory.Create("Boss", withRoot: true);   // 顺手补一个 Root 节点
    /// </code>
    /// </summary>
    public static class BTAssetFactory
    {
        /// <summary>行为树资产默认根目录（与 BTGraphIMGUI.BT_ROOT 保持一致）。</summary>
        public const string BT_ROOT = "Assets/BTTrees";

        /// <summary>创建单棵行为树并派发唯一 TreeId。folder 默认 <see cref="BT_ROOT"/>。</summary>
        public static BTTreeAsset Create(string treeName, string folder = null, bool withRoot = false)
        {
            if (string.IsNullOrEmpty(treeName)) treeName = "NewTree";
            var targetFolder = string.IsNullOrEmpty(folder) ? BT_ROOT : folder.Replace("\\", "/").Trim('/');
            EnsureFolder(targetFolder);

            var assetName = SanitizeFileName(treeName);
            var fullPath = AssetDatabase.GenerateUniqueAssetPath(targetFolder + "/" + assetName + ".asset");

            var tree = ScriptableObject.CreateInstance<BTTreeAsset>();
            tree.name = Path.GetFileNameWithoutExtension(fullPath);
            tree.TreeId = NextTreeId();
            if (withRoot) tree.EnsureRootNode();
            AssetDatabase.CreateAsset(tree, fullPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return tree;
        }

        /// <summary>批量创建：<c>prefix_1 … prefix_N</c>，逐个派发唯一 TreeId；任一失败整体回滚已创建资产。</summary>
        public static IReadOnlyList<BTTreeAsset> CreateMany(string prefix, int count, string folder = null, bool withRoot = false)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "count 必须 >= 1");
            if (string.IsNullOrEmpty(prefix)) prefix = "BTTree";

            var targetFolder = string.IsNullOrEmpty(folder) ? BT_ROOT : folder.Replace("\\", "/").Trim('/');
            EnsureFolder(targetFolder);

            var list = new List<BTTreeAsset>(count);
            try
            {
                for (var i = 1; i <= count; i++)
                    list.Add(Create($"{prefix}_{i}", targetFolder, withRoot));
            }
            catch
            {
                foreach (var t in list)
                {
                    var p = AssetDatabase.GetAssetPath(t);
                    if (!string.IsNullOrEmpty(p)) AssetDatabase.DeleteAsset(p);
                }
                throw;
            }
            return list;
        }

        /// <summary>菜单入口：新建一棵行为树（取代移除的 [CreateAssetMenu]，统一走工厂）。</summary>
        [MenuItem("HYC Framework/BT/New Tree", false, 12)]
        public static void CreateFromMenu() => Create("NewTree");

        // ---------- 内部 ----------

        // ---------- 多机唯一 TreeId（本机指纹派生，无需服务器）----------

        /// <summary>每机可分配的本地序号位数：2^15 = 32768 棵/机。高 48 位来自机器 MAC，跨机天然隔离。</summary>
        private const int LocalSeqBits = 15;
        private const long LocalSeqMask = (1L << LocalSeqBits) - 1;
        private static long? sMachineSeed;

        /// <summary>
        /// 机器指纹种子（高 48 位 = 主网卡 MAC，左移 <see cref="LocalSeqBits"/>）。不同机器 MAC 不同
        /// → 高 48 位天然隔离，跨机 TreeId 不会撞（无需局域网发号服）。取不到 MAC 时退化为主机名 FNV-1a 哈希。
        /// </summary>
        private static long MachineSeed()
        {
            if (sMachineSeed.HasValue) return sMachineSeed.Value;
            ulong mac = PrimaryMacAddress();
            if (mac == 0) mac = Fnv1a64(Environment.MachineName) & 0xFFFFFFFFFFFFUL;
            var seed = ((long)(mac << LocalSeqBits)) & 0x7FFFFFFFFFFFFFFF;
            sMachineSeed = seed;
            return seed;
        }

        private static ulong PrimaryMacAddress()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var b = ni.GetPhysicalAddress().GetAddressBytes();
                    if (b.Length != 6) continue;
                    ulong v = 0;
                    foreach (var x in b) v = (v << 8) | x;
                    if (v != 0) return v;
                }
            }
            catch { }
            return 0;
        }

        private static ulong Fnv1a64(string s)
        {
            const ulong offset = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            ulong h = offset;
            foreach (var c in s)
            {
                h ^= (byte)c;
                h *= prime;
            }
            return h;
        }

        /// <summary>
        /// 派发唯一 TreeId：高 48 位取本机 MAC 种子，低 15 位在本机段内取最小未占用序号
        /// （扫本地现有 BTTreeAsset.TreeId 去重）。跨机因 MAC 不同而段隔离，无需服务器也不会撞；
        /// 同一工程内旧的小号 TreeId 也由扫描兜底。本机段用尽（极不可能）时退回全局最小空闲。
        /// 供工厂内部与"先建内存对象、后落盘"流程（转换为子树、零资产临时树）取 ID。
        /// </summary>
        public static long NextTreeId()
        {
            var used = new HashSet<long>();
            foreach (var g in AssetDatabase.FindAssets("t:BTTreeAsset"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var t = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(p);
                if (t != null) used.Add(t.TreeId);
            }
            long seed = MachineSeed();
            for (long seq = 1; seq <= LocalSeqMask; seq++)
            {
                long cand = seed | seq;
                if (cand != 0 && !used.Contains(cand)) return cand;
            }
            long fallback = 1;
            while (used.Contains(fallback)) fallback++;
            return fallback;
        }

        // ---------- 节点 ID（树内唯一，工厂统管）----------

        /// <summary>
        /// 派发<b>本树内唯一</b>节点 ID：扫 tree.Nodes 现有 NodeId，返回最小未占用正整数（≠0）。
        /// 节点 ID 仅在本树内使用（子树引用走 TreeId，不跨树），故只需树内唯一；
        /// 由本厂统一派发，外部 / AI 不得手填，彻底消除原 GUID 哈希不校验导致的理论碰撞。
        /// </summary>
        public static long NextNodeId(BTTreeAsset tree)
        {
            var used = new HashSet<long>();
            CollectUsedNodeIds(tree, used);
            return FirstFreeNodeId(used);
        }

        /// <summary>
        /// 批量派发重载：extra 传入本次批量内已预留的 ID（节点尚未写入 tree.Nodes 时使用），
        /// 避免同批多次调用互相撞（复制分支 / 转子树先建内存对象后落盘）。
        /// </summary>
        public static long NextNodeId(BTTreeAsset tree, HashSet<long> extra)
        {
            var used = new HashSet<long>();
            CollectUsedNodeIds(tree, used);
            if (extra != null)
                foreach (var r in extra)
                    if (r != 0) used.Add(r);
            return FirstFreeNodeId(used);
        }

        private static void CollectUsedNodeIds(BTTreeAsset tree, HashSet<long> used)
        {
            if (tree == null) return;
            foreach (var n in tree.Nodes)
                if (n.NodeId != 0) used.Add(n.NodeId);
        }

        private static long FirstFreeNodeId(HashSet<long> used)
        {
            long id = 1;
            while (used.Contains(id)) id++;
            return id;
        }

        /// <summary>逐级创建目录（必须在 Assets 下）；已存在则跳过。</summary>
        private static void EnsureFolder(string folder)
        {
            if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"行为树目录必须在 Assets 下：{folder}", nameof(folder));
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        private static string SanitizeFileName(string name)
        {
            var s = name.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c.ToString(), "_");
            return string.IsNullOrEmpty(s) ? "BTTree" : s;
        }
    }
}
