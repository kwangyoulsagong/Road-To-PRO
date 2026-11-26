// specs/059 FR-01·10~15: 골망. 거친 변형 격자(UV = 면 위 m) 위에 육각 망을 해석적으로 그린다 — 텍스처 없음.
// 줄 = 육각 칸 테두리까지 거리 < 줄 반폭. 줄이 픽셀보다 가늘면 1 픽셀 폭으로 넓히고 덮임을 줄여(에너지 보존) 깜빡임을 막고,
// 칸이 화면에서 _FadePixels보다 작아지면 평균 덮임(2·줄/칸)으로 서서히 바꿔 모아레를 막는다(거리 단계 튐 없음, FR-12~14).
// 양면 · 반투명(ZWrite 끔) — 불투명 선수·공 뒤 정렬은 깊이 테스트가 맡는다. 노출 뒤 알베도 상한·발광 0으로 나이트에 하얗게 날리지 않는다(FR-15).
Shader "RoadToPro/GoalNet"
{
    Properties
    {
        _BaseColor ("줄 색", Color) = (0.88, 0.88, 0.88, 1)
        _CellSize ("육각 칸 (마주 보는 줄 사이, m)", Float) = 0.12
        _Strand ("줄 굵기 (m)", Float) = 0.004
        _FadePixels ("평균 덮임으로 바꾸는 칸 크기 (px)", Float) = 3
        _Opacity ("줄 불투명도", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _CellSize, _Strand, _FadePixels;
                half _Opacity;
            CBUFFER_END

            struct A
            {
                float4 pos : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                half3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
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
                o.uv = i.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // 칸 단위 좌표 p(마주 보는 줄 사이 = 1)에서 가장 가까운 육각 테두리까지 거리 (칸 단위, ≥ 0)
            float HexEdgeDistance(float2 p)
            {
                const float2 s = float2(1.0, 1.7320508);
                float4 c = floor(float4(p, p - float2(0.5, 0.8660254)) / s.xyxy) + 0.5;
                float4 h = float4(p - c.xy * s, p - (c.zw * s + float2(0.5, 0.8660254)));
                float2 q = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
                q = abs(q);
                float r = max(dot(q, float2(0.5, 0.8660254)), q.x);   // 내접 반지름 0.5 육각형 거리
                return max(0.0, 0.5 - r);
            }

            half4 Frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 p = i.uv / _CellSize;
                float d = HexEdgeDistance(p) * _CellSize;                 // m
                float2 dudx = ddx(i.uv), dudy = ddy(i.uv);
                float px = max(length(float2(dudx.x, dudy.x)), length(float2(dudx.y, dudy.y)));  // 한 픽셀이 덮는 면 위 길이 (m)
                px = max(px, 1e-6);
                // 줄: 픽셀보다 가늘면 1 픽셀 폭으로 넓히고 세기를 줄 / 픽셀로 (에너지 보존 AA)
                float half_w = max(_Strand, px) * 0.5;
                float strand = saturate((half_w - d) / px + 0.5) * saturate(_Strand / px);
                // 칸이 화면에서 _FadePixels보다 작으면 평균 덮임으로 (모아레 없음). 평균 = 2·줄/칸 (육각 격자 줄 밀도)
                float cellPx = _CellSize / px;
                float avg = saturate(2.0 * _Strand / _CellSize);
                float k = saturate((cellPx - _FadePixels) / _FadePixels);
                float cover = lerp(avg, strand, k) * _Opacity;

                half3 n = normalize(i.normal) * (front ? 1.0h : -1.0h);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                half ndl = abs(dot(n, main.direction));                        // 얇은 줄: 양면 모두 받는 빛
                half3 lit = _BaseColor.rgb * (main.color * (0.35h + 0.65h * ndl) * main.shadowAttenuation + SampleSH(n));
                lit = min(lit, half3(0.94h, 0.94h, 0.94h));                     // FR-15: 나이트에 하얗게 날리지 않게 (≤ 240)
                lit = MixFog(lit, i.fog);
                return half4(lit, cover);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
