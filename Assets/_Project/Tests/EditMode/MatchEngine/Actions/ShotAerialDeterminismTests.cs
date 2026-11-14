using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 AC-09: 크로스·경합·슈팅·페널티 실행 10만 건 2회 → 결과 해시 같음, 액션당 RNG 소비 고정(분기 무관).</summary>
    public class ShotAerialDeterminismTests
    {
        static ActionParams P => ActionTestKit.P;

        static ulong Run(out bool fixedCount)
        {
            var rng = new SeedRng(7109UL);
            var shadow = new SeedRng(7109UL);
            var h = StableHasher.Create();
            var score = new float[AerialSystem.MaxParticipants];
            var prob = new float[AerialSystem.MaxParticipants];
            fixedCount = true;
            for (int i = 0; i < 100_000; i++)
            {
                int expect;
                switch (i % 4)
                {
                    case 0: // 뜬 크로스 착지 오차
                    {
                        var aim = CrossModel.ApplyLanding(new Vec2(95f, 30f + i % 9), CrossModel.SigmaLanding(P, (i % 10) * 0.1f, (i % 3) * 0.3f), rng, out _);
                        h.Add(aim.X); h.Add(aim.Y);
                        expect = CrossModel.RngPerLanding;
                        break;
                    }
                    case 1: // 공중볼 경합: 참여 0~6명 (0명이어도 승자 1 + 결과 4)
                    {
                        var n = i % (AerialSystem.MaxParticipants + 1);
                        for (int j = 0; j < n; j++) score[j] = AerialModel.Score(P, 2.6f + 0.05f * j, (i + j) % 10 * 0.1f, 0.5f, 0.5f, 0.5f);
                        AerialModel.Softmax(score, n, prob);
                        h.Add(AerialModel.Pick(prob, n, rng.NextFloat()));
                        ShotExecution.SampleNormals(rng, out var a, out var b);
                        h.Add(a); h.Add(b);
                        expect = AerialSystem.RngPerContest;
                        break;
                    }
                    case 2: // 슈팅 실행
                    {
                        var v = ShotExecution.Apply(new Vec3(22f, 2f, 3f), ShotExecution.SigmaAimDeg(P, (i % 10) * 0.1f, 25f, 0.3f, 0.5f, 1f, i % 5 == 0), rng, out _, out _);
                        h.Add(v.X); h.Add(v.Y); h.Add(v.Z);
                        expect = ShotExecution.RngPerShot;
                        break;
                    }
                    default: // 페널티: 방향 1 + 슈팅 4
                    {
                        h.Add(rng.NextFloat() < P.PenNaturalP);
                        var v = ShotExecution.Apply(new Vec3(25f, 1f, 1f), 2f, rng, out _, out _);
                        h.Add(v.X); h.Add(v.Y); h.Add(v.Z);
                        expect = ShotSystem.RngPenaltySide + ShotExecution.RngPerShot;
                        break;
                    }
                }
                for (int k = 0; k < expect; k++) shadow.NextFloat();
                var x = rng.SaveState();
                var y = shadow.SaveState();
                if (x.S0 != y.S0 || x.S1 != y.S1 || x.S2 != y.S2 || x.S3 != y.S3) fixedCount = false;
            }
            return h.Value;
        }

        [Test, Category("Determinism")]
        public void Cross_Aerial_Shot_Penalty100k_TwoRuns_SameHash_FixedRngCount()
        {
            var a = Run(out var fa);
            var b = Run(out var fb);
            Assert.AreEqual(a, b);
            Assert.IsTrue(fa && fb, "액션당 RNG 소비 고정 (크로스 4 · 경합 5 · 슛 4 · 페널티 5)");
        }

        [Test, Category("Determinism")]
        [Category("Slow")]
        public void Match_TwoRuns_SameHash_With071()
        {
            ulong Hash()
            {
                var sim = RoadToPro.Tests.EditMode.MatchEngine.Tactics.ShapeTestKit.Sim();
                sim.StepTicks(9_000);
                return sim.StateHash();
            }
            Assert.AreEqual(Hash(), Hash(), "071 포함 실경기 15분 상태 해시 2회 같음");
        }
    }
}
