// 선수 키트 (specs/043 FR-20·21, 062 FR-20·25·26·30·31·97). kit_mask: R 주색·G 보조색·B 포인트색 가중치, A = 1 팀 색 / 0 축구화.
// 062: 구운 주름·봉제선 노멀(_DetailN)·AO(_DetailAO), 짜임 높이 타일(_WeaveTex RGB = 메시·평직·골지, 영역 맵 B로 고름),
//      영역 맵(_RegionTex R 거칠기·G 인쇄 거칠기·B 결 선택·A 젖음 대상) → 정규화 블린-퐁 정반사(F0 0.04). 인쇄는 같은 섭동 노멀을 따르고
//      거칠기만 G(더 매끈). 먼 밉은 노멀·짜임이 평탄해지도록 임포트(FR-94). 시간 의존 항 없음.
// 번호·이름: 팀 아틀라스(_TextTex, R = 글자 덮임)에서 이 선수 칸(_TextCell)을 영역(UV 사각형)에 매핑. 아틀라스는 팀당 1회 합성(KitCompositor).
Shader "RoadToPro/PlayerKit"
{
    Properties
    {
        _MaskTex ("키트 마스크", 2D) = "white" {}
        _TextTex ("팀 번호·이름 아틀라스", 2D) = "black" {}
        _Primary ("주색", Color) = (0.8, 0.1, 0.15, 1)
        _Secondary ("보조색", Color) = (1, 1, 1, 1)
        _Accent ("포인트색", Color) = (0.1, 0.1, 0.1, 1)
        _BootColor ("축구화 색", Color) = (0.08, 0.08, 0.09, 1)
        _TextColor ("글자 색", Color) = (1, 1, 1, 1)
        _TextCell ("선수 칸 (u, v, w, h)", Vector) = (0, 0, 0, 0)
        _NameArea ("뒤 이름 영역 UV (u0, v0, u1, v1)", Vector) = (0.60, 0.865, 0.90, 0.925)
        _NumberArea ("뒤 번호 영역 UV", Vector) = (0.63, 0.60, 0.87, 0.84)
        _FrontArea ("앞 번호 영역 UV", Vector) = (0.29, 0.80, 0.37, 0.88)
        _NameSub ("칸 안 이름 위치 (x, y, w, h 비율)", Vector) = (0.03125, 0.7, 0.9375, 0.3)
        _NumberSub ("칸 안 번호 위치", Vector) = (0.28125, 0, 0.4375, 0.7)
        _FabricTile ("원단 반복", Float) = 24
        _DetailN ("구운 주름 노멀", 2D) = "bump" {}
        _DetailAO ("구운 차폐", 2D) = "white" {}
        _WeaveTex ("짜임 높이 타일", 2D) = "gray" {}
        _RegionTex ("영역 맵", 2D) = "white" {}
        _DetailStrength ("주름 노멀 세기", Range(0, 2)) = 0.6
        _WeaveTile ("짜임 반복", Float) = 40
        _WeaveStrength ("짜임 노멀 세기", Range(0, 4)) = 0.5
        _Wetness ("젖음 0~1 (078 입력)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "PlayerCommon.hlsl"
        TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
        TEXTURE2D(_TextTex); SAMPLER(sampler_TextTex);
        TEXTURE2D(_DetailN); SAMPLER(sampler_DetailN);
        TEXTURE2D(_DetailAO); SAMPLER(sampler_DetailAO);
        TEXTURE2D(_WeaveTex); SAMPLER(sampler_WeaveTex);
        TEXTURE2D(_RegionTex); SAMPLER(sampler_RegionTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MaskTex_ST;
            half4 _Primary, _Secondary, _Accent, _BootColor, _TextColor;
            float4 _TextCell, _NameArea, _NumberArea, _FrontArea, _NameSub, _NumberSub;
            float _FabricTile;
            half _DetailStrength, _WeaveStrength, _Wetness;
            float _WeaveTile;
        CBUFFER_END

        // 영역 밖 = 0. 경계는 화면 1픽셀 폭으로 부드럽게. 미분(fwidth)은 분기 전에, 샘플은 LOD 0 명시(아틀라스는 밉맵 없음) —
        // 분기 안 암시적 미분은 정의되지 않아 프레임마다 값이 달라질 수 있다(캡처 불안정의 원인이었다).
        half TextCover(float2 uv, float4 area, float4 sub)
        {
            float2 l = (uv - area.xy) / (area.zw - area.xy);
            float2 fw = max(fwidth(l), 1e-5);
            float2 e = saturate(min(l, 1.0 - l) / fw + 0.5);
            float2 a = _TextCell.xy + (sub.xy + saturate(l) * sub.zw) * _TextCell.zw;
            half c = SAMPLE_TEXTURE2D_LOD(_TextTex, sampler_TextTex, a, 0).r;
            return _TextCell.z > 0 ? c * (half)(e.x * e.y) : 0;
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
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

            // 영역 맵 B(0 메시·0.5 평직·1 골지) → 짜임 타일 채널 가중치
            half WeaveH(half3 w, half sel)
            {
                half3 k = half3(step(sel, 0.25h), step(0.25h, sel) * step(sel, 0.75h), step(0.75h, sel));
                return dot(w, k);
            }

            half4 Frag(V i) : SV_Target
            {
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                half4 m = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);
                half3 team = m.r * _Primary.rgb + m.g * _Secondary.rgb + m.b * _Accent.rgb;
                half3 albedo = lerp(_BootColor.rgb, team, m.a);
                half c = max(TextCover(i.uv, _NameArea, _NameSub), max(TextCover(i.uv, _NumberArea, _NumberSub), TextCover(i.uv, _FrontArea, _NumberSub)));
                albedo = lerp(albedo, _TextColor.rgb, c);

                half4 rg = SAMPLE_TEXTURE2D(_RegionTex, sampler_RegionTex, i.uv);
                half3 dn = UnpackNormal(SAMPLE_TEXTURE2D(_DetailN, sampler_DetailN, i.uv));
                half ao = SAMPLE_TEXTURE2D(_DetailAO, sampler_DetailAO, i.uv).r;
                // 짜임: 높이 타일 3탭 차분 → 기울기. 먼 밉은 회색(평탄)으로 페이드돼 기울기가 0이 된다(FR-94)
                float2 wuv = i.uv * _WeaveTile;
                const float px = 1.0 / 256.0;
                half h0 = WeaveH(SAMPLE_TEXTURE2D(_WeaveTex, sampler_WeaveTex, wuv).rgb, rg.b);
                half hx = WeaveH(SAMPLE_TEXTURE2D(_WeaveTex, sampler_WeaveTex, wuv + float2(px, 0)).rgb, rg.b);
                half hy = WeaveH(SAMPLE_TEXTURE2D(_WeaveTex, sampler_WeaveTex, wuv + float2(0, px)).rgb, rg.b);
                // 타일 텍셀이 화면 픽셀보다 작아지면(축소) 무늬가 모아레가 되므로 꺼진다: 텍셀/픽셀 1 → 2에서 1 → 0
                float tpp = max(length(ddx(wuv)), length(ddy(wuv))) * 256.0;
                half wfade = (half)saturate(2.0 - tpp);
                half2 wg = half2(h0 - hx, h0 - hy) * _WeaveStrength * wfade * (1.0h - 0.7h * c);   // 인쇄 필름이 결을 대부분 덮는다
                // 탄젠트 틀: 정점 탄젠트 대신 픽셀 미분으로 만든 여접(cotangent) 틀 — 키트 UV(043)에는 넓이 0·심하게 늘어난 삼각형이 있어
                // 정점 탄젠트가 깨지고 면 단위로 각져 보였다(T20 진단: 셔츠 26·소매 56·반바지 142·축구화 17면). UV가 무너진 곳은 디테일을 끈다
                float3 nG = normalize((float3)i.normalWS);
                float3 dp1 = ddx(i.positionWS), dp2 = ddy(i.positionWS);
                float2 duv1 = ddx(i.uv), duv2 = ddy(i.uv);
                float3 dp2p = cross(dp2, nG), dp1p = cross(nG, dp1);
                float3 tc = dp2p * duv1.x + dp1p * duv2.x;
                float3 bc = dp2p * duv1.y + dp1p * duv2.y;
                float m2 = max(dot(tc, tc), dot(bc, bc));
                float uvArea = abs(duv1.x * duv2.y - duv1.y * duv2.x);
                float wsArea = length(cross(dp1, dp2));
                half uvOk = (half)saturate(uvArea / max(wsArea, 1e-12) * 50.0);   // UV/표면 넓이 비 0.02 → 1 (셔츠 중앙값 ≈ 0.8)
                float inv = m2 > 1e-30 ? rsqrt(m2) : 0.0;
                half3 t = (half3)(tc * inv), b = (half3)(bc * inv);
                half3 tn = half3((dn.xy * _DetailStrength + wg) * uvOk, 1.0h);
                half3 n = normalize(tn.x * t + tn.y * b + tn.z * (half3)nG);   // 인쇄도 같은 섭동 노멀 → 주름을 따라 음영 (FR-25)

                half rough = lerp(rg.r, rg.g, c);                       // 인쇄는 원단보다 매끈 (FR-25·30)
                half wet = _Wetness * rg.a;                             // FR-97: 젖으면 매끈하고 어둡다 (진행 정도는 078)
                rough = lerp(rough, rough * 0.45h, wet);
                albedo *= lerp(1.0h, 0.75h, wet);
                return half4(PlayerLightSpec(albedo, i.positionWS, n, i.fog, ao, 0.04h, rough), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex PlayerShadowVert
            #pragma fragment PlayerDepthFrag
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex PlayerDepthVert
            #pragma fragment PlayerDepthFrag
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex PlayerDepthNormalsVert
            #pragma fragment KitNormalsFrag
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            half4 KitNormalsFrag(PlayerNormalVaryings i) : SV_Target
            {
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
