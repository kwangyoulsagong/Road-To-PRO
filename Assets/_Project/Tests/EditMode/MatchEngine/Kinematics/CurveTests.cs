using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-04: 원 주행 — 속도 ≤ √(a_lat,max·R), 마찰원 위반 0 (FR-11).</summary>
    public class CurveTests
    {
        static readonly Vec2 C = new Vec2(52.5f, 34f);

        /// <summary>원 위 앞쪽 점을 계속 Seek. 실제 곡률 반경 R로 속도 한계를 확인한다.</summary>
        static void RunCircle(float maxSpeed, out float vMax, out float rMin, out float rMax, out float limitAtRMin, out long violations)
        {
            var k = One(KinematicAttributes.Uniform(15), new Vec2(C.X, C.Y - 5f), new Vec2(3f, 0f), new Vec2(1f, 0f));
            k.Monitor = new MotionLimitMonitor();
            var lat = k.Profile(0).MaxLateralAccel;
            vMax = 0f; rMin = float.MaxValue; rMax = 0f;
            float maxRatio = 0f;
            for (int i = 0; i < 3000; i++)
            {
                var r = k.State(0).Position - C;
                var ang = DMath.Atan2(r.Y, r.X) + 0.35f;
                k.SetCommandImmediate(1, SteeringCommand.Seek(C + new Vec2(DMath.Cos(ang), DMath.Sin(ang)) * 5f, maxSpeed));
                k.Step();
                if (i < 500) continue;
                var v = k.State(0).Velocity.Length();
                var rr = (k.State(0).Position - C).Length();
                vMax = DMath.Max(vMax, v);
                rMin = DMath.Min(rMin, rr);
                rMax = DMath.Max(rMax, rr);
                maxRatio = DMath.Max(maxRatio, v / DMath.Sqrt(lat * rr));
            }
            limitAtRMin = maxRatio; // 실제 반경 기준 속도/한계 최대 비
            violations = k.Monitor.TotalViolations;
        }

        [Test]
        public void Circle5m_SpeedBelowSqrtALatR_NoFrictionCircleViolation()
        {
            var lat = PlayerMotionProfile.Create(KinematicAttributes.Uniform(15), Params).MaxLateralAccel;
            var vLimit = DMath.Sqrt(lat * 5f);

            // 원 반경 5 m를 따라갈 만큼 속도를 제한한 주행: 반경 5 m 근처를 유지하고 속도 ≤ √(a_lat·R)
            RunCircle(vLimit, out var v1, out var rMin1, out var rMax1, out var ratio1, out var viol1);
            Assert.That(rMin1, Is.GreaterThan(4.5f));
            Assert.That(rMax1, Is.LessThan(5.6f));
            Assert.That(v1, Is.LessThanOrEqualTo(vLimit * 1.001f));
            Assert.That(viol1, Is.Zero, "마찰원·횡가속 위반");

            // 전력으로 원을 노리면 횡가속 한계 때문에 반경이 커진다: 속도는 언제나 실제 반경의 √(a_lat·R) 이하
            RunCircle(0f, out var v2, out var rMin2, out _, out var ratio2, out var viol2);
            Assert.That(rMin2, Is.GreaterThan(5f), "전력 주행은 5 m 원을 돌 수 없다");
            Assert.That(ratio2, Is.LessThanOrEqualTo(1.02f), "속도 ≤ √(a_lat·R_실제)");
            Assert.That(viol2, Is.Zero, "마찰원·횡가속 위반");
        }
    }
}
