using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-16: 운동 한계 자가 검사 (FR-52). 실제 90분 1,000경기는 017 (plan [제안] ⑤).</summary>
    public class MotionLimitTests
    {
        /// <summary>합성 경기 1,000개 × 5분 (22명, 고정 시드 무작위 명령·접촉) — "운동 한계 초과" 0건.</summary>
        [Test, Timeout(1_200_000)] // Mono 에디터에서 수 분 걸린다 (5분 × 1,000경기 = 3천만 서브스텝)
        public void SyntheticMatches1000x5min_NoViolation()
        {
            var monitor = new MotionLimitMonitor();
            for (int m = 0; m < 1000; m++)
            {
                var rng = new SeedRng(10_000UL + (ulong)m);
                var k = new KinematicsSystem(Params, RandomSquad(rng));
                k.Monitor = monitor;
                RandomCommands(k, rng, immediate: true);
                for (int t = 0; t < 5 * 60 * 10; t++)
                {
                    if (t % 10 == 0) RandomCommands(k, rng);
                    for (int s = 0; s < 10; s++) k.Step();
                }
                if (monitor.Failed)
                {
                    var v = monitor[0];
                    Assert.Fail($"경기 {m}: 서브스텝 {v.Substep} 선수 {v.PlayerId} {v.Kind} = {v.Value} > {v.Limit} (총 {monitor.TotalViolations}건)");
                }
            }
        }

        [Test]
        public void ForcedAccelOverCap_ReportedExactly_Fails()
        {
            var a = KinematicAttributes.Uniform(12);
            var k = new KinematicsSystem(Params, new[] { new PlayerSetup(5, a, new Vec2(30f, 30f)), new PlayerSetup(7, a, new Vec2(60f, 30f)) });
            k.Monitor = new MotionLimitMonitor();
            k.SetCommandImmediate(5, SteeringCommand.Seek(new Vec2(90f, 30f)));
            k.SetCommandImmediate(7, SteeringCommand.Seek(new Vec2(90f, 40f)));
            for (int i = 0; i < 37; i++) k.Step();
            Assert.That(k.Monitor.Failed, Is.False);
            var v = k.State(1).Velocity;
            k.InjectVelocityFault(7, v + v.Normalized() * 0.5f); // 한 서브스텝에 +0.5 m/s = 50 m/s²
            k.Step();
            for (int i = 0; i < 20; i++) k.Step();
            Assert.That(k.Monitor.Failed, Is.True, "검증 실패로 판정");
            // 그 서브스텝·그 선수만 보고된다 (종가속 초과 + 같은 값 때문에 마찰원 초과도 함께)
            Assert.That(k.Monitor.Count, Is.InRange(1, 2));
            for (int i = 0; i < k.Monitor.Count; i++)
            {
                Assert.That(k.Monitor[i].Substep, Is.EqualTo(37));
                Assert.That(k.Monitor[i].PlayerId, Is.EqualTo(7));
            }
            Assert.That(k.Monitor[0].Kind, Is.EqualTo(MotionLimitKind.LongAccel));
            Assert.That(k.Monitor[0].Value, Is.GreaterThan(40f));
        }

        [Test]
        public void ContactLoss_IsNotAViolation_ButDirectSpeedCutIs()
        {
            var a = KinematicAttributes.Uniform(3);
            var k = One(a, new Vec2(30f, 30f), new Vec2(7f, 0f), new Vec2(1f, 0f));
            k.Monitor = new MotionLimitMonitor();
            k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 30f)));
            k.ApplyContact(1);
            Assert.That(k.State(0).Velocity.X, Is.EqualTo(7f * (1f - k.Profile(0).ContactLoss)).Within(1e-4f));
            Run(k, 50);
            Assert.That(k.Monitor.Failed, Is.False, "접촉 손실은 008 판정 결과라 한계 검사 대상 아님");
        }
    }
}
