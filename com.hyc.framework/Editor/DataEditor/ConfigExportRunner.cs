using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置导出入口。这里只做三件事：ID/GUID 预检 → 把「要导出的东西」拆成作业行 →
    /// 交给 <see cref="HYC.Framework.Editor.Export.HyExportEngine"/> 弹通用导出窗；窗口关闭后再收尾（错误窗 / 刷左树 / 日志）。
    /// <para>行粒度＝一个配置类型（单资产导出＝一行）。窗口、分帧推进、逐行打勾都在通用层，这里不再自带 UI。</para>
    /// </summary>
    public static class ConfigExportRunner
    {
        private static readonly List<CheckError> sCheckErrors = new List<CheckError>();

        // ---------- 入口 ----------

        /// <summary>全量清理后全量导出（危险：会删旧 blob）。</summary>
        public static void StartFullExport(bool client, bool server)
        {
            const string op = "全量清理后全量导出";
            var all = Precheck(op);
            if (all == null) return;

            var req = NewRequest(op, true,
                "将删除客户端 / 服务器导出目录下所有旧 *.blob，再重新导出全部配置。" +
                "改名或删除的配置类型不会留下幽灵文件。");
            req.prepareLabel = "保存全部配置资产 + 清理旧 blob…";
            req.prepare = () =>
            {
                ConfigExportService.SaveFolder(ConfigDataSettings.RootFolder);
                if (client) ConfigExportService.ClearBlobDir(ConfigDataSettings.ClientExportDir);
                if (server) ConfigExportService.ClearBlobDir(ConfigDataSettings.ServerExportDir);
                return true;
            };
            AddTypeJobs(req, ConfigExportService.CollectConfigTypes(ConfigDataSettings.RootFolder), all, client, server);
            Launch(req, op);
        }

        /// <summary>保存并导出某个文件夹下所有配置（先落盘，再按类型批量导出）。</summary>
        public static void StartFolderExport(string folder, bool client, bool server)
        {
            const string op = "保存并导出文件夹";
            if (string.IsNullOrEmpty(folder)) return;
            var all = Precheck(op);
            if (all == null) return;

            var req = NewRequest(op, false, $"先落盘 {folder} 下全部配置资产，再按类型逐个导出为 Blob / JSON。");
            req.prepareLabel = "保存配置资产…";
            req.prepare = () =>
            {
                ConfigExportService.SaveFolder(folder);
                return true;
            };
            AddTypeJobs(req, ConfigExportService.CollectConfigTypes(folder), all, client, server);
            Launch(req, op);
        }

        /// <summary>导出单个配置类型（整类型一份产物）。<paramref name="saveFolder"/> 非空时先落盘该文件夹。</summary>
        public static void StartTypeExport(Type type, bool client, bool server, string saveFolder = null)
        {
            if (type == null) return;
            var op = "导出 " + type.Name;
            var all = Precheck(op);
            if (all == null) return;

            var req = NewRequest(op, false, null);
            if (!string.IsNullOrEmpty(saveFolder))
            {
                req.prepareLabel = "保存配置资产…";
                req.prepare = () =>
                {
                    ConfigExportService.SaveFolder(saveFolder);
                    return true;
                };
            }
            AddTypeJobs(req, new List<Type> { type }, all, client, server);
            Launch(req, op);
        }

        /// <summary>导出单个配置实例。</summary>
        public static void StartSingleExport(UnityEngine.Object asset, bool client, bool server)
        {
            var so = asset as ScriptableObject;
            if (so == null) return;
            var op = "导出 " + so.name;
            var all = Precheck(op, so);
            if (all == null) return;

            var req = NewRequest(op, false, null);
            req.jobs.Add(new HYC.Framework.Editor.Export.HyExportJob
            {
                name = so.name,
                count = 1,
                unit = "个资产",
                run = () => ConfigExportService.ExportSingle(so, client, server, false),
            });
            Launch(req, op);
        }

        /// <summary>保存并导出枚举定义（一个枚举一行）。</summary>
        public static void StartEnumExport(IReadOnlyList<ConfigEnumDefinition> defs, string opName = "保存导出所有枚举")
        {
            if (defs == null || defs.Count == 0) return;

            var req = NewRequest(opName, false, null);
            req.prepareLabel = "保存枚举定义…";
            req.prepare = () =>
            {
                for (var i = 0; i < defs.Count; i++)
                {
                    EditorUtility.SetDirty(defs[i]);
                    AssetDatabase.SaveAssetIfDirty(defs[i]);
                }
                return true;
            };
            for (var i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                req.jobs.Add(new HYC.Framework.Editor.Export.HyExportJob
                {
                    name = def.className,
                    // 逐枚举各自 Refresh 会变成 N 次全量刷新，统一交给 finalize 刷一次
                    run = () => ConfigEnumCodeGen.Export(def, true, true, false),
                });
            }
            Launch(req, opName);
        }

        // ---------- 组装 ----------

        private static HYC.Framework.Editor.Export.HyExportRequest NewRequest(string title, bool destructive, string description)
        {
            return new HYC.Framework.Editor.Export.HyExportRequest
            {
                title = title,
                destructive = destructive,
                description = description,
                finalizeLabel = "刷新资产数据库…",
                finalize = () =>
                {
                    AssetDatabase.Refresh(); // 整个操作唯一一次全量刷新
                    return true;
                },
            };
        }

        /// <summary>
        /// 一次遍历把资产按**精确类型**分组：窗口计数与真正导出的资产列表来自同一份分组，
        /// 生成期不再各自 FindAssets；注入 <see cref="ConfigExportService.ExportAll"/> 后，
        /// 每个作业也不再跑全工程扫描。
        /// </summary>
        private static void AddTypeJobs(HYC.Framework.Editor.Export.HyExportRequest req,
                                        List<Type> types, List<ScriptableObject> all,
                                        bool client, bool server)
        {
            if (types == null) return;
            var groups = GroupByExactType(all);
            for (var i = 0; i < types.Count; i++)
            {
                var type = types[i];
                var assets = groups.TryGetValue(type, out var list) ? list : null;
                req.jobs.Add(new HYC.Framework.Editor.Export.HyExportJob
                {
                    name = type.Name,
                    count = assets != null ? assets.Count : 0,
                    unit = "个资产",
                    // 必须整类型导出：Blob/JSON 一类型一个产物，逐资产导出会互相覆盖
                    run = () => ConfigExportService.ExportAll(type, client, server, false, assets),
                });
            }
        }

        /// <summary>按 GetType()==键 精确分组（不把派生类型算进基类），与 CollectAssets 口径一致。</summary>
        private static Dictionary<Type, List<ConfigBase>> GroupByExactType(List<ScriptableObject> all)
        {
            var groups = new Dictionary<Type, List<ConfigBase>>();
            if (all == null) return groups;
            for (var i = 0; i < all.Count; i++)
            {
                var so = all[i];
                if (so == null) continue;               // Unity 伪 null：已销毁
                var cb = so as ConfigBase;
                if (cb == null) continue;               // 枚举 / 模板等不进导出分组
                var t = so.GetType();
                if (!groups.TryGetValue(t, out var list))
                {
                    list = new List<ConfigBase>();
                    groups[t] = list;
                }
                list.Add(cb);
            }
            return groups;
        }

        /// <summary>
        /// ID/GUID 预检（可能弹出重新派发对话框）。**必须在弹模态窗前**：模态循环里不能弹框。
        /// 返回 null 表示用户放弃，调用方直接返回。
        /// </summary>
        private static List<ScriptableObject> Precheck(string opName, ScriptableObject extra = null)
        {
            var all = ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder);
            if (extra != null && !all.Contains(extra))
                all.Add(extra); // 单资产导出可能不在根目录下
            return ConfigIdService.EnsureUniqueBeforeExport(all, opName) ? all : null;
        }

        // ---------- 执行与收尾 ----------

        private static void Launch(HYC.Framework.Editor.Export.HyExportRequest req, string opName)
        {
            sCheckErrors.Clear();
            req.onFinished = summary => Finish(summary, opName);

            // 模态循环里不能弹框：检查结果先收进引擎，窗口关闭后再一次性开错误窗
            ConfigBaseExport.CheckErrorSink = Accumulate;
            try
            {
                HYC.Framework.Editor.Export.HyExportEngine.Run(req);
            }
            finally
            {
                ConfigBaseExport.CheckErrorSink = BuildErrorWindow.Report;
            }
        }

        private static void Accumulate(List<CheckError> errors, string context)
        {
            if (errors == null || errors.Count == 0) return;
            BuildErrorWindow.MarkReported(); // 结束后统一开窗，别再弹「未发现问题」
            for (var i = 0; i < errors.Count; i++)
            {
                var e = errors[i];
                sCheckErrors.Add(e);
                HYC.Framework.Editor.Export.HyExportEngine.Report(ToLevel(e.Level), context, e.Message);
            }
        }

        private static HYC.Framework.Editor.Export.HyExportLevel ToLevel(CheckErrorLevel level)
        {
            if (level == CheckErrorLevel.Error) return HYC.Framework.Editor.Export.HyExportLevel.Error;
            return level == CheckErrorLevel.Warning ? HYC.Framework.Editor.Export.HyExportLevel.Warning : HYC.Framework.Editor.Export.HyExportLevel.Info;
        }

        private static void Finish(HYC.Framework.Editor.Export.HyExportSummary summary, string opName)
        {
            if (summary == null) return;

            // 中止 / 取消：finalize（末尾 Refresh）没跑，这里补齐——已完成的类型产物还是要落盘
            var finished = summary.phase == HYC.Framework.Editor.Export.HyExportPhase.Done || summary.phase == HYC.Framework.Editor.Export.HyExportPhase.Failed;
            if (!finished && summary.Processed > 0)
                AssetDatabase.Refresh();

            BuildErrorWindow.EndExportOperation(opName, summary.AllOk);
            if (sCheckErrors.Count > 0)
                BuildErrorWindow.OpenWindow(sCheckErrors);
            RefreshDataWindows();
            Debug.Log($"[Config] {opName} 结束：{summary.succeeded} 成功，{summary.failed} 失败，" +
                      $"共 {summary.total}（{summary.phase}）");
        }

        /// <summary>刷新已打开的数据配置窗口左树（不主动创建窗口——导出不该顺手弹出别的面板）。</summary>
        private static void RefreshDataWindows()
        {
            var objs = Resources.FindObjectsOfTypeAll(typeof(ConfigDataWindow));
            for (var i = 0; i < objs.Length; i++)
            {
                var w = objs[i] as ConfigDataWindow;
                if (w != null) w.RefreshTree(); // 用 Unity 重载的 == null，跳过已销毁实例
            }
        }
    }
}
