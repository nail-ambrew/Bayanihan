// Bayanihan/FloodWater: the stylised, muddy flood for gameplay and the cutscenes (URP).
//  - Colour goes from a see-through shallow brown to an opaque deep brown with the water's depth over what is under it.
//  - Foam gathers where the water meets walls, curbs and anything standing in it (needs URP's depth texture; without
//    it there is simply no edge foam).
//  - Small procedural ripples, plus rain rings that follow the storm (global _BayanihanRain, 0..1, set by TyphoonWeather).
//  - The waves themselves are the WaterSurface mesh (moved on the CPU, the same heights the boat floats on).
// The flood's height is the WaterSurface's, which the shared FloodLevel moves.
Shader "Bayanihan/FloodWater"
{
    Properties
    {
        _BaseColor ("Shallow colour (alpha = see-through)", Color) = (0.48, 0.42, 0.30, 0.55)
        _DeepColor ("Deep colour", Color) = (0.27, 0.22, 0.14, 0.92)
        _DepthFade ("Depth to full murk (m)", Float) = 1.4
        _FoamColor ("Foam colour", Color) = (0.86, 0.80, 0.68, 1)
        _FoamWidth ("Edge foam width (m)", Float) = 0.35
        _FoamNoise ("Foam break-up scale", Float) = 1.6
        _RippleScale ("Ripple scale", Float) = 0.9
        _RippleStrength ("Ripple strength", Range(0, 1)) = 0.35
        _RainRings ("Rain ring strength", Range(0, 1)) = 0.8
        _Smoothness ("Smoothness", Range(0, 1)) = 0.78
        // Kept for older checks and material code that read the Lit shader's names: 1 = transparent.
        [HideInInspector] _Surface ("Surface", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 200

        Pass
        {
            Name "FloodWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _FoamColor;
                float _DepthFade;
                float _FoamWidth;
                float _FoamNoise;
                float _RippleScale;
                half _RippleStrength;
                half _RainRings;
                half _Smoothness;
                float _Surface;
            CBUFFER_END

            float _BayanihanRain;   // global: the storm's rain, 0..1

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x), lerp(hash21(i + float2(0, 1)), hash21(i + 1), u.x), u.y);
            }

            // Expanding rings where drops land: one per cell, each on its own clock. Returns the slope (xz) of the rings.
            float2 rainRings(float2 xz, float t)
            {
                float2 slope = 0;
                const float cellSize = 0.8;
                float2 cell = floor(xz / cellSize);
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 c = cell + float2(x, y);
                    float r = hash21(c);
                    float life = frac(t * (0.9 + r * 0.6) + r * 7.0);
                    float2 centre = (c + 0.25 + 0.5 * float2(hash21(c + 3.1), hash21(c + 7.7))) * cellSize;
                    float2 d = xz - centre;
                    float dist = length(d);
                    float radius = life * 0.45;
                    float band = (dist - radius) * 28.0;
                    float ring = sin(band) * exp(-band * band * 0.08) * (1.0 - life);
                    slope += (dist > 1e-4 ? d / dist : 0) * ring;
                }
                return slope;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 n = normalize(i.normalWS);

                // ripples and rain rings bend the normal
                float2 xz = i.positionWS.xz * _RippleScale;
                float2 ripple = float2(noise(xz + t * 0.35) - noise(xz + float2(3.7, 1.3) - t * 0.3),
                                       noise(xz.yx * 1.3 + t * 0.4) - noise(xz.yx * 1.1 - t * 0.25)) * _RippleStrength;
                float rain = saturate(_BayanihanRain);
                ripple += rainRings(i.positionWS.xz, t) * _RainRings * rain * 0.6;
                n = normalize(n + float3(ripple.x, 0, ripple.y));

                // how deep the water is over whatever is under this pixel
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float waterDepth = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float under = max(0, sceneDepth - waterDepth);
                float murk = saturate(under / max(0.01, _DepthFade));

                half4 col = lerp(_BaseColor, _DeepColor, murk);

                // foam along edges, broken up and drifting
                float edge = 1.0 - saturate(under / max(0.01, _FoamWidth));
                float breakup = noise(i.positionWS.xz * _FoamNoise + float2(t * 0.2, -t * 0.15));
                float foam = saturate(edge * edge * 1.6 - breakup * 0.7);
                col.rgb = lerp(col.rgb, _FoamColor.rgb, foam * _FoamColor.a);
                col.a = max(col.a, foam * 0.9);

                // light: soft diffuse, a glint, the sky's ambient
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 h = normalize(sun.direction + v);
                float ndl = saturate(dot(n, sun.direction) * 0.5 + 0.5);
                float spec = pow(saturate(dot(n, h)), exp2(10 * _Smoothness + 1)) * _Smoothness;
                float3 lit = col.rgb * (sun.color * ndl * lerp(0.6, 1.0, sun.shadowAttenuation) + SampleSH(n))
                           + sun.color * spec * 0.6 * sun.shadowAttenuation;
                float fresnel = pow(1.0 - saturate(dot(n, v)), 4);
                lit += SampleSH(reflect(-v, n)) * fresnel * 0.35;
                col.a = saturate(col.a + fresnel * 0.25);

                lit = MixFog(lit, i.fog);
                return half4(lit, col.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
