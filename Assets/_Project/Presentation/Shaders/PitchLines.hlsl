// 피치 라인 거리 함수 (specs/044 FR-32). 좌표는 월드 XZ(미터, 센터마크 원점). 음수 = 라인 안.
#ifndef ROADTOPRO_PITCH_LINES_INCLUDED
#define ROADTOPRO_PITCH_LINES_INCLUDED

#define PITCH_MAX_SEGMENTS 32
#define PITCH_MAX_ARCS 16
#define PITCH_MAX_SPOTS 8
#define PITCH_TWO_PI 6.28318530718

float4 _LineSegments[PITCH_MAX_SEGMENTS]; // (ax, az, bx, bz) 중심선
float4 _LineArcs[PITCH_MAX_ARCS];         // (cx, cz, r, cos(반각); 전체 원 −2)
float4 _LineArcEnds[PITCH_MAX_ARCS];      // (가운데 방향 x, z, 각 폭, 0)
float4 _LineSpots[PITCH_MAX_SPOTS];       // (cx, cz, r, 0)
float4 _LineCounts;                       // (선분, 원호, 마크, 접기 1/0)
float _LineHalfWidth;

// 끝을 둥글리지 않는 직사각형 선분
float PitchSegmentDistance(float2 p, float4 s, float hw)
{
    float2 a = s.xy;
    float2 d = s.zw - s.xy;
    float len = max(length(d), 1e-5);
    float2 t = d / len;
    float2 q = p - a;
    float u = dot(q, t);
    float v = abs(q.x * t.y - q.y * t.x);
    return max(v - hw, max(-u, u - len));
}

float PitchArcDistance(float2 p, float4 arc, float2 mid, float hw)
{
    float2 q = p - arc.xy;
    float len = length(q);
    float radial = abs(len - arc.z) - hw;
    // 각도 범위: 가운데 방향과의 내적 ≥ cos(반각) (atan2 없음)
    return dot(q, mid) >= arc.w * len ? radial : 1e3;
}

// 최소 거리. hw는 화면에서 사라지지 않게 늘린 반폭
float PitchLineDistance(float2 p, float hw)
{
    float dmin = 1e4;
    int ns = (int)_LineCounts.x;
    int na = (int)_LineCounts.y;
    int np = (int)_LineCounts.z;
    [loop] for (int i = 0; i < ns; i++) dmin = min(dmin, PitchSegmentDistance(p, _LineSegments[i], hw));
    [loop] for (int j = 0; j < na; j++) dmin = min(dmin, PitchArcDistance(p, _LineArcs[j], _LineArcEnds[j].xy, hw));
    [loop] for (int k = 0; k < np; k++) dmin = min(dmin, length(p - _LineSpots[k].xy) - max(_LineSpots[k].z, hw));
    return dmin;
}

// 라인 덮임 (0~1). 픽셀 크기보다 가는 라인은 최소 1 px로 넓히고 밝기를 비율만큼 줄여 끊김·계단을 막는다.
float PitchLineCoverage(float2 p)
{
    float px = max(length(fwidth(p)), 1e-5);
    float hw = max(_LineHalfWidth, px * 0.5);
    float energy = saturate(_LineHalfWidth / hw);
    // 두 축 대칭이면 1사분면으로 접는다 (바인더가 1사분면 라인만 넘김)
    float2 q = _LineCounts.w > 0.5 ? abs(p) : p;
    float d = PitchLineDistance(q, hw);
    return saturate(0.5 - d / px) * energy;
}

#endif
