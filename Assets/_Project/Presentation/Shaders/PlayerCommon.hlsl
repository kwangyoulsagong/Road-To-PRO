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

// specs/061 FR-42: 림 라이트 (전역 값 — StadiumLighting이 넣고 고품질 모드 컨트롤러가 보정). 0이면 기존과 같다.
half _RoadToProRim;

// 088 T30p: 추가 광원 (주 광원 외 — 시안 비교 스튜디오의 뒤 태양 등). Forward(Low·Medium)는 물체별 목록 전부,
// Forward+(High)는 방향광만 센다(점·스폿광은 클러스터 순회가 화면 UV를 요구 — 지금 경기장엔 추가 광원이 없다). 추가 광원 그림자는 없다
uint PlayerAddLightCount()
{
#if defined(_ADDITIONAL_LIGHTS)
  #if USE_CLUSTER_LIGHT_LOOP
    return min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS);
  #else
    return (uint)GetAdditionalLightsCount();
  #endif
#else
    return 0u;
#endif
}

Light PlayerAddLight(uint k, float3 positionWS)
{
#if defined(_ADDITIONAL_LIGHTS)
    return GetAdditionalLight(k, positionWS);
#else
    return (Light)0;
#endif
}

half3 PlayerLight(half3 albedo, PlayerVaryings i, half detail)
{
    half3 n = normalize(i.normalWS);
    float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
    Light light = GetMainLight(shadowCoord);
    half ndl = saturate(dot(n, light.direction));
    half3 sh = SampleSH(n);
    half3 c = albedo * (light.color * ndl * light.shadowAttenuation + sh) * detail;
    // 윤곽(시선과 수직인 면)을 하늘 환경광 + 약한 햇빛으로 살짝 밝힌다 — 빛나는 테두리가 아니라 잔디에서 떨어져 보일 만큼
    half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
    half fres = pow(1.0h - saturate(dot(n, v)), 3.0h);
    c += fres * _RoadToProRim * (sh + light.color * 0.25h * light.shadowAttenuation) * lerp(0.6h, 1.0h, albedo);
    return MixFog(c, i.fog);
}

// 062 FR-30: 정규화 블린-퐁 정반사 (PlayerBody 피부 Spec와 같은 식 — Blender heightfield.spec_peak가 같은 값으로 순서를 검사한다)
half PlayerSpec(half3 n, half3 h, half ndl, half rough)
{
    half ndh = saturate(dot(n, h));
    half power = exp2(10.0h * (1.0h - rough) + 1.0h);
    return (power + 8.0h) * (1.0h / 25.1327h) * pow(ndh, power) * ndl;
}

// 062: 섭동 노멀 n으로 PlayerLight와 같은 확산 + 림 + 정반사 (키트). occlusion은 확산·환경광에만
half3 PlayerLightSpec(half3 albedo, float3 positionWS, half3 n, half fog, half occlusion, half f0, half rough)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light light = GetMainLight(shadowCoord);
    half ndl = saturate(dot(n, light.direction));
    half3 sh = SampleSH(n);
    half3 lc = light.color * light.shadowAttenuation;
    half3 v = normalize(GetWorldSpaceViewDir(positionWS));
    half3 h = normalize(light.direction + v);
    half3 c = albedo * (lc * ndl + sh) * occlusion + lc * f0 * PlayerSpec(n, h, ndl, rough);
    uint addN = PlayerAddLightCount();   // 088 T30p
    [loop] for (uint k = 0u; k < addN; k++)
    {
        Light al = PlayerAddLight(k, positionWS);
        half3 alc = al.color * (al.distanceAttenuation * al.shadowAttenuation);
        half andl = saturate(dot(n, al.direction));
        c += albedo * alc * andl * occlusion + alc * f0 * PlayerSpec(n, normalize(al.direction + v), andl, rough);
    }
    half fres = pow(1.0h - saturate(dot(n, v)), 3.0h);
    c += fres * _RoadToProRim * (sh + light.color * 0.25h * light.shadowAttenuation) * lerp(0.6h, 1.0h, albedo);
    return MixFog(c, fog);
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

// specs/061: SSAO(High 고품질 모드)가 켜지면 URP는 깊이를 DepthNormals 패스로 그린다 — 이 패스가 없으면 선수가 깊이 텍스처에서 빠져
// 피사계 심도가 선수를 먼 배경으로 보고 흐린다.
struct PlayerNormalVaryings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };
PlayerNormalVaryings PlayerDepthNormalsVert(PlayerAttributes i)
{
    PlayerNormalVaryings o;
    o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
    o.normalWS = (half3)TransformObjectToWorldNormal(i.normalOS);
    o.uv = i.uv;
    return o;
}

half PlayerDepthFrag(float4 positionCS : SV_POSITION) : SV_Target
{
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(positionCS);
#endif
    return 0;
}

#endif
