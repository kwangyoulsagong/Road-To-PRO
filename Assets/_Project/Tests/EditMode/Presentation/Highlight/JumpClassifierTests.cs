using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>specs/058 FR-12: 속도 점프 원인 분류 — 배치(교대·재배치·기간 시작), 기록(프레임 건너뜀), 판단(나머지).</summary>
    public class JumpClassifierTests
    {
        static ReplayFrame Frame(long index, float x0, int id0 = 1)
        {
            var f = new ReplayFrame { Index = index, BallPhase = BallPhase.Rolling, BallOwnerSlot = -1 };
            for (int e = 0; e < ReplayFrame.Entities; e++)
            {
                f.Values[e * ReplayFrame.Stride] = 50f + e;
                f.Values[e * ReplayFrame.Stride + 1] = 30f;
                f.Hints[e] = MotionHint.Idle;
            }
            f.Values[0] = x0;
            for (int s = 0; s < 22; s++) f.PlayerIds[s] = s + 1;
            f.PlayerIds[0] = id0;
            return f;
        }

        static HighlightData Convert(ReplayFrame[] frames, params SimEvent[] events) =>
            ClipConverter.ToHighlight(new ReplayClip(frames[0].Index / 3, frames[frames.Length - 1].Index / 3 + 1, frames, events), PitchSpec.Default);

        [Test]
        public void Classify_ContinuousFrames_IsDecision()
        {
            var d = Convert(new[] { Frame(300, 10f), Frame(301, 10.2f), Frame(302, 13f), Frame(303, 13.2f) });
            Assert.That(JumpClassifier.Classify(d, 2, 0), Is.EqualTo(JumpCause.Decision));
        }

        [Test]
        public void Classify_FrameGap_IsRecord()
        {
            var d = Convert(new[] { Frame(300, 10f), Frame(301, 10.2f), Frame(303, 13f), Frame(304, 13.2f) });
            Assert.That(JumpClassifier.Classify(d, 2, 0), Is.EqualTo(JumpCause.Record));
            Assert.That(JumpClassifier.Label(JumpCause.Record), Is.EqualTo("기록"));
        }

        [Test]
        public void Classify_IdChange_IsPlacement()
        {
            var d = Convert(new[] { Frame(300, 10f), Frame(301, 10.2f), Frame(302, 10.2f, 99), Frame(303, 10.4f, 99) });
            Assert.That(JumpClassifier.Classify(d, 2, 0), Is.EqualTo(JumpCause.Placement));
            Assert.That(JumpClassifier.Classify(d, 2, 1), Is.EqualTo(JumpCause.Decision), "다른 슬롯은 영향 없음");
        }

        [Test]
        public void Classify_PeriodStartFrame_IsPlacementForAll_BallRelocationNot()
        {
            var ps = new SimEvent { Seq = 1, Tick = 101, Substep = SimEvent.TickStart, Kind = SimEventKind.PeriodStart, PlayerId = -1, OtherId = -1 };
            var rel = new SimEvent { Seq = 2, Tick = 100, Substep = SimEvent.TickEnd, Kind = SimEventKind.BallRelocated, PlayerId = -1, OtherId = -1 };
            // 기간 시작 틱 101 → 프레임 303 (시각 10.1 s)
            var d = Convert(new[] { Frame(300, 10f), Frame(301, 10f), Frame(302, 10f), Frame(303, 10f), Frame(304, 10f), Frame(305, 10f), Frame(306, 10f) }, ps, rel);
            Assert.That(JumpClassifier.Classify(d, 3, 5), Is.EqualTo(JumpCause.Placement));
            Assert.That(JumpClassifier.Classify(d, 5, 5), Is.EqualTo(JumpCause.Decision), "공 재배치는 선수 배치가 아니다");
        }
    }
}
