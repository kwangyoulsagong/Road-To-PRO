using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-02·03: 정지 출발 직선 스프린트 (F-V 선형 · 모노-지수, FR-10).</summary>
    public class SprintTests
    {
        static readonly float[] HaugenSplits = { 2.01f, 3.24f, 4.39f, 5.51f };

        /// <summary>움직이기 시작한 시각부터 10/20/30/40 m 통과 시각 (plan [제안] ③: Haugen은 첫 동작 보정 포함).</summary>
        static float[] Splits(int ability)
        {
            var k = One(KinematicAttributes.Uniform(ability), new Vec2(10f, 34f), default, new Vec2(1f, 0f));
            k.Monitor = new MotionLimitMonitor();
            k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 34f)));
            var res = new float[4];
            int mark = 0;
            float prev = 10f;
            for (int i = 1; i < 1000 && mark < 4; i++)
            {
                k.Step();
                var x = k.State(0).Position.X;
                var goal = 10f + 10f * (mark + 1);
                if (x >= goal)
                {
                    res[mark++] = (i - 1 + (goal - prev) / (x - prev)) * k.Params.Dt;
                }
                prev = x;
            }
            Assert.That(k.Monitor.TotalViolations, Is.Zero, "운동 한계 초과");
            return res;
        }

        [TestCase(15)]
        [TestCase(16)]
        public void StandingStart_SplitsMatchHaugen(int ability)
        {
            var s = Splits(ability);
            for (int m = 0; m < 4; m++)
                Assert.That(s[m], Is.EqualTo(HaugenSplits[m]).Within(0.1f), $"{(m + 1) * 10} m 스플릿 (능력치 {ability})");
        }

        [Test]
        public void TimeTo95Percent_Is3Tau()
        {
            var k = One(KinematicAttributes.Uniform(15), new Vec2(5f, 34f), default, new Vec2(1f, 0f));
            k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(108f, 34f)));
            var pr = k.Profile(0);
            int i = 0;
            while (k.State(0).Velocity.Length() < 0.95f * pr.MaxSpeed && i < 2000) { k.Step(); i++; }
            Assert.That(i * k.Params.Dt, Is.EqualTo(3f * pr.Tau).Within(0.1f * 3f * pr.Tau));
        }

        [Test]
        public void FasterPlayer_PullsAheadOver20m()
        {
            // 사용자 시나리오 1: 스피드 18 윙어 vs 스피드 10 수비수
            var fast = One(Attr(18), new Vec2(10f, 30f), default, new Vec2(1f, 0f));
            var slow = One(Attr(10), new Vec2(10f, 30f), default, new Vec2(1f, 0f));
            fast.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 30f)));
            slow.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 30f)));
            float tf = 0, ts = 0;
            for (int i = 1; i < 1000 && (tf == 0 || ts == 0); i++)
            {
                fast.Step(); slow.Step();
                if (tf == 0 && fast.State(0).Position.X >= 30f) tf = i * 0.01f;
                if (ts == 0 && slow.State(0).Position.X >= 30f) ts = i * 0.01f;
            }
            Assert.That(ts - tf, Is.GreaterThan(0.2f), "20 m 구간에서 눈에 띄는 차이");
        }
    }
}
