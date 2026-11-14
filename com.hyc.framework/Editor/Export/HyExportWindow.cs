using System;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Editor.Export
{
    /// <summary>
    /// HYC 通用导出窗（**模态**、无关闭按钮），三态：
    /// <list type="bullet">
    /// <item>预览：列出全部行与数量，[取消] / [执行]；危险操作点两次才真的开始。零副作用。</item>
    /// <item>执行：按钮只剩 [中止]，逐行「进行中 → 打勾 → 淡出移除」。</item>
    /// <item>结束：结果条（全部完成 / 有失败 / 已中止 / 已取消）+ [关闭]。</item>
    /// </list>
    /// <para>
    /// 推进靠本窗 OnGUI 每帧调 <see cref="HyExportEngine.Step"/>：<c>ShowModalUtility</c> 的模态消息循环
    /// 不跑 <c>EditorApplication.update</c>。状态推进与行移除只在 <see cref="EventType.Layout"/> 阶段做一次，
    /// 保证同帧 Layout / Repaint 的可见行集合一致（否则 IMGUI 报不匹配并把界面画花）。
    /// </para>
    /// </summary>
    public class HyExportWindow : EditorWindow
    {
        private const float RowHeight = 21f;
        private const float SolidSeconds = 0.40f; // 打勾后原样停留
        private const float FadeSeconds = 0.22f;  // 淡出时长，到点移除该行
        private const float RemoveAfter = SolidSeconds + FadeSeconds;

        private Vector2 mScroll;
        private bool mStepping = true;
        private bool mArmed;   // 危险操作：已点过一次「执行」，等第二次确认
        private bool mClosing; // 关闭只做一次
        private string mFatal;

        // 逐行动画状态：只在 Layout 阶段改，绘制阶段只读
        private HyExportRowState[] mState;
        private double[] mDoneAt;
        private bool[] mRemoved;

        private static GUIStyle sMarkSt, sNameSt, sCountSt, sStateSt, sRightSt, sHeadSt, sDescSt;

        private static readonly Color Accent = new Color(0.29f, 0.54f, 0.96f);
        private static readonly Color OkColor = new Color(0.42f, 0.75f, 0.42f);
        private static readonly Color ErrColor = new Color(0.88f, 0.42f, 0.46f);
        private static readonly Color WarnColor = new Color(0.86f, 0.72f, 0.42f);

        /// <summary>打开模态窗并阻塞到用户点「关闭」。由 <see cref="HyExportEngine.Run"/> 调用。</summary>
        public static void RunModal()
        {
            var w = CreateInstance<HyExportWindow>();
            var title = HyExportEngine.Title;
            w.titleContent = new GUIContent(string.IsNullOrEmpty(title) ? "导出" : title);
            w.minSize = new Vector2(560f, 440f);

            // 居中到主窗口
            var main = EditorGUIUtility.GetMainWindowPosition();
            const float width = 620f, height = 540f;
            w.position = new Rect(
                Mathf.Round(main.x + (main.width - width) * 0.5f),
                Mathf.Round(main.y + (main.height - height) * 0.5f),
                width, height);

            w.ShowModalUtility(); // 阻塞，直到窗口 Close()
        }

        private void OnEnable()
        {
            var n = HyExportEngine.Total;
            mState = new HyExportRowState[n];
            mDoneAt = new double[n];
            mRemoved = new bool[n];
            for (var i = 0; i < n; i++)
            {
                mState[i] = HyExportRowState.Pending;
                mDoneAt[i] = -1;
            }
            mStepping = true;
            mFatal = null;
            // 兜底：万一模态循环里重绘调度变稀，鼠标在本窗上移动也会触发一次 OnGUI 推进
            wantsMouseMove = true;
        }

        private void OnDestroy()
        {
            // 窗口被任何方式销毁：别让引擎停在「运行中」把后续导出全挡掉
            if (HyExportEngine.Running) HyExportEngine.Abort();
        }

        private void OnGUI()
        {
            try
            {
                if (Event.current.type == EventType.Layout)
                    Mutate();
                Draw();
            }
            catch (ExitGUIException)
            {
                throw; // SafeClose 之后的正常退出信号，不能当绘制异常处理
            }
            catch (Exception ex)
            {
                if (mFatal == null)
                {
                    mFatal = ex.Message;
                    mStepping = false;
                    Debug.LogError("[Export] 导出窗绘制异常：" + ex);
                    HyExportEngine.Abort(); // 必须退出运行态，否则底部不会出现「关闭」
                }
                DrawFallback(); // 绝对定位兜底，保证用户永远能退出去
            }
        }

        /// <summary>
        /// 关闭窗口。**这里必须同步 Close，不能按 §9③ 挂 delayCall**：模态窗的关闭动作就是
        /// <c>ShowModalUtility</c> 嵌套循环的退出条件，而 delayCall 走 <c>EditorApplication.update</c>，
        /// 那个循环里根本不跑——挂 delayCall 会把用户永久锁在模态里。
        /// 幂等靠 <see cref="mClosing"/>；调用方紧接着 <c>GUIUtility.ExitGUI()</c> 中止本帧绘制。
        /// </summary>
        private void SafeClose()
        {
            if (mClosing) return;
            mClosing = true;
            if (this == null) return; // Unity 伪 null：已被销毁
            Close();
        }

        // ---------- 状态推进（每帧一次） ----------

        private void Mutate()
        {
            var now = EditorApplication.timeSinceStartup;
            var working = HyExportEngine.Running && HyExportEngine.Phase != HyExportPhase.Preview;

            if (working && mStepping && mFatal == null)
            {
                if (!HyExportEngine.Step())
                    mStepping = false;
            }

            var rows = HyExportEngine.Rows;
            var count = Mathf.Min(mState.Length, rows.Count);
            for (var i = 0; i < count; i++)
            {
                var st = rows[i].state;
                if (st != mState[i])
                {
                    mState[i] = st;
                    if (st == HyExportRowState.Done) mDoneAt[i] = now;
                }
                if (!mRemoved[i] && mDoneAt[i] >= 0 && now - mDoneAt[i] >= RemoveAfter)
                    mRemoved[i] = true;
            }

            // 还有行在做「打勾 → 淡出」就继续重绘，否则动画会停在半路
            var settling = false;
            for (var i = 0; i < count; i++)
            {
                if (!mRemoved[i] && mDoneAt[i] >= 0) { settling = true; break; }
            }
            if (working || settling) Repaint();
        }

        // ---------- 绘制 ----------

        private void Draw()
        {
            EnsureStyles();

            var phase = HyExportEngine.Phase;
            var isPreview = phase == HyExportPhase.Preview;
            var running = HyExportEngine.Running;
            var succeeded = HyExportEngine.Succeeded;
            var failed = HyExportEngine.Failed;
            var processed = succeeded + failed;
            var total = HyExportEngine.Total;

            GUILayout.Space(6f);
            DrawHeader(phase, isPreview, total, succeeded, processed);
            if (isPreview)
            {
                GUILayout.Space(4f);
                DrawDescription(HyExportEngine.Description, HyExportEngine.Destructive);
            }
            GUILayout.Space(6f);
            DrawProgress(phase, isPreview, total, processed);
            GUILayout.Space(6f);
            DrawList(isPreview);
            GUILayout.Space(4f);
            if (!running && !isPreview)
            {
                DrawResult(phase, total, succeeded, failed);
                GUILayout.Space(4f);
            }
            DrawFooter(isPreview, running, total);
        }

        private void DrawHeader(HyExportPhase phase, bool isPreview, int total, int succeeded, int processed)
        {
            var badge = BadgeText(phase);
            var color = BadgeColor(phase);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            LabelInline(badge, EditorStyles.miniLabel, color, GUILayout.Width(46f));
            GUILayout.Label(HyExportEngine.Title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            LabelInline(
                $"已完成 {succeeded} · 待处理 {Mathf.Max(0, total - processed)} · 共 {total}",
                EditorStyles.miniLabel, Dim(1f));
            EditorGUILayout.EndHorizontal();
        }

        private static string BadgeText(HyExportPhase phase)
        {
            switch (phase)
            {
                case HyExportPhase.Preview: return "待执行";
                case HyExportPhase.Preparing: return "准备";
                case HyExportPhase.Working: return "执行中";
                case HyExportPhase.Finalizing: return "收尾";
                case HyExportPhase.Done: return "完成";
                case HyExportPhase.Cancelled: return "已取消";
                case HyExportPhase.Aborted: return "已中止";
                default: return "有失败";
            }
        }

        private Color BadgeColor(HyExportPhase phase)
        {
            switch (phase)
            {
                case HyExportPhase.Preview: return HyExportEngine.Destructive ? ErrColor : Accent;
                case HyExportPhase.Preparing:
                case HyExportPhase.Working:
                case HyExportPhase.Finalizing: return WarnColor;
                case HyExportPhase.Done: return OkColor;
                case HyExportPhase.Cancelled:
                case HyExportPhase.Aborted:
                case HyExportPhase.Failed: return ErrColor;
                default: return ErrColor;
            }
        }

        /// <summary>预览态的说明块（危险操作的警示写这里，不再弹确认框）。</summary>
        private void DrawDescription(string text, bool danger)
        {
            if (string.IsNullOrEmpty(text)) return;

            var content = new GUIContent((danger ? "⚠ " : "ℹ ") + text);
            var innerWidth = Mathf.Max(60f, position.width - 28f - 18f);
            var height = sDescSt.CalcHeight(content, innerWidth) + 14f;

            var rect = EditorGUILayout.GetControlRect(false, height);
            var tint = danger ? ErrColor : new Color(0.44f, 0.70f, 0.85f);
            EditorGUI.DrawRect(rect, new Color(tint.r, tint.g, tint.b, 0.10f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(tint.r, tint.g, tint.b, 0.45f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(tint.r, tint.g, tint.b, 0.45f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), new Color(tint.r, tint.g, tint.b, 0.45f));
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(tint.r, tint.g, tint.b, 0.45f));

            var prev = GUI.color;
            GUI.color = danger ? new Color(0.95f, 0.78f, 0.80f) : new Color(0.78f, 0.87f, 0.93f);
            GUI.Label(new Rect(rect.x + 9f, rect.y + 7f, innerWidth, rect.height - 14f), content, sDescSt);
            GUI.color = prev;
        }

        private void DrawProgress(HyExportPhase phase, bool isPreview, int total, int processed)
        {
            var frac = total > 0 ? (float)processed / total : 0f;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("总体进度", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            string pct;
            if (isPreview) pct = "0%";
            else if (phase == HyExportPhase.Preparing) pct = "准备中…";
            else if (phase == HyExportPhase.Finalizing) pct = "收尾中…";
            else pct = total > 0 ? $"{frac * 100f:0}%" : "—";
            LabelInline(pct, sRightSt, isPreview ? Accent : phase == HyExportPhase.Done ? OkColor : Accent,
                        GUILayout.Width(70f));
            EditorGUILayout.EndHorizontal();

            var rect = EditorGUILayout.GetControlRect(false, 14f);
            EditorGUI.ProgressBar(rect, frac, "");

            GUILayout.Space(3f);
            LabelInline(CurrentLine(phase, isPreview), EditorStyles.miniLabel, Dim(1f));
        }

        private static string CurrentLine(HyExportPhase phase, bool isPreview)
        {
            if (isPreview) return "当前：—（点「执行」开始）";

            var stage = HyExportEngine.Stage;
            if (phase == HyExportPhase.Preparing || phase == HyExportPhase.Finalizing)
                return "当前：" + stage;

            if (phase == HyExportPhase.Done) return "当前：完成";
            if (phase == HyExportPhase.Aborted) return "当前：已中止";
            if (phase == HyExportPhase.Cancelled) return "当前：已取消";
            if (phase == HyExportPhase.Failed && !HyExportEngine.Running) return "当前：完成（有失败）";

            var rows = HyExportEngine.Rows;
            var idx = HyExportEngine.CurrentIndex;
            if (idx < 0 || idx >= rows.Count) return "当前：—";
            var row = rows[idx];
            var head = row.unit.Length > 0 ? $"{row.name}（{row.count} {row.unit}）" : row.name;
            return string.IsNullOrEmpty(stage) ? "当前：" + head : $"当前：{head} · {stage}";
        }

        private void DrawList(bool isPreview)
        {
            var rows = HyExportEngine.Rows;
            mScroll = EditorGUILayout.BeginScrollView(mScroll, GUILayout.ExpandHeight(true));

            DrawColumnHeader();

            var visible = 0;
            for (var i = 0; i < rows.Count && i < mState.Length; i++)
            {
                // 预览态：全部列出来（还没跑，不会有「已完成并移除」）
                if (!isPreview && mRemoved[i]) continue;
                visible++;
                DrawRow(i, rows[i]);
            }

            if (visible == 0)
            {
                GUILayout.Space(14f);
                LabelCentered(rows.Count == 0 ? "（没有可导出的内容）" : "（列表已清空）", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawColumnHeader()
        {
            var rect = EditorGUILayout.GetControlRect(false, 15f);
            ColumnRects(rect, out _, out var nameR, out var countR, out var stateR);
            LabelColored(nameR, "名称", sHeadSt, Dim(1f));
            LabelColored(countR, "数量", sHeadSt, Dim(1f));
            LabelColored(stateR, "状态", sHeadSt, Dim(1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax, rect.width, 1f), new Color(0.5f, 0.5f, 0.5f, 0.25f));
        }

        private void DrawRow(int i, HyExportRow row)
        {
            var rect = EditorGUILayout.GetControlRect(false, RowHeight);
            var st = mState[i];
            var a = RowAlpha(i);

            if (st == HyExportRowState.Active)
            {
                EditorGUI.DrawRect(rect, new Color(Accent.r, Accent.g, Accent.b, 0.13f * a));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height),
                                   new Color(Accent.r, Accent.g, Accent.b, 0.95f * a));
            }
            else if (st == HyExportRowState.Failed)
            {
                EditorGUI.DrawRect(rect, new Color(ErrColor.r, ErrColor.g, ErrColor.b, 0.13f * a));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height),
                                   new Color(ErrColor.r, ErrColor.g, ErrColor.b, 0.95f * a));
            }
            else if (st == HyExportRowState.Done)
            {
                EditorGUI.DrawRect(rect, new Color(OkColor.r, OkColor.g, OkColor.b, 0.08f * a));
            }

            ColumnRects(rect, out var markR, out var nameR, out var countR, out var stateR);
            var tint = StateTint(st, a);
            LabelColored(markR, MarkGlyph(st), sMarkSt, tint);
            LabelColored(nameR, row.name, sNameSt, tint);
            LabelColored(countR, CountText(row), sCountSt, Dim(a));
            LabelColored(stateR, StateText(st), sStateSt, tint);
        }

        private static string CountText(HyExportRow row)
            => row.unit.Length > 0 ? $"{row.count} {row.unit}" : row.count.ToString();

        private void DrawResult(HyExportPhase phase, int total, int succeeded, int failed)
        {
            Color color;
            string mark, title, sub;

            if (phase == HyExportPhase.Cancelled)
            {
                color = WarnColor; mark = "—";
                title = "已取消"; sub = "未做任何改动";
            }
            else if (phase == HyExportPhase.Aborted)
            {
                color = WarnColor; mark = "■";
                title = "已中止";
                sub = $"已完成 {succeeded} / 失败 {failed} / 共 {total}";
            }
            else if (failed > 0 || phase == HyExportPhase.Failed)
            {
                color = ErrColor; mark = "✕";
                title = "完成但有失败";
                sub = $"{succeeded} 成功 · {failed} 失败 · 共 {total}";
            }
            else
            {
                color = OkColor; mark = "✓";
                title = "全部完成";
                sub = $"{succeeded}/{total} · 0 个失败";
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            LabelInline(mark, sMarkSt, color, GUILayout.Width(18f));
            LabelInline(title, EditorStyles.boldLabel, color);
            GUILayout.Space(6f);
            LabelInline(sub, EditorStyles.miniLabel, Dim(1f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFooter(bool isPreview, bool running, int total)
        {
            EditorGUILayout.BeginHorizontal();

            if (isPreview)
            {
                LabelInline(
                    HyExportEngine.Destructive
                        ? "危险操作 · 点两次「执行」才开始"
                        : "点「执行」开始导出",
                    EditorStyles.miniLabel, Dim(1f));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("取消", EditorStyles.miniButton, GUILayout.Width(80f), GUILayout.Height(22f)))
                {
                    HyExportEngine.CancelPreview();
                    SafeClose();
                    GUIUtility.ExitGUI();
                }

                var prevEnabled = GUI.enabled;
                GUI.enabled = prevEnabled && total > 0;
                var prevBg = GUI.backgroundColor;
                if (HyExportEngine.Destructive) GUI.backgroundColor = new Color(0.85f, 0.38f, 0.42f);
                else GUI.backgroundColor = Accent;
                var label = mArmed ? "确认执行?" : "执行";
                if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(96f), GUILayout.Height(22f)))
                {
                    if (HyExportEngine.Destructive && !mArmed) mArmed = true; // 第一次点击只是上膛
                    else HyExportEngine.StartExecution();
                }
                GUI.backgroundColor = prevBg;
                GUI.enabled = prevEnabled;
            }
            else if (running)
            {
                LabelInline("导出进行中，窗口不可关闭；当前行完成后可中止。", EditorStyles.miniLabel, Dim(1f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("中止", EditorStyles.miniButton, GUILayout.Width(80f), GUILayout.Height(22f)))
                    HyExportEngine.RequestAbort();
            }
            else
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("关闭", EditorStyles.miniButton, GUILayout.Width(88f), GUILayout.Height(22f)))
                {
                    SafeClose();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>绘制本身出错时的绝对定位兜底：不依赖 GUILayout 栈，保证一定画得出「关闭」。</summary>
        private void DrawFallback()
        {
            var w = position.width;
            var h = position.height;
            GUI.Label(new Rect(14f, 14f, w - 28f, 20f), "导出中断：" + mFatal);
            GUI.Label(new Rect(14f, 40f, w - 28f, 20f), "详情见 Console。点下方按钮关闭本窗口。");
            if (GUI.Button(new Rect(w - 102f, h - 36f, 88f, 24f), "关闭"))
            {
                SafeClose();
                GUIUtility.ExitGUI();
            }
        }

        // ---------- 小工具 ----------

        private float RowAlpha(int i)
        {
            if (mDoneAt[i] < 0) return 1f;
            var t = EditorApplication.timeSinceStartup - mDoneAt[i];
            if (t <= SolidSeconds) return 1f;
            return Mathf.Clamp01(1f - (float)((t - SolidSeconds) / FadeSeconds));
        }

        private static string MarkGlyph(HyExportRowState st)
        {
            switch (st)
            {
                case HyExportRowState.Active:
                    return "|/-\\"[(int)(EditorApplication.timeSinceStartup * 10.0) % 4].ToString();
                case HyExportRowState.Done: return "✓";
                case HyExportRowState.Failed: return "✕";
                default: return "○";
            }
        }

        private static string StateText(HyExportRowState st)
        {
            switch (st)
            {
                case HyExportRowState.Active: return "进行中";
                case HyExportRowState.Done: return "已完成";
                case HyExportRowState.Failed: return "失败";
                default: return "待处理";
            }
        }

        private static Color StateTint(HyExportRowState st, float a)
        {
            switch (st)
            {
                case HyExportRowState.Active: return new Color(Accent.r, Accent.g, Accent.b, a);
                case HyExportRowState.Done: return new Color(OkColor.r, OkColor.g, OkColor.b, a * 0.9f);
                case HyExportRowState.Failed: return new Color(ErrColor.r, ErrColor.g, ErrColor.b, a);
                default: return Dim(a);
            }
        }

        /// <summary>次级文字：比 Unity 默认 miniLabel 更亮一档，不再像禁用态灰字（用户：文字不要那么灰）。</summary>
        private static Color Dim(float a) => new Color(0.80f, 0.82f, 0.85f, a);

        private static void ColumnRects(Rect r, out Rect mark, out Rect name, out Rect count, out Rect state)
        {
            const float pad = 8f, markW = 16f, countW = 84f, stateW = 104f, gap = 6f;
            mark = new Rect(r.x + pad, r.y, markW, r.height);
            state = new Rect(r.xMax - pad - stateW, r.y, stateW, r.height);
            count = new Rect(state.x - gap - countW, r.y, countW, r.height);
            var nameX = mark.xMax + gap;
            name = new Rect(nameX, r.y, Mathf.Max(10f, count.x - gap - nameX), r.height);
        }

        private static void LabelColored(Rect rect, string text, GUIStyle style, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = prev;
        }

        private static void LabelInline(string text, GUIStyle style, Color color, params GUILayoutOption[] options)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, style, options);
            GUI.color = prev;
        }

        private static void LabelCentered(string text, GUIStyle style)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, style);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static void EnsureStyles()
        {
            if (sMarkSt != null) return;

            // 只改 alignment / fontSize / clipping 这类普通字段，不碰 normal.textColor：
            // 颜色统一走 GUI.color 染色，避免 GUIStyleState 被样式副本共享而污染 EditorStyles。
            sMarkSt = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            sNameSt = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
            };
            sCountSt = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            sStateSt = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            sRightSt = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };
            sHeadSt = new GUIStyle(EditorStyles.miniLabel);
            sDescSt = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        }
    }
}
