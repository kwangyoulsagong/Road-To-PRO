// specs/061 FR-10·11·15·16·17: 절차 하늘. 그라데이션 + 구름(시뮬 시각으로 흐름) + 지평선 원경 실루엣 + 나이트 도시광.
// 외부 HDRI 없음 (라이선스·용량·시간대 방위 맞춤 문제). 원경은 건물·나무의 일반 형태 노이즈 — 실존 스카이라인 아님 (01-legal).
// 구름 위치는 CPU가 double 시뮬 시각으로 계산해 0~1로 감싼 _CloudOffset으로 넘긴다 (렌더 프레임 누적 없음 → 결정론).
Shader "RoadToPro/Sky"
{
    Properties
    {
        _SkyTop ("하늘 위", Color) = (0.30, 0.50, 0.85, 1)
        _SkyHorizon ("지평선", Color) = (0.72, 0.80, 0.88, 1)
        _GroundColor ("지평선 아래", Color) = (0.32, 0.34, 0.30, 1)
        _SunDir ("태양 방향 (월드, 태양 쪽)", Vector) = (0.3, 0.6, 0.4, 0)
        _SunColor ("태양 색", Color) = (1, 0.96, 0.9, 1)
        _SunSize ("태양 원반", Range(0, 1)) = 0.6
        _CloudColor ("구름", Color) = (0.95, 0.96, 0.98, 1)
        _CloudShade ("구름 아래 그늘", Color) = (0.62, 0.66, 0.72, 1)
        _CloudCover ("구름 양", Range(0, 1)) = 0.45
        _CloudScale ("구름 크기", Float) = 0.9
        _CloudOffset ("구름 이동 (CPU)", Vector) = (0, 0, 0, 0)
        _CityGlow ("도시광 (rgb, a = 세기)", Color) = (1.0, 0.62, 0.35, 0)
        _BackdropColor ("원경 실루엣", Color) = (0.42, 0.47, 0.52, 1)
        _BackdropHeight ("원경 높이 (고도 사인)", Range(0, 0.2)) = 0.03
        _Haze ("원경 대기 원근", Range(0, 1)) = 0.55
        [Toggle(_SKY_MASK)] _SkyMask ("검증 마스크 (하늘 = 자홍, 원경 = 파랑)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _SKY_MASK // 검증 마스크 — shader_feature는 빌드에서 빠진다
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop, _SkyHorizon, _GroundColor, _SunColor, _CloudColor, _CloudShade, _CityGlow, _BackdropColor;
                float4 _SunDir, _CloudOffset;
                half _SunSize, _CloudCover, _CloudScale, _BackdropHeight, _Haze;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            V Vert(A i)
            {
                V o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.dir = i.pos.xyz;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++) { v += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            // 원경 높이 (방위각 → 고도 사인): 블록형 건물 띠 + 둥근 나무 띠
            float Backdrop(float az)
            {
                float seg = floor(az * 160.0);
                float building = step(0.45, Hash(float2(seg, 3.7))) * (0.35 + 0.65 * Hash(float2(seg, 9.1)));
                float trees = 0.20 + 0.30 * Fbm(float2(az * 240.0, 1.3));
                return _BackdropHeight * max(building, trees);
            }

            half4 Frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float e = d.y;
                float az = atan2(d.z, d.x) / (2.0 * PI) + 0.5;
                float h = Backdrop(az);
                bool below = e < 0.0;
                bool backdrop = !below && e < h;
            #if defined(_SKY_MASK)
                return below || backdrop ? half4(0, 0, 1, 1) : half4(1, 0, 1, 1);
            #endif
                // 그라데이션: 지평선 → 위 (위로 갈수록 진하게)
                float up = saturate(e);
                half3 col = lerp(_SkyHorizon.rgb, _SkyTop.rgb, pow(up, 0.32));

                // 태양: 원반 + 넓은 번짐 (밝은 쪽 = 방향광 방위, FR-17)
                float3 sd = normalize(_SunDir.xyz);
                float c = saturate(dot(d, sd));
                col += _SunColor.rgb * (pow(c, 900.0 * (1.1 - _SunSize)) * 2.0 + pow(c, 12.0) * 0.18) * step(0.0, e);

                // 구름: 하늘 평면 투영 (지평선 근처는 옅게)
                if (e > 0.0)
                {
                    float2 uv = d.xz / (e + 0.06) * _CloudScale + _CloudOffset.xy * 8.0;
                    float n = Fbm(uv);
                    float cov = smoothstep(1.0 - _CloudCover, 1.0 - _CloudCover + 0.28, n);
                    float lit = saturate(Fbm(uv + sd.xz * 0.35) - n + 0.6);
                    half3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, lit) + _SunColor.rgb * pow(c, 8.0) * 0.25;
                    // 지평선 바로 위까지 구름이 내려온다 (클로즈업·피치 레벨 시점에서도 하늘이 단색이 아니게, FR-11)
                    col = lerp(col, cloud, cov * smoothstep(0.0, 0.035, e) * (0.75 + 0.25 * smoothstep(0.0, 0.3, e)));
                }

                // 나이트 도시광: 지평선 위 따뜻한 번짐
                col += _CityGlow.rgb * _CityGlow.a * exp(-max(e, 0.0) * 18.0);

                // 원경 실루엣 (대기 원근으로 지평선 색에 섞음)
                if (backdrop)
                {
                    half3 sil = _BackdropColor.rgb * (0.85 + 0.15 * Hash(float2(floor(az * 160.0), 5.0)));
                    col = lerp(sil, _SkyHorizon.rgb, _Haze * 0.6);
                }
                if (below) col = lerp(_GroundColor.rgb, _SkyHorizon.rgb, _Haze * exp(e * 30.0) * 0.5);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
