using System;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>재구성 클립 → 재생 입력 (specs/041 FR-02·03).</summary>
    public class ClipConverterTests
    {
        static readonly PitchSpec Pitch = PitchSpec.Default;

        static ReplayFrame Frame(long index)
        {
            var f = new ReplayFrame { Index = index };
            for (int i = 0; i < f.PlayerIds.Length; i++) f.PlayerIds[i] = i + 1;
            return f;
        }

        static void Put(ReplayFrame f, int e, float x, float y, float z, float yaw)
        {
            int o = e * ReplayFrame.Stride;
            f.Values[o] = x;
            f.Values[o + 1] = y;
            f.Values[o + 2] = z;
            f.Values[o + 3] = yaw;
        }

        static ReplayClip Clip(ReplayFrame[] frames, params SimEvent[] events) =>
            new ReplayClip(frames[0].Index / 3, frames[frames.Length - 1].Index / 3 + 1, frames, events);

        [Test]
        public void ToHighlight_Coordinates_MatchPitchConvention()
        {
            var a = Frame(30);
            var b = Frame(31);
            Put(a, 3, 60f, 40f, 0f, 0f);
            Put(a, ReplayFrame.BallEntity, 100f, 30f, 1.5f, 0f);
            var d = ClipConverter.ToHighlight(Clip(new[] { a, b }), Pitch);

            Assert.AreEqual(1f, d.StartTime, 1e-5f);
            Assert.AreEqual(60f - 52.5f, d.X[3], 1e-5f);
            Assert.AreEqual(40f - 34f, d.Z[3], 1e-5f);
            Assert.AreEqual(100f - 52.5f, d.Ball[0].X, 1e-5f);
            Assert.AreEqual(1.5f, d.Ball[0].Y, 1e-5f, "시뮬 z가 월드 높이");
            Assert.AreEqual(30f - 34f, d.Ball[0].Z, 1e-5f);
        }

        [Test]
        public void ToHighlight_OfficialsAfterBall_MapToEntity22()
        {
            var a = Frame(0);
            var b = Frame(1);
            Put(a, ReplayFrame.FirstOfficial, 52.5f, 34f, 0f, 0f);
            var d = ClipConverter.ToHighlight(Clip(new[] { a, b }), Pitch);
            Assert.AreEqual(0f, d.X[HighlightData.Players], 1e-5f);
            Assert.AreEqual(0f, d.Z[HighlightData.Players], 1e-5f);
        }

        [Test]
        public void ToHighlight_Yaw_ConvertsToAnimationConvention()
        {
            var a = Frame(0);
            var b = Frame(1);
            Put(a, 0, 0f, 0f, 0f, 0f);  // 시뮬 +x(길이) 방향 → 애니메이션 atan2(1, 0) = π/2
            Put(a, 1, 0f, 0f, 0f, (float)Math.PI * 0.5f);  // 시뮬 +y(폭) 방향 → 0
            var d = ClipConverter.ToHighlight(Clip(new[] { a, b }), Pitch);
            Assert.AreEqual(Math.PI * 0.5d, d.Yaw[0], 1e-5d);
            Assert.AreEqual(0d, d.Yaw[1], 1e-5d);
        }

        [Test]
        public void ToHighlight_YawAcrossPi_StaysContinuous()
        {
            var a = Frame(0);
            var b = Frame(1);
            Put(a, 0, 0f, 0f, 0f, (float)Math.PI - 0.01f);
            Put(b, 0, 0f, 0f, 0f, -(float)Math.PI + 0.01f);
            var d = ClipConverter.ToHighlight(Clip(new[] { a, b }), Pitch);
            Assert.AreEqual(0.02d, Math.Abs(d.Yaw[HighlightData.Entities] - d.Yaw[0]), 1e-4d);
        }

        [Test]
        public void ToHighlight_AbsentHint_NotPresent()
        {
            var a = Frame(0);
            var b = Frame(1);
            a.Hints[5] = MotionHint.Absent;
            var d = ClipConverter.ToHighlight(Clip(new[] { a, b }), Pitch);
            Assert.IsFalse(d.Present[5]);
            Assert.IsTrue(d.Present[HighlightData.Entities + 5]);
        }

        [Test]
        public void ToHighlight_ActionEvent_MapsPlayerIdToSlotAndNormalizedPosition()
        {
            var a = Frame(30);
            var b = Frame(31);
            var e = SimEvent.Make(SimEventKind.Pass, 0, 8, 9);
            e.Tick = 10;
            e.Substep = 5;
            e.X = 0.5f;
            e.Y = 0.25f;
            e.Z = 0.2f;
            e.Part = BodyPart.RightFoot;

            var d = ClipConverter.ToHighlight(Clip(new[] { a, b }, e), Pitch);

            Assert.AreEqual(1, d.Events.Count);
            Assert.AreEqual(1.05f, d.Events[0].Time, 1e-5f);
            Assert.AreEqual(7, d.Events[0].Entity);
            Assert.AreEqual(8, d.Events[0].Other);
            Assert.AreEqual(0f, d.Events[0].BallPos.X, 1e-5f);
            Assert.AreEqual(0.2f, d.Events[0].BallPos.Y, 1e-5f);
            Assert.AreEqual(-17f, d.Events[0].BallPos.Z, 1e-5f);
        }

        [Test]
        public void ToHighlight_NonActionEvent_NotAnimated()
        {
            var a = Frame(30);
            var b = Frame(31);
            var e = SimEvent.Make(SimEventKind.ScenarioStart, 0, 8);
            e.Tick = 10;
            var d = ClipConverter.ToHighlight(Clip(new[] { a, b }, e), Pitch);
            Assert.AreEqual(0, d.Events.Count);
        }

        [Test]
        public void ToHighlight_SingleFrame_Throws()
        {
            Assert.Throws<ArgumentException>(() => ClipConverter.ToHighlight(Clip(new[] { Frame(0) }), Pitch));
        }

        [Test]
        public void ToCues_MixedOrder_SortedBySimTime()
        {
            var a = Frame(30);
            var b = Frame(31);
            var e1 = SimEvent.Make(SimEventKind.Goal, 0, 8);
            e1.Tick = 11;
            e1.Seq = 2;
            var e2 = SimEvent.Make(SimEventKind.Shot, 0, 8);
            e2.Tick = 10;
            e2.Seq = 1;
            var cues = ClipConverter.ToCues(Clip(new[] { a, b }, e1, e2));
            Assert.AreEqual(1, cues[0].Seq);
            Assert.AreEqual((int)SimEventKind.Goal, cues[1].Kind);
        }
    }
}
