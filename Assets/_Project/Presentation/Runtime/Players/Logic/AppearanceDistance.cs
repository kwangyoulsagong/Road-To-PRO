using System;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 팀 안 겉보기 거리 (specs/062 FR-65). 0~1 = 가중합:
    /// 얼굴 랜드마크(바탕 머리 혼합 포함, 056 거리 벡터 RMS ÷ (2 × 무작위 평균 쌍 거리), 1에서 자름) 0.35
    /// + 헤어 실루엣 계열(같은 스타일 0 · 같은 FR-51 계열 0.5 · 다른 계열 1) 0.25
    /// + 피부톤(단계 차 ÷ 7) 0.15 + 머리색(같음 0 · 다름 1) 0.15 + 수염(같음 0 · 다름 1) 0.10. 가중치·임계값 [제안] (T29).
    /// GenerateTeam: 외형 묶음(043 FR-03·057 FR-32 유지) 뒤 얼굴을 시도 번호로 다시 뽑아 앞 선수와 임계값 이상이 되게 한다.
    /// 외형 조합은 건드리지 않아 헤어 상한·조합 중복 규칙이 그대로다. 입력에 사진·이름 없음(FR-61).
    /// </summary>
    public static class AppearanceDistance
    {
        public const float WFace = 0.35f, WHair = 0.25f, WSkin = 0.15f, WHairColor = 0.15f, WBeard = 0.10f;
        /// <summary>FR-65 [제안] 임계값.</summary>
        public const float Threshold = 0.10f;

        public static float Distance(FaceLandmarks lm, in PlayerAppearance a, FaceShape fa, in PlayerAppearance b, FaceShape fb) =>
            Distance(lm, a, lm.DistanceVector(fa, a), b, lm.DistanceVector(fb, b));

        static float Distance(FaceLandmarks lm, in PlayerAppearance a, float[] va, in PlayerAppearance b, float[] vb)
        {
            float face = Math.Min(1f, FaceLandmarks.Distance(va, vb) / (2f * lm.MeanPairDistance));
            return WFace * face + Discrete(a, b);
        }

        /// <summary>얼굴을 뺀 나머지 항 (헤어 계열·피부톤·머리색·수염).</summary>
        static float Discrete(in PlayerAppearance a, in PlayerAppearance b)
        {
            float hair = a.HairStyle == b.HairStyle ? 0f
                : HairTaxonomy.GroupOf(a.HairStyle) == HairTaxonomy.GroupOf(b.HairStyle) ? 0.5f : 1f;
            float skin = Math.Abs(a.SkinTone - b.SkinTone) / (float)(AppearanceGenerator.SkinTones - 1);
            float hc = a.HairColor == b.HairColor ? 0f : 1f;
            float bd = a.Beard == b.Beard ? 0f : 1f;
            return WHair * hair + WSkin * skin + WHairColor * hc + WBeard * bd;
        }

        /// <summary>
        /// 한 팀(입력 순서대로)의 외형·얼굴. 얼굴은 FaceGenerator 시도 번호 0..MaxTries−1 중 앞 선수 모두와 임계값 이상인 첫 시도,
        /// 없으면 최소 거리가 가장 큰 시도(결정론 — 같은 입력 → 같은 결과).
        /// </summary>
        public static void GenerateTeam(FaceData data, FaceLandmarks lm, BodyInput[] inputs, PlayerAppearance[] looks, FaceShape[] faces)
        {
            if (looks.Length < inputs.Length || faces.Length < inputs.Length) throw new ArgumentException("출력 배열이 작다");
            AppearanceGenerator.GenerateGroup(inputs, looks);
            var vecs = new float[inputs.Length][];
            for (int i = 0; i < inputs.Length; i++)
            {
                FaceShape best = null;
                float[] bestV = null;
                float bestMin = -1f;
                for (int t = 0; t < FaceGenerator.MaxTries; t++)
                {
                    var f = FaceGenerator.Generate(data, inputs[i].Id, t);
                    var v = lm.DistanceVector(f, looks[i]);
                    float min = float.MaxValue;
                    for (int j = 0; j < i && min >= Threshold; j++) min = Math.Min(min, Distance(lm, looks[i], v, looks[j], vecs[j]));
                    if (min > bestMin)
                    {
                        bestMin = min;
                        best = f;
                        bestV = v;
                    }
                    if (min >= Threshold) break;
                }
                faces[i] = best;
                vecs[i] = bestV;
            }
        }
    }
}
