// 선수 몸 (specs/043 FR-01·02). body.png: R 피부·머리카락 명암, G 머리카락 마스크, B 얼굴 특징 어둡게, A 눈 흰자(얼굴 칸 0 영역에서 프리셋 오프셋).
Shader "RoadToPro/PlayerBody"
{
    Properties
    {
        _BodyTex ("몸 텍스처", 2D) = "white" {}
        _SkinColor ("피부 톤", Color) = (0.80, 0.62, 0.50, 1)
        _HairColor ("머리카락 색", Color) = (0.15, 0.10, 0.07, 1)
        _FaceCell ("얼굴 프리셋 0~7", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "PlayerCommon.hlsl"
        TEXTURE2D(_BodyTex); SAMPLER(sampler_BodyTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _BodyTex_ST;
            half4 _SkinColor;
            half4 _HairColor;
            float _FaceCell;
        CBUFFER_END
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
                half4 t = SAMPLE_TEXTURE2D(_BodyTex, sampler_BodyTex, i.uv);
                // 얼굴 칸 오프셋: 분기 없이(암시적 미분이 정의되도록) 항상 읽고 얼굴 영역에서만 쓴다
                float cell = floor(_FaceCell + 0.5);
                float2 off = float2(fmod(cell, 4.0) * 0.25, -floor(cell / 4.0) * 0.25);
                half inFace = (half)(step(i.uv.x, 0.25) * step(0.75, i.uv.y));
                half4 f = SAMPLE_TEXTURE2D(_BodyTex, sampler_BodyTex, i.uv + off * inFace);
                half face = lerp(1.0h, f.b, inFace);
                half sclera = f.a * inFace;
                half3 albedo = lerp(_SkinColor.rgb, _HairColor.rgb, t.g) * t.r * face;
                albedo = lerp(albedo, half3(0.86h, 0.86h, 0.84h) * face, sclera);
                return half4(PlayerLight(albedo, i, 1), 1);
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
