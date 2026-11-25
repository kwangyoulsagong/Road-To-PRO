// 선수 몸 (specs/056 FR-21·30·31·40, 043 FR-01·02 대체). 피부·눈·머리카락이 한 머티리얼(드로우콜 증가 없음).
// UV u < 1 → 피부(088: _SkinTex RGB = MakeHuman CC0 diffuse(세트 light·asian·dark), A 거칠기 × _SkinColor 톤 틴트 / _SkinNormal 탄젠트 노멀,
//            _BrowMask RGB = 눈썹 02·04·09(_BrowChannel로 선택) · A = 속눈썹 선 → 머리색으로 칠한다)
// UV u ≥ 1 → parts(_PartsTex, u − 1): 눈 칸(R 밝기·반사점, G 홍채 마스크, A 거칠기), 머리카락 칸(R 가닥 밝기, G 결 위상, B 마스크 1).
// UV u ≥ 2 → 가닥 카드(specs/057 FR-10·12): parts(u − 2) 카드 칸(R 가닥 밝기, G 알파, B 마스크 1). 4×4 화면 디더로 알파 클립 —
//            투명 큐·정렬 없이 불투명 패스 안에서 그린다(드로우콜 증가 0). 깊이·그림자 패스도 같은 클립.
// 조명: 피부는 채널별 랩 조명(그림자 경계가 붉고 부드러움 — 저비용 산란 근사) + 정규화 블린-퐁(정반사 세기 상한, "플라스틱 피부" 방지),
//       눈은 매끈한 정반사, 머리카락은 Kajiya-Kay 이방성 하이라이트(가닥 = UV v 방향). 시간 의존 항 없음.
// 062 FR-02·12·32·33·91·97: 근육·손 구운 노멀(_MuscleN, 피부 UV)·차폐(_MuscleAO), 피부 두께(_ThicknessTex R: 귀·코끝·손가락 끝 역광 비침),
//       땀(_ThicknessTex G 부위 거칠기 − 0.04 × _Sweat, 정반사 ≤ 1.3배), 젖음(_Wetness, 078 입력).
// 088 FR-53 (T29): 눈썹·속눈썹은 마스크(바탕) + 메시(LOD0 띠·클로즈업 원본 가닥). 마스크 세기 = 1 − 피부 UV1.y (클로즈업 머리 0.65 → 0.35배).
// 062 FR-42·43·54: UV1(HairData, 헤어 메시만 — 몸은 (1, 0)) x = 밀도 → 카드 알파처럼 화면 디더 클립(페이드 아래는 피부가 비친다,
//       모든 패스 같게), y = 컬러 포인트 마스크 → _HairColor(뿌리·옆) ↔ _HairColor2(위·끝). 결 계열 이방성: parts 거칠기(A, 088 groom.atlas 결 타일)가
//       클수록(코일·브레이드·드레드) Kajiya-Kay 지수를 낮춰 하이라이트를 덜 날카롭게.
Shader "RoadToPro/PlayerBody"
{
    Properties
    {
        _SkinTex ("피부 텍스처", 2D) = "white" {}
        _SkinNormal ("피부 노멀", 2D) = "bump" {}
        _PartsTex ("눈·머리카락", 2D) = "white" {}
        _SkinColor ("피부 톤 틴트 (diffuse 배율)", Color) = (1, 1, 1, 1)
        _BrowMask ("눈썹(RGB)·속눈썹(A) 마스크", 2D) = "black" {}
        _BrowChannel ("눈썹 종류 선택", Vector) = (1, 0, 0, 0)
        _HairColor ("머리카락 색", Color) = (0.15, 0.10, 0.07, 1)
        _HairColor2 ("머리카락 위·끝 색 (컬러 포인트)", Color) = (0.15, 0.10, 0.07, 1)
        _EyeColor ("홍채 색", Color) = (0.30, 0.18, 0.10, 1)
        _BeardLevel ("수염 0~1", Range(0, 1)) = 0
        _BeardMask ("수염 영역 RGBA (full·콧수염·염소수염·진한 스텁블)", 2D) = "black" {}
        _BeardChannel ("수염 영역 선택", Vector) = (1, 0, 0, 0)
        _BeardColor ("자연 모색 (수염·눈썹, 088 T30b)", Color) = (0.15, 0.10, 0.07, 1)
        _BeardGrey ("회색 수염 (흰·짙은 가닥 섞기)", Range(0, 1)) = 0
        _NormalStrength ("노멀 세기", Range(0, 2)) = 0.6
        _MuscleN ("근육·손 구운 노멀", 2D) = "bump" {}
        _MuscleAO ("근육·손 구운 차폐", 2D) = "white" {}
        _ThicknessTex ("피부 두께(R)·땀(G)", 2D) = "black" {}
        _MuscleDef ("근육 노멀 세기", Range(0, 2)) = 0.5
        _Translucency ("얇은 부위 역광 비침", Range(0, 1)) = 0.35
        _Sweat ("땀 0/1", Range(0, 1)) = 0
        _MouthOpen ("입 벌림 (088 T30j 치아 차폐, 표정에서)", Range(0, 1)) = 0
        _Wetness ("젖음 0~1 (078 입력)", Range(0, 1)) = 0
        _ShellLift ("셸 헤어 결 밝기 보정 (088 T30a, LOD1·2 렌더러만 1)", Range(0, 1)) = 0
        _CloseupBody ("클로즈업 그림자 맵 받기 (088 T30o, 최고 등급 LOD0 렌더러만 1)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "PlayerCommon.hlsl"
        TEXTURE2D(_SkinTex); SAMPLER(sampler_SkinTex);
        TEXTURE2D(_SkinNormal); SAMPLER(sampler_SkinNormal);
        TEXTURE2D(_PartsTex); SAMPLER(sampler_PartsTex);
        TEXTURE2D(_MuscleN); SAMPLER(sampler_MuscleN);
        TEXTURE2D(_MuscleAO); SAMPLER(sampler_MuscleAO);
        TEXTURE2D(_ThicknessTex); SAMPLER(sampler_ThicknessTex);
        TEXTURE2D(_BeardMask); SAMPLER(sampler_BeardMask);
        TEXTURE2D(_BrowMask); SAMPLER(sampler_BrowMask);
        CBUFFER_START(UnityPerMaterial)
            float4 _SkinTex_ST;
            half4 _SkinColor;
            half4 _HairColor;
            half4 _HairColor2;
            half4 _EyeColor;
            half _BeardLevel;
            half4 _BeardChannel;
            half4 _BeardColor;
            half _BeardGrey;
            half4 _BrowChannel;
            half _NormalStrength;
            half _MuscleDef, _Translucency, _Sweat, _Wetness;
            half _MouthOpen;
            half _ShellLift;
            half _CloseupBody;
        CBUFFER_END

        // 088 T23: 헤어 셸 연속 UV — u ≥ 10이면 u = 10 + 타일 × 100 + 연속 u, v = 연속 v. parts 머리카락 칸 3열 × 2행 타일 안에서 frac으로 반복
        // (Blender groom/bake.shell_rect와 같은 배치: 칸 (0.25, 0) ~ (1, 0.75), 여백 6/1024). 기울기는 frac 전 좌표로 → 이음매 밉 줄 없음
        static const float ShellPad = 6.0 / 1024.0;
        // 088 T29: 눈썹(마스크·메시) 색 = 머리색 × 0.3과 거의 검정의 중간 — 시안(MakeHuman 눈썹 재질)은 머리색과 상관없이 진하다
        half3 BrowColor(half3 hair) { return lerp(hair * 0.3h, half3(0.035h, 0.028h, 0.022h), 0.5h); }
        float2 ShellTileOrigin(float tile)
        {
            float col = tile - 3.0 * floor(tile / 3.0);
            float row = floor(tile / 3.0);
            return float2(0.25 + col * 0.25 + ShellPad, row * 0.375 + ShellPad);
        }

        // parts 아틀라스 좌표 + 기울기 (u < 1 피부는 쓰지 않음). 카드(2 ≤ u < 10) = u − 2, 눈·옛 칸(1 ≤ u < 2) = u − 1, 셸(u ≥ 10) = 타일 반복
        half4 SampleParts(float2 uv)
        {
            half tiled = (half)step(10.0, uv.x);
            half card = (half)step(2.0, uv.x) * (1.0h - tiled);
            float t = max(uv.x - 10.0, 0.0);
            float tile = floor(t / 100.0);
            float2 cont = float2(t - tile * 100.0, uv.y);
            float2 sz = float2(0.25, 0.375) - 2.0 * ShellPad;
            float2 tuv = ShellTileOrigin(tile) + frac(cont) * sz;
            // 088 T30j: parts 영역(1 ≤ u < 2)의 v > 1.25 = 치아(v + 1 표시) — 1을 빼 본래 텍셀을 읽는다.
            // T30k: 클로즈업 치아 = parts 치아 칸(face.TEETH_REGION, v 0.5~0.75 → 표시 1.5~1.75), LOD0 치아 = 흰자 텍셀(1.875)
            float teeth = step(1.25, uv.y) * (1.0 - card) * (1.0 - tiled);
            float2 puv = float2(uv.x - 1.0 - card, uv.y - teeth);
            float2 a = lerp(puv, tuv, tiled);
            float2 dx = lerp(ddx(puv), ddx(cont) * sz, tiled);
            float2 dy = lerp(ddy(puv), ddy(cont) * sz, tiled);
            return SAMPLE_TEXTURE2D_GRAD(_PartsTex, sampler_PartsTex, a, dx, dy);
        }

        // 057: 카드 픽셀 알파(2 ≤ u < 10만, 나머지 1). 화면 4×4 베이어 디더 문턱 — MSAA·TAA 없이도 가장자리가 가닥처럼 끊긴다
        half CardAlpha(float2 uv)
        {
            half isCard = (half)step(2.0, uv.x) * (1.0h - (half)step(10.0, uv.x));
            half a = SampleParts(uv).g;
            return lerp(1.0h, a, isCard);
        }

        half Bayer4(float2 pix)
        {
            uint2 p = (uint2)pix & 3u;
            const half m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            return (m[p.y * 4u + p.x] + 0.5h) / 16.0h;
        }

        // 062: 카드 알파 × 헤어 밀도(UV1.x, 몸·눈은 1) — 같은 화면 디더로 클립
        void ClipCard(float2 uv, half density, float4 positionCS)
        {
            clip(CardAlpha(uv) * density - Bayer4(positionCS.xy));
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
                float2 uv1 : TEXCOORD1;
            };

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fog : TEXCOORD4;
                half2 hd : TEXCOORD5;
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
                o.hd = (half2)i.uv1;
                o.fog = (half)ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // 채널별 랩: 빨강이 가장 넓게 감싼다 → 그림자 경계가 붉게 번진다
            half3 SkinDiffuse(half ndl)
            {
                const half3 w = half3(0.42h, 0.22h, 0.16h);
                return saturate((ndl + w) / (1.0h + w)) * saturate((ndl + w) / (1.0h + w));
            }

            // 088 FR-56 (T30) 피하 산란 근사 — 시안(Cycles MPFB ENHANCED_SSS)과 나란히 비교해 맞춘다.
            // 빛은 피부 속에서 빨강이 가장 멀리 번진다 → 채널마다 다른 노멀로 확산을 계산한다(빨강 = 기하 노멀 쪽, 파랑 = 디테일 노멀).
            // 모공·근육 노멀은 정반사에만 다 들어가고 확산에는 덜 들어가 "얼룩·평면" 대신 부드러운 음영.
            static const half3 TeethTint = half3(1.0h, 0.96h, 0.88h);   // 088 T30k: 치아 상아색 [제안]
            static const half3 GumTint = half3(1.25h, 0.55h, 0.52h);    // 088 T30k: 잇몸 [제안]
            static const half ShellLiftAmount = 0.28h;   // 088 T30a: 카드 보이는 결 밝기 0.74 − 셸 타일 0.46 (parts.png 실측) [제안]
            static const half TeethOcc = 0.12h;  // 088 T30j: 입 다문 표정의 치아 빛 비율 [제안] (0.3은 회색 줄이 남음 — 선형 0.3 ≈ 화면 0.57배)
            static const half3 SssNormalKeep = half3(0.15h, 0.45h, 0.65h);   // 채널별 디테일 노멀 비율 [제안]
            static const half SssAlbedoBlur = 0.6h;                          // 흐린 알베도 비율 [제안]
            static const half SssSheen = 0.02h;                              // 넓은 광택 로브 세기 [제안]
            static const half3 StubbleTint = half3(0.55h, 0.58h, 0.66h);    // 088 T30l: 면도 자국 그늘 = 피부 × 푸르스름한 회색 [제안] (선형 — 화면 ≈ 0.8배 목표)
            static const half StubbleShadow = 0.50h;                          // 088 T30l: 면도 자국 그늘 덮임(stubble), heavy는 × 1.2 [제안] (0.7·× 1.4는 경기장 조명에서 회색 칠·볼 경계선)
            static const half StubbleDots = 0.75h;                            // 088 T30l: 면도 자국 털뿌리 점이 있는 칸 비율 [제안]
            static const float BeardCells = 600.0;                           // 088 T30b: 수염 가닥 칸 수(피부 UV 1당, 얼굴 ≈ 1~1.5 mm 간격) [제안]
            // 088 T30c: 시안 MPFB 피부 재질(ENHANCED_SSS 노드 그룹)의 colorMixIn — 바탕색 = diffuse × (1 − 0.05) + (1, 0.2, 0.2) × 0.05 (선형).
            // T30의 눈대중 색조 (1.07, 1.02, 1.0)를 대체 — 어두운 피부에서 시안이 더 밝고 붉은 원인이 이것이었다(동일 조건 비교)
            static const half3 SkinMixIn = half3(1.0h, 0.2h, 0.2h);
            static const half SkinMixInStrength = 0.05h;
            // 088 T30h: 피부 그림자 — 빛에 수직인 원판(반경 SkinShadowR) 8곳에서 그림자 맵을 읽어 평균: 피부 속 빛 번짐만큼 경계가 흐려진다.
            // 한 점 그림자는 코 그림자가 "칼로 벤 것" 같은 직선 경계였다(사용자 2026-11-24). 그림자 거리 밖·그림자 없음 품질은 1
            static const float SkinShadowR = 0.006;   // m [제안]
            // 샘플 점은 피부 표면을 따라(법선에 수직) 퍼뜨리고 법선 쪽으로 띄운다 — 빛에 수직인 원판 위 점은 일부가 피부 속으로 들어가
            // 자기 표면에 가려 목에 얼룩 셀프 섀도가 났다
            half SkinSoftShadow(float3 positionWS, half3 nGeo)
            {
                float3 nn = (float3)nGeo;
                float3 u = normalize(cross(abs(nn.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0), nn));
                float3 v = cross(nn, u);
                positionWS += nn * (SkinShadowR * 0.5);
                const float2 k[8] = { float2(1, 0), float2(0.707, 0.707), float2(0, 1), float2(-0.707, 0.707),
                                      float2(-1, 0), float2(-0.707, -0.707), float2(0, -1), float2(0.707, -0.707) };
                half acc = 0.0h;
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float r = SkinShadowR * ((i & 1) ? 0.55 : 1.0);
                    float3 p = positionWS + (u * k[i].x + v * k[i].y) * r;
                    acc += MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                }
                return acc * 0.125h;
            }

            // 088 T30i: 클로즈업 전용 그림자 맵(CloseupShadow.cs — 클로즈업 머리만 태양 정사영, 2048²·머리 하나면 ≈ 0.24 mm/칸).
            // 얼굴 피부가 경기장 맵 대신 이 맵을 같은 표면 원판 8샘플로 읽는다. 꺼짐·맵 밖은 1(빛)
            TEXTURE2D_SHADOW(_CloseupShadowMap); SAMPLER_CMP(sampler_CloseupShadowMap);
            float4x4 _CloseupShadowMatrix;
            float4 _CloseupShadowParams;   // x 켬, y 텍셀 크기 m
            half CloseupShadowAt(float3 p)
            {
                float3 c = mul(_CloseupShadowMatrix, float4(p, 1.0)).xyz;
                if (any(c.xy < 0.0) || any(c.xy > 1.0)) return 1.0h;
                return (half)SAMPLE_TEXTURE2D_SHADOW(_CloseupShadowMap, sampler_CloseupShadowMap, c);
            }
            // 088 T30o: 맵 가장자리(중심에서 반경의 70 → 95 %)에서 경기장 그림자로 부드럽게 넘어간다 — 최고 등급 LOD0 피부용
            half CloseupMapFade(float3 positionWS)
            {
                if (_CloseupShadowParams.x < 0.5) return 0.0h;
                float2 c = mul(_CloseupShadowMatrix, float4(positionWS, 1.0)).xy;
                return (half)(1.0 - smoothstep(0.7, 0.95, length(c - 0.5) * 2.0));
            }
            half CloseupSoftShadow(float3 positionWS, half3 nGeo)
            {
                if (_CloseupShadowParams.x < 0.5) return 1.0h;
                float3 nn = (float3)nGeo;
                float3 u = normalize(cross(abs(nn.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0), nn));
                float3 v = cross(nn, u);
                positionWS += nn * (SkinShadowR * 0.5 + _CloseupShadowParams.y * 4.0);   // 받는 쪽 법선 띄움(눈 밑 비스듬한 면 줄무늬 방지) [제안]
                const float2 k[8] = { float2(1, 0), float2(0.707, 0.707), float2(0, 1), float2(-0.707, 0.707),
                                      float2(-1, 0), float2(-0.707, -0.707), float2(0, -1), float2(0.707, -0.707) };
                half acc = 0.0h;
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float r = SkinShadowR * ((i & 1) ? 0.55 : 1.0);
                    acc += CloseupShadowAt(positionWS + (u * k[i].x + v * k[i].y) * r);
                }
                return acc * 0.125h;
            }

            half3 SkinScatterDiffuse(half3 nD, half3 nGeo, half3 l)
            {
                half3 ndl = half3(dot(SafeNormalize(lerp(nGeo, nD, SssNormalKeep.r)), l),
                                  dot(SafeNormalize(lerp(nGeo, nD, SssNormalKeep.g)), l),
                                  dot(SafeNormalize(lerp(nGeo, nD, SssNormalKeep.b)), l));
                // 088 T30c: 시안 피부는 산란 반경 ≈ 0.5 mm(MPFB SSS radius 0.1 × scale 0.005)라 거의 람베르트 — 이전 랩²(0.42·0.22·0.16)은
                // 중간 밝기를 람베르트보다 15~25 % 어둡게, 초록·파랑을 더 깎아 채도를 올렸다(동일 조건 비교). 작은 랩, 제곱 없음 [제안]
                const half3 w = half3(0.15h, 0.06h, 0.04h);
                return saturate((ndl + w) / (1.0h + w));
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
                ClipCard(i.uv, i.hd.x, i.positionCS);
                // 두 텍스처를 분기 없이 읽는다(암시적 미분이 정의되도록) — 043 키트 셰이더 교훈
                half isPart = (half)step(1.0, i.uv.x);
                half isCard = (half)step(2.0, i.uv.x) * (1.0h - (half)step(10.0, i.uv.x));   // 088: u ≥ 10은 셸(연속 UV)
                half4 s = SAMPLE_TEXTURE2D(_SkinTex, sampler_SkinTex, frac(i.uv));
                // 088 T30: 산란이 알베도 얼룩을 번지게 한다(시안 Cycles 피부는 같은 MakeHuman diffuse인데 매끈) → 밉 바이어스로 흐린 값과 섞는다
                half3 sBlur = SAMPLE_TEXTURE2D_BIAS(_SkinTex, sampler_SkinTex, frac(i.uv), 2.5).rgb;
                half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_SkinNormal, sampler_SkinNormal, frac(i.uv)));
                half3 mn = UnpackNormal(SAMPLE_TEXTURE2D(_MuscleN, sampler_MuscleN, frac(i.uv)));
                half mao = SAMPLE_TEXTURE2D(_MuscleAO, sampler_MuscleAO, frac(i.uv)).r;
                half2 thk = SAMPLE_TEXTURE2D(_ThicknessTex, sampler_ThicknessTex, frac(i.uv)).rg;
                half4 pt = SampleParts(i.uv);
                pt.g = lerp(pt.g, 0.5h, isCard); // 카드의 G는 알파 — 결 위상은 가운데

                half3 nG = SafeNormalize(i.normalWS);   // 088 T29: 0 법선 보간에서 NaN(검은 픽셀) 방지
                // 088 T29: 탄젠트가 0인 정점(UV가 한 점에 몰린 얇은 조각)에서 normalize가 NaN → 픽셀이 검게 → 0이면 법선에 수직인 임의 축
                float3 t0 = (float3)i.tangentWS.xyz;
                half3 t = dot(t0, t0) > 1e-6 ? (half3)normalize(t0) : normalize(cross(nG, abs(nG.y) < 0.99h ? half3(0, 1, 0) : half3(1, 0, 0)));
                half3 b = cross(nG, t) * i.tangentWS.w;
                tn.xy = (tn.xy * _NormalStrength + mn.xy * _MuscleDef) * (1.0h - isPart);   // 모공·잔주름 + 근육·손 형태 (피부만)
                half3 n = SafeNormalize(tn.x * t + tn.y * b + tn.z * nG);
                // 088 T30: 산란 기준 노멀 = 기하 + 근육 형태(큰 굴곡)만 — 모공·잔주름은 빼서 확산이 매끈하다
                half2 mxy = mn.xy * _MuscleDef * (1.0h - isPart);
                half3 nGeo = SafeNormalize(mxy.x * t + mxy.y * b + nG);

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
                half3 skin = lerp(lerp(s.rgb, sBlur, SssAlbedoBlur) * _SkinColor.rgb, SkinMixIn, SkinMixInStrength);   // 088: MakeHuman diffuse × 톤 틴트 (T30: 산란 흐림 섞기, T30c: 시안 colorMixIn)
                half sLum = dot(s.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                // 088 FR-02: 눈썹·속눈썹 = MakeHuman 프록시를 구운 덮임 마스크 × 머리색 (속눈썹은 더 어둡게)
                half4 brow = SAMPLE_TEXTURE2D(_BrowMask, sampler_BrowMask, frac(i.uv));
                // 088 FR-53 (T29): 마스크 세기 = 1 − 피부 UV1.y. 클로즈업 머리 피부는 0.65(원본 가닥이 주, 마스크는 옅은 바탕), LOD0·1·2 몸은 0
                half browOn = (1.0h - isPart) * (1.0h - saturate(i.hd.y));
                half bcov = saturate(dot(brow.rgb, _BrowChannel.rgb) * 1.6h) * browOn;
                skin = lerp(skin, BrowColor(_BeardColor.rgb), bcov * 0.9h);   // 눈썹은 머리색보다 어둡게 (088 T29: 시안 눈썹 ≈ 검정)
                skin = lerp(skin, half3(0.03h, 0.025h, 0.02h), brow.a * 0.8h * browOn);
                // 057 FR-24: 수염 = 밀도(s.b)의 부드러운 문턱(경계 흐림) × 종류별 덮임. 짧을수록 피부가 비치는 옅은 색조(잔털), 길수록 짙다.
                // 062 FR-66: 밀도 = 수염 영역 마스크(beard_mask0)에서 종류별 채널 하나 — 없으면 057 피부 B(full)
                half lvl = _BeardLevel;
                half lo = 0.75h - lvl * 0.6h;
                half bden = dot(SAMPLE_TEXTURE2D_BIAS(_BeardMask, sampler_BeardMask, frac(i.uv), 1.5).rgba, _BeardChannel);   // 088 T30b: 밉 바이어스로 흐리게 — 1024 마스크 작은 UV 섬의 계단 경계 완화
                // 062 T35 육안: 경계 폭 0.45 → 0.65(흐린 가장자리), 털 결(피부 밝기 디테일 R)로 덮임을 흔들어 평면 색 조각처럼 보이지 않게,
                // 회색·밝은 수염은 피부 그늘을 조금 섞어 분칠처럼 뜨지 않게
                half beard = smoothstep(lo, lo + 0.65h, bden) * lvl * step(0.01h, lvl);
                beard *= saturate(0.88h + (sLum - 0.45h) * 1.5h);   // 088: 털 결 = diffuse 밝기 변동
                // 088 T30b: 수염 = 밑바탕 그늘(털뿌리·짧은 털이 모인 평균색) + 털 가닥. 색은 자연 모색(_BeardColor — 탈색 머리여도 짙다).
                // 이전(062·057)은 머리색 반투명 칠 하나 → 회색·금발 머리에서 회청색·올리브색 물감처럼 보였다(T30 비교).
                half3 bcol = _BeardColor.rgb;
                half3 baseCol = lerp(skin * 0.62h, bcol * 0.85h, lerp(0.45h, 0.8h, lvl));           // 밑바탕: 짧을수록 피부 그늘에 가깝다
                baseCol = lerp(baseCol, skin * 0.85h, _BeardGrey * 0.6h);                           // 회색: 흰 털이 섞여 밑바탕이 밝다
                half baseCov = beard * lerp(0.45h, 0.8h, lvl);
                // 088 T30l: 면도 자국 — 짧은 수염(stubble 0.45·heavy 0.6)은 피부 밑 털뿌리가 수염 영역 전체를 고르게 푸르스름하게 어둡게 한다.
                // 위 문턱(lo = 0.48)으로는 턱 가운데만 덮임 ≤ 0.25 → 낮은 문턱의 고른 그늘로 바꾼다. 콧수염·염소수염·짧음·덥수룩은 위 그대로 [제안]
                half shortB = saturate((0.68h - lvl) / 0.08h) * step(0.01h, lvl);   // stubble 0.45·heavy 0.6 → 1, 콧수염·염소수염 0.7 이상 → 0 (PlayerLook.BeardLevels)
                half region = smoothstep(0.2h, 1.0h, bden) * saturate(0.9h + (sLum - 0.45h) * 0.8h);
                half3 shadowCol = lerp(skin * StubbleTint, bcol, 0.12h);
                shadowCol = lerp(shadowCol, skin * 0.92h, _BeardGrey * 0.6h);
                baseCol = lerp(baseCol, shadowCol, shortB);
                baseCov = lerp(baseCov, region * min(lerp(StubbleShadow, StubbleShadow * 1.2h, saturate((lvl - 0.45h) / 0.15h)), 1.0h), shortB);
                beard = lerp(beard, region * lvl, shortB);
                // 털 가닥: 피부 UV 칸마다 가닥 하나(있을 확률 = 덮임), 칸 안 임의 위치·기울기의 짧은 획. 칸이 화면 1.3 px보다 작아지면
                // 가닥 평균 덮임으로 페이드 — 거리·LOD에 따라 평균색이 바뀌지 않는다(FR-58)
                float2 bg = frac(i.uv) * BeardCells;
                float2 bcell = floor(bg);
                float3 hp = frac(float3(bcell.xyx) * float3(0.1031, 0.1030, 0.0973));
                hp += dot(hp, hp.yzx + 33.33);
                half3 hs = (half3)frac((hp.xxy + hp.yzz) * hp.zyx);
                half hw = (half)frac(hs.x * 7.13 + hs.z * 3.71);
                half ang = (hs.x - 0.5h) * 1.6h;
                half2 sd = half2(sin(ang), -cos(ang));
                half2 bq = (half2)(frac(bg) - 0.5) - (hs.yz - 0.5h) * 0.5h;   // 칸 안 위치를 넓게 흩어 격자 줄무늬를 깬다
                half halfLen = lerp(0.14h, 0.30h, lvl), halfW = lerp(0.06h, 0.09h, lvl);
                halfLen = lerp(halfLen, 0.09h, shortB);   // 088 T30l: 면도 자국 = 짧은 털뿌리 점
                halfW = lerp(halfW, 0.065h, shortB);
                half sdist = length(half2(max(abs(dot(bq, sd)) - halfLen, 0.0h), dot(bq, half2(-sd.y, sd.x)))) - halfW;
                float bpx = max(max(fwidth(bg.x), fwidth(bg.y)), 1e-3);
                half prob = saturate((beard - 0.12h) * 1.6h);   // 옅은 가장자리(입술 경계 등)엔 가닥 없음 — 밑바탕만
                prob = lerp(prob, region * StubbleDots, shortB);   // 088 T30l: 면도 자국 점은 영역 전체에 촘촘히
                half spix = saturate(0.5h - sdist / (half)bpx) * step(hw, prob);
                half savg = prob * (4.0h * halfLen * halfW + 3.1416h * halfW * halfW);
                half strandCov = lerp(spix, savg, saturate((half)bpx * 1.5h - 0.6h));
                half3 strandCol = bcol * lerp(0.7h, 1.15h, hs.y);
                strandCol = lerp(strandCol, lerp(half3(0.10h, 0.085h, 0.075h), half3(0.70h, 0.68h, 0.64h), step(0.5h, hs.z)), _BeardGrey);
                skin = lerp(skin, baseCol, baseCov);
                skin = lerp(skin, strandCol, strandCov * 0.9h);
                half skinRough = saturate(s.a + beard * 0.3h);
                skinRough = saturate(skinRough - 0.04h * thk.g * _Sweat);                 // FR-33 땀 (heightfield.SWEAT_DROP)
                skinRough = lerp(skinRough, skinRough * 0.6h, _Wetness);                 // FR-97 젖음
                half occ = lerp(1.0h, mao, 1.0h - isPart);
                // 088 T30: 확산 = 채널별 산란 노멀, 환경광도 산란 노멀로(얼룩 없이). 정반사 = 디테일 노멀 두 로브(좁은 번들 + 넓은 광택 — 시안 이마·코의 부드러운 하이라이트)
                // 088 T30h: 아래를 향한 면(턱 밑·목 앞)은 턱·몸이 가려 하늘빛을 덜 받는다 — 가림이 없으면 턱선을 따라 푸른 띠(사용자 "목이 색이 이상한데") [제안]
                half downOcc = saturate(nGeo.y * 0.6h + 0.75h);
                half3 ambS = SampleSH(nGeo) * lerp(0.55h, 1.0h, downOcc);
                // 피부: 부드러운 그림자 + 그늘 경계는 빨강이 가장 늦게 사라진다(산란) [제안]
                half sh = lerp(light.shadowAttenuation, SkinSoftShadow(i.positionWS, nGeo), 1.0h - isPart);
                // 클로즈업 머리 피부(UV1.y = 0.65, T29 표시)는 그림자 맵을 받지 않는다 — 경기장 해상도 그림자 맵으로는 코 그림자가 칼로 벤 직선·
                // 셀프 섀도 얼룩이 됐다(사용자 "칼로 벤 거 같아서", "그림자 있으면 좋은데 제대로 해야지"). 얼굴 자기 그림자는 클로즈업 전용 그림자로 따로(T30i)
                // T30i: 대신 클로즈업 전용 그림자 맵(코·눈두덩·헤어 그림자)
                half faceW = (1.0h - isPart) * smoothstep(0.45h, 0.6h, i.hd.y);   // 얼굴 0.65 · 목 0.4 (headlod.CLOSEUP_NECK_HD_Y)
                // T30o: 최고 등급 LOD0 피부도 같은 맵 (클로즈업 머리와 같은 귀·옆얼굴 그늘 — FR-58 등급 전환 낙차)
                UNITY_BRANCH if (_CloseupBody > 0.5h) faceW = max(faceW, (1.0h - isPart) * CloseupMapFade(i.positionWS));
                UNITY_BRANCH if (faceW > 0.0h) sh = lerp(sh, CloseupSoftShadow(i.positionWS, nGeo), faceW);
                half3 shRGB = pow(saturate(sh).xxx, half3(0.55h, 0.9h, 1.0h));
                half3 lcSkin = light.color * lerp(light.shadowAttenuation.xxx, shRGB, 1.0h - isPart);
                half3 cSkin = skin * (lcSkin * SkinScatterDiffuse(n, nGeo, l) + ambS) * occ
                            + light.color * sh * (0.028h * Spec(n, h, ndl, skinRough) + SssSheen * Spec(n, h, ndl, saturate(skinRough + 0.2h)));
                // 088 T30c: 환경광 반사 — 시안(Principled IOR 1.5 → F0 0.04, 코트 0.1)은 하늘빛이 피부 표면에 얹혀 어두운 피부가 덜 어둡고 덜 진하다.
                // 슐리크 프레넬 × 반사 방향 SH, 거칠수록 약하게 [제안]
                half ndv = saturate(dot(n, vdir));
                half fre = 0.04h + 0.5h * pow(1.0h - ndv, 5.0h);   // 가장자리 상승 0.96 → 0.5: 하늘색 테두리가 너무 셈(시안 가장자리는 뒤 림 태양의 흰빛 — 셰이더는 주 광원만)
                cSkin += SampleSH(reflect(-vdir, n)) * fre * lerp(1.0h, 0.5h, skinRough) * occ * downOcc * downOcc;
                // FR-32 얇은 부위 역광: 빛이 뒤에서 올 때(시선과 빛이 마주볼 때) 두께 맵만큼 붉게 비친다
                half back = pow(saturate(dot(vdir, -l)), 3.0h) * thk.r * _Translucency;
                cSkin += light.color * back * skin * half3(1.0h, 0.45h, 0.35h);

                // parts: 눈 / 머리카락
                half hairM = pt.b;
                half isTeeth = isPart * (1.0h - isCard) * (1.0h - (half)step(10.0, i.uv.x)) * (half)step(1.25, i.uv.y);
                half iris = pt.g * (1.0h - hairM) * (1.0h - isTeeth);
                half3 eye = lerp(half3(0.88h, 0.86h, 0.83h) * pt.r, _EyeColor.rgb * (0.3h + 3.5h * pt.r), iris);   // 088 T29: 홍채 밝기(MakeHuman 갈색 눈 0.035~0.19)를 0.4~1.0배로 펼쳐 무늬가 보이게 (이전 0.5 + R → 0.54~0.69배, 단색처럼 보임)
                // 088 T30k: 치아 칸 G = 잇몸 — 치아 상아색, 잇몸 붉은 점막 (MakeHuman 텍스처 밝기 × 색조) [제안]
                eye = lerp(eye, pt.r * lerp(TeethTint, GumTint, pt.g), isTeeth);
                half3 cEye = eye * (lc * ndl + amb) + lc * 0.06h * Spec(nG, h, ndl, pt.a) + pt.r * step(0.97h, pt.r) * 0.35h;
                // 088 T30j: 치아(parts v > 1.25)는 입 벌림만큼만 빛을 받는다 — 무표정 입술 틈으로 보이는 윗니 끝이 그늘 없이(클로즈업 얼굴은 그림자 맵을
                // 안 받음, T30h) 하늘빛·주광을 받아 입술 사이 흰 줄이 됐다. 환호(입 벌림 1)는 원래 밝기 [제안]
                cEye *= lerp(1.0h, lerp(TeethOcc, 1.0h, _MouthOpen), isTeeth);
                half3 hairBase = lerp(_HairColor.rgb, _HairColor2.rgb, smoothstep(0.3h, 0.7h, i.hd.y));   // 062 FR-54 두 색
                // 088 T29: 눈썹 메시(셸 UV 연속 90~91, browlash.BROW_UV_U0)는 마스크와 같은 머리색 × BrowDark (시안 눈썹은 머리보다 진하다)
                half isBrow = (half)(step(99.5, i.uv.x) * (1.0 - step(101.0, i.uv.x)));
                hairBase = lerp(hairBase, BrowColor(_BeardColor.rgb), isBrow);
                // 088 T30a (FR-58): LOD1·2 헤어는 셸뿐 — 셸 결 타일 평균 밝기(R ≈ 0.46)가 LOD0 카드의 보이는 텍셀(≈ 0.74)보다 낮아 헤어가 최대 21 % 어두웠다
                // (088-tier-match 동아시아 원거리 ΔE 5.6). LOD1·2 렌더러만(PlayerRig 속성 블록) 셸 결 밝기를 ShellLiftAmount 올린다. LOD0 속심 셸은 그대로 [제안]
                half hairR = pt.r + _ShellLift * ShellLiftAmount * (half)step(10.0, i.uv.x) * (1.0h - isBrow);
                half3 hairA = hairBase * lerp(lerp(0.6h, 1.3h, hairR), 1.0h, isBrow) * lerp(1.0h, 0.7h, _Wetness);   // FR-97 젖은 머리는 어둡다. 088 T29: 눈썹은 결 밝기 변동 없이 고르게
                half3 strand = SafeNormalize(b + nG * (pt.g - 0.5h) * 0.4h); // 가닥 방향 = UV v(바이탄젠트), 결 위상으로 흔든다
                half th = dot(strand, h);
                half curl = saturate((pt.a - 0.40h) / 0.12h);   // 062 FR-43: 직모 0 → 코일·브레이드·드레드 1 (groom.atlas 결 타일 거칠기)
                half kk = pow(saturate(sqrt(saturate(1.0h - th * th))), lerp(60.0h, 18.0h, curl)) * ndl * lerp(1.0h, 0.6h, curl);
                half3 cHair = hairA * (lc * (ndl * 0.8h + 0.2h) + amb) + lc * (0.10h + 0.5h * hairA) * kk * lerp(1.0h, 0.25h, isBrow);   // 088 T29: 눈썹 하이라이트 약하게(가는 가닥 반짝임)
                // 088 T30p: 추가 광원 — 피부(산란 확산 + 두 로브 정반사)·눈(확산)·헤어(감싼 확산 + 결 하이라이트). 그림자·클로즈업 맵 없음
                uint addN = PlayerAddLightCount();
                [loop] for (uint ak = 0u; ak < addN; ak++)
                {
                    Light al = PlayerAddLight(ak, i.positionWS);
                    half3 alc = al.color * (al.distanceAttenuation * al.shadowAttenuation);
                    half3 al2 = al.direction;
                    half3 ah = SafeNormalize(al2 + vdir);
                    half andl = saturate(dot(n, al2));
                    cSkin += skin * alc * SkinScatterDiffuse(n, nGeo, al2) * occ
                           + alc * (0.028h * Spec(n, ah, andl, skinRough) + SssSheen * Spec(n, ah, andl, saturate(skinRough + 0.2h)));
                    cEye += eye * alc * andl * lerp(1.0h, lerp(TeethOcc, 1.0h, _MouthOpen), isTeeth);
                    half ath = dot(strand, ah);
                    half akk = pow(saturate(sqrt(saturate(1.0h - ath * ath))), lerp(60.0h, 18.0h, curl)) * andl * lerp(1.0h, 0.6h, curl);
                    cHair += hairA * alc * (andl * 0.8h + 0.2h) + alc * (0.10h + 0.5h * hairA) * akk * lerp(1.0h, 0.25h, isBrow);
                }
                half3 cPart = lerp(cEye, cHair, hairM);

                half3 c = lerp(cSkin, cPart, isPart);
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }

        HLSLINCLUDE
        struct DA { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
        struct DV { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half density : TEXCOORD1; };
        struct DNV { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; half density : TEXCOORD2; };

        DV BodyShadowVert(DA i)
        {
            PlayerAttributes a;
            a.positionOS = i.positionOS; a.normalOS = i.normalOS; a.uv = i.uv;
            DV o; o.positionCS = PlayerShadowVert(a); o.uv = i.uv; o.density = (half)i.uv1.x; return o;
        }

        DV BodyDepthVert(DA i)
        {
            DV o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; o.density = (half)i.uv1.x; return o;
        }

        DNV BodyDepthNormalsVert(DA i)
        {
            DNV o;
            o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
            o.normalWS = (half3)TransformObjectToWorldNormal(i.normalOS);
            o.uv = i.uv;
            o.density = (half)i.uv1.x;
            return o;
        }

        half BodyDepthFrag(DV i) : SV_Target
        {
        #if defined(LOD_FADE_CROSSFADE)
            LODFadeCrossFade(i.positionCS);
        #endif
            ClipCard(i.uv, i.density, i.positionCS);
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

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex BodyDepthNormalsVert
            #pragma fragment BodyNormalsFrag
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            half4 BodyNormalsFrag(DNV i) : SV_Target
            {
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                ClipCard(i.uv, i.density, i.positionCS);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
