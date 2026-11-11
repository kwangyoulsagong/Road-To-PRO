using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    /// <summary>specs/058 FR-30·31: 샷 안 튐 검출, 컷 프레임은 허용.</summary>
    public class CameraContinuityTests
    {
        static CameraTrack Track(System.Func<int, float> yawDeg, System.Func<int, bool> cut = null)
        {
            var t = new CameraTrack(0, 60, 8, 30);
            for (int f = 0; f < 60; f++)
            {
                float a = yawDeg(f) * MathF.PI / 180f;
                t.Poses[f] = new CameraPose(new Vec3(0, 20, -60), new Vec3(MathF.Sin(a), -0.3f, MathF.Cos(a)).Normalized(), 0.2f, 0.35f);
                t.Shots[f] = ShotKind.Main;
                t.CutAt[f] = cut != null && cut(f);
            }
            return t;
        }

        [Test]
        public void SmoothPan_NoViolation()
        {
            var r = CameraContinuity.Measure(Track(f => f * 0.5f), 0.3, 60, 240, 10); // 15°/s
            Assert.That(r.Violations, Is.EqualTo(0));
            Assert.That(r.MaxAngularSpeed, Is.EqualTo(15f).Within(1f));
        }

        [Test]
        public void Jump_Detected()
        {
            var r = CameraContinuity.Measure(Track(f => f < 30 ? 0f : 20f), 0.3, 60, 240, 10);
            Assert.That(r.Violations, Is.GreaterThan(0));
        }

        [Test]
        public void JumpAtCut_Allowed()
        {
            var r = CameraContinuity.Measure(Track(f => f < 30 ? 0f : 20f, f => f == 30), 0.3, 60, 240, 10);
            Assert.That(r.Violations, Is.EqualTo(0));
        }
    }
}
