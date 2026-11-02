using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-13·16: 결정론.</summary>
    public class BallDeterminismTests
    {
        static BallState RandomState(SeedRng rng, BallParams p)
        {
            var pos = new Vec3(rng.Range(-2f, 107f), rng.Range(-2f, 70f), rng.Range(p.Radius, 4f));
            var vel = new Vec3(rng.Range(-35f, 35f), rng.Range(-35f, 35f), rng.Range(-8f, 15f));
            var spin = new Vec3(rng.Range(-120f, 120f), rng.Range(-120f, 120f), rng.Range(-120f, 120f));
            return rng.Bernoulli(0.3f)
                ? BallState.RollingAt(pos.XY, vel.XY, p.Radius)
                : BallState.InAir(pos, vel, spin);
        }

        static ulong RunAll(int count, int steps, IBallDiagnostics diag, List<Checkpoint> checkpoints)
        {
            var ps = new[]
            {
                BallTestKit.Params(BallType.Modern, SurfaceCondition.Dry),
                BallTestKit.Params(BallType.Panel32, SurfaceCondition.Wet),
                BallTestKit.Params(BallType.Modern, SurfaceCondition.Artificial),
                BallTestKit.Params(BallType.Modern, SurfaceCondition.Flooded),
                BallTestKit.Params(BallType.Panel32, SurfaceCondition.LongGrass),
            };
            var rng = new SeedRng(20261102UL);
            var ev = new BallEventBuffer();
            var all = StableHasher.Create();
            for (int n = 0; n < count; n++)
            {
                var p = ps[n % ps.Length];
                var s = RandomState(rng, p);
                var eh = StableHasher.Create();
                for (int i = 0; i < steps; i++)
                {
                    ev.Clear();
                    BallStepper.Step(ref s, p, ev, out _, diag);
                    ev.AddTo(ref eh);
                    if (s.Phase == BallPhase.Rest) break;
                }
                var sh = StableHasher.Create();
                s.AddTo(ref sh);
                checkpoints?.Add(new Checkpoint(n, n, eh.Value, sh.Value));
                all.Add(eh.Value);
                all.Add(sh.Value);
            }
            return all.Value;
        }

        [Test, Category("Determinism")]
        public void TenThousandInitialStates_TwoRuns_SameHash()
        {
            var a = new List<Checkpoint>();
            var b = new List<Checkpoint>();
            var h1 = RunAll(10000, 600, null, a);
            var h2 = RunAll(10000, 600, null, b);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i], $"초기 조건 {i}");
            Assert.AreEqual(h1, h2);
        }

        sealed class Recorder : IBallDiagnostics
        {
            public int Substeps, Events, Landings;
            public void OnSubstep(in BallState state, in BallSegment segment) => Substeps++;
            public void OnEvent(in BallEvent e) => Events++;
            public void OnPredictedLanding(Vec3 point, float timeSeconds) => Landings++;
        }

        [Test, Category("Determinism")]
        public void DiagOnOff_SameEventAndCheckpointHash()
        {
            var off = new List<Checkpoint>();
            var on = new List<Checkpoint>();
            var rec = new Recorder();
            var h1 = RunAll(500, 600, null, off);
            var h2 = RunAll(500, 600, rec, on);
            Assert.AreEqual(h1, h2);
            CollectionAssert.AreEqual(off, on);
            Assert.Greater(rec.Substeps, 1000);
            Assert.Greater(rec.Events, 100);

            // 예측기 진단 출력도 결과를 바꾸지 않는다
            var p = BallTestKit.Params();
            var k = BallState.InAir(new Vec3(30f, 34f, p.Radius), new Vec3(15f, 2f, 9f), Vec3.Zero);
            var pa = new BallPrediction(250);
            var pb = new BallPrediction(250);
            BallPredictor.Predict(k, p, 0.04f, 10f, pa);
            BallPredictor.Predict(k, p, 0.04f, 10f, pb, rec);
            Assert.AreEqual(pa.Count, pb.Count);
            for (int i = 0; i < pa.Count; i++) Assert.AreEqual(pa.Positions[i], pb.Positions[i]);
            Assert.AreEqual(1, rec.Landings);
        }
    }
}
