using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-15: 진단 출력 켬/끔이 결과를 바꾸지 않는다 (FR-51).</summary>
    public class KinematicsDiagnosticsTests
    {
        sealed class Collector : IKinematicsDiagnostics
        {
            public long Samples;
            public double Sum;
            public void OnPlayerSubstep(in KinematicsSample s)
            {
                Samples++;
                Sum += s.LongAccel + s.LatAccel + s.FrictionUse + s.W + s.C + s.PendingCommands + s.SeparationCorrection;
            }
            public void OnCommandDropped(long substep, int playerId) { }
        }

        static (ulong final, ulong checkpoints) Run(bool diagOn, bool monitorOn, out long samples)
        {
            var rng = new SeedRng(51UL);
            var k = new KinematicsSystem(Params, RandomSquad(rng));
            var c = diagOn ? new Collector() : null;
            k.Diagnostics = c;
            if (monitorOn) k.Monitor = new MotionLimitMonitor();
            var cp = StableHasher.Create();
            for (int t = 0; t < 3000; t++)
            {
                if (t % 10 == 0) RandomCommands(k, rng);
                k.Step();
                if (t % 100 == 0)
                {
                    var h = StableHasher.Create();
                    k.AddTo(ref h);
                    cp.Add(h.Value);
                }
            }
            var f = StableHasher.Create();
            k.AddTo(ref f);
            samples = c?.Samples ?? 0;
            return (f.Value, cp.Value);
        }

        [Test, Category("Determinism")]
        public void DiagOnOff_SameHash()
        {
            var off = Run(false, false, out var s0);
            var on = Run(true, true, out var s1);
            Assert.That(s0, Is.Zero);
            Assert.That(s1, Is.EqualTo(3000L * 22));
            Assert.That(on.final, Is.EqualTo(off.final), "최종 상태 해시");
            Assert.That(on.checkpoints, Is.EqualTo(off.checkpoints), "체크포인트 해시");
        }
    }
}
