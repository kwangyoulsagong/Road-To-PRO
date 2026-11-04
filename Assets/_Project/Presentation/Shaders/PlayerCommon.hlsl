// 선수 셰이더 공통 (specs/043 FR-11·13·20). 단순 램버트 + SH + 메인 라이트 그림자 + LOD 크로스페이드. 시간 의존 항 없음.
#ifndef ROADTOPRO_PLAYER_COMMON
#define ROADTOPRO_PLAYER_COMMON

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct PlayerAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
};

struct PlayerVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    half fog : TEXCOORD3;
};

PlayerVaryings PlayerVert(PlayerAttributes i)
{
    PlayerVaryings o;
    VertexPositionInputs v = GetVertexPositionInputs(i.positionOS.xyz);
    o.positionCS = v.positionCS;
    o.positionWS = v.positionWS;
    o.normalWS = (half3)TransformObjectToWorldNormal(i.normalOS);
    o.uv = i.uv;
    o.fog = (half)ComputeFogFactor(v.positionCS.z);
    return o;
}

half3 PlayerLight(half3 albedo, PlayerVaryings i, half detail)
{
    half3 n = normalize(i.normalWS);
    float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
    Light light = GetMainLight(shadowCoord);
    half ndl = saturate(dot(n, light.direction));
    half3 c = albedo * (light.color * ndl * light.shadowAttenuation + SampleSH(n)) * detail;
    return MixFog(c, i.fog);
}

// 그림자·깊이 공통
float3 _LightDirection;

float4 PlayerShadowVert(PlayerAttributes i) : SV_POSITION
{
    float3 posWS = TransformObjectToWorld(i.positionOS.xyz);
    float3 nWS = TransformObjectToWorldNormal(i.normalOS);
    float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
#if UNITY_REVERSED_Z
    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
#else
    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return cs;
}

float4 PlayerDepthVert(PlayerAttributes i) : SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }

half PlayerDepthFrag(float4 positionCS : SV_POSITION) : SV_Target
{
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(positionCS);
#endif
    return 0;
}

#endif
