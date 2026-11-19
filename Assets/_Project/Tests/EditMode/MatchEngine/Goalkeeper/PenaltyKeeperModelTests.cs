using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.Tests.EditMode.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/074 PK GK 전략 FR-01~04 (AC-01·09).</summary>
    public class PenaltyKeeperModelTests
    {
        static readonly Vec2 Goal = new Vec2(0f, 34f);
        static GoalkeeperParams P => GoalkeeperParams.Defaults;
        static KeeperProfile Avg => KeeperProfile.From(P, KeeperAttributes.Uniform(10.5f));
        static readonly BallParams Bp = BallTestKit.Params();
        const float PkSpeed = 20.8f; // FR-02 (리서치 04 § A1)
        static readonly Vec3 Spot = new Vec3(11f, 34f, 0.11f);

        static SaveModel Model()
        {
            var m = new SaveModel(P, new KeeperPositioner(P, new GoalGrid(P)));
            m.Bind(Bp);
            return m;
        }

        static BallState Aim(Vec3 origin, float y, float z, float speed)
        {
            var target = new Vec3(Goal.X, Goal.Y + y, z);
            var dx = target.X - origin.X;
            var dy = target.Y - origin.Y;
            var horiz = (float)Math.Sqrt(dx * dx + dy * dy);
            var t = horiz / speed;
            var vz = (target.Z - origin.Z + 0.5f * Bp.Gravity * t * t) / t;
            return BallState.InAir(origin, new Vec3(dx / horiz * speed, dy / horiz * speed, vz), Vec3.Zero);
        }

        static readonly KeeperState OnLine = new KeeperState { Position = new Vec2(0f, 34f) };

        static SaveJudgement Judge(SaveModel m, float y, float z, int commit = 0, float lead = 0f)
        {
            var s = Aim(Spot, y, z, PkSpeed);
            Assert.IsTrue(m.Predict(in s, Bp, Goal, 1f), $"유효 슛이어야 한다 y {y} z {z}");
            return m.Evaluate(in OnLine, Avg, new SaveContext { Goal = Goal, DirIn = 1f, CommitDir = commit, CommitLead = lead });
        }

        /// <summary>AC-01 (플랜 Q1): 골문면 100 × 100 균등 격자(1만 점), PK 20.8 m/s, 리그 평균 GK 정중앙·정지·반응형 — 도달 불가(max margin &lt; 0) 비율 28 % ± 3 %p.</summary>
        [Test]
        public void UnsaveableArea_28pct()
        {
            var m = Model();
            int on = 0, unsaveable = 0;
            var r = Bp.Radius;
            var half = BallParams.GoalWidth * 0.5f - r;
            var top = BallParams.GoalHeight - r;
            for (int iy = 0; iy < 100; iy++)
                for (int iz = 0; iz < 100; iz++)
                {
                    var y = -half + 2f * half * (iy + 0.5f) / 100f;
                    var z = r + (top - r) * (iz + 0.5f) / 100f;
                    var s = Aim(Spot, y, z, PkSpeed);
                    if (!m.Predict(in s, Bp, Goal, 1f)) continue;
                    on++;
                    var j = m.Evaluate(in OnLine, Avg, new SaveContext { Goal = Goal, DirIn = 1f });
                    if (!(j.MaxMargin >= 0f)) unsaveable++;
                }
            var ratio = (double)unsaveable / on;
            TestContext.WriteLine($"유효 {on} · 도달 불가 {unsaveable} → {ratio:P1}");
            Assert.Greater(on, 9500);
            Assert.That(ratio, Is.InRange(0.25, 0.31));
        }

        /// <summary>FR-02 보정 진단 (판정 없음): rmaxBase·dive.base 조합별 도달 불가 비율.</summary>
        [Test, Explicit("074 T06 보정 진단")]
        public void Diagnose_UnsaveableSweep()
        {
            var root = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning", "goalkeeper.json");
            var reg = RoadToPro.Core.Tuning.TuningLoader.LoadDefinitionsFile(root);
            foreach (var rmax in new[] { 2.6f, 2.7f, 2.8f, 2.9f, 3.0f, 3.1f, 3.2f })
                foreach (var dive in new[] { 3.0f, 3.2f, 3.4f, 3.6f })
                {
                    var json = FormattableString.Invariant($"{{\"version\": 1, \"values\": [{{\"id\": \"gk.reach.rmaxBase\", \"value\": {rmax}}}, {{\"id\": \"gk.map.dive.base\", \"value\": {dive}}}]}}");
                    var p = GoalkeeperParams.Create(RoadToPro.Core.Tuning.TuningLoader.ParseBundle(reg, json));
                    var m = new SaveModel(p, new KeeperPositioner(p, new GoalGrid(p)));
                    m.Bind(Bp);
                    var k = KeeperProfile.From(p, KeeperAttributes.Uniform(10.5f));
                    int on = 0, un = 0;
                    var r = Bp.Radius;
                    var half = BallParams.GoalWidth * 0.5f - r;
                    var top = BallParams.GoalHeight - r;
                    for (int iy = 0; iy < 100; iy++)
                        for (int iz = 0; iz < 100; iz++)
                        {
                            var s = Aim(Spot, -half + 2f * half * (iy + 0.5f) / 100f, r + (top - r) * (iz + 0.5f) / 100f, PkSpeed);
                            if (!m.Predict(in s, Bp, Goal, 1f)) continue;
                            on++;
                            if (!(m.Evaluate(in OnLine, k, new SaveContext { Goal = Goal, DirIn = 1f }).MaxMargin >= 0f)) un++;
                        }
                    TestContext.WriteLine(FormattableString.Invariant($"rmax {rmax} dive {dive}: {(double)un / on:P1}"));
                }
        }

        [Test]
        public void Commit0_BitIdentical()
        {
            var m = Model();
            foreach (var (y, z) in new[] { (2.9f, 0.3f), (-1.5f, 1.8f), (0.2f, 1.0f) })
            {
                var a = Judge(m, y, z);
                var b = Judge(m, y, z, 0, 0.3f);
                Assert.AreEqual(a.MaxMargin, b.MaxMargin);
                Assert.AreEqual(a.ContactIndex, b.ContactIndex);
                Assert.AreEqual(a.PReach, b.PReach);
                Assert.AreEqual(a.ReactTime, b.ReactTime);
            }
        }

        [Test]
        public void SameSide_EarlierLead_Better_Opposite_Worse()
        {
            var m = Model();
            var sameEarly = Judge(m, 3.0f, 0.3f, +1, 0.35f).MaxMargin;
            var sameLate = Judge(m, 3.0f, 0.3f, +1, 0.05f).MaxMargin;
            var react = Judge(m, 3.0f, 0.3f).MaxMargin;
            var oppEarly = Judge(m, 3.0f, 0.3f, -1, 0.35f).MaxMargin;
            var oppLate = Judge(m, 3.0f, 0.3f, -1, 0.05f).MaxMargin;
            TestContext.WriteLine($"같은 쪽 0.35 {sameEarly:F3} · 0.05 {sameLate:F3} · 반응형 {react:F3} · 반대 0.35 {oppEarly:F3} · 0.05 {oppLate:F3}");
            Assert.GreaterOrEqual(sameEarly, sameLate, "도달 상한에 걸리면 같을 수 있다");
            Assert.GreaterOrEqual(sameLate, react);
            Assert.Greater(sameEarly, react);
            Assert.Less(oppEarly, oppLate);
            Assert.Less(oppLate, react);
            // 중앙 공: 일찍 날면 못 막는다
            Assert.Less(Judge(m, 0.1f, 0.5f, +1, 0.35f).MaxMargin, Judge(m, 0.1f, 0.5f).MaxMargin);
        }

        [Test]
        public void Opposite_LateLead_ReachNotNegative()
        {
            var m = Model();
            var j = Judge(m, 3.0f, 0.3f, -1, 0.35f);
            Assert.GreaterOrEqual(j.Reach, 0f);
            Assert.Less(j.MaxMargin, 0f, "반대쪽 구석은 못 막는다");
        }

        /// <summary>AC-09 (FR-04): 승부차기 + BigMatch·Composure 낮음의 기울기 분산이 일반 PK보다 크다 (F 검정 p &lt; 0.01, 각 5,000).</summary>
        [Test]
        public void ShootoutLowBigMatchComposure_DirNoiseGreater()
        {
            var model = new PenaltyKeeperModel(P);
            var mulS = PenaltyKeeperModel.PressureMul(P, true, 0f, 0f);
            var mulN = PenaltyKeeperModel.PressureMul(P, false, 0f, 0f);
            Assert.AreEqual(1f, mulN);
            Assert.AreEqual(1f, PenaltyKeeperModel.PressureMul(P, true, 1f, 1f), 1e-6f, "BigMatch·Composure 최고면 배수 1");
            var vs = Var(model, mulS, 11UL);
            var vn = Var(model, mulN, 12UL);
            var f = vs / vn;
            TestContext.WriteLine($"승부차기 배수 {mulS:F2}: 분산 {vs:F3} / 일반 {vn:F3} = F {f:F3}");
            Assert.Greater(f, 1.0681, "F(4999, 4999) 0.99 분위 ≈ 1.068");
        }

        static double Var(PenaltyKeeperModel m, float mul, ulong seed)
        {
            var rng = new SeedRng(seed);
            const int n = 5000;
            double sum = 0, sq = 0;
            for (int i = 0; i < n; i++)
            {
                double l = m.Draw(1, 0, mul, rng).Lean;
                sum += l;
                sq += l * l;
            }
            var mean = sum / n;
            return (sq - n * mean * mean) / (n - 1);
        }

        [Test]
        public void CenterBand_StaysReactive_NaturalSidePreferred()
        {
            var model = new PenaltyKeeperModel(P);
            var rng = new SeedRng(21UL);
            int center = 0, natural = 0, dives = 0;
            for (int i = 0; i < 10000; i++)
            {
                var s = model.Draw(-1, 0, 1f, rng);
                Assert.AreEqual(Math.Abs(s.Lean) < P.PkCenterBand, s.Dir == 0);
                if (s.Dir == 0) center++;
                else { dives++; if (s.Dir == -1) natural++; }
            }
            TestContext.WriteLine($"중앙 {center} · 다이빙 {dives} 중 자연 측 {natural} ({(double)natural / dives:P1})");
            Assert.Greater(center, 500);
            Assert.Greater((double)natural / dives, 0.55);
        }

        [Test]
        public void Lead_WithinRange_50_350ms_OffLineRate()
        {
            var model = new PenaltyKeeperModel(P);
            var rng = new SeedRng(31UL);
            int off = 0;
            float lo = 1f, hi = 0f;
            for (int i = 0; i < 10000; i++)
            {
                var s = model.Draw(1, 0, 1f, rng);
                Assert.That(s.Lead, Is.InRange(0.05f, 0.35f));
                lo = Math.Min(lo, s.Lead);
                hi = Math.Max(hi, s.Lead);
                if (s.OffLine) off++;
            }
            Assert.Less(lo, 0.06f);
            Assert.Greater(hi, 0.34f);
            Assert.That(off / 10000.0, Is.InRange(0.07, 0.09));
        }

        [Test]
        public void NaturalSign_MatchesKickerRule_Memory_Fatigue()
        {
            Assert.AreEqual(1, PenaltyKeeperModel.NaturalSign(Foot.Right, 1));
            Assert.AreEqual(-1, PenaltyKeeperModel.NaturalSign(Foot.Left, 1));
            Assert.AreEqual(-1, PenaltyKeeperModel.NaturalSign(Foot.Right, -1));
            var model = new PenaltyKeeperModel(P);
            Assert.Greater(model.Mean(1, 3), model.Mean(1, 0), "상대가 + 쪽으로 많이 찼으면 + 쪽으로 기운다");
            var k = Avg;
            Assert.Less(model.Fatigued(in k, 5).DiveSpeed, k.DiveSpeed);
            Assert.AreEqual(k.DiveSpeed, model.Fatigued(in k, 0).DiveSpeed);
        }
    }
}
