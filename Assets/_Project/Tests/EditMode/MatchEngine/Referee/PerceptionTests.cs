using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Referee;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-13 (FR-30~32, P2·P8): 일관성·시야 → 오심률, 홈 편향 → 휘슬 비율 (이항 검정 p &lt; 0.01).</summary>
    public class PerceptionTests
    {
        const int N = 10000;

        static int Errors(RefereeProfile r, float sight, float score, int seed)
        {
            var rng = new SeedRng((ulong)seed);
            var th = FoulJudge.ForReferee(P, r, 0);
            var truth = FoulJudge.Decide(score, FoulJudge.Base(P));
            var err = 0;
            for (int i = 0; i < N; i++)
                if (FoulJudge.Decide(Perception.Perceive(P, r, score, sight, false, 0f, rng), th) != truth) err++;
            return err;
        }

        [Test]
        public void Consistency_Sight_ErrorRate_10000()
        {
            var bad = RefereeProfile.LeagueAverage.With(consistency: 0f);
            var good = RefereeProfile.LeagueAverage.With(consistency: 1f);
            // 같은 접촉: 점수 0.47(파울, 임계 0.40~0.55 사이) — 같은 RNG 시드
            var eBad = Errors(bad, 0.2f, 0.47f, 77);
            var eGood = Errors(good, 1.0f, 0.47f, 77);
            TestContext.WriteLine($"오심률 일관성0·시야0.2 {eBad / (double)N:P1} · 일관성1·시야1 {eGood / (double)N:P1}");
            Assert.Greater(Z(eBad, N, eGood, N), 2.58, "p < 0.01");
        }

        [Test]
        public void HomeBias_WhistleRate_10000()
        {
            var biased = RefereeProfile.LeagueAverage.With(homeBias: 1f); // 1 → 0.05
            var fair = RefereeProfile.LeagueAverage.With(homeBias: 0f);
            int Whistles(RefereeProfile r)
            {
                var rng = new SeedRng(91);
                var th = FoulJudge.ForReferee(P, r, 0);
                var n = 0;
                for (int i = 0; i < N; i++)
                    if (FoulJudge.Decide(Perception.Perceive(P, r, P.FoulThreshold, 0.6f, true, 0f, rng), th) != EventCodes.DecisionNone) n++;
                return n;
            }
            var wb = Whistles(biased);
            var wf = Whistles(fair);
            TestContext.WriteLine($"홈 피해 휘슬 편향 0.05 {wb / (double)N:P1} · 편향 0 {wf / (double)N:P1}");
            Assert.AreEqual(0.05f, biased.HomeBias * P.HomeBiasMax, 1e-6f);
            Assert.Greater(Z(wb, N, wf, N), 2.58, "p < 0.01");
        }

        [Test]
        public void SightQuality_Monotone_Distance_Occlusion()
        {
            var w = World();
            for (int s = 0; s < 22; s++) w.Active[s] = false;
            w.Officials[0] = new Vec2(30f, 34f);
            w.Officials[1] = new Vec2(100f, -1.5f);
            w.Officials[2] = new Vec2(0f, 69.5f);
            var best = Perception.SightQuality(P, w, new Vec2(50f, 34f), -1, -1, Vec2.Zero);   // 20 m
            var far = Perception.SightQuality(P, w, new Vec2(75f, 34f), -1, -1, Vec2.Zero);    // 45 m
            Assert.Greater(best, far, "최적 거리 > 먼 거리");
            w.Active[3] = true; w.Pos[3] = new Vec2(40f, 34.3f); // 주심–사건 선 위
            var occluded = Perception.SightQuality(P, w, new Vec2(50f, 34f), -1, -1, Vec2.Zero);
            Assert.Less(occluded, best, "가림 1명");
            var perp = Perception.SightQuality(P, w, new Vec2(50f, 34f), -1, -1, new Vec2(0f, 5f));
            var along = Perception.SightQuality(P, w, new Vec2(50f, 34f), -1, -1, new Vec2(5f, 0f));
            Assert.Greater(perp, along, "진행 방향이 시선과 직각이면 잘 보임");
        }

        [Test]
        public void DiveFool_LowerWithBetterSightAndConsistency()
        {
            var r = RefereeProfile.LeagueAverage;
            Assert.Greater(Perception.DiveFoolProb(P, r, 0.2f, false), Perception.DiveFoolProb(P, r, 0.9f, false));
            Assert.Greater(Perception.DiveFoolProb(P, r.With(consistency: 0f), 0.5f, false), Perception.DiveFoolProb(P, r.With(consistency: 1f), 0.5f, false));
            Assert.Greater(Perception.DiveFoolProb(P, r, 0.5f, true), Perception.DiveFoolProb(P, r, 0.5f, false), "약한 접촉 가산");
        }
    }
}
