// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTEditorStyles.cs
// 说明: BT 编辑器视觉常量与样式(零 using NodeCanvas/ParadoxNotion)。
//       ★ 圆角卡片 / 端口圆点 / 状态角标等几何形状均**程序化生成纹理**:
//         - RoundedBoxTexture : 4x 超采样 + SDF 抗锯齿 + mipmap(替代 NodeCanvas 9-slice .psd)
//         - AACircleTexture   : SDF 抗锯齿实心圆(替代 NodeCanvas Circle.png 硬边圆)
//         => 解决了旧 9-slice 拉伸的色块瑕疵与硬边锯齿(详见 docs/84)。
//       ★ 仅保留两张 NodeCanvas 原版纹理(Editor/BT/Styles/Textures/, 用户自有资产):
//         - Bezier.psd       : 连线贝塞尔的纹理参数(Handles.DrawBezier)
//         - WindowShadow.psd : 小地图(minimap)节点阴影(NodeShadowStyle)
// ============================================================

using UnityEditor;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    /// <summary>BT 编辑器视觉常量与样式(程序化纹理 + 深色主题调色板, 不引用 ParadoxNotion/NodeCanvas)。</summary>
    public static class BTEditorStyles
    {
        // ---- 状态色(★自定义深色主题★: 与 BTEditor-UI-Prototype.html 一致, 不再沿用 NodeCanvas 黄/绿/红) ----
        public static readonly Color StatusFailure  = new Color(0.937f, 0.267f, 0.267f); // #ef4444
        public static readonly Color StatusSuccess  = new Color(0.133f, 0.773f, 0.369f); // #22c55e
        public static readonly Color StatusRunning  = new Color(0.431f, 0.659f, 0.996f); // #6ea8fe
        public static readonly Color StatusResting  = new Color(0.7f, 0.7f, 1f, 0.8f);   // 保留(兼容旧引用)
        public static readonly Color StatusError    = Color.red;
        public static readonly Color StatusOptional = new Color(0.5f, 0.5f, 0.5f);

        // ---- 深色现代主题调色板(与 BTEditor-UI-Prototype.html 一一对应) ----
        public static readonly Color CanvasBg   = new Color(0.078f, 0.082f, 0.102f); // #14151a 画布底
        public static readonly Color NodeBg     = new Color(0.094f, 0.102f, 0.125f); // #181a20 节点卡
        public static readonly Color NodeBorder = new Color(0.173f, 0.188f, 0.220f); // #2c3038 卡片描边
        public static readonly Color Edge       = new Color(0.290f, 0.322f, 0.376f); // #4a5260 连线静止
        public static readonly Color EdgeActive = new Color(0.561f, 0.702f, 1.000f); // #8fb3ff 连线选中
        public static readonly Color Accent     = new Color(0.431f, 0.659f, 0.996f); // #6ea8fe 选中描边

        // 分类色(左侧色条 / 类别圆点)
        public static readonly Color CatRoot      = new Color(0.753f, 0.502f, 0.988f); // #c084fc
        public static readonly Color CatComposite = new Color(0.220f, 0.741f, 0.973f); // #38bdf8
        public static readonly Color CatDecorator = new Color(0.984f, 0.749f, 0.141f); // #fbbf24
        public static readonly Color CatCondition = new Color(0.290f, 0.871f, 0.502f); // #4ade80
        public static readonly Color CatAction    = new Color(0.984f, 0.443f, 0.522f); // #fb7185
        public static readonly Color CatEnd       = new Color(0.392f, 0.455f, 0.545f); // #64748b
        public static readonly Color CatCustom    = new Color(0.655f, 0.545f, 0.980f); // #a78bfa

        // 连线静止(非激活)灰, 对应 NodeCanvas Colors.Grey(0.3f)
        public static readonly Color ConnectionInactive = new Color(0.3f, 0.3f, 0.3f);
        // 连线激活/选中: Resting 浅蓝
        public static readonly Color ConnectionActive   = StatusResting;

        // 选中节点整体染色, 对应 Editor.Node: GUI.color = new Color(0.9,0.9,1)
        public static readonly Color SelectedTint = new Color(0.9f, 0.9f, 1f);

        // ---- 几何常量(对应 NodeCanvas) ----
        public const float GRID     = 20f;   // GRID_SIZE
        public const float RIGIDITY = 0.8f;  // Prefs.connectionsMLT 默认值
        public const float CONN_SIZE = 3f;    // Connection.defaultSize
        public const float PORT_OFFSET = 6f;  // Editor.Node: portOffset=6
        public const float PORT_SIZE = 12f;  // GUIPort 12x12
        public const float PORT_END  = 16f;  // 连线末端圆 16x16

        public static Color Grey(float v) => new Color(v, v, v);

        /// <summary>HYC BTNodeState → NodeCanvas 状态色。</summary>
        public static Color StatusColor(BTNodeState state)
        {
            switch (state)
            {
                case BTNodeState.Running:  return StatusRunning;
                case BTNodeState.Success:  return StatusSuccess;
                case BTNodeState.Failed:   return StatusFailure;
                case BTNodeState.Optional: return StatusOptional;
                default:                   return StatusResting; // None / Paused => Resting 浅蓝
            }
        }

        /// <summary>节点分类色(自定义深色主题: 左色条 / 类别圆点)。按"入口/组合/装饰/条件/动作"五大类取色。</summary>
        public static Color CategoryColor(BTNodeType type)
        {
            switch (type)
            {
                case BTNodeType.Root:  return CatRoot;
                case BTNodeType.End:   return CatEnd;

                // ---- 组合 ----
                case BTNodeType.Sequence:
                case BTNodeType.Selector:
                case BTNodeType.Parallel:
                case BTNodeType.RandomSelector:
                case BTNodeType.RandomSequence:
                case BTNodeType.FlipSelector:
                case BTNodeType.UtilitySelector:
                case BTNodeType.ProbabilitySelector:
                case BTNodeType.StepSequencer:
                case BTNodeType.Switch:
                case BTNodeType.BinarySelector:
                    return CatComposite;

                // ---- 装饰 ----
                case BTNodeType.Invert:
                case BTNodeType.Repeat:
                case BTNodeType.UntilSuccess:
                case BTNodeType.UntilFail:
                case BTNodeType.AlwaysSuccess:
                case BTNodeType.AlwaysFail:
                case BTNodeType.CooldownGate:
                case BTNodeType.Conditional:
                case BTNodeType.TimeLimit:
                case BTNodeType.Timeout:
                case BTNodeType.WaitUntil:
                case BTNodeType.Guard:
                case BTNodeType.Interruptor:
                case BTNodeType.Filter:
                case BTNodeType.Iterator:
                case BTNodeType.Monitor:
                case BTNodeType.Optional:
                case BTNodeType.Remapper:
                    return CatDecorator;

                // ---- 条件 ----
                case BTNodeType.CheckDistance:
                case BTNodeType.CheckBlackboard:
                case BTNodeType.CheckEvent:
                    return CatCondition;

                // ---- 动作 ----
                case BTNodeType.Wait:
                case BTNodeType.NoOp:
                case BTNodeType.SubTree:
                case BTNodeType.SendEvent:
                    return CatAction;

                case BTNodeType.GameCustom: return CatCustom;
                default:                    return CatAction;
            }
        }

        /// <summary>类别中文名(节点副标题: 入口/出口/组合/装饰/条件/动作/自定义)。</summary>
        public static string CategoryName(BTNodeType type)
        {
            switch (type)
            {
                case BTNodeType.Root:  return "入口";
                case BTNodeType.End:   return "出口";
                case BTNodeType.CheckDistance:
                case BTNodeType.CheckBlackboard:
                case BTNodeType.CheckEvent:
                    return "条件";
                case BTNodeType.Wait:
                case BTNodeType.NoOp:
                case BTNodeType.SubTree:
                case BTNodeType.SendEvent:
                    return "动作";
                case BTNodeType.GameCustom: return "自定义";
                case BTNodeType.Invert:
                case BTNodeType.Repeat:
                case BTNodeType.UntilSuccess:
                case BTNodeType.UntilFail:
                case BTNodeType.AlwaysSuccess:
                case BTNodeType.AlwaysFail:
                case BTNodeType.CooldownGate:
                case BTNodeType.Conditional:
                case BTNodeType.TimeLimit:
                case BTNodeType.Timeout:
                case BTNodeType.WaitUntil:
                case BTNodeType.Guard:
                case BTNodeType.Interruptor:
                case BTNodeType.Filter:
                case BTNodeType.Iterator:
                case BTNodeType.Monitor:
                case BTNodeType.Optional:
                case BTNodeType.Remapper:
                    return "装饰";
                default:
                    return "组合";
            }
        }

        /// <summary>类别圆点内的一字(对应原型 glyph: 根/终/组/装/条/动/戏)。</summary>
        public static string CategoryGlyph(BTNodeType type)
        {
            switch (type)
            {
                case BTNodeType.Root:  return "根";
                case BTNodeType.End:   return "终";
                case BTNodeType.Sequence:
                case BTNodeType.Selector:
                case BTNodeType.Parallel:
                case BTNodeType.RandomSelector:
                case BTNodeType.RandomSequence:
                case BTNodeType.FlipSelector:
                case BTNodeType.UtilitySelector:
                case BTNodeType.ProbabilitySelector:
                case BTNodeType.StepSequencer:
                case BTNodeType.Switch:
                case BTNodeType.BinarySelector:
                    return "组";
                case BTNodeType.Invert:
                case BTNodeType.Repeat:
                case BTNodeType.UntilSuccess:
                case BTNodeType.UntilFail:
                case BTNodeType.AlwaysSuccess:
                case BTNodeType.AlwaysFail:
                case BTNodeType.CooldownGate:
                case BTNodeType.Conditional:
                case BTNodeType.TimeLimit:
                case BTNodeType.Timeout:
                case BTNodeType.WaitUntil:
                case BTNodeType.Guard:
                case BTNodeType.Interruptor:
                case BTNodeType.Filter:
                case BTNodeType.Iterator:
                case BTNodeType.Monitor:
                case BTNodeType.Optional:
                case BTNodeType.Remapper:
                    return "装";
                case BTNodeType.CheckDistance:
                case BTNodeType.CheckBlackboard:
                case BTNodeType.CheckEvent:
                    return "条";
                case BTNodeType.Wait:
                case BTNodeType.NoOp:
                case BTNodeType.SubTree:
                case BTNodeType.SendEvent:
                    return "动";
                case BTNodeType.GameCustom: return "戏";
                default:                    return "动";
            }
        }

        // ============================================================
        //  原版纹理加载(Packages/com.hyc.framework/Editor/BT/Styles/Textures/)
        // ============================================================
        private const string TexPath = "Packages/com.hyc.framework/Editor/BT/Styles/Textures/";

        private static Texture2D LoadTex(string file)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath + file);
            if (t == null) Debug.LogError($"[BTEditorStyles] 缺少纹理 {TexPath}{file} —— 节点将回退为纯色。请确认 Editor/BT/Styles/Textures/ 下的 NodeCanvas 原版纹理齐全。");
            return t;
        }

        private static Texture2D _shadow, _bezier;

        public static Texture2D ShadowTexture       => _shadow       ?? (_shadow       = LoadTex("WindowShadow.psd"));
        public static Texture2D BezierTexture       => _bezier       ?? (_bezier       = LoadTex("Bezier.psd"));

        // ============================================================
        //  GUIStyle(数值逐项取自 StyleSheetDark.asset)
        // ============================================================
        private static GUIStyle _shadowStyle;

        /// <summary>节点阴影(StyleSheet.windowShadow): WindowShadow.psd, border 10, overflow(9,7,10,10)。
        /// 直接画在 node.rect 上(overflow 负责外扩), 无需手工偏移 —— 与原版 Styles.Draw(node.rect, windowShadow) 一致。</summary>
        public static GUIStyle NodeShadowStyle
        {
            get
            {
                if (_shadowStyle == null)
                {
                    _shadowStyle = new GUIStyle
                    {
                        normal = { background = ShadowTexture },
                        border = new RectOffset(10, 10, 10, 10),
                        margin = new RectOffset(0, 0, 0, 0),
                        padding = new RectOffset(0, 0, 0, 0),
                        overflow = new RectOffset(9, 10, 7, 10),
                    };
                }
                return _shadowStyle;
            }
        }

        /// <summary>通用 box(用于 minimap 面板/lens)。</summary>
        private static GUIStyle _box;
        public static GUIStyle Box
        {
            get
            {
                if (_box == null)
                {
                    _box = new GUIStyle(GUI.skin.box) { fontSize = 11 };
                }
                return _box;
            }
        }

        // ============================================================
        //  绘制辅助(顺序与形状严格对应 Editor.Node / Editor.Connection)
        // ============================================================

        /// <summary>画连线末端圆头(16x16, 程序化 AACircleTexture 抗锯齿纹理)。</summary>
        public static void DrawCircle(Vector2 center, float size, Color color)
        {
            var r = new Rect(0, 0, size, size) { center = center };
            GUI.color = color;
            GUI.DrawTexture(r, AACircleTexture);
            GUI.color = Color.white;
        }

        // ============================================================
        //  ★自定义深色主题绘制辅助(圆角卡片 / 端口点 / 角标)★
        // ============================================================

        private const int ROUND_TEX_SIZE = 48;
        private const int ROUND_CORNER = 12;
        private const int ROUND_SS = 4;        // 超采样倍数(抗锯齿)

        private static Texture2D _roundTex;
        /// <summary>白色圆角方块纹理(9-slice, border=ROUND_CORNER)。用于卡片/描边/阴影背景, 由 GUI.color 染色。</summary>
        public static Texture2D RoundedBoxTexture
        {
            get
            {
                if (_roundTex == null)
                {
                    // 4x 超采样 + SDF(符号距离场)抗锯齿: alpha 由像素到圆角矩形边界的距离算出。
                    // 旧实现是二值 alpha(inside?255:0), 边缘无灰度过渡 => 楼梯锯齿(用户截图反馈的根因)。
                    int s = ROUND_TEX_SIZE, r = ROUND_CORNER, ss = ROUND_SS;
                    int hs = s * ss, hr = r * ss;
                    var hi = new Color32[hs * hs];
                    float half = hs * 0.5f, inner = half - hr;
                    for (int y = 0; y < hs; y++)
                    {
                        float qy = Mathf.Abs(y + 0.5f - half) - inner;
                        for (int x = 0; x < hs; x++)
                        {
                            float qx = Mathf.Abs(x + 0.5f - half) - inner;
                            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                            float sd = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - hr;
                            // 2 hi-res px(~0.5 最终 px)的 alpha 过渡带, 居中在边界上
                            hi[y * hs + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - sd * 0.5f) * 255f));
                        }
                    }
                    // 盒式降采样 ss*ss -> 1px: 边缘得到平滑灰度, 且最终纹理与 9-slice border 尺寸一致
                    var buf = new Color32[s * s];
                    for (int y = 0; y < s; y++)
                        for (int x = 0; x < s; x++)
                        {
                            int sum = 0;
                            for (int dy = 0; dy < ss; dy++)
                            {
                                int row = (y * ss + dy) * hs + x * ss;
                                for (int dx = 0; dx < ss; dx++) sum += hi[row + dx].a;
                            }
                            buf[y * s + x] = new Color32(255, 255, 255, (byte)(sum / (ss * ss)));
                        }
                    // 带 mipmap: 画布 Zoom<1(缩小)时走 mipmap, 不闪不糊
                    var t = new Texture2D(s, s, TextureFormat.RGBA32, true);
                    t.hideFlags = HideFlags.HideAndDontSave;
                    t.wrapMode = TextureWrapMode.Clamp;
                    t.filterMode = FilterMode.Bilinear;
                    t.alphaIsTransparency = true;
                    t.SetPixels32(buf);
                    t.Apply(true, false);
                    _roundTex = t;
                }
                return _roundTex;
            }
        }

        private static GUIStyle _roundStyle;
        public static GUIStyle RoundedBoxStyle
        {
            get
            {
                if (_roundStyle == null)
                {
                    _roundStyle = new GUIStyle
                    {
                        normal = { background = RoundedBoxTexture },
                        border = new RectOffset(ROUND_CORNER, ROUND_CORNER, ROUND_CORNER, ROUND_CORNER),
                        margin = new RectOffset(0, 0, 0, 0),
                        padding = new RectOffset(0, 0, 0, 0),
                    };
                }
                return _roundStyle;
            }
        }

        private static Texture2D _aaCircle;
        /// <summary>白色实心圆纹理(SDF 抗锯齿 + mipmap)。替代 Circle.png(硬边), 用于类别圆点/端口/角标/连线端头。</summary>
        public static Texture2D AACircleTexture
        {
            get
            {
                if (_aaCircle == null)
                {
                    const int s = 64;
                    var buf = new Color32[s * s];
                    float half = s * 0.5f, rad = half - 1.5f;
                    for (int y = 0; y < s; y++)
                        for (int x = 0; x < s; x++)
                        {
                            float dx = x + 0.5f - half, dy = y + 0.5f - half;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            // ~1.4px 的 alpha 过渡带
                            float a = Mathf.Clamp01((rad - d) * 0.7f + 0.5f);
                            buf[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                        }
                    var t = new Texture2D(s, s, TextureFormat.RGBA32, true);
                    t.hideFlags = HideFlags.HideAndDontSave;
                    t.wrapMode = TextureWrapMode.Clamp;
                    t.filterMode = FilterMode.Bilinear;
                    t.alphaIsTransparency = true;
                    t.SetPixels32(buf);
                    t.Apply(true, false);
                    _aaCircle = t;
                }
                return _aaCircle;
            }
        }

        /// <summary>画一个纯色圆角矩形(9-slice, 任意尺寸圆角半径固定为 ROUND_CORNER)。</summary>
        public static void DrawRounded(Rect r, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.Box(r, GUIContent.none, RoundedBoxStyle);
            GUI.color = prev;
        }

        /// <summary>画带描边的圆角矩形: 先描边色铺满, 再内缩 fill 色, 露出 borderWidth 宽的边框。</summary>
        public static void DrawRoundedCard(Rect r, Color borderColor, Color fillColor, float borderWidth)
        {
            DrawRounded(r, borderColor);
            DrawRounded(new Rect(r.x + borderWidth, r.y + borderWidth, r.width - borderWidth * 2f, r.height - borderWidth * 2f), fillColor);
        }

        /// <summary>画端口圆点: 外环 ring + 内填充 fill(用 AACircleTexture 染色)。</summary>
        public static void DrawPortDot(Vector2 center, float size, Color ring, Color fill)
        {
            var outer = new Rect(0, 0, size, size) { center = center };
            GUI.color = ring; GUI.DrawTexture(outer, AACircleTexture);
            float inner = Mathf.Max(2f, size - 4f);
            var innerR = new Rect(0, 0, inner, inner) { center = center };
            GUI.color = fill; GUI.DrawTexture(innerR, AACircleTexture);
            GUI.color = Color.white;
        }

        /// <summary>画连线末端小圆头。</summary>
        public static void DrawConnEnd(Vector2 center, float size, Color color)
        {
            GUI.color = color;
            var r = new Rect(0, 0, size, size) { center = center };
            GUI.DrawTexture(r, AACircleTexture);
            GUI.color = Color.white;
        }

        /// <summary>标题文字色(自定义主题: 节点标题白字)。</summary>
        public static Color NodeTextColor(Color cat) => Color.white;
    }
}
