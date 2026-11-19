using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Referee;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/074 손 보유 시간 FR-14 (AC-03).</summary>
    public class KeeperHoldTests
    {
        static GoalkeeperParams P => GoalkeeperParams.Defaults;
        static readonly Vec2 Gk = new Vec2(6f, 34f);
        const float Dt = 0.1f; // 결정 주기

        [Test]
        public void Hold_Recorded_InDistributionEvent()
        {
            int hands = 0;
            foreach (var sim in DistributionLiveTests.Played())
                for (int i = 0; i < sim.Log.Count; i++)
                {
                    var e = sim.Log[i];
                    if (e.Kind != SimEventKind.Distribution || e.B < 0) continue;
                    hands++;
                    Assert.Greater(e.B, 0);
                    Assert.AreEqual(e.B * 0.01f, e.F0, 1e-5f, "F0 = 보유 서브스텝 / 100");
                    Assert.IsTrue(DistributionModel.IsHand((DistributionKind)e.A) || e.A == (int)DistributionKind.Punt || e.A == (int)DistributionKind.DropKick || e.A == (int)DistributionKind.ShortKick);
                }
            Assert.Greater(hands, 0);
        }

        /// <summary>AC-03 경계: 8.00 s = 800 서브스텝은 위반 아님, 8.01 s = 801은 위반, 7.99 s = 799 기록이 정확.</summary>
        [Test]
        public void Boundary_800_799_Substeps_Exact()
        {
            var limit = RestartRules.KeeperHoldLimit(8f, 0.01f);
            Assert.AreEqual(800, limit);
            Assert.IsFalse(RestartRules.KeeperHoldViolation(800L, limit));
            Assert.IsFalse(RestartRules.KeeperHoldViolation(799L, limit));
            Assert.IsTrue(RestartRules.KeeperHoldViolation(801L, limit));
            Assert.AreEqual(7.99f, 799 * 0.01f, 1e-6f);
            Assert.AreEqual(8.0f, 800 * 0.01f, 1e-6f);
        }

        /// <summary>
        /// AC-03: 리그 평균 GK 1만 회 캐치(무작위 배치 픽스처, 결정 틱 루프만 — 물리 없음, 플랜 Q4) 중 8 s 초과 비율 ≤ 0.5 %.
        /// 같은 규칙(<see cref="DistributionModel.ReleaseNow"/>·<see cref="DistributionModel.DeadlineSeconds"/>)을 쓴다.
        /// </summary>
        [Test]
        public void Hold_10000Catches_Over8s_LE_0_5pct()
        {
            var r = HoldStats(KeeperProfile.From(P, KeeperAttributes.Uniform(10.5f)).DecisionsN, 10000, 41UL);
            TestContext.WriteLine($"평균 보유 {r.mean:F2} s · 최대 {r.max:F2} s · 8 s 초과 {r.over}/10000");
            Assert.LessOrEqual(r.over / 10000.0, 0.005);
        }

        static (double mean, float max, int over) HoldStats(float decisionsN, int n, ulong seed)
        {
            var layout = new SeedRng(seed);
            var rng = new SeedRng(seed + 1);
            var m = new DistributionModel(P);
            var ap = ActionTestKit.P;
            double sum = 0;
            float max = 0f;
            int over = 0;
            for (int i = 0; i < n; i++)
            {
                var w = ActionTestKit.World();
                w.PrepareRoll(ap, ActionTestKit.Ball);
                ActionTestKit.Put(w, 0, Gk);
                for (int s = 1; s < 22; s++) ActionTestKit.Put(w, s, new Vec2(layout.Range(8f, 100f), layout.Range(3f, 65f)));
                var quick = layout.NextFloat() < 0.2f;
                var c = new DistributionContext { Source = DistributionSource.Hands, Quick = quick, Directness = 0.5f, DecisionsN = decisionsN };
                var count = m.Evaluate(w, ap, ActionTestKit.Ball, 0, Gk, 0.2f, in c);
                var best = count > 0 ? m.Candidate(m.Best()).Eu : 0f;
                var deadline = DistributionModel.DeadlineSeconds(P, decisionsN, rng);
                var t = 0f;
                while (!DistributionModel.ReleaseNow(P, t, deadline, quick, count > 0, best)) t += Dt;
                sum += t;
                if (t > max) max = t;
                if (t > 8f) over++;
            }
            return (sum / n, max, over);
        }

        /// <summary>앞이 막혀(모든 대상 EU 낮음) 기다려도 시계는 흐르고 마감에 배급 (판정은 012).</summary>
        [Test]
        public void Hold_Blocked_ClockRuns_ReleasesAtDeadline()
        {
            var deadline = 6.4f;
            var t = 0f;
            while (!DistributionModel.ReleaseNow(P, t, deadline, false, true, 0.1f)) t += Dt;
            Assert.AreEqual(6.4f, t, 0.1001f);
            Assert.IsFalse(DistributionModel.ReleaseNow(P, 0.5f, deadline, false, true, 5f), "최소 대기 전");
            Assert.IsTrue(DistributionModel.ReleaseNow(P, 0f, deadline, true, true, 5f), "빠른 스로는 대기 없음");
        }

        /// <summary>FR-14 "낮은 Decisions는 늦어질 수 있음": 마감 분포 (각 1만 표본) — 앞이 다 막히면 마감에 배급하므로 이것이 보유 시간 상한.</summary>
        [Test]
        public void LowDecisions_HoldsLonger()
        {
            var rng = new SeedRng(51UL);
            double lo = 0, hi = 0, avg = 0;
            int loOver = 0, avgOver = 0;
            var avgN = KeeperProfile.From(P, KeeperAttributes.Uniform(10.5f)).DecisionsN;
            for (int i = 0; i < 10000; i++)
            {
                var a = DistributionModel.DeadlineSeconds(P, 0f, rng);
                var b = DistributionModel.DeadlineSeconds(P, 1f, rng);
                var c = DistributionModel.DeadlineSeconds(P, avgN, rng);
                lo += a; hi += b; avg += c;
                if (a > 8f) loOver++;
                if (c > 8f) avgOver++;
            }
            TestContext.WriteLine($"마감 평균: Decisions 1 {lo / 10000:F2} s (8 s 초과 {loOver}) · 리그 평균 {avg / 10000:F2} s (초과 {avgOver}) · 20 {hi / 10000:F2} s");
            Assert.Greater(lo, hi);
            Assert.AreEqual(P.HoldTarget * 10000, hi, 1e-2, "Decisions 20은 목표 그대로");
            Assert.LessOrEqual(avgOver / 10000.0, 0.005, "AC-03 최악(앞이 다 막힘)에서도 리그 평균 GK 8 s 초과 ≤ 0.5 %");
        }

        /// <summary>손 보유 시계는 GK가 자기 박스 안에서 공을 가진 동안만 (놓치거나 박스 밖이면 끝) — 실경기 틱마다 불변식.</summary>
        [Test]
        public void Hold_LostBall_EndsClock()
        {
            var sim = new MatchSim(ShapeTestKit.Setup(seed: 7410UL), ShapeTestKit.TuningWithShape());
            var ctx = sim.Context;
            int active = 0;
            for (long t = 0; t < 20_000; t++)
            {
                sim.RunUntilTick(t + 1);
                for (int team = 0; team < 2; team++)
                {
                    if (!ctx.Keepers.Hold(team).Active) continue;
                    active++;
                    var gk = team * Squad.SlotsPerTeam;
                    Assert.IsTrue(ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId == gk, $"틱 {t}: 시계가 도는데 GK 공이 아님");
                }
            }
            Assert.Greater(active, 0);
        }
    }
}
