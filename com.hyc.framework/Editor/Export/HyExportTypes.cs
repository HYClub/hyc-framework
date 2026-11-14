using System;
using System.Collections.Generic;

namespace HYC.Framework.Editor.Export
{
    /// <summary>导出过程中的记录级别。</summary>
    public enum HyExportLevel
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>导出过程中一条记录（检查项 / 失败原因）。</summary>
    public sealed class HyExportError
    {
        public HyExportLevel level;
        public string context;
        public string message;

        public HyExportError() { }

        public HyExportError(HyExportLevel level, string context, string message)
        {
            this.level = level;
            this.context = context;
            this.message = message;
        }
    }

    /// <summary>
    /// 导出窗里的一行 = 一个作业。<see cref="run"/> 是**回调**：这一行具体干什么由调用方决定，
    /// 通用层只负责排队、打勾、汇总。
    /// </summary>
    public sealed class HyExportJob
    {
        /// <summary>行标题（配置类型名 / 关卡名 / 树名…）。</summary>
        public string name = "";

        /// <summary>右侧数量（资产数 / 关卡数…），仅显示用。</summary>
        public int count;

        /// <summary>数量单位，如「个资产」「棵」。留空则只显示数字。</summary>
        public string unit = "";

        /// <summary>执行体，返回是否成功。抛异常按失败处理。</summary>
        public Func<bool> run;
    }

    /// <summary>导出窗 / 引擎所处阶段。</summary>
    public enum HyExportPhase
    {
        /// <summary>预览：一行都还没跑，可以取消，零副作用。</summary>
        Preview,
        /// <summary>执行前的原子重活（落盘 / 清空旧产物），不可切片。</summary>
        Preparing,
        /// <summary>逐行执行。</summary>
        Working,
        /// <summary>收尾的原子重活（末尾 Refresh / 同步清单），不可切片。</summary>
        Finalizing,
        Done,
        Failed,
        /// <summary>预览态点了取消，什么都没做。</summary>
        Cancelled,
        /// <summary>执行中点了中止（当前行做完即停）。</summary>
        Aborted,
    }

    /// <summary>单行的显示状态。</summary>
    public enum HyExportRowState
    {
        Pending,
        Active,
        Done,
        Failed,
    }

    /// <summary>进度窗一行的快照。窗口每帧从这里拉数据，不走事件。</summary>
    public struct HyExportRow
    {
        public string name;
        public int count;
        public string unit;
        public HyExportRowState state;
    }

    /// <summary>一次导出的结果。窗口关闭后才交给调用方，用于开错误窗 / 刷面板 / 打日志。</summary>
    public sealed class HyExportSummary
    {
        public HyExportPhase phase;
        public int total;
        public int succeeded;
        public int failed;
        public readonly List<HyExportError> errors = new List<HyExportError>();

        /// <summary>已处理行数（成功 + 失败）。</summary>
        public int Processed => succeeded + failed;

        /// <summary>执行中被中止。</summary>
        public bool Aborted => phase == HyExportPhase.Aborted;

        /// <summary>预览态取消，或窗口异常销毁，总之没跑完。</summary>
        public bool NotStarted => phase == HyExportPhase.Cancelled || phase == HyExportPhase.Preview;

        /// <summary>全部跑完且零失败。</summary>
        public bool AllOk => phase == HyExportPhase.Done && failed == 0;
    }

    /// <summary>
    /// 一次导出请求。调用方只描述「做什么」，窗口长什么样、怎么分帧推进由通用层负责。
    /// <para>
    /// 构造 <see cref="jobs"/> 必须廉价：它在窗口弹出前同步执行。重活放进 <see cref="prepare"/> /
    /// <see cref="finalize"/> 或某一行的 <see cref="HyExportJob.run"/>。
    /// </para>
    /// </summary>
    public sealed class HyExportRequest
    {
        /// <summary>窗口头部操作名，也用于日志。</summary>
        public string title = "导出";

        /// <summary>预览态的说明文字。危险操作的警示就写在这里，不再弹确认框。</summary>
        public string description;

        /// <summary>危险操作（会删文件）：执行按钮用红色，且需要点两次确认。</summary>
        public bool destructive;

        public readonly List<HyExportJob> jobs = new List<HyExportJob>();

        /// <summary>执行前的原子重活（落盘 / 清空旧产物）。null 跳过；返回 false 视为整体失败。</summary>
        public Func<bool> prepare;

        public string prepareLabel = "准备中…";

        /// <summary>全部行之后的原子重活（末尾 Refresh / 同步清单）。null 跳过。</summary>
        public Func<bool> finalize;

        public string finalizeLabel = "收尾中…";

        /// <summary>窗口关闭后的收尾回调（开错误窗 / 刷新面板 / 打日志）。此时已退出模态。</summary>
        public Action<HyExportSummary> onFinished;
    }
}
