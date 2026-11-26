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

        // ── 059 FR-34 · AC-05: 그물 접촉 큐 ──

        static GoalBox NetBox() =>
            GoalBox.Default(PitchSpec.Default);

        /// <summary>골(틱 0) + 공이 옆그물 → 뒤 그물에 닿는 장면 (접촉 2회).</summary>
        static (ReplayClip clip, HighlightData data) TwoContactGoal()
        {
            var data = NetContactDetectorTests.Frames(NetContactDetectorTests.InGoal(0.2f, 2.5f, 1.0f, 6f, 8f, 0f), 3f);
            var goal = SimEvent.Make(SimEventKind.Goal, 0, 8);
            goal.Tick = 0;
            goal.Seq = 7;
            return (Clip(new[] { Frame(0), Frame(1) }, goal), data);
        }

        [Test]
        public void NetCue_OnePerContact_Ordered()
        {
            var (clip, data) = TwoContactGoal();
            var contacts = new System.Collections.Generic.List<NetContact>();
            var cues = ClipConverter.ToCues(clip, data, NetBox(), contacts);
            var net = Array.FindAll(cues, c => c.Kind == NetContactDetector.CueKind);
            Assert.AreEqual(2, contacts.Count);
            Assert.AreEqual(2, net.Length, "접촉마다 큐 1개");
            for (int i = 0; i < net.Length; i++)
            {
                Assert.AreEqual(contacts[i].Time, net[i].Time, 1e-12);
                Assert.AreEqual(i, net[i].Entity, "엔티티 = 접촉 번호");
                Assert.AreEqual(7, net[i].Seq, "일련번호 = 직전 골");
            }
            for (int i = 1; i < cues.Length; i++) Assert.LessOrEqual(CueDispatcher.Compare(cues[i - 1], cues[i]), 0, "시뮬 시각 순");
        }

        /// <summary>3배속 한 프레임(≈ 0.1 s)에 접촉이 몰려도 빠짐없이 순서대로 (041 FR-55).</summary>
        [Test]
        public void NetCue_3x_NoneDropped()
        {
            var (clip, data) = TwoContactGoal();
            var cues = ClipConverter.ToCues(clip, data, NetBox());
            var d = new CueDispatcher();
            var sink = new FakeAudio();
            d.Reset(cues, 0d);
            for (double t = 0d; t <= 3.0d; t += 3d / 60d) d.FireUpTo(t, sink); // 60 fps × 3배속
            var net = sink.Cues.FindAll(c => c.Kind == NetContactDetector.CueKind);
            Assert.AreEqual(2, net.Count);
            Assert.Less(net[0].Time, net[1].Time);
        }

        [Test]
        public void FirstNetCue_EqualsNetContactTime()
        {
            var (clip, data) = TwoContactGoal();
            var contacts = new System.Collections.Generic.List<NetContact>();
            var cues = ClipConverter.ToCues(clip, data, NetBox(), contacts);
            var first = Array.Find(cues, c => c.Kind == NetContactDetector.CueKind);
            Assert.AreEqual(NetContactDetector.FirstAfter(contacts, ClipConverter.EventTime(clip.Events[0])), first.Time, 1e-12,
                "046 그물 소리·042 메인 샷 유지가 쓰는 첫 접촉 시각");
        }

        /// <summary>그물에 닿지 않는 골(골 안에서 멈춘 약한 공)·골라인 위 공: 그물 큐 0 (FR-51).</summary>
        [Test]
        public void NoNetGoal_NoCue()
        {
            var p = RoadToPro.Tests.EditMode.MatchEngine.Ball.BallTestKit.Params();
            var weak = NetContactDetectorTests.Frames(RoadToPro.MatchEngine.Ball.BallState.RollingAt(
                new RoadToPro.Core.Math.Vec2(0.5f, p.GoalCenterY), new RoadToPro.Core.Math.Vec2(-1.6f, 0f), p.Radius), 4f);
            var goal = SimEvent.Make(SimEventKind.Goal, 0, 8);
            var cues = ClipConverter.ToCues(Clip(new[] { Frame(0), Frame(1) }, goal), weak, NetBox());
            Assert.AreEqual(0, Array.FindAll(cues, c => c.Kind == NetContactDetector.CueKind).Length);
        }
    }
}
