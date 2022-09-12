Shader "Unlit/BlendShader"
{
    Properties
    {
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            struct LuminanceData
            {
                float m, n, e, s, w;
                float ne, nw, se, sw;
                float highest, lowest, contrast;
            };

            struct EdgeData
            {
                bool isHorizontal;
                float pixelStep;
                float oppositeLuminance, gradient;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            uniform float4 _MainTex_TexelSize;
            sampler2D _BgTex;
            float subpixelBlending;

            #define CONTRAST_THRESHOLD 0.0833
            #define RELATIVE_THRESHOLD 0.166
            #define EDGE_STEP_COUNT 10
            #define EDGE_STEPS 1, 1.5, 2, 2, 2, 2, 2, 2, 2, 4
            #define EDGE_GUESS 8
            #define LUMINANCE_SAMPLE_COUNT 1

            static const float edgeSteps[EDGE_STEP_COUNT] = {EDGE_STEPS};

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // FXAA source: https://catlikecoding.com/unity/tutorials/advanced-rendering/fxaa/
            float4 Sample(float2 uv)
            {
                const fixed4 bg = tex2D(_BgTex, uv);
                fixed4 col = tex2D(_MainTex, uv);
                col = fixed4(bg * (1.0 - col.w) + col.xyz * col.w, 1.0);

                col = saturate(col);
                col.a = LinearRgbToLuminance(col.rgb);
                col.rgb = LinearToGammaSpace(col.rgb);
                return col;
            }

            float GetLuminance(float2 uv, float2 offset)
            {
                uv += _MainTex_TexelSize * offset;
                const float4 col = Sample(uv);
                return col.a;
            }

            LuminanceData SampleLuminanceNeighborhood(float2 uv)
            {
                LuminanceData l;
                l.m = GetLuminance(uv, 0);
                l.n = GetLuminance(uv, float2(0, 1));
                l.e = GetLuminance(uv, float2(1, 0));
                l.s = GetLuminance(uv, float2(0, -1));
                l.w = GetLuminance(uv, float2(-1, 0));
                l.ne = GetLuminance(uv, float2(1, 1));
                l.nw = GetLuminance(uv, float2(-1, 1));
                l.se = GetLuminance(uv, float2(1, -1));
                l.sw = GetLuminance(uv, float2(-1, -1));

                UNITY_UNROLL
                for (int i = 2; i < LUMINANCE_SAMPLE_COUNT; i++)
                {
                    l.n += GetLuminance(uv, float2(0, 1) * i);
                    l.e += GetLuminance(uv, float2(1, 0) * i);
                    l.s += GetLuminance(uv, float2(0, -1) * i);
                    l.w += GetLuminance(uv, float2(-1, 0) * i);
                    l.ne += GetLuminance(uv, float2(1, 1) * i);
                    l.nw += GetLuminance(uv, float2(-1, 1) * i);
                    l.se += GetLuminance(uv, float2(1, -1) * i);
                    l.sw += GetLuminance(uv, float2(-1, -1) * i);
                }

                l.n /= LUMINANCE_SAMPLE_COUNT - 1;
                l.e /= LUMINANCE_SAMPLE_COUNT - 1;
                l.s /= LUMINANCE_SAMPLE_COUNT - 1;
                l.w /= LUMINANCE_SAMPLE_COUNT - 1;
                l.ne /= LUMINANCE_SAMPLE_COUNT - 1;
                l.nw /= LUMINANCE_SAMPLE_COUNT - 1;
                l.se /= LUMINANCE_SAMPLE_COUNT - 1;
                l.sw /= LUMINANCE_SAMPLE_COUNT - 1;

                l.highest = max(max(max(max(l.n, l.e), l.s), l.w), l.m);
                l.lowest = min(min(min(min(l.n, l.e), l.s), l.w), l.m);
                l.contrast = l.highest - l.lowest;
                return l;
            }

            bool InThreshold(LuminanceData l)
            {
                float a = CONTRAST_THRESHOLD;
                float b = RELATIVE_THRESHOLD;
                const float threshold = max(a, b * l.highest);
                return l.contrast < threshold;
            }

            float GetPixelBlendFactor(LuminanceData l)
            {
                float filter = 2.0 * (l.n + l.e + l.s + l.w);
                filter += l.ne + l.nw + l.se + l.sw;
                filter /= 12.0;
                filter = abs(filter - l.m);
                filter = saturate(filter / l.contrast);
                const float blendFactor = smoothstep(0.0, 1.0, filter);
                return blendFactor * blendFactor * subpixelBlending;
            }

            EdgeData DetermineEdge(LuminanceData l)
            {
                EdgeData e;
                const float horizontal =
                    abs(l.n + l.s - 2 * l.m) * 2 +
                    abs(l.ne + l.se - 2 * l.e) +
                    abs(l.nw + l.sw - 2 * l.w);
                const float vertical =
                    abs(l.e + l.w - 2 * l.m) * 2 +
                    abs(l.ne + l.nw - 2 * l.n) +
                    abs(l.se + l.sw - 2 * l.s);
                e.isHorizontal = horizontal >= vertical;

                const float pLuminance = e.isHorizontal ? l.n : l.e;
                const float nLuminance = e.isHorizontal ? l.s : l.w;
                const float pGradient = abs(pLuminance - l.m);
                const float nGradient = abs(nLuminance - l.m);

                e.pixelStep = e.isHorizontal ? _MainTex_TexelSize.y : _MainTex_TexelSize.x;
                e.pixelStep *= 2;

                if (pGradient < nGradient)
                {
                    e.pixelStep = -e.pixelStep;
                    e.oppositeLuminance = nLuminance;
                    e.gradient = nGradient;
                }
                else
                {
                    e.oppositeLuminance = pLuminance;
                    e.gradient = pGradient;
                }

                return e;
            }

            float GetEdgeBlendFactor(LuminanceData l, EdgeData e, float2 uv)
            {
                float2 uvEdge = uv;
                float2 edgeStep;
                if (e.isHorizontal)
                {
                    uvEdge.y += e.pixelStep * 0.5;
                    edgeStep = float2(_MainTex_TexelSize.x, 0);
                }
                else
                {
                    uvEdge.x += e.pixelStep * 0.5;
                    edgeStep = float2(0, _MainTex_TexelSize.y);
                }

                const float edgeLuminance = (l.m + e.oppositeLuminance) * 0.5;
                const float gradientThreshold = e.gradient * 0.25;
                float2 puv = uvEdge + edgeStep * edgeSteps[0];
                float pLuminanceDelta = GetLuminance(puv, 0) - edgeLuminance;
                bool pAtEnd = abs(pLuminanceDelta) >= gradientThreshold;

                UNITY_UNROLL
                for (int i = 1; i < EDGE_STEP_COUNT && !pAtEnd; i++)
                {
                    puv += edgeStep * edgeSteps[i];
                    pLuminanceDelta = GetLuminance(puv, 0) - edgeLuminance;
                    pAtEnd = abs(pLuminanceDelta) >= gradientThreshold;
                }

                if (!pAtEnd)
                {
                    puv += edgeStep * EDGE_GUESS;
                }

                float2 nuv = uvEdge - edgeStep * edgeSteps[0];
                float nLuminanceDelta = GetLuminance(nuv, 0) - edgeLuminance;
                bool nAtEnd = abs(nLuminanceDelta) >= gradientThreshold;

                UNITY_UNROLL
                for (int index = 1; index < EDGE_STEP_COUNT && !nAtEnd; index++)
                {
                    nuv -= edgeStep * edgeSteps[index];
                    nLuminanceDelta = GetLuminance(nuv, 0) - edgeLuminance;
                    nAtEnd = abs(nLuminanceDelta) >= gradientThreshold;
                }

                if (!nAtEnd)
                {
                    nuv -= edgeStep * EDGE_GUESS;
                }

                float pDistance, nDistance;
                if (e.isHorizontal)
                {
                    pDistance = puv.x - uv.x;
                    nDistance = uv.x - nuv.x;
                }
                else
                {
                    pDistance = puv.y - uv.y;
                    nDistance = uv.y - nuv.y;
                }

                float shortestDistance;
                bool deltaSign;
                if (pDistance <= nDistance)
                {
                    shortestDistance = pDistance;
                    deltaSign = pLuminanceDelta >= 0;
                }
                else
                {
                    shortestDistance = nDistance;
                    deltaSign = nLuminanceDelta >= 0;
                }

                if (deltaSign == l.m - edgeLuminance >= 0)
                {
                    return 0;
                }

                return 0.5 - shortestDistance / (pDistance + nDistance);
            }

            float4 Fxaa(float2 uv)
            {
                LuminanceData l = SampleLuminanceNeighborhood(uv);

                if (InThreshold(l))
                {
                    return Sample(uv);
                }

                const float pixelBlend = GetPixelBlendFactor(l);
                const EdgeData e = DetermineEdge(l);
                const float edgeBlend = GetEdgeBlendFactor(l, e, uv);
                const float finalBlend = max(pixelBlend, edgeBlend);

                if (e.isHorizontal)
                {
                    uv.y += e.pixelStep * finalBlend;
                }
                else
                {
                    uv.x += e.pixelStep * finalBlend;
                }
                
                return float4(Sample(uv).rgb, l.m);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 col = Sample(i.uv);
                col.rgb = GammaToLinearSpace(col.rgb);
                return col;
            }
            ENDCG
        }
    }
}