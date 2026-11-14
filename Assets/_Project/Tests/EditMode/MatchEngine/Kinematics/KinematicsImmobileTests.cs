using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>specs/005 FR-29 · AC-17 (specs/070 T01): 이동 불가 시간.</summary>
    public class KinematicsImmobileTests
    {
        static ulong Hash(KinematicsSystem k)
        {
            var h = StableHasher.Create();
            k.AddTo(ref h);
            return h.Value;
        }

        static KinematicsSystem Runner()
        {
            var k = One(Attr(15), new Vec2(20f, 34f), new Vec2(8f, 0f), new Vec2(1f, 0f));
            k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 34f)));
            return k;
        }

        [Test]
        public void Immobile12s_IgnoresCommands_StopsWithinLimits_ThenResumes()
        {
            var k = Runner();
            k.Monitor = new MotionLimitMonitor();
            Run(k, 50);
            var until = k.Substep + 120; // 1.2 s
            k.SetImmobile(1, until);
            var sent = 0;
            var v0 = k.State(0).Velocity.Length();
            var prev = v0;
            while (k.Substep < until)
            {
                if (k.Substep % 10 == 0) { k.SetCommand(1, SteeringCommand.Seek(new Vec2(100f, 34f))); sent++; }
                k.Step();
                var v = k.State(0).Velocity.Length();
                Assert.That(v, Is.LessThanOrEqualTo(prev + 1e-4f), "이동 불가 중 가속 없음");
                prev = v;
            }
            Assert.That(k.IgnoredCommands, Is.EqualTo(sent), "이동 불가 중 보낸 명령은 모두 무시");
            Assert.That(prev, Is.LessThan(v0 * 0.3f), "1.2 s 동안 감속 한계로 줄어든다 (전력 질주에서 완전 정지는 감속 한계상 더 걸릴 수 있음)");
            Assert.That(k.PendingCommands(0), Is.Zero);
            Assert.That(k.Monitor.TotalViolations, Is.Zero, "감속은 005 한계 안");
            Assert.That(k.IsImmobile(0), Is.False);

            k.SetCommand(1, SteeringCommand.Seek(new Vec2(100f, 34f)));
            Run(k, 150);
            Assert.That(k.State(0).Velocity.X, Is.GreaterThan(2f), "끝난 뒤 명령을 따른다");
            Assert.That(k.Monitor.TotalViolations, Is.Zero);
        }

        [Test]
        public void SetImmobile_KeepsLaterEnd_IgnoresPast()
        {
            var k = Runner();
            k.SetImmobile(1, 200);
            k.SetImmobile(1, 100);
            Assert.That(k.ImmobileUntil(0), Is.EqualTo(200));
            var k2 = Runner();
            k2.SetImmobile(1, 0);
            Assert.That(k2.IsImmobile(0), Is.False, "과거 끝 시각은 무시");
        }

        [Test, Category("Determinism")]
        public void Unused_HashUnchanged_StateChangesHash_SnapshotRoundTrip()
        {
            var a = Runner();
            var b = Runner();
            Run(a, 30); Run(b, 30);
            Assert.That(Hash(a), Is.EqualTo(Hash(b)));
            b.SetImmobile(1, b.Substep + 120);
            Assert.That(Hash(b), Is.Not.EqualTo(Hash(a)), "이동 불가 상태는 해시에 들어간다");

            Run(b, 20);
            var w = new StateWriter();
            b.WriteState(w);
            var c = Runner();
            c.ReadState(new StateReader(w.ToArray()));
            Assert.That(Hash(c), Is.EqualTo(Hash(b)));
            Assert.That(c.IsImmobile(0), Is.True);
            for (int i = 0; i < 200; i++)
            {
                if (i % 10 == 0) { b.SetCommand(1, SteeringCommand.Seek(new Vec2(90f, 20f))); c.SetCommand(1, SteeringCommand.Seek(new Vec2(90f, 20f))); }
                b.Step(); c.Step();
            }
            Assert.That(Hash(c), Is.EqualTo(Hash(b)), "복원 후 같은 진행");
        }

        [Test, Category("Determinism")]
        public void Squad_TwoRuns_SameHash_WithImmobile()
        {
            ulong Run1()
            {
                var k = new KinematicsSystem(Params, RandomSquad(new SeedRng(3UL)));
                var cmd = new SeedRng(4UL);
                for (int t = 0; t < 300; t++)
                {
                    if (t % 37 == 0) k.SetImmobile(k.State(t % 22).Id, k.Substep + 120);
                    RandomCommands(k, cmd);
                    for (int s = 0; s < 10; s++) k.Step();
                }
                return Hash(k);
            }
            Assert.That(Run1(), Is.EqualTo(Run1()));
        }
    }
}
