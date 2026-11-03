using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>specs/006 T01: 운동 시스템 상태 직렬화·비활성·슬롯 재설정.</summary>
    public class KinematicsSnapshotTests
    {
        static ulong Hash(KinematicsSystem k)
        {
            var h = StableHasher.Create();
            k.AddTo(ref h);
            return h.Value;
        }

        static void Advance(KinematicsSystem k, SeedRng cmd, int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                RandomCommands(k, cmd);
                for (int s = 0; s < 10; s++) k.Step();
            }
        }

        [Test, Category("Determinism")]
        public void WriteRead_RoundTrip_SameHashAndContinuation()
        {
            var a = new KinematicsSystem(Params, RandomSquad(new SeedRng(5UL)));
            var cmd = new SeedRng(9UL);
            Advance(a, cmd, 300);
            a.SetActive(4, false);
            a.ResetSlot(7, KinematicAttributes.Uniform(18), new Vec2(50f, 30f), new Vec2(0f, 1f));
            Advance(a, cmd, 5);
            var w = new StateWriter();
            a.WriteState(w);
            var cmdState = cmd.SaveState();

            var b = new KinematicsSystem(Params, RandomSquad(new SeedRng(5UL)));
            b.ReadState(new StateReader(w.ToArray()));
            Assert.That(Hash(b), Is.EqualTo(Hash(a)), "복원 직후 해시");
            Assert.That(b.IsActive(b.IndexOf(4)), Is.False);
            Assert.That(b.Profile(b.IndexOf(7)).MaxSpeed, Is.EqualTo(a.Profile(a.IndexOf(7)).MaxSpeed), "능력치 → 프로필 재계산");

            var cmdB = new SeedRng(cmdState);
            Advance(a, cmd, 200);
            Advance(b, cmdB, 200);
            Assert.That(Hash(b), Is.EqualTo(Hash(a)), "복원 후 20초 진행");
        }

        [Test]
        public void ReadState_WrongPlayerSet_Throws()
        {
            var a = new KinematicsSystem(Params, RandomSquad(new SeedRng(5UL)));
            var w = new StateWriter();
            a.WriteState(w);
            var b = new KinematicsSystem(Params, RandomSquad(new SeedRng(5UL), 20));
            Assert.Throws<System.FormatException>(() => b.ReadState(new StateReader(w.ToArray())));
        }

        [Test]
        public void Inactive_FrozenAndSkippedInSeparation()
        {
            var a = KinematicAttributes.Uniform(12);
            var k = new KinematicsSystem(Params, new[]
            {
                new PlayerSetup(1, a, new Vec2(45f, 34f)),
                new PlayerSetup(2, a, new Vec2(50.3f, 34f)),
                new PlayerSetup(3, a, new Vec2(20f, 20f)),
            });
            k.SetActive(2, false);
            k.SetCommand(1, SteeringCommand.Arrive(new Vec2(50.3f, 34f)));
            k.SetCommand(3, SteeringCommand.Pursue(2));
            Run(k, 300);
            var s2 = k.State(k.IndexOf(2));
            Assert.That(s2.Position, Is.EqualTo(new Vec2(50.3f, 34f)), "비활성은 움직이지 않는다");
            Assert.That(k.Stats(k.IndexOf(2)).Distance, Is.EqualTo(0d));
            var d = (k.State(0).Position - s2.Position).Length();
            Assert.That(d, Is.LessThan(0.2f), "비활성과는 겹침 해소를 하지 않는다 (목표점까지 감)");
            Assert.That(k.State(2).Velocity.Length(), Is.LessThan(0.01f), "비활성 대상 Pursue는 대상 없음 → 정지");
        }

        [Test, Category("Determinism")]
        public void AllActive_HashUnchangedByActiveToggleRoundTrip()
        {
            var a = new KinematicsSystem(Params, RandomSquad(new SeedRng(3UL)));
            var b = new KinematicsSystem(Params, RandomSquad(new SeedRng(3UL)));
            b.SetActive(5, false);
            b.SetActive(5, true);
            Assert.That(Hash(b), Is.EqualTo(Hash(a)), "다시 활성화하면 표시가 사라진다 (상태는 Freeze 값과 같은 초기 정지)");
        }

        [Test]
        public void ResetSlot_NewProfile_FreshFatigue()
        {
            var k = One(KinematicAttributes.Uniform(5), new Vec2(10f, 34f), facing: new Vec2(1f, 0f));
            k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 34f)));
            Run(k, 800);
            Assert.That(k.State(0).W, Is.LessThan(1f));
            var slow = k.Profile(0).MaxSpeed;
            k.ResetSlot(1, KinematicAttributes.Uniform(20), new Vec2(30f, 34f), new Vec2(-1f, 0f));
            var s = k.State(0);
            Assert.That(k.Profile(0).MaxSpeed, Is.GreaterThan(slow));
            Assert.That(s.W, Is.EqualTo(1f));
            Assert.That(s.C, Is.EqualTo(0f));
            Assert.That(s.Velocity, Is.EqualTo(Vec2.Zero));
            Assert.That(s.Facing, Is.EqualTo(new Vec2(-1f, 0f)));
            Assert.That(k.Stats(0).Distance, Is.EqualTo(0d));
            Assert.That(k.State(0).Active.Kind, Is.EqualTo(SteeringKind.Idle));
        }

        [Test]
        public void Place_KeepsFatigue_StopsMotion()
        {
            var k = One(KinematicAttributes.Uniform(10), new Vec2(10f, 34f), facing: new Vec2(1f, 0f));
            k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 34f)));
            Run(k, 500);
            var w = k.State(0).W;
            var dist = k.Stats(0).Distance;
            k.Place(1, new Vec2(52.5f, 34f), new Vec2(1f, 0f));
            Assert.That(k.State(0).Position, Is.EqualTo(new Vec2(52.5f, 34f)));
            Assert.That(k.State(0).Velocity, Is.EqualTo(Vec2.Zero));
            Assert.That(k.State(0).W, Is.EqualTo(w));
            Assert.That(k.Stats(0).Distance, Is.EqualTo(dist));
        }
    }
}
