using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Referee;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 FR-80: 주심 대각선 목표·공 거리 15~20 m, 부심 = 두 번째 수비수/공 중 골라인 쪽, 체력 속도.</summary>
    public class OfficialsMotionTests
    {
        const float L = 105f, W = 68f;

        [Test]
        public void Referee_Diagonal_Distance15to20()
        {
            var ball = new Vec2(70f, 10f);
            var t = OfficialsMotion.RefereeTarget(P, ball, L, W);
            var d = (t - ball).Length();
            Assert.That(d, Is.InRange(15f - 1e-3f, 20f + 1e-3f));
            // 대각선(0,0)→(L,W) 쪽으로 이동: 공보다 대각선에 가까워야
            float DistToDiag(Vec2 p) => DMath.Abs(p.X * W - p.Y * L) / new Vec2(L, W).Length();
            Assert.Less(DistToDiag(t), DistToDiag(ball));
            var onDiag = OfficialsMotion.RefereeTarget(P, new Vec2(52.5f, 34f), L, W);
            Assert.That((onDiag - new Vec2(52.5f, 34f)).Length(), Is.InRange(15f - 1e-3f, 20f + 1e-3f), "공이 대각선 위여도 비켜선다");
        }

        [Test]
        public void Assistants_FollowSecondLastOrBall()
        {
            var hi = OfficialsMotion.AssistantHigh(P, 90f, 70f, L);
            Assert.AreEqual(new Vec2(90f, -P.AssistantOffset), hi, "두 번째 수비수가 골라인 쪽");
            Assert.AreEqual(95f, OfficialsMotion.AssistantHigh(P, 90f, 95f, L).X, "공이 더 깊으면 공");
            Assert.AreEqual(L * 0.5f, OfficialsMotion.AssistantHigh(P, 30f, 20f, L).X, "담당 반 밖 → 하프라인");
            var lo = OfficialsMotion.AssistantLow(P, 15f, 30f, L, W);
            Assert.AreEqual(new Vec2(15f, W + P.AssistantOffset), lo);
        }

        [Test]
        public void Speed_ByFitness()
        {
            Assert.AreEqual(P.RefSpeedMin, OfficialsMotion.MaxSpeed(P, 0f), 1e-5f);
            Assert.AreEqual(P.RefSpeedMax, OfficialsMotion.MaxSpeed(P, 1f), 1e-5f);
            Assert.Greater(OfficialsMotion.MaxSpeed(P, 0.8f), OfficialsMotion.MaxSpeed(P, 0.2f));
        }
    }
}
