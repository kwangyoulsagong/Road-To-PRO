// specs/044 FR-31 깎기 줄무늬 + specs/061 FR-53·55 근거리 잎 방향·바람. 피치(Pitch.shader)와 셸 잔디(GrassShell.shader)가 같은 식을 쓴다.
// 포함 전에 float4 _StripeParams (폭 m, 패턴 0 직선·1 체커·2 원형), float _StripeStrength가 선언돼 있어야 한다.
// C# 판: Presentation/Runtime/Stadium/GrassShell.cs (LeanDirection·Wind) — 식을 바꾸면 둘 다 바꾼다.
#ifndef ROADTOPRO_GRASS_COMMON
#define ROADTOPRO_GRASS_COMMON

// 띠마다 잎이 눕는 방향(±X 또는 ±Z, 원형은 반지름 방향)
float2 GrassBladeDir(float2 p)
{
    float w = max(_StripeParams.x, 0.1);
    int pattern = (int)_StripeParams.y;
    if (pattern == 2)
    {
        float band = floor(length(p) / w);
        float s = fmod(band, 2.0) < 0.5 ? 1.0 : -1.0;
        return normalize(p + 1e-4) * s;
    }
    float bx = floor(p.x / w);
    float parity = pattern == 1 ? bx + floor(p.y / w) : bx;
    float s = abs(fmod(parity, 2.0)) < 0.5 ? 1.0 : -1.0;
    return float2(s, 0.0);
}

// 시선과 같은 쪽으로 누운 띠가 밝다 → 카메라가 반대로 돌면 뒤집힌다 (044 FR-31). viewDirWS = 카메라 → 점
float StripeShade(float3 posWS, float3 viewDirWS)
{
    float2 blade = GrassBladeDir(posWS.xz);
    float2 vh = viewDirWS.xz;
    float lenV = length(vh);
    vh = lenV > 1e-4 ? vh / lenV : float2(0, 0);
    return 1.0 - _StripeStrength * dot(blade, vh);
}

// 바람 흔들림 (잎 높이 배율). phase = 0~1 위상 두 개(CPU가 double 시뮬 시각으로 계산), amp = 최대 크기
float2 GrassWind(float2 p, float2 phase, float amp)
{
    const float tau = 6.28318530718;
    float u = sin((phase.x + (p.x * 0.11 + p.y * 0.07)) * tau);
    float v = sin((phase.y + (p.x * 0.05 - p.y * 0.13)) * tau);
    float2 d = float2(0.8 * u + 0.2 * v, 0.3 * v) * amp;
    float m = length(d);
    return m > amp ? d * (amp / m) : d;
}
// specs/061 FR-44: 선수 발 접촉 어둡기 — 발 위치(전역 배열, PlayerBlobShadows가 매 프레임 넣음) 주변 바닥을 어둡게.
// x, z = 발 위치, w = 1(사용) / 0. 반지름·세기는 _ContactParams (반지름 m, 세기 0~1, 개수, 0).
#define ROADTOPRO_MAX_FEET 25
float4 _ContactFeet[ROADTOPRO_MAX_FEET];
float4 _ContactParams;

half ContactShade(float2 p)
{
    half shade = 1.0h;
    int n = min((int)_ContactParams.z, ROADTOPRO_MAX_FEET);
    float r = max(_ContactParams.x, 0.01);
    for (int i = 0; i < n; i++)
    {
        float d = distance(p, _ContactFeet[i].xz);
        shade *= 1.0h - (half)(_ContactParams.y * (1.0 - smoothstep(0.0, r, d)) * _ContactFeet[i].w);
    }
    return shade;
}
#endif
