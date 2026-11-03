// 피치 셰이더 (specs/044 FR-30~33). 잔디 베이스 + 두 번째 스케일 디테일 + 깎기 줄무늬(시선 방향으로 명암 반전) + SDF 라인.
// 셸·마모·젖음은 _HighParams 자리만 (048). 시간 의존 항 없음 (캡처 결정론).
Shader "RoadToPro/Pitch"
{
    Properties
    {
        _BaseMap ("잔디 색", 2D) = "white" {}
        _NormalMap ("잔디 노멀 (GL)", 2D) = "bump" {}
        _Tint ("색조", Color) = (0.82, 0.95, 0.78, 1)
        _Tile ("타일 크기 m", Float) = 4.0
        _DetailTile ("디테일 타일 크기 m", Float) = 23.0
        _DetailStrength ("디테일 세기", Range(0, 1)) = 0.35
        _NormalStrength ("노멀 세기", Range(0, 2)) = 0.6
        _StripeStrength ("줄무늬 세기", Range(0, 0.5)) = 0.14
        _LineColor ("라인 색", Color) = (0.93, 0.93, 0.9, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PitchLines.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint;
                float _Tile;
                float _DetailTile;
                float _DetailStrength;
                float _NormalStrength;
                float _StripeStrength;
                float4 _LineColor;
            CBUFFER_END

            float4 _StripeParams; // (폭 m, 패턴 0 직선·1 체커·2 원형, 0, 0)
            float4 _HighParams;   // (셸, 마모, 젖음, 0) — 048 자리, 지금 0

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fog : TEXCOORD1;
            };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.positionWS = v.positionWS;
                o.fog = ComputeFogFactor(v.positionCS.z);
                return o;
            }

            // 띠마다 잎이 눕는 방향(±X 또는 ±Z). 시선과 같은 쪽으로 누운 띠가 밝다 → 카메라가 반대로 돌면 뒤집힌다 (FR-31)
            float StripeShade(float3 posWS, float3 viewDirWS)
            {
                float w = max(_StripeParams.x, 0.1);
                int pattern = (int)_StripeParams.y;
                float2 p = posWS.xz;
                float2 blade;
                if (pattern == 2)
                {
                    float band = floor(length(p) / w);
                    float s = fmod(band, 2.0) < 0.5 ? 1.0 : -1.0;
                    blade = normalize(p + 1e-4) * s;
                }
                else
                {
                    float bx = floor(p.x / w);
                    float parity = pattern == 1 ? bx + floor(p.y / w) : bx;
                    float s = abs(fmod(parity, 2.0)) < 0.5 ? 1.0 : -1.0;
                    blade = float2(s, 0.0);
                }
                float2 vh = viewDirWS.xz;
                float lenV = length(vh);
                vh = lenV > 1e-4 ? vh / lenV : float2(0, 0);
                // 잎이 시선에서 멀어지는 쪽으로 누우면(= 잎 방향 · 시선 방향(카메라→점) > 0) 밝다
                return 1.0 - _StripeStrength * dot(blade, vh);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.positionWS.xz;
                float2 uv = p / _Tile;
                half3 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                // 디테일(반복 감추기)은 저주파라 높은 밉으로 읽는다 — 캐시·대역폭 절약
                half3 detail = SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, p / _DetailTile + 0.37, 2.0).rgb;
                half3 albedo = base * lerp(1.0h, detail * 2.0h, (half)_DetailStrength) * (half3)_Tint.rgb;

                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalStrength);
                // 평면 피치: 탄젠트 = +X, 비탄젠트 = +Z, 노멀 = +Y
                half3 n = normalize(half3(nTS.x, nTS.z, nTS.y));

                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS); // 점 → 카메라
                albedo *= StripeShade(i.positionWS, -viewDir);

                half cover = (half)PitchLineCoverage(p);
                albedo = lerp(albedo, (half3)_LineColor.rgb, cover);
                n = normalize(lerp(n, half3(0, 1, 0), cover));

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                half3 diffuse = light.color * (saturate(dot(n, light.direction)) * light.shadowAttenuation);
                half3 ambient = SampleSH(n);
                half3 c = albedo * (diffuse + ambient);
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 Vert(Attributes i) : SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }
            half Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // SSAO(High 렌더러)가 읽는 깊이·노멀. 피치는 평면이라 노멀 = +Y
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 Vert(Attributes i) : SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }
            half4 Frag() : SV_Target { return half4(NormalizeNormalPerPixel(half3(0, 1, 0)), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
