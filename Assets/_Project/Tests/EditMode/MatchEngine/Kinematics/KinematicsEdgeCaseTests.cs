using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-11: 경계 · 예외 상황.</summary>
    public class KinematicsEdgeCaseTests
    {
        static void AssertFinite(KinematicsSystem k)
        {
            for (int i = 0; i < k.Count; i++)
            {
                var s = k.State(i);
                Assert.That(float.IsNaN(s.Position.X) || float.IsNaN(s.Position.Y) || float.IsNaN(s.Velocity.X) || float.IsNaN(s.Velocity.Y), Is.False);
            }
        }

        [Test]
        public void ZeroDistance_StaysStill_NoNaN()
        {
            var pos = new Vec2(40f, 30f);
            var k = One(Attr(), pos);
            k.SetCommandImmediate(1, SteeringCommand.Arrive(pos));
            Run(k, 200);
            AssertFinite(k);
            Assert.That(k.State(0).Position, Is.EqualTo(pos));
            k.SetCommandImmediate(1, SteeringCommand.Seek(pos));
            Run(k, 200);
            Assert.That(k.State(0).Position, Is.EqualTo(pos));
            Assert.That(Steering.Seek(pos, pos, 5f), Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void TargetOutsidePitch_Clamped()
        {
            var k = One(Attr(18), new Vec2(100f, 34f), default, new Vec2(1f, 0f));
            k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(500f, -300f)));
            Run(k, 3000);
            var s = k.State(0);
            Assert.That(s.Position.X, Is.LessThanOrEqualTo(Params.MaxX + 1e-3f));
            Assert.That(s.Position.Y, Is.GreaterThanOrEqualTo(Params.MinY - 1e-3f));
            Assert.That(s.Position.X, Is.GreaterThan(105f), "여유 안까지는 나간다");
        }

        [Test]
        public void SamePosition_DeterministicSplit()
        {
            var a = KinematicAttributes.Uniform(10);
            var p = new Vec2(50f, 30f);
            var k1 = new KinematicsSystem(Params, new[] { new PlayerSetup(3, a, p), new PlayerSetup(1, a, p), new PlayerSetup(2, a, p) });
            var k2 = new KinematicsSystem(Params, new[] { new PlayerSetup(2, a, p), new PlayerSetup(3, a, p), new PlayerSetup(1, a, p) });
            Run(k1, 100); Run(k2, 100);
            for (int i = 0; i < 3; i++) Assert.That(k1.State(i).Position, Is.EqualTo(k2.State(i).Position));
            AssertFinite(k1);
        }

        [Test]
        public void PivotBoundary_NoChatter()
        {
            // 2 m/s 근처에서 목표 속도를 1.9 ↔ 2.2로 흔들어도 피벗 상태 전환이 매 스텝 일어나지 않는다
            var k = One(Attr(), new Vec2(10f, 34f), new Vec2(2.1f, 0f), new Vec2(1f, 0f));
            int switches = 0;
            var prev = k.State(0).Pivoting;
            for (int i = 0; i < 1000; i++)
            {
                k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 34f), (i / 5) % 2 == 0 ? 1.95f : 2.2f));
                k.Step();
                if (k.State(0).Pivoting != prev) { switches++; prev = k.State(0).Pivoting; }
            }
            Assert.That(switches, Is.LessThanOrEqualTo(1), "히스테리시스 2.0/2.3");
        }

        [Test]
        public void Match120min_NoNaN_FatigueBounded()
        {
            var rng = new SeedRng(120UL);
            var k = new KinematicsSystem(Params, RandomSquad(rng));
            var ticks = 120 * 60 * 10 + 300; // 연장 120분 + 추가시간
            for (int t = 0; t < ticks; t++)
            {
                if (t % 10 == 0) RandomCommands(k, rng);
                for (int s = 0; s < 10; s++) k.Step();
            }
            AssertFinite(k);
            for (int i = 0; i < k.Count; i++)
            {
                var s = k.State(i);
                Assert.That(s.W, Is.InRange(0f, 1f));
                Assert.That(s.C, Is.InRange(0f, 1f));
                Assert.That(k.Stats(i).Distance, Is.GreaterThan(1000.0));
            }
        }

        [Test]
        public void InvalidInputs_Rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new KinematicAttributes(101, 50, 50, 50, 50, 50, 50));
            var a = Attr();
            Assert.Throws<ArgumentException>(() => new KinematicsSystem(Params, new[] { new PlayerSetup(1, a, Vec2.Zero), new PlayerSetup(1, a, Vec2.Zero) }));
            var k = One(a, new Vec2(10f, 10f));
            Assert.Throws<ArgumentException>(() => k.SetCommand(99, SteeringCommand.Idle()));
        }

        [Test]
        public void MissingTarget_StopsInsteadOfThrowing()
        {
            var k = One(Attr(), new Vec2(10f, 10f), new Vec2(4f, 0f), new Vec2(1f, 0f));
            k.SetCommandImmediate(1, SteeringCommand.Pursue(42));
            Run(k, 300);
            Assert.That(k.State(0).Velocity.Length(), Is.LessThan(0.01f));
        }
    }
}
