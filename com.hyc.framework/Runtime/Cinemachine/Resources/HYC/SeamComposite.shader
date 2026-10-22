// ============================================================
// HYC Framework - Cinemachine 相机装配 · 接缝合成
// 文件: Runtime/Cinemachine/Resources/HYC/SeamComposite.shader
// 说明: 两张整屏 RenderTexture 按「接缝」逐像素择一合成。
//       放在 Resources 下是为了保证打包不被剥离（合成材质是运行期 new 出来的，
//       没有序列化引用，Shader.Find 单独用会在 Build 里失效）。
//       SeamDistance 必须与 SeamState.SignedDistance 逐项一致。
// ============================================================

Shader "HYC/Cinemachine/SeamComposite"
{
    Properties
    {
        _TexA ("Seam A", 2D) = "black" {}
        _TexB ("Seam B", 2D) = "black" {}
        _TexCross ("Crossing Layer", 2D) = "black" {}
        _SeamPoint ("Seam Point", Vector) = (0.5, 0.5, 0, 0)
        _SeamAngle ("Seam Angle (radian)", Float) = 0
        _Curvature ("Curvature", Range(-1, 1)) = 0
        _GapWidth ("Gap Width", Float) = 0
        _GapColor ("Gap Color", Color) = (0, 0, 0, 1)
        _Feather ("Feather", Float) = 0
        _Noise ("Noise", Range(0, 1)) = 0
        _NoiseSpeed ("Noise Speed", Float) = 0
        _Push ("Push", Range(-1, 1)) = 0
        _Crossfade ("Crossfade", Range(-1, 1)) = 0
        _CrossingWidth ("Crossing Width", Float) = 0
        _CrossEnabled ("Cross Enabled", Float) = 0
        _TintA ("Side A Tint", Color) = (0, 0, 0, 0)
        _TintB ("Side B Tint", Color) = (0, 0, 0, 0)
        _FlipY ("Flip Y", Float) = 0
        _GlowWidth ("Glow Width", Float) = 0
        _GlowColor ("Glow Color", Color) = (0, 0, 0, 0)
        _GlowPulse ("Glow Pulse (Hz)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TexA;
            sampler2D _TexB;
            sampler2D _TexCross;
            float4 _SeamPoint;
            float _SeamAngle;
            float _Curvature;
            float _GapWidth;
            fixed4 _GapColor;
            float _Feather;
            float _Noise;
            float _NoiseSpeed;
            float _Push;
            float _Crossfade;
            float _CrossingWidth;
            float _CrossEnabled;
            float4 _TintA;
            float4 _TintB;
            float _FlipY;
            float _GlowWidth;
            fixed4 _GlowColor;
            float _GlowPulse;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 与 SeamState.SignedDistance 逐项一致：>0 归 B，<0 归 A。
            // _SeamAngle 是**法线**方向角：0 = 竖线（法线朝 +x），PI/2 = 横线
            float SeamDistance(float2 p)
            {
                float2 normal = float2(cos(_SeamAngle), sin(_SeamAngle));
                float2 tangent = float2(-normal.y, normal.x);
                float2 d = p - _SeamPoint.xy;

                float s = dot(d, normal);
                float u = dot(d, tangent);

                s -= _Curvature * (1.0 - 4.0 * u * u);
                s += (Hash21(p * 32.0 + _Time.y * _NoiseSpeed) - 0.5) * _Noise * 0.06;

                return s + _Push;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                // i.uv 是视口空间（原点左下），与 SeamState 的坐标系一致
                float2 seamUv = i.uv;

                // _FlipY 默认 0：RawImage 上 RT 与 UI 四边形 UV 同向，本来就不需要翻。
                // 只有自己 Graphics.Blit 的自定义后端才传 1。接缝数学始终不参与翻转。
                float2 sampleUv = seamUv;
                sampleUv.y = lerp(sampleUv.y, 1.0 - sampleUv.y, _FlipY);

                float s = SeamDistance(seamUv);

                float feather = max(_Feather, 1e-5);
                float w = saturate(smoothstep(-feather, feather, s) + _Crossfade);

                float3 colorA = tex2D(_TexA, sampleUv).rgb;
                float3 colorB = tex2D(_TexB, sampleUv).rgb;
                float3 color = lerp(colorA, colorB, w);

                // 跨缝层：只在接缝带内覆盖，让横跨接缝的物体接得上。
                // 跨缝层相机清屏为全透明，所以按 alpha 混，透明处不覆盖原画面
                if (_CrossEnabled > 0.5)
                {
                    float band = 1.0 - smoothstep(0.0, _CrossingWidth * 0.5 + 1e-5, abs(s));
                    float4 crossSample = tex2D(_TexCross, sampleUv);
                    color = lerp(color, crossSample.rgb, band * crossSample.a);
                }

                // 单侧染色：按接缝权重叠色，alpha 为强度。
                // 权重跟的是「玩家编号」而不是屏幕左右，所以横切/竖切/斜切都跟着人走
                color = lerp(color, _TintA.rgb, _TintA.a * (1.0 - w));
                color = lerp(color, _TintB.rgb, _TintB.a * w);

                // 缝隙：宽度为 0 时整条关掉，避免接缝处出现一像素黑线
                float gap = step(1e-5, _GapWidth) * (1.0 - smoothstep(0.0, _GapWidth * 0.5 + 1e-5, abs(s)));
                color = lerp(color, _GapColor.rgb, gap * _GapColor.a);

                // 接缝光带：把接缝「画出来」，不然一条看不见的线用户不知道能拖。
                // 加法混合 ⇒ 不管底下画面多亮都压得住；_GlowColor.a 当强度，0 = 完全关。
                float glowW = _GlowWidth;
                if (glowW > 1e-5 && _GlowColor.a > 1e-5)
                {
                    float band = 1.0 - smoothstep(0.0, glowW, abs(s));
                    band *= band;                                   // 收得更紧，边缘不糊
                    float core = 1.0 - smoothstep(0.0, glowW * 0.18, abs(s));
                    float pulse = _GlowPulse > 1e-5
                        ? 0.72 + 0.28 * sin(_Time.y * _GlowPulse * 6.2831853)
                        : 1.0;
                    color += _GlowColor.rgb * _GlowColor.a * (band * 0.75 + core * 0.6) * pulse;
                }

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
