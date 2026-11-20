// specs/061 FR-07·14·30: 경기장 구조물용 가벼운 Lit. 기본 색·매끄러움 + 정점 색 R = 베이크된 차폐(044 생성기 `ao`, 없으면 1).
// URP 표준 PBR 조명(UniversalFragmentPBR)을 써서 메인 라이트 그림자·하늘 환경광(SH)·반사 큐브맵이 그대로 들어간다. SSR 없음.
Shader "RoadToPro/StadiumSurface"
{
    Properties
    {
        _BaseColor ("기본 색", Color) = (0.6, 0.6, 0.6, 1)
        _Smoothness ("매끄러움", Range(0, 1)) = 0.2
        _Specular ("정반사 세기 (유전체 F0)", Range(0, 0.1)) = 0.04
        _AOStrength ("베이크 차폐 세기", Range(0, 1)) = 1
        [HDR] _EmissionColor ("발광", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EmissionColor;
            half _Smoothness, _Specular, _AOStrength;
        CBUFFER_END
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A
            {
                float4 pos : POSITION;
                float3 normal : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                half3 normal : TEXCOORD1;
                half ao : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            V Vert(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS;
                o.wpos = p.positionWS;
                o.normal = TransformObjectToWorldNormal(i.normal);
                // glb에 `ao`가 없으면 Unity 기본 정점 색(흰색) → 차폐 없음
                o.ao = lerp(1.0h, i.color.r, _AOStrength);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                InputData input = (InputData)0;
                input.positionWS = i.wpos;
                input.positionCS = i.pos;
                input.normalWS = normalize(i.normal);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.wpos);
                input.shadowCoord = TransformWorldToShadowCoord(i.wpos);
                input.fogCoord = i.fog;
                input.bakedGI = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.pos);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = _BaseColor.rgb;
                s.alpha = 1;
                s.metallic = 0;
                s.specular = _Specular.xxx;
                s.smoothness = _Smoothness;
                s.occlusion = i.ao;
                s.emission = _EmissionColor.rgb;
                s.normalTS = half3(0, 0, 1);

                half4 c = UniversalFragmentPBR(input, s);
                // 베이크 차폐는 간접광만이 아니라 직사광도 절반 줄인다 — Low(방향광 그림자 없음)에서 관중석 안쪽이 떠 보이지 않게
                c.rgb *= lerp(1.0h, i.ao, 0.5h);
                c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 pos : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 Vert(A i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 w = TransformObjectToWorld(i.pos.xyz);
                float3 n = TransformObjectToWorldNormal(i.normal);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 l = normalize(_LightPosition - w);
            #else
                float3 l = _LightDirection;
            #endif
                float4 c = TransformWorldToHClip(ApplyShadowBias(w, n, l));
            #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
            #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return c;
            }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 Vert(A i) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(i); return TransformObjectToHClip(i.pos.xyz); }
            half Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            struct A { float4 pos : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; half3 n : TEXCOORD0; };
            V Vert(A i) { UNITY_SETUP_INSTANCE_ID(i); V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.n = TransformObjectToWorldNormal(i.normal); return o; }
            half4 Frag(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.n), 0); }
            ENDHLSL
        }
    }
    Fallback Off
}
