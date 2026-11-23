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
// 088 FR-53: 피부 정점의 UV1.y = 1(LOD0·클로즈업)이면 눈썹·속눈썹 마스크를 끈다(원본 메시가 그린다).
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
        _NormalStrength ("노멀 세기", Range(0, 2)) = 0.6
        _MuscleN ("근육·손 구운 노멀", 2D) = "bump" {}
        _MuscleAO ("근육·손 구운 차폐", 2D) = "white" {}
        _ThicknessTex ("피부 두께(R)·땀(G)", 2D) = "black" {}
        _MuscleDef ("근육 노멀 세기", Range(0, 2)) = 0.5
        _Translucency ("얇은 부위 역광 비침", Range(0, 1)) = 0.35
        _Sweat ("땀 0/1", Range(0, 1)) = 0
        _Wetness ("젖음 0~1 (078 입력)", Range(0, 1)) = 0
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
            half4 _BrowChannel;
            half _NormalStrength;
            half _MuscleDef, _Translucency, _Sweat, _Wetness;
        CBUFFER_END

        // 088 T23: 헤어 셸 연속 UV — u ≥ 10이면 u = 10 + 타일 × 100 + 연속 u, v = 연속 v. parts 머리카락 칸 3열 × 2행 타일 안에서 frac으로 반복
        // (Blender groom/bake.shell_rect와 같은 배치: 칸 (0.25, 0) ~ (1, 0.75), 여백 6/1024). 기울기는 frac 전 좌표로 → 이음매 밉 줄 없음
        static const float ShellPad = 6.0 / 1024.0;
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
            float2 puv = float2(uv.x - 1.0 - card, uv.y);
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
                half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_SkinNormal, sampler_SkinNormal, frac(i.uv)));
                half3 mn = UnpackNormal(SAMPLE_TEXTURE2D(_MuscleN, sampler_MuscleN, frac(i.uv)));
                half mao = SAMPLE_TEXTURE2D(_MuscleAO, sampler_MuscleAO, frac(i.uv)).r;
                half2 thk = SAMPLE_TEXTURE2D(_ThicknessTex, sampler_ThicknessTex, frac(i.uv)).rg;
                half4 pt = SampleParts(i.uv);
                pt.g = lerp(pt.g, 0.5h, isCard); // 카드의 G는 알파 — 결 위상은 가운데

                half3 nG = normalize(i.normalWS);
                half3 t = normalize(i.tangentWS.xyz);
                half3 b = cross(nG, t) * i.tangentWS.w;
                tn.xy = (tn.xy * _NormalStrength + mn.xy * _MuscleDef) * (1.0h - isPart);   // 모공·잔주름 + 근육·손 형태 (피부만)
                half3 n = normalize(tn.x * t + tn.y * b + tn.z * nG);

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
                half3 skin = s.rgb * _SkinColor.rgb;                       // 088: MakeHuman diffuse × 톤 틴트
                half sLum = dot(s.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                // 088 FR-02: 눈썹·속눈썹 = MakeHuman 프록시를 구운 덮임 마스크 × 머리색 (속눈썹은 더 어둡게)
                half4 brow = SAMPLE_TEXTURE2D(_BrowMask, sampler_BrowMask, frac(i.uv));
                // 088 FR-53 (T28): LOD0·클로즈업 피부 정점은 UV1.y = 1(눈썹·속눈썹 메시가 그린다) → 마스크 끔. LOD1·2 몸은 (1, 0)
                half browOn = (1.0h - isPart) * (1.0h - saturate(i.hd.y));
                half bcov = saturate(dot(brow.rgb, _BrowChannel.rgb) * 1.6h) * browOn;
                skin = lerp(skin, _HairColor.rgb * 0.45h, bcov * 0.9h);   // 눈썹은 머리색보다 어둡게
                skin = lerp(skin, half3(0.03h, 0.025h, 0.02h), brow.a * 0.8h * browOn);
                // 057 FR-24: 수염 = 밀도(s.b)의 부드러운 문턱(경계 흐림) × 종류별 덮임. 짧을수록 피부가 비치는 옅은 색조(잔털), 길수록 짙다.
                // 062 FR-66: 밀도 = 수염 영역 마스크(beard_mask0)에서 종류별 채널 하나 — 없으면 057 피부 B(full)
                half lvl = _BeardLevel;
                half lo = 0.75h - lvl * 0.6h;
                half bden = dot(SAMPLE_TEXTURE2D(_BeardMask, sampler_BeardMask, frac(i.uv)).rgba, _BeardChannel);
                // 062 T35 육안: 경계 폭 0.45 → 0.65(흐린 가장자리), 털 결(피부 밝기 디테일 R)로 덮임을 흔들어 평면 색 조각처럼 보이지 않게,
                // 회색·밝은 수염은 피부 그늘을 조금 섞어 분칠처럼 뜨지 않게
                half beard = smoothstep(lo, lo + 0.65h, bden) * lvl * step(0.01h, lvl);
                beard *= saturate(0.88h + (sLum - 0.45h) * 1.5h);   // 088: 털 결 = diffuse 밝기 변동
                half3 beardCol = _HairColor.rgb * lerp(0.75h, 0.55h, lvl) * lerp(0.9h, 1.1h, bden);
                half hairLum = dot(_HairColor.rgb, half3(0.3h, 0.59h, 0.11h));
                beardCol = lerp(beardCol, skin * 0.55h, 0.35h * saturate((hairLum - 0.25h) * 2.0h));   // 밝은(회색·금발) 수염만
                skin = lerp(skin, beardCol, beard * lerp(0.55h, 0.92h, lvl));
                half skinRough = saturate(s.a + beard * 0.3h);
                skinRough = saturate(skinRough - 0.04h * thk.g * _Sweat);                 // FR-33 땀 (heightfield.SWEAT_DROP)
                skinRough = lerp(skinRough, skinRough * 0.6h, _Wetness);                 // FR-97 젖음
                half occ = lerp(1.0h, mao, 1.0h - isPart);
                half3 cSkin = skin * (lc * SkinDiffuse(ndlRaw) + amb) * occ + lc * 0.028h * Spec(n, h, ndl, skinRough);
                // FR-32 얇은 부위 역광: 빛이 뒤에서 올 때(시선과 빛이 마주볼 때) 두께 맵만큼 붉게 비친다
                half back = pow(saturate(dot(vdir, -l)), 3.0h) * thk.r * _Translucency;
                cSkin += light.color * back * skin * half3(1.0h, 0.45h, 0.35h);

                // parts: 눈 / 머리카락
                half hairM = pt.b;
                half iris = pt.g * (1.0h - hairM);
                half3 eye = lerp(half3(0.88h, 0.86h, 0.83h) * pt.r, _EyeColor.rgb * (0.5h + pt.r), iris);
                half3 cEye = eye * (lc * ndl + amb) + lc * 0.06h * Spec(nG, h, ndl, pt.a) + pt.r * step(0.97h, pt.r) * 0.35h;
                half3 hairBase = lerp(_HairColor.rgb, _HairColor2.rgb, smoothstep(0.3h, 0.7h, i.hd.y));   // 062 FR-54 두 색
                half3 hairA = hairBase * lerp(0.6h, 1.3h, pt.r) * lerp(1.0h, 0.7h, _Wetness);   // FR-97 젖은 머리는 어둡다
                half3 strand = normalize(b + nG * (pt.g - 0.5h) * 0.4h); // 가닥 방향 = UV v(바이탄젠트), 결 위상으로 흔든다
                half th = dot(strand, h);
                half curl = saturate((pt.a - 0.40h) / 0.12h);   // 062 FR-43: 직모 0 → 코일·브레이드·드레드 1 (groom.atlas 결 타일 거칠기)
                half kk = pow(saturate(sqrt(saturate(1.0h - th * th))), lerp(60.0h, 18.0h, curl)) * ndl * lerp(1.0h, 0.6h, curl);
                half3 cHair = hairA * (lc * (ndl * 0.8h + 0.2h) + amb) + lc * (0.10h + 0.5h * hairA) * kk;
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
