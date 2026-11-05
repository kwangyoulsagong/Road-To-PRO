// 선수 몸 (specs/056 FR-21·30·31·40, 043 FR-01·02 대체). 피부·눈·머리카락이 한 머티리얼(드로우콜 증가 없음).
// UV u < 1 → 피부(_SkinTex: R 밝기 디테일 0.8 = 1배, G 붉은 기운, B 수염 밀도, A 거칠기 / _SkinNormal 탄젠트 노멀)
// UV u ≥ 1 → parts(_PartsTex, u − 1): 눈 칸(R 밝기·반사점, G 홍채 마스크, A 거칠기), 머리카락 칸(R 가닥 밝기, G 결 위상, B 마스크 1).
// UV u ≥ 2 → 가닥 카드(specs/057 FR-10·12): parts(u − 2) 카드 칸(R 가닥 밝기, G 알파, B 마스크 1). 4×4 화면 디더로 알파 클립 —
//            투명 큐·정렬 없이 불투명 패스 안에서 그린다(드로우콜 증가 0). 깊이·그림자 패스도 같은 클립.
// 조명: 피부는 채널별 랩 조명(그림자 경계가 붉고 부드러움 — 저비용 산란 근사) + 정규화 블린-퐁(정반사 세기 상한, "플라스틱 피부" 방지),
//       눈은 매끈한 정반사, 머리카락은 Kajiya-Kay 이방성 하이라이트(가닥 = UV v 방향). 시간 의존 항 없음.
Shader "RoadToPro/PlayerBody"
{
    Properties
    {
        _SkinTex ("피부 텍스처", 2D) = "white" {}
        _SkinNormal ("피부 노멀", 2D) = "bump" {}
        _PartsTex ("눈·머리카락", 2D) = "white" {}
        _SkinColor ("피부 톤", Color) = (0.80, 0.62, 0.50, 1)
        _HairColor ("머리카락 색", Color) = (0.15, 0.10, 0.07, 1)
        _EyeColor ("홍채 색", Color) = (0.30, 0.18, 0.10, 1)
        _BeardLevel ("수염 0~1", Range(0, 1)) = 0
        _NormalStrength ("노멀 세기", Range(0, 2)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "PlayerCommon.hlsl"
        TEXTURE2D(_SkinTex); SAMPLER(sampler_SkinTex);
        TEXTURE2D(_SkinNormal); SAMPLER(sampler_SkinNormal);
        TEXTURE2D(_PartsTex); SAMPLER(sampler_PartsTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _SkinTex_ST;
            half4 _SkinColor;
            half4 _HairColor;
            half4 _EyeColor;
            half _BeardLevel;
            half _NormalStrength;
        CBUFFER_END

        // 057: 카드 픽셀 알파(u ≥ 2만, 나머지 1). 화면 4×4 베이어 디더 문턱 — MSAA·TAA 없이도 가장자리가 가닥처럼 끊긴다
        half CardAlpha(float2 uv)
        {
            half isCard = (half)step(2.0, uv.x);
            half a = SAMPLE_TEXTURE2D(_PartsTex, sampler_PartsTex, float2(uv.x - 2.0, uv.y)).g;
            return lerp(1.0h, a, isCard);
        }

        half Bayer4(float2 pix)
        {
            uint2 p = (uint2)pix & 3u;
            const half m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            return (m[p.y * 4u + p.x] + 0.5h) / 16.0h;
        }

        void ClipCard(float2 uv, float4 positionCS)
        {
            clip(CardAlpha(uv) - Bayer4(positionCS.xy));
        }
        ENDHLSL

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
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fog : TEXCOORD4;
            };

            V Vert(A i)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = (half3)n.normalWS;
                o.tangentWS = half4((half3)n.tangentWS, (half)(i.tangentOS.w * GetOddNegativeScale()));
                o.uv = i.uv;
                o.fog = (half)ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // 채널별 랩: 빨강이 가장 넓게 감싼다 → 그림자 경계가 붉게 번진다
            half3 SkinDiffuse(half ndl)
            {
                const half3 w = half3(0.42h, 0.22h, 0.16h);
                return saturate((ndl + w) / (1.0h + w)) * saturate((ndl + w) / (1.0h + w));
            }

            half Spec(half3 n, half3 h, half ndl, half rough)
            {
                half ndh = saturate(dot(n, h));
                half power = exp2(10.0h * (1.0h - rough) + 1.0h);
                return (power + 8.0h) * (1.0h / 25.1327h) * pow(ndh, power) * ndl;
            }

            half4 Frag(V i) : SV_Target
            {
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                ClipCard(i.uv, i.positionCS);
                // 두 텍스처를 분기 없이 읽는다(암시적 미분이 정의되도록) — 043 키트 셰이더 교훈
                half isPart = (half)step(1.0, i.uv.x);
                half isCard = (half)step(2.0, i.uv.x);
                half4 s = SAMPLE_TEXTURE2D(_SkinTex, sampler_SkinTex, frac(i.uv));
                half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_SkinNormal, sampler_SkinNormal, frac(i.uv)));
                half4 pt = SAMPLE_TEXTURE2D(_PartsTex, sampler_PartsTex, float2(i.uv.x - 1.0 - isCard, i.uv.y));
                pt.g = lerp(pt.g, 0.5h, isCard); // 카드의 G는 알파 — 결 위상은 가운데

                half3 nG = normalize(i.normalWS);
                half3 t = normalize(i.tangentWS.xyz);
                half3 b = cross(nG, t) * i.tangentWS.w;
                tn.xy *= _NormalStrength * (1.0h - isPart);
                half3 n = normalize(tn.x * t + tn.y * b + tn.z * nG);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                half3 l = light.direction;
                half3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 h = normalize(l + vdir);
                half ndlRaw = dot(n, l);
                half ndl = saturate(ndlRaw);
                half3 lc = light.color * light.shadowAttenuation;
                half3 amb = SampleSH(n);

                // 피부
                half3 skin = _SkinColor.rgb * (s.r * 1.25h);
                skin *= lerp(half3(1, 1, 1), half3(1.10h, 0.86h, 0.82h), s.g * 0.7h);
                // 057 FR-24: 수염 = 밀도(s.b)의 부드러운 문턱(경계 흐림) × 종류별 덮임. 짧을수록 피부가 비치는 옅은 색조(잔털), 길수록 짙다.
                half lvl = _BeardLevel;
                half lo = 0.75h - lvl * 0.6h;
                half beard = smoothstep(lo, lo + 0.45h, s.b) * lvl * step(0.01h, lvl);
                half3 beardCol = _HairColor.rgb * lerp(0.75h, 0.55h, lvl) * lerp(0.9h, 1.1h, s.b);
                skin = lerp(skin, beardCol, beard * lerp(0.55h, 0.92h, lvl));
                half skinRough = saturate(s.a + beard * 0.3h);
                half3 cSkin = skin * (lc * SkinDiffuse(ndlRaw) + amb) + lc * 0.028h * Spec(n, h, ndl, skinRough);

                // parts: 눈 / 머리카락
                half hairM = pt.b;
                half iris = pt.g * (1.0h - hairM);
                half3 eye = lerp(half3(0.88h, 0.86h, 0.83h) * pt.r, _EyeColor.rgb * (0.5h + pt.r), iris);
                half3 cEye = eye * (lc * ndl + amb) + lc * 0.06h * Spec(nG, h, ndl, pt.a) + pt.r * step(0.97h, pt.r) * 0.35h;
                half3 hairA = _HairColor.rgb * lerp(0.6h, 1.3h, pt.r);
                half3 strand = normalize(b + nG * (pt.g - 0.5h) * 0.4h); // 가닥 방향 = UV v(바이탄젠트), 결 위상으로 흔든다
                half th = dot(strand, h);
                half kk = pow(saturate(sqrt(saturate(1.0h - th * th))), 60.0h) * ndl;
                half3 cHair = hairA * (lc * (ndl * 0.8h + 0.2h) + amb) + lc * (0.10h + 0.5h * hairA) * kk;
                half3 cPart = lerp(cEye, cHair, hairM);

                half3 c = lerp(cSkin, cPart, isPart);
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }

        HLSLINCLUDE
        struct DA { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
        struct DV { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

        DV BodyShadowVert(DA i)
        {
            PlayerAttributes a;
            a.positionOS = i.positionOS; a.normalOS = i.normalOS; a.uv = i.uv;
            DV o; o.positionCS = PlayerShadowVert(a); o.uv = i.uv; return o;
        }

        DV BodyDepthVert(DA i)
        {
            DV o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; return o;
        }

        half BodyDepthFrag(DV i) : SV_Target
        {
        #if defined(LOD_FADE_CROSSFADE)
            LODFadeCrossFade(i.positionCS);
        #endif
            ClipCard(i.uv, i.positionCS);
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex BodyShadowVert
            #pragma fragment BodyDepthFrag
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex BodyDepthVert
            #pragma fragment BodyDepthFrag
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            ENDHLSL
        }
    }
    FallBack Off
}
