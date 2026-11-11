using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>specs/011 FR-17 외부 구동(GK 다이빙) — 005 조향·한계 검사·Separation 제외, 해제 뒤 복귀 (T07).</summary>
    public class ExternalDriveTests
    {
        static KinematicsSystem Two(Vec2 a, Vec2 b) => new KinematicsSystem(Params, new[]
        {
            new PlayerSetup(1, KinematicAttributes.Uniform(10), a, default, new Vec2(1f, 0f)),
            new PlayerSetup(2, KinematicAttributes.Uniform(10), b, default, new Vec2(1f, 0f)),
        });

        [Test]
        public void External_MovesAtGivenVelocity_IgnoresCommands()
        {
            var k = Two(new Vec2(2f, 34f), new Vec2(30f, 34f));
            k.SetCommandImmediate(1, SteeringCommand.Arrive(new Vec2(20f, 34f)));
            k.BeginExternal(1, new Vec2(0f, 4f));
            k.SetCommand(1, SteeringCommand.Arrive(new Vec2(20f, 34f)));
            for (int i = 0; i < 50; i++) k.Step();
            Assert.AreEqual(2f, k.State(0).Position.X, 1e-5f);
            Assert.AreEqual(34f + 4f * 0.5f, k.State(0).Position.Y, 1e-3f);
            Assert.AreEqual(new Vec2(0f, 4f), k.State(0).Velocity);
        }

        [Test]
        public void External_InstantVelocity_NoMonitorViolation()
        {
            var k = Two(new Vec2(2f, 34f), new Vec2(30f, 34f));
            k.Monitor = new MotionLimitMonitor();
            k.BeginExternal(1, new Vec2(0f, 4.6f)); // 정지에서 즉시 4.6 m/s — 005 가속 한계 밖
            for (int i = 0; i < 30; i++) k.Step();
            k.DriveExternal(1, Vec2.Zero);
            for (int i = 0; i < 30; i++) k.Step();
            Assert.IsFalse(k.Monitor.Failed);
        }

        [Test]
        public void External_KeepsOverlapProjection_NoSteeringPush()
        {
            var k = Two(new Vec2(10f, 34f), new Vec2(10.2f, 34f));
            k.BeginExternal(1, Vec2.Zero);
            k.Step();
            Assert.Less(k.State(0).Position.X, 10f, "겹침 투영은 그대로 (해제 순간 튐 방지)");
            Assert.AreEqual(Vec2.Zero, k.State(0).Velocity, "속도는 외부 구동 값 그대로");
        }

        [Test]
        public void End_ReturnsToSteering_Stopped()
        {
            var k = Two(new Vec2(2f, 34f), new Vec2(30f, 34f));
            k.Monitor = new MotionLimitMonitor();
            k.BeginExternal(1, new Vec2(0f, 4f));
            for (int i = 0; i < 20; i++) k.Step();
            k.EndExternal(1);
            Assert.IsFalse(k.IsExternal(0));
            Assert.AreEqual(Vec2.Zero, k.State(0).Velocity);
            Assert.AreEqual(SteeringKind.Idle, k.State(0).Active.Kind);
            k.SetCommandImmediate(1, SteeringCommand.Arrive(new Vec2(2f, 30f)));
            for (int i = 0; i < 300; i++) k.Step();
            Assert.Less((k.State(0).Position - new Vec2(2f, 30f)).Length(), 0.5f);
            Assert.IsFalse(k.Monitor.Failed, "해제 뒤 일반 운동은 한계 안");
        }

        [Test]
        public void Unused_HashUnchanged_Snapshot_RoundTripMidDrive()
        {
            var a = Two(new Vec2(2f, 34f), new Vec2(30f, 34f));
            var b = Two(new Vec2(2f, 34f), new Vec2(30f, 34f));
            for (int i = 0; i < 10; i++) { a.Step(); b.Step(); }
            Assert.AreEqual(Hash(a), Hash(b));

            a.BeginExternal(1, new Vec2(1f, 3f));
            for (int i = 0; i < 5; i++) a.Step();
            var w = new StateWriter();
            a.WriteState(w);
            var c = Two(new Vec2(0f, 0f), new Vec2(0f, 0f));
            c.ReadState(new StateReader(w.ToArray()));
            Assert.IsTrue(c.IsExternal(0));
            Assert.AreEqual(Hash(a), Hash(c));
            for (int i = 0; i < 10; i++) { a.Step(); c.Step(); }
            Assert.AreEqual(Hash(a), Hash(c));
        }

        [Test]
        public void FlightVelocity_CappedByDiveSpeed_ReachesContact()
        {
            var v = DiveMotion.FlightVelocity(new Vec2(1f, 34f), new Vec2(1f, 37f), 0.25f, 4f, 0.5f);
            Assert.AreEqual(4f, v.Length(), 1e-5f, "필요 5.5 m/s → 상한 4");
            var v2 = DiveMotion.FlightVelocity(new Vec2(1f, 34f), new Vec2(1f, 35.25f), 0.25f, 4f, 0.5f);
            Assert.AreEqual(2f, v2.Length(), 1e-5f, "1 m / 0.5 s");
            Assert.AreEqual(Vec2.Zero, DiveMotion.FlightVelocity(new Vec2(1f, 34f), new Vec2(1f, 34.1f), 0.25f, 4f, 0.5f));
        }

        static ulong Hash(KinematicsSystem k)
        {
            var h = StableHasher.Create();
            k.AddTo(ref h);
            return h.Value;
        }
    }
}
