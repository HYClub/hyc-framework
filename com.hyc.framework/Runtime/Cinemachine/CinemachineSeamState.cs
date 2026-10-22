// ============================================================
// HYC Framework - Cinemachine 相机装配 · 接缝状态
// 文件: Runtime/Cinemachine/CinemachineSeamState.cs
// 说明: 「分屏线」的参数化描述。斜切 / 弯曲 / 加宽 / 羽化 / 抖动 /
//       推出屏幕（合并）全部由这一组参数表达，布局枚举只是它的特例。
//       SignedDistance 与 SeamComposite.shader 里的公式必须逐项一致，
//       否则会出现「玩法判定在 A 侧、画面显示在 B 侧」。
// ============================================================

using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>接缝由谁驱动。</summary>
    public enum SeamSource
    {
        /// <summary>导演打点：只认 <see cref="CinemachineSeamCompositor.SetSeam"/> 传入的值。</summary>
        Authored,

        /// <summary>
        /// 跟随两个世界目标：接缝中点取两人屏幕投影的中点，
        /// 角度取两人连线在屏幕上的垂线（Lego 系 Voronoi 分屏）。
        /// </summary>
        FollowPlayers,

        /// <summary>世界内一根线：取该 Transform 的位置与朝向投影到屏幕。</summary>
        Transform,

        /// <summary>
        /// 跟随单个目标：接缝保持作者指定的角度，整体平移到该目标的屏幕位置上
        /// （「分屏线跟着这个角色走」）。
        /// </summary>
        FollowTarget,
    }

    /// <summary>
    /// 屏幕空间接缝。所有分屏布局都是它的特例：
    /// 左右分屏 = <see cref="Angle"/> 0、<see cref="Point"/> x=0.5；
    /// 单屏 = 把接缝推出屏幕；合并 = <see cref="Push"/> 走到头 + <see cref="Crossfade"/> 叠化。
    /// <para>屏幕坐标为视口归一化坐标，原点**左下**（与 <c>Camera.WorldToViewportPoint</c> 一致）。</para>
    /// </summary>
    [System.Serializable]
    public struct SeamState
    {
        /// <summary>接缝中点（视口归一化，原点左下）。</summary>
        public Vector2 Point;

        /// <summary>
        /// 接缝**法线**的方向角（度）。0 = 法线朝 +x（接缝是竖线），90 = 法线朝 +y（接缝是横线）。
        /// </summary>
        public float Angle;

        /// <summary>弯曲量，-1..1。0 = 直线；正负决定接缝向哪一侧鼓出。</summary>
        public float Curvature;

        /// <summary>缝隙宽度（屏幕高度比例）。中间露出 <see cref="GapColor"/>，0 = 无缝隙。</summary>
        public float GapWidth;

        /// <summary>羽化宽度（屏幕比例）。0 = 硬边；越大两侧画面渗透越明显。</summary>
        public float Feather;

        /// <summary>接缝边缘抖动幅度，0..1。用来做「撕纸 / 溶解」的有机边缘。</summary>
        public float Noise;

        /// <summary>抖动滚动速度（每秒）。</summary>
        public float NoiseSpeed;

        /// <summary>缝隙颜色（含 alpha，alpha 为 0 时不画缝隙）。</summary>
        public Color GapColor;

        /// <summary>
        /// 接缝光带宽度（屏幕比例）。&gt; 0 时沿接缝叠一条加法发光带，
        /// 让「切在哪儿」肉眼可见 —— 玩家能拖拽的接缝建议开到 0.015~0.03。
        /// <para>纯视觉，不参与 <see cref="SignedDistance"/>，所以不影响玩法判定。</para>
        /// </summary>
        public float GlowWidth;

        /// <summary>光带颜色（alpha = 强度）。alpha = 0 或 <see cref="GlowWidth"/> = 0 时不发光。</summary>
        public Color GlowColor;

        /// <summary>光带呼吸频率（Hz）。0 = 恒定不闪。</summary>
        public float GlowPulse;

        /// <summary>位移偏置，-1..1。整体平移接缝；推到 ±1 即「接缝移出屏幕 = 只剩一侧」。</summary>
        public float Push;

        /// <summary>叠化强度，-1..1。正值让 B 侧画面吃掉 A，负值反之；用于合并/分开的软化。</summary>
        public float Crossfade;

        /// <summary>左右分屏预设。</summary>
        public static SeamState Vertical => new SeamState
        {
            Point = new Vector2(0.5f, 0.5f),
            GapColor = new Color(0f, 0f, 0f, 1f),
        };

        /// <summary>上下分屏预设。</summary>
        public static SeamState Horizontal => new SeamState
        {
            Point = new Vector2(0.5f, 0.5f),
            Angle = 90f,
            GapColor = new Color(0f, 0f, 0f, 1f),
        };

        /// <summary>只显示 A 侧：接缝推出屏幕右缘。</summary>
        public static SeamState SingleA => new SeamState
        {
            Point = new Vector2(0.5f, 0.5f),
            Push = -1.5f,
            GapColor = new Color(0f, 0f, 0f, 1f),
        };

        /// <summary>只显示 B 侧：接缝推出屏幕左缘。</summary>
        public static SeamState SingleB => new SeamState
        {
            Point = new Vector2(0.5f, 0.5f),
            Push = 1.5f,
            GapColor = new Color(0f, 0f, 0f, 1f),
        };

        /// <summary>
        /// 接缝的切向与法向单位向量（视口空间）。
        /// <para><see cref="Angle"/> 描述的是**法线**方向：0° 法线朝 +x（接缝是竖线），
        /// 90° 法线朝 +y（接缝是横线）。切向 = 法线逆时针转 90°。</para>
        /// </summary>
        public void GetFrame(out Vector2 tangent, out Vector2 normal)
        {
            var rad = Angle * Mathf.Deg2Rad;
            normal = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            tangent = new Vector2(-normal.y, normal.x);
        }

        /// <summary>
        /// 视口坐标到接缝的有符号距离：&gt; 0 归 B 侧，&lt; 0 归 A 侧。
        /// <para>与 <c>SeamComposite.shader</c> 的 <c>SeamDistance</c> 逐项对应，改一处必须改两处。</para>
        /// </summary>
        public float SignedDistance(Vector2 viewport)
        {
            GetFrame(out var tangent, out var normal);
            var d = viewport - Point;
            var s = Vector2.Dot(d, normal);
            var u = Vector2.Dot(d, tangent);
            s -= Curvature * (1f - 4f * u * u);
            return s + Push;
        }

        /// <summary>
        /// 不参与噪声的「干净」有符号距离：玩法查询用它，避免抖动导致判定闪烁。
        /// </summary>
        public float SignedDistanceStable(Vector2 viewport)
        {
            return SignedDistance(viewport);
        }

        /// <summary>
        /// 接缝相对屏幕中心在法向上的偏移（视口单位）。0 = 接缝过屏幕中心。
        /// <para><see cref="GetNormalSpan"/> 的区间以接缝为原点，算「跨屏占比」够用；
        /// 但要把目标摆到某一侧的中心（取景）必须叠加本偏移，否则接缝被推偏后目标会被推到屏幕外。</para>
        /// <para><b><see cref="Push"/> 取负号</b>：<see cref="SignedDistance"/> 是 <c>s = dot(p-P,n) + Push</c>，
        /// 所以 s = 0 的等值线在 <c>dot(P,n) - Push</c> 处 —— 不是 <c>+ Push</c>。
        /// 与 shader 的 <c>SeamDistance</c> 同源，改一处必须改两处。</para>
        /// </summary>
        public float NormalOffset()
        {
            GetFrame(out _, out var normal);
            return Vector2.Dot(Point - ScreenCenter, normal) - Push;
        }

        /// <summary>
        /// 屏幕四角在接缝法向上的投影范围（以接缝为原点，忽略弯曲与噪声）。
        /// <para><c>min &lt; 0 &lt; max</c> 表示接缝确实穿过屏幕：A 侧占 <c>-min</c>、
        /// B 侧占 <c>max</c>，总跨度恒为 <c>max - min</c>，所以两侧占比可直接由此相除。</para>
        /// </summary>
        public void GetNormalSpan(out float min, out float max)
        {
            GetFrame(out _, out var normal);
            var seam = NormalOffset();

            min = float.MaxValue;
            max = float.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var corner = new Vector2(i & 1, (i >> 1) & 1);
                var q = Vector2.Dot(corner - ScreenCenter, normal) - seam;
                if (q < min) min = q;
                if (q > max) max = q;
            }
        }

        /// <summary>A 侧占据屏幕的比例（0..1，沿法向的一维近似）。</summary>
        public float SideAShare()
        {
            GetNormalSpan(out var min, out var max);
            var span = max - min;
            return span <= 1e-5f ? 0.5f : Mathf.Clamp01(-min / span);
        }

        /// <summary>
        /// 把接缝沿法向平移，使两侧占比都不低于 <paramref name="minShare"/>。
        /// <para>跟随玩家时两人靠近会把接缝推向一侧，极端情况下某一侧会被压成零宽；
        /// 用它兜底，保证那半屏至少还看得见人。</para>
        /// <para>只在「接缝居中、由跟随驱动」时生效：<see cref="Push"/> 非零表示宿主正在把接缝
        /// 推出屏幕（合并 / 单屏），或接缝已经整个离开屏幕 —— 这两种情况钳制会与补间打架，直接跳过。</para>
        /// </summary>
        /// <returns>true = 接缝被移动过。</returns>
        public bool ClampShare(float minShare)
        {
            if (minShare <= 0f || Mathf.Abs(Push) > 1e-4f)
                return false;

            GetNormalSpan(out var min, out var max);
            var span = max - min;
            if (span <= 1e-5f || min >= 0f || max <= 0f)
                return false;

            // 沿 +法向 平移 delta 会让 min/max 同时减 delta（A 变大、B 变小）
            var limit = Mathf.Clamp(minShare, 0f, 0.49f) * span;
            var lo = limit + min;
            var hi = max - limit;
            var delta = lo > hi ? (lo + hi) * 0.5f : Mathf.Clamp(0f, lo, hi);
            if (Mathf.Abs(delta) <= 1e-5f)
                return false;

            GetFrame(out _, out var normal);
            Point += normal * delta;
            return true;
        }

        private static readonly Vector2 ScreenCenter = new Vector2(0.5f, 0.5f);
    }

    /// <summary>布局枚举到接缝参数的映射（Compositor / 分屏调度共用）。</summary>
    public static class SeamPresets
    {
        /// <summary>
        /// 把 57 号文档里的布局枚举转成接缝参数。
        /// <para><c>Quad</c> 与 <c>PictureInPicture</c> 不是半平面，表达不了，
        /// 必须留在 <c>Camera.rect</c> 路径 —— 这里对它们返回左右分屏仅作兜底，
        /// 调用方应先判断 <see cref="IsHalfPlane"/>。</para>
        /// </summary>
        public static SeamState FromLayout(ViewLayout layout)
        {
            switch (layout)
            {
                case ViewLayout.Horizontal:
                    return SeamState.Horizontal;
                case ViewLayout.Single:
                    return SeamState.SingleA;
                default:
                    return SeamState.Vertical;
            }
        }

        /// <summary>该布局能否用单个半平面接缝表达。</summary>
        public static bool IsHalfPlane(ViewLayout layout)
        {
            return layout == ViewLayout.Vertical
                || layout == ViewLayout.Horizontal
                || layout == ViewLayout.Single;
        }
    }
}
