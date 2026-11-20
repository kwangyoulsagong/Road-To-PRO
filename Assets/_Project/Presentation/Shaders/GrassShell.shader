// specs/061 FR-50~58: 연출 장면 근거리 입체 잔디 (셸). 같은 격자를 높이만 다르게 여러 겹 인스턴스로 그리고, 겹마다 잎 단면만 남긴다.
// 색 = 피치와 같은 텍스처·색조·줄무늬 명암·라인(머티리얼은 피치에서 복사, 라인·줄무늬 데이터는 피치 프로퍼티 블록) → 경계·모드 전환에서 튀지 않음.
// 잎 방향 = 깎기 띠 방향, 바람 = 시뮬 시각 위상(CPU) — 결정론. 공·몸은 옮기지 않는다 (잎 높이 2.8 cm).
Shader "RoadToPro/GrassShell"
{
    Properties
    {
        _BaseMap ("잔디 색", 2D) = "white" {}
        _Tint ("색조", Color) = (0.82, 0.95, 0.78, 1)
        _Tile ("타일 크기 m", Float) = 4.0
        _DetailTile ("디테일 타일 크기 m", Float) = 23.0
        _DetailStrength ("디테일 세기", Range(0, 1)) = 0.35
        _StripeStrength ("줄무늬 세기", Range(0, 0.5)) = 0.14
        _LineColor ("라인 색", Color) = (0.85, 0.85, 0.83, 1)
        _BladeCell ("잎 간격 m", Float) = 0.008
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PitchLines.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint;
                float _Tile;
                float _DetailTile;
                float _DetailStrength;
                float _StripeStrength;
                float4 _LineColor;
                float _BladeCell;
            CBUFFER_END
            float4 _StripeParams;
            float4 _ShellParams; // (잎 높이 m, 페이드 시작 m, 반경 m, 고품질 모드 가중치)
            float4 _WindPhase;   // (위상 x, 위상 y, 진폭, 0)
            #include "GrassCommon.hlsl"

            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                half fog : TEXCOORD1;
            };

            V Vert(A i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS;
                o.wpos = p.positionWS;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            half4 Frag(V i) : SV_Target
            {
                float leaf = _ShellParams.x;
                float h = saturate(i.wpos.y / leaf);                      // 0 뿌리 ~ 1 끝
                float2 p = i.wpos.xz;
                float dist = distance(_WorldSpaceCameraPos.xz, p);
                float fade = 1.0 - smoothstep(_ShellParams.y, _ShellParams.z, dist);

                // 잎 기울기: 띠 방향으로 눕고 바람에 흔들림 (끝으로 갈수록 크게)
                float2 lean = GrassBladeDir(p) * 0.55 + GrassWind(p, _WindPhase.xy, _WindPhase.z);
                float2 q = p - lean * (h * h) * leaf;
                float2 cellId = floor(q / _BladeCell);
                float2 f = frac(q / _BladeCell) - 0.5;
                float r1 = Hash(cellId), r2 = Hash(cellId + 17.3);
                // 잎마다 키가 다르고 끝이 가늘어진다. 페이드·모드 가중치만큼 잎이 서서히 사라진다 (경계에서 튀지 않음)
                float height = 0.55 + 0.45 * r1;
                float radius = 0.42 * (1.0 - h / height);
                clip(height - h);
                clip(radius - length(f));
                clip(fade * _ShellParams.w - r2);

                float2 uv = p / _Tile;
                half3 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                half3 detail = SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, p / _DetailTile + 0.37, 2.0).rgb;
                half3 albedo = base * lerp(1.0h, detail * 2.0h, (half)_DetailStrength) * (half3)_Tint.rgb;
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.wpos);
                albedo *= StripeShade(i.wpos, -viewDir);
                // 뿌리 조금 어둡게·끝 조금 밝게 — 평균은 바닥과 같게 (FR-53 소멸 경계 밝기 차)
                albedo *= (half)(0.86 + 0.28 * h);
                albedo *= ContactShade(p);
                half cover = (half)PitchLineCoverage(p);
                albedo = lerp(albedo, (half3)_LineColor.rgb, cover); // 라인 위 잎은 라인 색 (FR-54)

                half3 n = normalize(half3(lean.x * 0.6, 1.0, lean.y * 0.6));
                Light light = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                half3 c = albedo * (light.color * saturate(dot(n, light.direction)) * light.shadowAttenuation + SampleSH(n));
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
