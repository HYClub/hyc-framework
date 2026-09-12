// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTEditorStyles.cs
// 说明: NodeCanvas 样式移植(零 using NodeCanvas/ParadoxNotion)。
//       数值严格取自 NodeCanvas 源码(StyleSheetDark.asset / Editor.Node.cs /
//       Editor.Connection.cs / StyleSheet.GetStatusColor / Prefs.connectionsMLT):
//         - 节点体 window 9-slice 圆角 6, 标题 windowTitle 12px 居中粗体
//         - 头部 windowHeader: 分类色条(顶部圆角)
//         - 阴影 windowShadow: 偏移(3.5,3.5) 半透明黑
//         - 状态色 GetStatusColor: Failure(1,.3,.3) Success(.4,.7,.2)
//                     Running=yellow Resting(.7,.7,1,.8) Error=red Optional=grey
//         - 连线 defaultSize=3, connectionsMLT=0.8, 末端 Circle 16px
//         - 网格 black a0.15, GRID_SIZE=20
//       GUIStyle/纹理为 HYC 自有实现(程序化生成等价纹理), 视觉等价。
// ============================================================

using UnityEditor;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    /// <summary>NodeCanvas 视觉常量与样式(视觉等价移植, 不引用 ParadoxNotion)。</summary>
    public static class BTEditorStyles
    {
        // ---- 状态色(对应 NodeCanvas StyleSheet.GetStatusColor) ----
        public static readonly Color StatusFailure  = new Color(1.0f, 0.3f, 0.3f);
        public static readonly Color StatusSuccess  = new Color(0.4f, 0.7f, 0.2f);
        public static readonly Color StatusRunning  = Color.yellow;
        public static readonly Color StatusResting  = new Color(0.7f, 0.7f, 1f, 0.8f); // 浅蓝(连线默认色)
        public static readonly Color StatusError    = Color.red;
        public static readonly Color StatusOptional = new Color(0.5f, 0.5f, 0.5f);

        // 连线静止(非激活)灰, 对应 NodeCanvas Colors.Grey(0.3f)
        public static readonly Color ConnectionInactive = new Color(0.3f, 0.3f, 0.3f);
        // 连线激活/选中: Resting 浅蓝
        public static readonly Color ConnectionActive   = StatusResting;

        // ---- 节点身体(对应 window 面板: 中性深灰) ----
        public static readonly Color NodeBody   = new Color(0.21f, 0.21f, 0.25f);
        public static readonly Color NodeBorder = new Color(0.34f, 0.34f, 0.40f);

        // ---- 几何常量(对应 NodeCanvas) ----
        public const float CORNER   = 6f;    // window.border 圆角半径
        public const float GRID     = 20f;   // GRID_SIZE
        public const float RIGIDITY = 0.8f;  // Prefs.connectionsMLT 默认值
        public const float CONN_SIZE = 3f;    // Connection.defaultSize
        public const float PORT_OUT_OFFSET = 6f; // 输出端口相对右缘外凸 6px
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

        /// <summary>节点头部分类色(对应 node.nodeColor)。</summary>
        public static Color CategoryColor(BTNodeType type) => BTNodeCatalog.ColorOf(type);

        // ============================================================
        //  程序化纹理(一次性懒生成, 等价 NodeCanvas 资源)
        // ============================================================
        private static Texture2D _body, _header, _shadow, _highlight, _circle, _bezier;

        public static Texture2D BodyTexture     => _body     ?? (_body     = MakeRounded(48, CORNER, CORNER, NodeBody,   NodeBorder, 1f));
        public static Texture2D HeaderTexture   => _header   ?? (_header   = MakeHeader());
        public static Texture2D ShadowTexture   => _shadow   ?? (_shadow   = MakeShadow());
        public static Texture2D HighlightTexture => _highlight?? (_highlight= MakeRounded(48, 8f, 8f, Color.white, (Color?)null, 0f));
        public static Texture2D CircleTexture   => _circle   ?? (_circle   = MakeSolidCircle(32));
        public static Texture2D BezierTexture   => _bezier   ?? (_bezier   = MakeSolid(Color.white));

        // ---- GUIStyle(9-slice) ----
        private static GUIStyle _bodyStyle, _headerStyle, _highlightStyle, _shadowStyle, _titleStyle;

        /// <summary>节点身体(window): 圆角 6, 边界 6。基类用 GUI.skin.box(实测可渲染), 不用 GUIStyle.none。</summary>
        public static GUIStyle NodeBodyStyle
        {
            get
            {
                if (_bodyStyle == null)
                {
                    _bodyStyle = new GUIStyle(GUI.skin.box)
                    {
                        normal = { background = BodyTexture },
                        border = new RectOffset((int)CORNER, (int)CORNER, (int)CORNER, (int)CORNER),
                        padding = new RectOffset(0, 0, 0, 0),
                        margin = new RectOffset(0, 0, 0, 0),
                    };
                }
                return _bodyStyle;
            }
        }

        /// <summary>节点头部(windowHeader): 顶部圆角 6, 底部直角, 由 GUI.color 染色。</summary>
        public static GUIStyle NodeHeaderStyle
        {
            get
            {
                if (_headerStyle == null)
                {
                    _headerStyle = new GUIStyle(GUI.skin.box)
                    {
                        normal = { background = HeaderTexture },
                        border = new RectOffset((int)CORNER, (int)CORNER, (int)CORNER, 0),
                        padding = new RectOffset(0, 0, 0, 0),
                        margin = new RectOffset(0, 0, 0, 0),
                    };
                }
                return _headerStyle;
            }
        }

        /// <summary>选中/状态高亮(windowHighlight): 整节点染色叠加。</summary>
        public static GUIStyle NodeHighlightStyle
        {
            get
            {
                if (_highlightStyle == null)
                {
                    _highlightStyle = new GUIStyle(GUIStyle.none)
                    {
                        normal = { background = HighlightTexture },
                        border = new RectOffset(8, 8, 8, 8),
                        padding = new RectOffset(0, 0, 0, 0),
                        margin = new RectOffset(0, 0, 0, 0),
                    };
                }
                return _highlightStyle;
            }
        }

        /// <summary>节点阴影(windowShadow): 软黑, 偏移绘制。</summary>
        public static GUIStyle NodeShadowStyle
        {
            get
            {
                if (_shadowStyle == null)
                {
                    _shadowStyle = new GUIStyle(GUIStyle.none)
                    {
                        normal = { background = ShadowTexture },
                        border = new RectOffset(8, 8, 8, 8),
                        padding = new RectOffset(0, 0, 0, 0),
                        margin = new RectOffset(0, 0, 0, 0),
                    };
                }
                return _shadowStyle;
            }
        }

        /// <summary>通用 box(对应 StyleSheet.box, 用于 minimap 面板/lens)。</summary>
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

        /// <summary>节点标题(windowTitle: 居中 / 12px / 粗体 / richText / padding 5,7,5,7)。</summary>
        public static GUIStyle NodeTitleStyle
        {
            get
            {
                if (_titleStyle == null)
                {
                    _titleStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Normal, // 粗体由文本 <b> 标签提供
                        fontSize = 12,
                        richText = true,
                        padding = new RectOffset(5, 5, 7, 5),
                        margin = new RectOffset(0, 0, 0, 0),
                        wordWrap = false,
                    };
                }
                return _titleStyle;
            }
        }

        // ============================================================
        //  绘制辅助
        // ============================================================

        /// <summary>画节点阴影(对应 windowShadow, 偏移(3.5,3.5) 半透明黑; 用 DrawRect 保证可见)。</summary>
        public static void DrawNodeShadow(Rect rect)
        {
            var sh = rect;
            sh.x += 3.5f; sh.y += 3.5f;
            EditorGUI.DrawRect(sh, new Color(0f, 0f, 0f, 0.28f));
        }

        /// <summary>画一个填充圆(用于连线端帽/状态点/端口)。</summary>
        public static void DrawCircle(Vector2 center, float size, Color color)
        {
            var r = new Rect(0, 0, size, size) { center = center };
            GUI.color = color;
            GUI.DrawTexture(r, CircleTexture);
            GUI.color = Color.white;
        }

        /// <summary>根据分类色明暗决定标题文字色(暗底白字 / 亮底黑字)。</summary>
        public static Color NodeTextColor(Color cat)
        {
            return cat.grayscale > 0.6f ? new Color(0.12f, 0.12f, 0.14f) : Color.white;
        }

        // ============================================================
        //  纹理生成
        // ============================================================

        private static Texture2D MakeSolid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static Texture2D MakeSolidCircle(int s)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s - 0.5f;
                    float dy = (y + 0.5f) / s - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    t.SetPixel(x, y, d <= 1f ? Color.white : Color.clear);
                }
            t.Apply();
            return t;
        }

        /// <summary>生成圆角矩形纹理(等价 window 9-slice 资源)。</summary>
        private static Texture2D MakeRounded(int size, float topR, float botR, Color fill, Color? border, float borderT)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float r = (py < size * 0.5f) ? topR : botR;
                    float sd = SignedRound(px, py, size, r);
                    if (sd > 0f)
                    {
                        t.SetPixel(x, y, new Color(0, 0, 0, 0));
                        continue;
                    }
                    float edge = -sd;
                    Color col = fill;
                    if (border.HasValue && borderT > 0f && edge < borderT) col = border.Value;
                    // 顶部轻微高光(玻璃感)
                    if (py < size * 0.4f) col = Color.Lerp(col, Color.white, 0.05f);
                    t.SetPixel(x, y, col);
                }
            t.Apply();
            return t;
        }

        /// <summary>头部: 白色 + 顶亮底暗渐变, 由 GUI.color 染色得到带光泽的分类色条。</summary>
        private static Texture2D MakeHeader()
        {
            int size = 32;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float sd = SignedRound(px, py, size, CORNER); // 仅顶部圆角
                    if (sd > 0f) { t.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
                    float g = Mathf.Lerp(0.78f, 1.0f, (size - py) / size); // 顶亮底暗
                    t.SetPixel(x, y, new Color(g, g, g, 1f));
                }
            t.Apply();
            return t;
        }

        /// <summary>软阴影: 圆角黑, 外部柔化。</summary>
        private static Texture2D MakeShadow()
        {
            int size = 48;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float sd = SignedRound(px, py, size, 10f);
                    float a;
                    if (sd > 0f) a = Mathf.Clamp(1f - sd / 8f, 0f, 1f) * 0.55f;
                    else a = 0.4f;
                    t.SetPixel(x, y, new Color(0, 0, 0, a));
                }
            t.Apply();
            return t;
        }

        /// <summary>有符号距离到圆角矩形(负=内部)。</summary>
        private static float SignedRound(float px, float py, float size, float r)
        {
            float hw = size * 0.5f;
            float qx = Mathf.Abs(px - hw) - (hw - r);
            float qy = Mathf.Abs(py - hw) - (hw - r);
            Vector2 q = new Vector2(qx, qy);
            float outside = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
            return outside;
        }
    }
}
