using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Editor.Export
{
    /// <summary>
    /// HYC 通用导出引擎。所有导出（配置 / 行为树 / 场景内容 / 地图 / 音频…）都走这里：
    /// <see cref="Run"/> 收一个 <see cref="HyExportRequest"/>，弹 <see cref="HyExportWindow"/> 模态窗，
    /// 由窗口每帧调 <see cref="Step"/> 推进，结束后把 <see cref="HyExportSummary"/> 交回调用方。
    /// <para>
    /// 推进节奏：**每帧只走一行**，且分两拍——上一拍把该行标成「进行中」让窗口画出来，下一拍才真正执行，
    /// 所以每行都能看到「进行中 → 打勾」。prepare / finalize 这两段原子重活同样两拍（先画阶段文案）。
    /// </para>
    /// <para>
    /// 为什么由窗口 OnGUI 驱动而不是 <c>EditorApplication.update</c>：模态窗走原生
    /// <c>ContainerWindowBindings::MakeModal</c>，那个消息循环不跑 <c>EditorApplication.update</c>。
    /// </para>
    /// </summary>
    public static class HyExportEngine
    {
        private static HyExportRequest sRequest;
        private static readonly List<HyExportRow> sRows = new List<HyExportRow>();
        private static readonly List<HyExportError> sErrors = new List<HyExportError>();

        private static int sIndex;
        private static int sActiveRow = -1; // 已标「进行中」但还没执行的行
        private static bool sNotify;        // 「重活开始前先画一帧」的标记
        private static bool sAbort;
        private static int sSucceeded;
        private static int sFailed;
        private static string sStage = "";

        public static bool Running { get; private set; }
        public static HyExportPhase Phase { get; private set; }
        public static string Title => sRequest?.title ?? "";
        public static string Description => sRequest?.description ?? "";
        public static bool Destructive => sRequest != null && sRequest.destructive;
        public static int Total => sRows.Count;
        public static int Succeeded => sSucceeded;
        public static int Failed => sFailed;
        public static int CurrentIndex => sActiveRow;
        public static string Stage => sStage;
        public static IReadOnlyList<HyExportRow> Rows => sRows;
        public static IReadOnlyList<HyExportError> Errors => sErrors;

        // ---------- 入口 ----------

        /// <summary>
        /// 走完整流程：建快照 → 弹模态窗（批模式则同步跑）→ 关窗后回调 <see cref="HyExportRequest.onFinished"/>。
        /// 返回本次结果；重复调用（已有导出在跑）返回 null。
        /// </summary>
        public static HyExportSummary Run(HyExportRequest request)
        {
            if (request == null) return null;
            if (Running)
            {
                Debug.LogWarning("[Export] 已有导出在运行，忽略本次请求");
                return null;
            }

            Bind(request);

            try
            {
                if (Application.isBatchMode)
                    RunSynchronous(); // CI：不弹窗，同步跑
                else
                    HyExportWindow.RunModal(); // 阻塞到用户点「关闭」
            }
            catch (Exception ex)
            {
                // 建窗 / 模态循环本身出错：绝不能把 Running 留在 true，否则之后每次导出都被挡
                Debug.LogError("[Export] 导出流程异常：" + ex);
                Report(HyExportLevel.Error, request.title, ex.Message);
                Phase = HyExportPhase.Failed;
            }
            finally
            {
                Running = false;
            }

            var summary = BuildSummary();

            var callback = request.onFinished;
            if (callback != null)
            {
                try
                {
                    callback(summary); // 已经退出模态，这里可以开窗 / 弹框 / 刷面板
                }
                catch (Exception ex)
                {
                    Debug.LogError("[Export] onFinished 回调异常：" + ex);
                }
            }
            return summary;
        }

        private static void Bind(HyExportRequest request)
        {
            sRequest = request;
            sRows.Clear();
            sErrors.Clear();
            sIndex = 0;
            sActiveRow = -1;
            sNotify = false;
            sAbort = false;
            sSucceeded = 0;
            sFailed = 0;
            sStage = "";
            Phase = HyExportPhase.Preview;
            Running = true;

            var jobs = request.jobs;
            for (var i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                sRows.Add(new HyExportRow
                {
                    name = j != null && !string.IsNullOrEmpty(j.name) ? j.name : "(空作业)",
                    count = j != null ? j.count : 0,
                    unit = j != null && j.unit != null ? j.unit : "", // 窗口按 Length 判空，不能留 null
                    state = HyExportRowState.Pending,
                });
            }
        }

        private static HyExportSummary BuildSummary()
        {
            var summary = new HyExportSummary
            {
                phase = Phase,
                total = sRows.Count,
                succeeded = sSucceeded,
                failed = sFailed,
            };
            summary.errors.AddRange(sErrors);
            return summary;
        }

        // ---------- 窗口驱动 ----------

        /// <summary>窗口点「执行」（危险操作已二次确认）：进入执行阶段。</summary>
        public static void StartExecution()
        {
            if (!Running || Phase != HyExportPhase.Preview) return;
            sAbort = false;
            sNotify = false;
            if (sRequest.prepare != null)
            {
                Phase = HyExportPhase.Preparing;
                sStage = sRequest.prepareLabel;
            }
            else
            {
                Phase = HyExportPhase.Working;
                sStage = "";
            }
        }

        /// <summary>窗口点「取消」（仅预览态）：什么都不做，零副作用。</summary>
        public static void CancelPreview()
        {
            if (!Running || Phase != HyExportPhase.Preview) return;
            Phase = HyExportPhase.Cancelled;
            Running = false;
        }

        /// <summary>窗口点「中止」：当前行做完即停，已完成产物保留。</summary>
        public static void RequestAbort()
        {
            if (Running) sAbort = true;
        }

        /// <summary>强制退出运行态（窗口绘制异常兜底）。</summary>
        public static void Abort()
        {
            if (!Running) return;
            sAbort = true;
            Phase = HyExportPhase.Failed;
            Running = false;
        }

        /// <summary>记录一条检查项 / 失败原因，同时落 Console。</summary>
        public static void Report(HyExportLevel level, string context, string message)
        {
            sErrors.Add(new HyExportError(level, context, message));
            var text = $"[{context}] {message}";
            if (level == HyExportLevel.Error) Debug.LogError(text);
            else if (level == HyExportLevel.Warning) Debug.LogWarning(text);
            else Debug.Log(text);
        }

        /// <summary>推进一步。返回 true 表示还有工作。任何异常都收敛为 Failed，避免把用户锁死在模态里。</summary>
        public static bool Step()
        {
            if (!Running) return false;
            if (Phase == HyExportPhase.Preview) return true; // 预览态不推进，等用户点执行
            try
            {
                return StepInternal();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Export] 导出步进异常：" + ex);
                Report(HyExportLevel.Error, Title, ex.Message);
                Phase = HyExportPhase.Failed;
                Running = false;
                return false;
            }
        }

        private static bool StepInternal()
        {
            switch (Phase)
            {
                case HyExportPhase.Preparing:
                    if (!sNotify)
                    {
                        sNotify = true;
                        sStage = sRequest.prepareLabel;
                        return true; // 先让窗口把这句文案画出来，下一拍才干活
                    }
                    sNotify = false;
                    if (!Invoke(sRequest.prepare, sRequest.prepareLabel))
                    {
                        Phase = HyExportPhase.Failed;
                        Running = false;
                        return false;
                    }
                    Phase = HyExportPhase.Working;
                    sStage = "";
                    return true;

                case HyExportPhase.Working:
                {
                    // 上一拍占位的行：这一拍真正干活
                    if (sActiveRow >= 0)
                    {
                        RunJob(sActiveRow);
                        sIndex = sActiveRow + 1;
                        sActiveRow = -1;
                        if (sAbort)
                        {
                            sStage = "已中止";
                            Phase = HyExportPhase.Aborted;
                            Running = false;
                            return false;
                        }
                    }

                    if (sIndex >= sRows.Count)
                    {
                        if (sRequest.finalize != null)
                        {
                            sNotify = false;
                            Phase = HyExportPhase.Finalizing;
                            return true;
                        }
                        Finish();
                        return false;
                    }

                    // 占位拍：先把这一行标成「进行中」并让窗口画出来，下一拍才执行。
                    // 否则一个作业在同一帧里 Pending→Done，用户永远看不到「进行中」。
                    sActiveRow = sIndex;
                    EnterJob(sIndex);
                    return true;
                }

                case HyExportPhase.Finalizing:
                    if (!sNotify)
                    {
                        sNotify = true;
                        sStage = sRequest.finalizeLabel;
                        return true;
                    }
                    sNotify = false;
                    Invoke(sRequest.finalize, sRequest.finalizeLabel); // 收尾失败不翻转整体结果
                    Finish();
                    return false;

                default:
                    return false;
            }
        }

        private static void Finish()
        {
            sStage = "完成";
            Phase = sFailed > 0 ? HyExportPhase.Failed : HyExportPhase.Done;
            Running = false;
        }

        private static bool Invoke(Func<bool> action, string label)
        {
            if (action == null) return true;
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Export] {label}异常：" + ex);
                Report(HyExportLevel.Error, Title, label + "异常：" + ex.Message);
                return false;
            }
        }

        /// <summary>进入作业：把该行标成「进行中」并更新「当前」文案（此时还没干活）。</summary>
        private static void EnterJob(int i)
        {
            var row = sRows[i];
            row.state = HyExportRowState.Active;
            sRows[i] = row;
            sStage = sRequest.jobs[i]?.name ?? "";
        }

        /// <summary>执行作业并落该行最终状态。</summary>
        private static void RunJob(int i)
        {
            var job = sRequest.jobs[i];
            var ok = Invoke(job?.run, job?.name ?? "(空作业)");

            var row = sRows[i];
            row.state = ok ? HyExportRowState.Done : HyExportRowState.Failed;
            sRows[i] = row;

            if (ok) sSucceeded++;
            else sFailed++;
        }

        // ---------- 批模式（CI） ----------

        private static void RunSynchronous()
        {
            Phase = HyExportPhase.Preparing;
            sStage = sRequest.prepareLabel;
            if (!Invoke(sRequest.prepare, sRequest.prepareLabel))
            {
                Phase = HyExportPhase.Failed;
                Running = false;
                return;
            }

            Phase = HyExportPhase.Working;
            for (var i = 0; i < sRows.Count; i++)
            {
                EnterJob(i);
                RunJob(i);
            }

            if (sRequest.finalize != null)
            {
                Phase = HyExportPhase.Finalizing;
                sStage = sRequest.finalizeLabel;
                Invoke(sRequest.finalize, sRequest.finalizeLabel);
            }
            Finish();
        }
    }
}
