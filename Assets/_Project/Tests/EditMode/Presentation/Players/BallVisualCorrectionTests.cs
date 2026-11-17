using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Logic.Motion;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// specs/082 FR-02·05 · AC-04: 화면 공 보정은 접촉 직전에만 들어가고 접촉 프레임에서는 0(화면 공 = 시뮬 공, 043 접촉 정확도).
    /// 그래서 082 실경기 진단의 접촉 프레임 거리(DistanceAtContact)는 시뮬 공 거리(DistanceRaw)와 같아야 하고, FR-02는 그 값으로 판정한다.
    /// (플랜의 "접촉 프레임까지 연속 보정"은 043 ContactAccuracyTests 기준 ±5 cm와 충돌해 취소 — tasks T07 결과 줄)
    /// </summary>
    public class BallVisualCorrectionTests
    {
        const float C = 5f;
        static readonly Vec3 R = new Vec3(0f, 0.1f, 0f);

        [Test]
        public void AtContact_EqualsSimBall()
        {
            Assert.AreEqual(Vec3.Zero, BallVisualCorrection.Offset(R, C, C), "접촉 프레임 = 시뮬 공");
            Assert.AreEqual(Vec3.Zero, BallVisualCorrection.Offset(R, C, C + 0.05f), "접촉 뒤");
            Assert.Greater(BallVisualCorrection.Offset(R, C, C - 0.01f).Y, 0.09f, "접촉 직전에는 거의 최대");
            Assert.AreEqual(Vec3.Zero, BallVisualCorrection.Offset(R, C, C - BallVisualCorrection.BallPullTime));
        }

        /// <summary>082 T07b: 접촉 시각이 30 Hz 프레임 경계와 같아도(긴 경기 시각의 float 오차) 접촉 키 = 이벤트 공 위치.</summary>
        [Test]
        public void ContactKeys_EventOnFrameBoundary_ReturnsEventBall()
        {
            for (int k = 0; k < 600; k++)
            {
                var d = new HighlightData(300f + k * 0.1f, 61);
                for (int f = 0; f < d.Frames; f++) d.Ball[f] = new Vec3(0f, 2f - f * 0.05f, 0f);
                float t = (float)(d.StartTime + 42 / 30.0); // ClipConverter처럼 double → float
                var ev = new Vec3(0.1f, 1.7f, 0.2f);
                d.Events.Add(new AnimEvent(t, 1, RoadToPro.MatchEngine.Match.Events.SimEventKind.Clearance, 0, -1,
                    RoadToPro.MatchEngine.Match.Events.BodyPart.Head, ActionOutcome.Success, ev, Vec3.Zero, AnimEvent.FromSim));
                var keys = new ContactKeys(d);
                Assert.AreEqual(0f, (keys.Ball(t) - ev).Length(), 1e-5f, $"start {d.StartTime}");
            }
        }

        /// <summary>082 T07b: 다음 터치의 보정은 앞 접촉 키 뒤에서 시작 — 앞 접촉 프레임에서 0.</summary>
        [Test]
        public void WindowStartsAfterPreviousContact()
        {
            const float prev = C - 0.05f;
            Assert.AreEqual(Vec3.Zero, BallVisualCorrection.Offset(R, C, prev, prev), "앞 접촉 프레임");
            Assert.Greater(BallVisualCorrection.Offset(R, C, C - 0.01f, prev).Y, 0.05f, "접촉 직전에는 들어간다");
            Assert.AreEqual(BallVisualCorrection.Offset(R, C, C - 0.05f), BallVisualCorrection.Offset(R, C, C - 0.05f, float.NegativeInfinity), "앞 접촉 없으면 기존과 같다");
        }

        [Test]
        public void ClampedTo15cm()
        {
            var big = BallVisualCorrection.Offset(new Vec3(0, 0.5f, 0), C, C - 0.001f);
            Assert.LessOrEqual(big.Length(), BallVisualCorrection.BallPullMax + 1e-5f);
            Assert.Greater(big.Length(), BallVisualCorrection.BallPullMax * 0.99f);
        }
    }
}
