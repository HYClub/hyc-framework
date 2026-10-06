// =============================================================================
//  HYCSimple · 配置系统示例 · 运行时演示（数据文件驱动）
//  演示「配置 = 数据」：真实项目里这张表来自 Excel/CSV → 导出 blob。
//  本例把一份真实物品数据放在 Config/Resources/items.csv，运行期读取后
//  Build 成 ConfigBlobTable<HycSimpleItemRow> 并注册进 ConfigManager，再读取验证。
//  这样示例就不再是「代码里手敲 3 行」，而是真正从外部数据加载真实配置。
//
//  ⚠️ 刻意标了 [DisableAutoCreation]：示例 System 若自动进宿主工程的 World，会悄悄
//     往 ConfigManager 里注册示例表、改写其他静态单例，干扰宿主。
//     改由 HYCSimpleSceneBootstrap（用户显式挂到场景才生效）手动 Update。
// =============================================================================
using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using HYC.Framework.Config;
using HYC.Simple.Config;

namespace HYC.Simple
{
    /// <summary>一次性演示：从 CSV 读真实配置数据 → 建表 → 注册 → 读取。默认不自动进 World。</summary>
    [DisableAutoCreation]
    public partial class HYCSimpleConfigDemoSystem : SystemBase
    {
        private bool _seeded;

        protected override void OnUpdate()
        {
            if (_seeded) return;
            _seeded = true;

            // 1) 从「数据文件」读取真实配置（不是写死在代码里）
            var rows = LoadRowsFromCsv();
            if (rows == null || rows.Count == 0)
            {
                Debug.LogWarning("[HYCSimple] 未读到 items.csv，回退到内置 3 行示例数据。");
                rows = FallbackRows();
            }

            // 2) 建 id→行序 索引（Build 第二个参数）
            var index = new List<KeyValuePair<long, int>>();
            for (int i = 0; i < rows.Count; i++)
                index.Add(new KeyValuePair<long, int>(rows[i].Id, i));

            // 3) 建表并注册（ConfigBlobTable<TRow>.Build 接受任意 unmanaged 行）
            var table = ConfigBlobTable<HycSimpleItemRow>.Build(rows, index);
            ConfigManager.Register(table);

            // 4) 读取验证
            Debug.Log($"[HYCSimple] 配置表加载：{rows.Count} 行，已注册到 ConfigManager（HycSimpleItemRow）。");
            if (ConfigManager.TryGet<HycSimpleItemRow>(1002, out var cfg))
                Debug.Log($"[HYCSimple] 配置演示：查 Id=1002 → " +
                          $"Price={cfg.Price} StackMax={cfg.StackMax} Weight={cfg.Weight} Rarity={cfg.Rarity} Category={cfg.Category}");
            Debug.Log($"[HYCSimple] 配置表总行数 GetRowCount = {ConfigManager.GetRowCount<HycSimpleItemRow>()}");
        }

        /// <summary>从 Resources/items.csv 读取（跨编辑器/真机稳定）。列：Id,Name,Price,StackMax,Weight,Rarity,Category。</summary>
        private static List<HycSimpleItemRow> LoadRowsFromCsv()
        {
            var ta = Resources.Load<TextAsset>("items");
            if (ta == null) return null;
            var lines = ta.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return null;
            var list = new List<HycSimpleItemRow>();
            for (int i = 1; i < lines.Length; i++) // 跳过表头
            {
                var c = lines[i].Split(',');
                if (c.Length < 7) continue;
                list.Add(new HycSimpleItemRow
                {
                    Id = long.Parse(c[0]),
                    Price = int.Parse(c[2]),
                    StackMax = int.Parse(c[3]),
                    Weight = float.Parse(c[4]),
                    Rarity = int.Parse(c[5]),
                    Category = int.Parse(c[6]),
                });
            }
            return list;
        }

        /// <summary>读不到 CSV 时的兜底（保证示例始终可跑）。</summary>
        private static List<HycSimpleItemRow> FallbackRows()
        {
            return new List<HycSimpleItemRow>
            {
                new HycSimpleItemRow { Id = 1001, Price = 50,  StackMax = 99, Weight = 1.2f, Rarity = 1, Category = 1 },
                new HycSimpleItemRow { Id = 1002, Price = 120, StackMax = 10, Weight = 0.3f, Rarity = 2, Category = 2 },
                new HycSimpleItemRow { Id = 1003, Price = 0,   StackMax = 1,  Weight = 0.1f, Rarity = 3, Category = 3 },
            };
        }
    }
}
