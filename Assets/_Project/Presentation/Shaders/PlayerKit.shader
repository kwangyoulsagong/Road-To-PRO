// 선수 키트 (specs/043 FR-20·21). kit_mask: R 주색·G 보조색·B 포인트색 가중치, A = 1 팀 색 / 0 축구화.
// 번호·이름: 팀 아틀라스(_TextTex, R = 글자 덮임)에서 이 선수 칸(_TextCell)을 영역(UV 사각형)에 매핑. 아틀라스는 팀당 1회 합성(KitCompositor).
Shader "RoadToPro/PlayerKit"
{
    Properties
    {
        _MaskTex ("키트 마스크", 2D) = "white" {}
        _FabricTex ("원단 노말", 2D) = "bump" {}
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
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "PlayerCommon.hlsl"
        TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
        TEXTURE2D(_FabricTex); SAMPLER(sampler_FabricTex);
        TEXTURE2D(_TextTex); SAMPLER(sampler_TextTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MaskTex_ST;
            half4 _Primary, _Secondary, _Accent, _BootColor, _TextColor;
            float4 _TextCell, _NameArea, _NumberArea, _FrontArea, _NameSub, _NumberSub;
            float _FabricTile;
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
            #pragma vertex PlayerVert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            half4 Frag(PlayerVaryings i) : SV_Target
            {
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                half4 m = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);
                half3 team = m.r * _Primary.rgb + m.g * _Secondary.rgb + m.b * _Accent.rgb;
                half3 albedo = lerp(_BootColor.rgb, team, m.a);
                half c = max(TextCover(i.uv, _NameArea, _NameSub), max(TextCover(i.uv, _NumberArea, _NumberSub), TextCover(i.uv, _FrontArea, _NumberSub)));
                albedo = lerp(albedo, _TextColor.rgb, c);
                half2 fab = SAMPLE_TEXTURE2D(_FabricTex, sampler_FabricTex, i.uv * _FabricTile).rg * 2 - 1;
                half detail = 1 + (fab.x + fab.y) * 0.04h;
                return half4(PlayerLight(albedo, i, detail), 1);
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
    }
    FallBack Off
}
