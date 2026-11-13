using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>配置导出运行状态（供进度窗展示）。</summary>
    public enum ConfigExportPhase
    {
        Prepare,    // 保存资产 / 查重 / 清理旧 blob
        Exporting,  // 逐类型导出
        Finalizing, // 末尾单次 Refresh
        Done,
        Cancelled,
        Failed,
    }

    /// <summary>进度快照（值类型，整包传给进度窗）。</summary>
    public struct ConfigExportProgress
    {
        public ConfigExportPhase phase;
        public int totalJobs;
        public int completedJobs;
        public string currentLabel;
        public string currentStage;
        public int failedCount;
        public bool cancelled;
    }

    /// <summary>
    /// 非阻塞配置导出引擎。把「全量 / 文件夹 / 单类型 / 单实例」四种入口统一为
    /// 一个 <see cref="EditorApplication.update"/> 状态机：每帧只处理一个导出作业（一个配置类型或单个资产），
    /// 处理完即 return 把主线程交还 Unity——编辑器可正常 repaint / 响应输入 / 取消，
    /// 不再触发 Unity 的 "Hold on (busy for …)" 强制等待框。
    /// <para>
    /// 关键修复：循环内不再逐类型 <c>AssetDatabase.Refresh()</c>（原实现 N 类型 = N 次全量刷新，
    /// 是卡死主线程的主因），改为整个操作只在 <see cref="Finalize"/> 里 <c>Refresh()</c> 一次。
    /// 检查错误经 <see cref="ConfigBaseExport.CheckErrorSink"/> 汇聚进进度窗日志，结束后再一次性开窗。
    /// </para>
    /// <para>批模式（CI）不走本引擎，直接调用 <see cref="ConfigExportService"/> 的同步入口（保持原行为且仍受益於末尾单次 Refresh）。</para>
    /// </summary>
    public static class ConfigExportRunner
    {
        public static event Action<ConfigExportProgress> OnProgress;
        public static event Action<string, CheckErrorLevel> OnLog;
        public static event Action<bool, int, int, List<CheckError>> OnComplete;

        public static bool IsRunning => sRunning;

        private class Job
        {
            public Type type;
            public UnityEngine.Object singleAsset;
            public string label;
        }

        private static List<Job> sJobs;
        private static int sIndex;
        private static bool sRunning;
        private static bool sCancel;
        private static bool sClient;
        private static bool sServer;
        private static string sOpName;
        private static List<CheckError> sErrors;
        private static int sExported;
        private static int sFailed;
        private static ConfigExportProgress sProgress;

        // ---------- 入口 ----------

        public static void StartFullExport(bool client, bool server)
            => Begin(ConfigExportService.CollectConfigTypes(ConfigDataSettings.RootFolder), client, server, "全量清理后全量导出");

        public static void StartFolderExport(string folder, bool client, bool server)
            => Begin(ConfigExportService.CollectConfigTypes(folder), client, server, "保存并导出文件夹");

        public static void StartTypeExport(Type type, bool client, bool server)
            => Begin(new List<Type> { type }, client, server, "导出 " + ConfigTypeDisplay.GetShortName(type));

        public static void StartSingleExport(UnityEngine.Object asset, bool client, bool server)
        {
            if (asset == null) return;
            BeginJobs(new List<Job> { new Job { singleAsset = asset, label = asset.name } }, client, server, "导出 " + asset.name);
        }

        public static void RequestCancel() => sCancel = true;

        // ---------- 启动 ----------

        private static void Begin(List<Type> types, bool client, bool server, string opName)
        {
            var jobs = new List<Job>(types.Count);
            foreach (var t in types)
                jobs.Add(new Job { type = t, label = t.Name });
            BeginJobs(jobs, client, server, opName);
        }

        private static void BeginJobs(List<Job> jobs, bool client, bool server, string opName)
        {
            if (sRunning)
            {
                Debug.LogWarning("[Config] 已有导出任务在运行，忽略本次请求");
                return;
            }
            if (Application.isBatchMode)
            {
                RunSynchronous(jobs, client, server); // CI：同步路径，保持原行为
                return;
            }

            sJobs = jobs;
            sIndex = 0;
            sRunning = true;
            sCancel = false;
            sClient = client;
            sServer = server;
            sOpName = opName;
            sErrors = new List<CheckError>();
            sExported = 0;
            sFailed = 0;
            sProgress = new ConfigExportProgress
            {
                phase = ConfigExportPhase.Prepare,
                totalJobs = jobs.Count,
                completedJobs = 0,
                currentLabel = opName,
                currentStage = "准备中…",
            };
            ConfigBaseExport.CheckErrorSink = AccumulateError;

            // 同步准备（含可能弹出的查重对话框——必须在主线程菜单回调里做，不能在 update 里弹窗）：
            // 落盘全部资产 + ID/GUID 查重 + 清理旧 blob。这部分体量小，不属于卡死主因。
            if (!PrepareSync())
            {
                ConfigBaseExport.CheckErrorSink = BuildErrorWindow.Report;
                sRunning = false; // 复位运行标志，否则后续导出会被误判为"运行中"
                return; // 用户取消查重 → 不启动
            }

            ConfigExportWindow.ShowWindow();
            EditorApplication.update += Tick;
            EmitProgress();
        }

        private static void RunSynchronous(List<Job> jobs, bool client, bool server)
        {
            // 批模式：走同步入口（内部已改为末尾单次 Refresh，仍受益）
            foreach (var job in jobs)
            {
                if (job.singleAsset != null)
                    ConfigExportService.ExportSingle(job.singleAsset, client, server);
                else
                    ConfigExportService.ExportAll(job.type, client, server);
            }
        }

        // ---------- 帧驱动状态机 ----------

        private static void Tick()
        {
            if (sIndex >= sJobs.Count || sCancel)
            {
                var finishedOk = !sCancel && sIndex >= sJobs.Count && sFailed == 0;
                Finalize(finishedOk, sCancel);
                return;
            }

            var job = sJobs[sIndex];
            sProgress.phase = ConfigExportPhase.Exporting;
            sProgress.currentLabel = job.label;
            sProgress.completedJobs = sIndex;
            sProgress.currentStage = job.singleAsset != null ? "导出单个资产" : "检查 + 写 Blob/JSON";
            EmitProgress();

            bool ok;
            try
            {
                ok = job.singleAsset != null
                    ? ConfigExportService.ExportSingle(job.singleAsset, sClient, sServer, false)
                    : ConfigExportService.ExportAll(job.type, sClient, sServer, false);
            }
            catch (Exception ex)
            {
                ok = false;
                Log($"导出 {job.label} 异常：{ex.Message}", CheckErrorLevel.Error);
            }

            if (ok)
            {
                sExported++;
                Log($"导出 {job.label} 完成", CheckErrorLevel.Info);
            }
            else
            {
                sFailed++;
                Log($"导出 {job.label} 失败", CheckErrorLevel.Error);
            }

            sIndex++;
            // 让出主线程：下一帧再处理下一个作业（编辑器保持响应，不触发 Hold on）
            ConfigExportWindow.RepaintWindow();
        }

        /// <summary>同步准备：落盘全部资产 → ID/GUID 查重（可能弹窗）→ 清理旧 blob。返回是否继续。</summary>
        private static bool PrepareSync()
        {
            ConfigExportService.SaveFolder(ConfigDataSettings.RootFolder);
            var all = ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder);
            if (!ConfigIdService.EnsureUniqueBeforeExport(all, sOpName))
                return false; // 用户取消查重 → 中止
            if (sClient) ConfigExportService.ClearBlobDir(ConfigDataSettings.ClientExportDir);
            if (sServer) ConfigExportService.ClearBlobDir(ConfigDataSettings.ServerExportDir);
            Log("准备完成，开始导出", CheckErrorLevel.Info);
            return true;
        }

        // ---------- 收尾（单次 Refresh） ----------

        private static void Finalize(bool success, bool cancelled = false)
        {
            EditorApplication.update -= Tick;
            sRunning = false;
            ConfigBaseExport.CheckErrorSink = BuildErrorWindow.Report; // 还原默认

            sProgress.phase = ConfigExportPhase.Finalizing;
            sProgress.currentStage = "刷新资产数据库…";
            EmitProgress();

            // 整个操作只在结尾 Refresh 一次（原实现在循环内逐类型 Refresh，是卡死主因）
            AssetDatabase.Refresh();

            sProgress.phase = cancelled ? ConfigExportPhase.Cancelled : (success ? ConfigExportPhase.Done : ConfigExportPhase.Failed);
            sProgress.completedJobs = sJobs.Count;
            sProgress.failedCount = sFailed;
            sProgress.cancelled = cancelled;
            EmitProgress();

            if (sErrors.Count > 0)
                BuildErrorWindow.OpenWindow(sErrors);

            // 导出后刷新左树（清除 * 脏标记等）
            var w = EditorWindow.GetWindow<ConfigDataWindow>();
            w?.RefreshTree();

            OnComplete?.Invoke(success && !cancelled, sExported, sFailed, sErrors);
            Log(cancelled ? "导出已取消（已导出项保留在磁盘）"
                : success ? $"导出完成：{sExported} 类成功，{sFailed} 类失败"
                          : $"导出结束：{sExported} 类成功，{sFailed} 类失败",
                cancelled ? CheckErrorLevel.Warning : CheckErrorLevel.Info);
        }

        // ---------- 辅助 ----------

        private static void AccumulateError(List<CheckError> errs, string ctx)
        {
            if (errs == null || errs.Count == 0) return;
            foreach (var e in errs)
            {
                sErrors.Add(e);
                OnLog?.Invoke($"[{ctx}] {e.Message}", e.Level);
            }
        }

        private static void Log(string msg, CheckErrorLevel level)
            => OnLog?.Invoke(msg, level);

        private static void EmitProgress()
            => OnProgress?.Invoke(sProgress);
    }
}
