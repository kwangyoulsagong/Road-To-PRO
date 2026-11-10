using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    /// <summary>프레이밍 대상·메인 카메라 (specs/042 FR-02~04, 50~52, AC-01·07).</summary>
    public class FramingTests
    {
        static CameraRigData Rig() => CameraRigData.Default();

        [Test]
        public void Collect_Carrier_BallCarrierTwoDefendersReceiver()
        {
            var d = BroadcastTestKit.Scene(0f, 2, _ => new Vec3(10f, 0f, 0f));
            var buf = new FramingTarget[FramingTargets.Capacity];
            int n = FramingTargets.Collect(d, 0, buf);
            Assert.AreEqual(5, n);
            Assert.AreEqual(3f, buf[0].Weight);
            Assert.AreEqual(2f, buf[1].Weight);
            Assert.AreEqual(2f, buf[2].Weight);
            Assert.AreEqual(2f, buf[3].Weight);
            Assert.AreEqual(1f, buf[4].Weight);
        }

        [Test]
        public void Collect_NoOwner_BallOnly()
        {
            var d = BroadcastTestKit.Scene(0f, 2, _ => new Vec3(10f, 0f, 0f), owner: -1);
            var buf = new FramingTarget[FramingTargets.Capacity];
            Assert.AreEqual(1, FramingTargets.Collect(d, 0, buf));
        }

        [Test]
        public void Collect_BallNotFinite_Zero()
        {
            var d = BroadcastTestKit.Scene(0f, 2, _ => new Vec3(float.NaN, 0f, 0f), owner: -1);
            Assert.AreEqual(0, FramingTargets.Collect(d, 0, new FramingTarget[FramingTargets.Capacity]));
        }

        [Test]
        public void Framing_LiveTenSeconds_PositionFixed_FovInRange()
        {
            var t = new CameraTuning();
            var solver = new FramingSolver(Rig(), t);
            var d = BroadcastTestKit.Scene(0f, 300, f => new Vec3(-45f + f * 0.3f, 0f, 25f * MathF.Sin(f * 0.05f)));
            var buf = new FramingTarget[FramingTargets.Capacity];
            var first = solver.Position;
            for (int f = 0; f < d.Frames; f++)
            {
                var pose = solver.Step(buf, FramingTargets.Collect(d, f, buf));
                Assert.AreEqual(first, pose.Position);
                Assert.That(pose.FovDeg(BroadcastTestKit.Aspect, t.FovMinDeg, t.FovMaxDeg), Is.InRange(8f, 30f));
            }
        }

        [Test]
        public void Framing_BoxArea_NarrowerThanMidfield()
        {
            var t = new CameraTuning();
            float Settle(Vec3 ball)
            {
                var solver = new FramingSolver(Rig(), t);
                var buf = new[] { new FramingTarget(ball, 3f, false) };
                CameraPose p = default;
                for (int i = 0; i < 300; i++) p = solver.Step(buf, 1);
                return p.FovDeg(BroadcastTestKit.Aspect, t.FovMinDeg, t.FovMaxDeg);
            }
            Assert.Less(Settle(new Vec3(45f, 0f, 0f)), Settle(new Vec3(0f, 0f, 0f)));
        }

        [Test]
        public void Framing_BallFarOutside_PanClamped()
        {
            var t = new CameraTuning { PanLimitDeg = 20f };
            var solver = new FramingSolver(Rig(), t);
            var buf = new[] { new FramingTarget(new Vec3(90f, 0f, 54f), 3f, false) };
            for (int i = 0; i < 120; i++)
            {
                var p = solver.Step(buf, 1);
                float pan = MathF.Atan2(p.Forward.X, p.Forward.Z) * CameraMath.Rad2Deg;
                Assert.That(MathF.Abs(pan), Is.LessThanOrEqualTo(20.01f));
            }
        }

        [Test]
        public void Framing_NearTouchline_BallInFrame()
        {
            var t = new CameraTuning();
            var solver = new FramingSolver(Rig(), t);
            var buf = new FramingTarget[1];
            for (int f = 0; f < 240; f++)
            {
                // 중앙에서 카메라 쪽 터치라인까지 빠르게 이동
                var ball = new Vec3(0f, 0f, MathF.Max(-34f, -f * 0.6f));
                buf[0] = new FramingTarget(ball, 3f, false);
                var p = solver.Step(buf, 1);
                var v = CameraMath.Viewport(p, BroadcastTestKit.Aspect, t.FovMinDeg, t.FovMaxDeg, ball);
                Assert.That(v.Y, Is.InRange(0.049f, 0.951f), $"프레임 {f}");
                Assert.That(v.X, Is.InRange(0.049f, 0.951f), $"프레임 {f}");
            }
        }

        [Test]
        public void Framing_FastShot_BallStaysInFrame()
        {
            var t = new CameraTuning();
            var solver = new FramingSolver(Rig(), t);
            var buf = new FramingTarget[1];
            for (int f = 0; f < 60; f++)
            {
                var ball = new Vec3(-20f + f * 1.2f, 0f, 0f); // 36 m/s
                buf[0] = new FramingTarget(ball, 3f, false);
                var p = solver.Step(buf, 1);
                var v = CameraMath.Viewport(p, BroadcastTestKit.Aspect, t.FovMinDeg, t.FovMaxDeg, ball);
                Assert.That(v.X, Is.InRange(0f, 1f), $"프레임 {f}");
            }
        }

        [Test]
        public void Framing_BallOverStands_AimsNearPitch_BallStillInFrame()
        {
            var t = new CameraTuning();
            var solver = new FramingSolver(Rig(), t);
            var ball = new Vec3(0f, 14f, -56f);
            var buf = new[] { new FramingTarget(ball, 3f, false) };
            CameraPose p = default;
            for (int i = 0; i < 240; i++) p = solver.Step(buf, 1);

            // 화면 중앙 시선이 닿는 지면은 터치라인 + 여백 안쪽이다
            float groundZ = p.Position.Z + p.Forward.Z * (-p.Position.Y / p.Forward.Y);
            Assert.GreaterOrEqual(groundZ, -40f);
            var v = CameraMath.Viewport(p, BroadcastTestKit.Aspect, t.FovMinDeg, t.FovMaxDeg, ball);
            Assert.That(v.Y, Is.InRange(0.049f, 0.951f));
        }

        [Test]
        public void Step_NoTargets_KeepsPreviousFraming()
        {
            var solver = new FramingSolver(Rig(), new CameraTuning());
            var buf = new[] { new FramingTarget(new Vec3(20f, 0f, 5f), 3f, false) };
            var before = solver.Step(buf, 1);
            var after = solver.Step(buf, 0);
            Assert.AreEqual(before.Forward, after.Forward);
            Assert.AreEqual(before.TanV, after.TanV);
        }
    }
}
