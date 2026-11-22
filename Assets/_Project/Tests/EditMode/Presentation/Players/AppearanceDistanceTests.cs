using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// 팀 안 겉보기 중복 (specs/062 FR-65, AC-07). T29가 구현할 API:
    /// <c>AppearanceDistance.Distance(lm, a, fa, b, fb)</c> = 얼굴 랜드마크(바탕 머리 혼합 포함) + 헤어 실루엣 계열 + 피부톤 + 머리색 + 수염 가중합(0~1, 가중치는 T29 [제안]),
    /// <c>AppearanceDistance.Threshold</c>, <c>AppearanceDistance.GenerateTeam(data, lm, inputs, looks, faces)</c> = 외형·얼굴 묶음 생성 + 임계값 미만 쌍 재추첨.
    /// </summary>
    public class AppearanceDistanceTests
    {
        const int Teams = 1000, Size = 22;

        static void Team(int team, BodyInput[] inputs, PlayerAppearance[] looks, FaceShape[] faces, FaceLandmarks lm)
        {
            for (int i = 0; i < Size; i++) inputs[i] = new BodyInput(300_000 + team * 41 + i * 1_009);
            AppearanceDistance.GenerateTeam(FaceGeneratorTests.Data, lm, inputs, looks, faces);
        }

        [Test]
        public void ThousandTeams_NoPairBelowThreshold()
        {
            // FR-65: 무작위 1,000팀 × 22명에서 겉보기 거리 임계값 미만 쌍 0
            var lm = new FaceLandmarks(FaceGeneratorTests.Data);
            Assert.Greater(AppearanceDistance.Threshold, 0f);
            var inputs = new BodyInput[Size];
            var looks = new PlayerAppearance[Size];
            var faces = new FaceShape[Size];
            int below = 0;
            float min = float.MaxValue;
            for (int t = 0; t < Teams; t++)
            {
                Team(t, inputs, looks, faces, lm);
                for (int i = 0; i < Size; i++)
                    for (int j = i + 1; j < Size; j++)
                    {
                        float d = AppearanceDistance.Distance(lm, looks[i], faces[i], looks[j], faces[j]);
                        min = System.Math.Min(min, d);
                        if (d < AppearanceDistance.Threshold) below++;
                    }
            }
            Assert.AreEqual(0, below, $"임계값 {AppearanceDistance.Threshold} 미만 쌍 (최소 {min})");
        }

        [Test]
        public void NoIdenticalCombo()
        {
            // 043 FR-03 유지: 같은 팀에 완전히 같은 외형 조합(피부·헤어·색·컬러 포인트·수염·주 바탕 머리) 0
            var lm = new FaceLandmarks(FaceGeneratorTests.Data);
            var inputs = new BodyInput[Size];
            var looks = new PlayerAppearance[Size];
            var faces = new FaceShape[Size];
            var seen = new HashSet<long>();
            for (int t = 0; t < 200; t++)
            {
                Team(t, inputs, looks, faces, lm);
                seen.Clear();
                foreach (var a in looks) Assert.IsTrue(seen.Add(a.ComboKey), "팀 " + t + " 같은 조합");
            }
        }

        [Test]
        public void HairCapFour_LeFivePercent()
        {
            // 057 FR-32 유지: 같은 헤어 4명 이상인 팀 ≤ 5 % (재추첨이 헤어 상한을 깨지 않는다)
            var lm = new FaceLandmarks(FaceGeneratorTests.Data);
            var inputs = new BodyInput[Size];
            var looks = new PlayerAppearance[Size];
            var faces = new FaceShape[Size];
            var cnt = new int[AppearanceGenerator.HairStyles];
            int bad = 0;
            for (int t = 0; t < Teams; t++)
            {
                Team(t, inputs, looks, faces, lm);
                System.Array.Clear(cnt, 0, cnt.Length);
                int max = 0;
                foreach (var a in looks) max = System.Math.Max(max, ++cnt[a.HairStyle]);
                if (max >= 4) bad++;
            }
            Assert.LessOrEqual(bad, Teams * 5 / 100);
        }
    }
}
