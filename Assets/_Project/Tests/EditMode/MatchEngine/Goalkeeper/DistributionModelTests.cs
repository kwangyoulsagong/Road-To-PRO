using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/074 배급 FR-10~13 (AC-02·08). 팀 0 GK = 슬롯 0, +x 공격, 상대 = 슬롯 11~21.</summary>
    public class DistributionModelTests
    {
        static GoalkeeperParams P => GoalkeeperParams.Defaults;
        static ActionParams Ap => ActionTestKit.P;
        static readonly Vec2 Gk = new Vec2(6f, 34f);

        static ActionWorld World()
        {
            var w = ActionTestKit.World();
            w.PrepareRoll(Ap, ActionTestKit.Ball);
            ActionTestKit.Put(w, 0, Gk);
            return w;
        }

        static DistributionContext Ctx(DistributionSource s = DistributionSource.Hands, bool quick = false, bool press = false) =>
            new DistributionContext { Source = s, Quick = quick, HighPress = press, Directness = 0.5f, Role = KeeperRole.Standard, DecisionsN = 0.5f };

        /// <summary>AC-02 ①: 캐치 직후 상대 미정렬 + 비어 있는 풀백 → 오버암 던지기가 EU 1위.</summary>
        [Test]
        public void Catch_Unset_FreeFullback_ThrowTop()
        {
            var w = World();
            ActionTestKit.Put(w, 1, new Vec2(36f, 6f));   // 왼쪽 풀백 — 빔
            ActionTestKit.Put(w, 2, new Vec2(17f, 26f));  // 센터백 — 바짝 마크
            ActionTestKit.Put(w, 3, new Vec2(17f, 44f));  // 센터백 — 바짝 마크
            ActionTestKit.Put(w, 9, new Vec2(62f, 34f));  // 공격수 — 마크
            ActionTestKit.Put(w, 12, new Vec2(18f, 27f));
            ActionTestKit.Put(w, 13, new Vec2(18f, 43f));
            ActionTestKit.Put(w, 14, new Vec2(63f, 34.5f));
            ActionTestKit.Put(w, 15, new Vec2(30f, 34f));
            var m = new DistributionModel(P);
            var n = m.Evaluate(w, Ap, ActionTestKit.Ball, 0, Gk, 0.1f, Ctx(quick: true));
            Assert.Greater(n, 0);
            var b = m.Candidate(m.Best());
            for (int i = 0; i < n; i++) TestContext.WriteLine($"{m.Candidate(i).Target} {m.Candidate(i).Kind} p {m.Candidate(i).P:F2} EU {m.Candidate(i).Eu:F2}");
            Assert.AreEqual(DistributionKind.Throw, b.Kind);
            Assert.AreEqual(1, b.Target);
        }

        /// <summary>AC-02 ②: 압박 높음 + 짧은 대상 성공 확률 낮음 → 롱 배급이 선택된다 (1,000회 최빈).</summary>
        [Test]
        public void HighPress_ShortLowP_LongChosen()
        {
            var w = World();
            ActionTestKit.Put(w, 2, new Vec2(16f, 24f));
            ActionTestKit.Put(w, 3, new Vec2(16f, 44f));
            ActionTestKit.Put(w, 9, new Vec2(66f, 34f));   // 공간 있는 공격수
            ActionTestKit.Put(w, 11 + 9, new Vec2(8f, 33f)); // GK 압박
            ActionTestKit.Put(w, 12, new Vec2(16.8f, 24.5f));
            ActionTestKit.Put(w, 13, new Vec2(16.8f, 43.5f));
            ActionTestKit.Put(w, 14, new Vec2(12f, 30f));
            ActionTestKit.Put(w, 15, new Vec2(12f, 38f));
            ActionTestKit.Put(w, 16, new Vec2(85f, 30f));
            var m = new DistributionModel(P);
            var c = Ctx(press: true);
            var n = m.Evaluate(w, Ap, ActionTestKit.Ball, 0, Gk, 0.8f, in c);
            Assert.Greater(n, 0);
            for (int i = 0; i < n; i++) TestContext.WriteLine($"{m.Candidate(i).Target} {m.Candidate(i).Kind} p {m.Candidate(i).P:F2} EU {m.Candidate(i).Eu:F2}");
            var rng = new SeedRng(5UL);
            int longs = 0;
            for (int i = 0; i < 1000; i++)
                if (DistributionModel.IsLong(m.Candidate(m.Choose(in c, rng)).Kind)) longs++;
            TestContext.WriteLine($"롱 {longs}/1000");
            Assert.Greater(longs, 500);
        }

        /// <summary>AC-02 ③: 모든 배급 조준 거리가 방식별 사거리 안 (무작위 배치 10종 × 선택 1,000회 + 공 상태 3종).</summary>
        [Test]
        public void AllDistributions_WithinModeRange()
        {
            var layout = new SeedRng(77UL);
            var rng = new SeedRng(78UL);
            var m = new DistributionModel(P);
            var checkedN = 0;
            for (int f = 0; f < 10; f++)
            {
                var w = World();
                for (int s = 1; s < 22; s++)
                    ActionTestKit.Put(w, s, new Vec2(layout.Range(8f, 100f), layout.Range(3f, 65f)));
                foreach (var src in new[] { DistributionSource.Hands, DistributionSource.GoalKick, DistributionSource.Feet })
                {
                    var c = Ctx(src, quick: f % 2 == 0, press: f % 3 == 0);
                    var n = m.Evaluate(w, Ap, ActionTestKit.Ball, 0, Gk, 0.3f, in c);
                    for (int i = 0; i < 1000 / 3; i++)
                    {
                        var cand = n == 0 ? m.Forced(w, ActionTestKit.Ball, 0, Gk, src) : m.Candidate(m.Choose(in c, rng));
                        Assert.IsTrue(DistributionModel.Allowed(cand.Kind, src), $"{src}에 {cand.Kind}");
                        var d = (cand.Aim - Gk).Length();
                        m.Range(cand.Kind, out var lo, out var hi);
                        var tol = n == 0 ? 1f : 1e-3f; // 강제 롱은 피치 안으로 자를 때 조금 짧아질 수 있다
                        Assert.That(d, Is.InRange(lo - tol, hi + tol), $"{src} {cand.Kind} 거리 {d:F1}");
                        checkedN++;
                    }
                }
            }
            Assert.Greater(checkedN, 9000);
        }

        [Test]
        public void NoCandidates_ForcedLong()
        {
            var w = World();
            ActionTestKit.Put(w, 2, new Vec2(9f, 34f)); // 5 m 안 — 모든 사거리 밖
            var m = new DistributionModel(P);
            Assert.AreEqual(0, m.Evaluate(w, Ap, ActionTestKit.Ball, 0, Gk, 0f, Ctx()));
            var f = m.Forced(w, ActionTestKit.Ball, 0, Gk, DistributionSource.Hands);
            Assert.AreEqual(DistributionKind.Punt, f.Kind);
            Assert.That((f.Aim - Gk).Length(), Is.InRange(P.DistLongMin - 1e-3f, P.DistLongMax + 1e-3f));
            Assert.AreEqual(DistributionKind.LongKick, m.Forced(w, ActionTestKit.Ball, 0, Gk, DistributionSource.GoalKick).Kind);
        }

        [Test]
        public void GoalKick_FootModesOnly_BackPass_NoHandModes()
        {
            foreach (var src in new[] { DistributionSource.GoalKick, DistributionSource.Feet })
                for (int ki = 0; ki < DistributionModel.Kinds; ki++)
                {
                    var k = (DistributionKind)ki;
                    Assert.AreEqual(k == DistributionKind.ShortKick || k == DistributionKind.LongKick, DistributionModel.Allowed(k, src), $"{src} {k}");
                }
            Assert.IsFalse(DistributionModel.Allowed(DistributionKind.LongKick, DistributionSource.Hands), "손에 든 공은 펀트·드롭킥");
            Assert.IsTrue(DistributionModel.Allowed(DistributionKind.Throw, DistributionSource.Hands));
        }

        /// <summary>AC-08 ①: Distribution 1 vs 20 롱 배급 각도 오차 표준편차 6.0° / 1.5° ±3 % (각 10만 표본, 실제 조준점 각도로).</summary>
        [Test]
        public void LongAngleSigma_Dist1_6deg_Dist20_1_5deg()
        {
            var m = new DistributionModel(P);
            Assert.AreEqual(6.0, Std(m, 0f, 1UL), 6.0 * 0.03);
            Assert.AreEqual(1.5, Std(m, 1f, 2UL), 1.5 * 0.03);
        }

        static double Std(DistributionModel m, float n, ulong seed)
        {
            var rng = new SeedRng(seed);
            var sa = DistributionExecution.SigmaAngleDeg(P, m, DistributionKind.LongKick, n);
            var sd = DistributionExecution.SigmaDist(P, n);
            var aim = new Vec2(66f, 34f);
            double sum = 0, sq = 0;
            const int N = 100000;
            for (int i = 0; i < N; i++)
            {
                var a = DistributionExecution.PerturbAim(Gk, aim, sa, sd, rng, out _, out _);
                var d = a - Gk;
                double ang = Math.Atan2(d.Y, d.X) * 180.0 / Math.PI;
                sum += ang;
                sq += ang * ang;
            }
            var mean = sum / N;
            return Math.Sqrt((sq - N * mean * mean) / (N - 1));
        }

        /// <summary>AC-08 ②: Distribution 20 쪽 롱 배급 성공률(낙하점이 받는 선수에게 상대보다 가까움)이 높다 — 각 2,000회, 이항 p &lt; 0.01.</summary>
        [Test]
        public void LongSuccess_Dist20_Higher()
        {
            var m = new DistributionModel(P);
            var lo = Success(m, 0f, 3UL);
            var hi = Success(m, 1f, 4UL);
            TestContext.WriteLine($"Distribution 1: {lo}/2000 · 20: {hi}/2000");
            double pa = hi / 2000.0, pb = lo / 2000.0, pp = (hi + lo) / 4000.0;
            Assert.Greater((pa - pb) / Math.Sqrt(pp * (1 - pp) * 2.0 / 2000), 2.33);
        }

        static int Success(DistributionModel m, float n, ulong seed)
        {
            var rng = new SeedRng(seed);
            var sa = DistributionExecution.SigmaAngleDeg(P, m, DistributionKind.LongKick, n);
            var sd = DistributionExecution.SigmaDist(P, n);
            var recv = new Vec2(64f, 34f);
            var opp = new Vec2(68f, 36f);
            var ok = 0;
            for (int i = 0; i < 2000; i++)
            {
                var a = DistributionExecution.PerturbAim(Gk, recv, sa, sd, rng, out _, out _);
                if ((a - recv).LengthSq() < (a - opp).LengthSq()) ok++;
            }
            return ok;
        }

        [Test]
        public void Directness_RaisesLong_Role_SweeperShort_TraditionalLong()
        {
            var m = new DistributionModel(P);
            var c0 = Ctx(); c0.Directness = 0f;
            var c1 = Ctx(); c1.Directness = 1f;
            Assert.Greater(m.Multiplier(DistributionKind.Punt, in c1), m.Multiplier(DistributionKind.Punt, in c0));
            Assert.AreEqual(m.Multiplier(DistributionKind.Throw, in c1), m.Multiplier(DistributionKind.Throw, in c0));
            var sw = Ctx(DistributionSource.GoalKick); sw.Role = KeeperRole.Sweeper;
            var tr = Ctx(DistributionSource.GoalKick); tr.Role = KeeperRole.Traditional;
            var st = Ctx(DistributionSource.GoalKick);
            Assert.Greater(m.Multiplier(DistributionKind.ShortKick, in sw), m.Multiplier(DistributionKind.ShortKick, in st));
            Assert.Greater(m.Multiplier(DistributionKind.LongKick, in tr), m.Multiplier(DistributionKind.LongKick, in st));
            Assert.Greater(m.Multiplier(DistributionKind.ShortKick, Ctx(DistributionSource.Feet)), m.Multiplier(DistributionKind.ShortKick, in st), "백패스 → 숏 빌드업");
            Assert.Greater(m.Multiplier(DistributionKind.LongKick, Ctx(DistributionSource.GoalKick, press: true)), m.Multiplier(DistributionKind.LongKick, in st), "압박 → 롱");
        }

        /// <summary>FR-12: 시나리오 의도(G-DS)는 효용 보너스 — 의도 지점 근처 후보가 더 자주 뽑힌다.</summary>
        [Test]
        public void Intent_TargetBonus()
        {
            var w = World();
            ActionTestKit.Put(w, 2, new Vec2(20f, 20f));
            ActionTestKit.Put(w, 3, new Vec2(20f, 48f));
            var m = new DistributionModel(P);
            var c = Ctx(DistributionSource.GoalKick);
            Assert.AreEqual(2, m.Evaluate(w, Ap, ActionTestKit.Ball, 0, Gk, 0f, in c));
            int Count(in DistributionContext cc, ulong seed)
            {
                var rng = new SeedRng(seed);
                var k = 0;
                for (int i = 0; i < 1000; i++) if (m.Candidate(m.Choose(in cc, rng)).Target == 3) k++;
                return k;
            }
            var none = Count(in c, 9UL);
            c.HasIntent = true;
            c.IntentPoint = new Vec2(20f, 48f);
            var with = Count(in c, 9UL);
            TestContext.WriteLine($"슬롯 3 선택: 의도 없음 {none} · 의도 {with}");
            Assert.Greater(with, none);
        }
    }
}
