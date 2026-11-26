using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    /// <summary>
    /// specs/059 FR-30~33 · AC-06: 골 시퀀스 — 그물에 닿는 골은 첫 접촉 + 1.0 s 전에 클로즈업 컷 없음, 닿지 않는 골은 골 + 0.5 s,
    /// 어떤 경우도 골 + 1.5 s 안에 컷. 순서는 042 AC-04 그대로. 골대 뒤 하이앵글 리플레이는 접촉 순간 접촉점이 뷰포트 여백 5 % 안.
    /// 참조 경기(058 실경기 카메라 테스트와 같은 시드)를 쓴다.
    /// </summary>
    public class GoalNetSequenceTests
    {
        const double Frame = 1d / 30d;
        const float Aspect = 16f / 10f, Margin = 0.05f;
        static LiveHighlightKit.Built _built;
        static readonly object Gate = new object();

        static LiveHighlightKit.Built Built()
        {
            lock (Gate) return _built ??= LiveHighlightKit.Build(Reference());
        }

        static double FirstShot(CameraTrack t, ShotKind kind)
        {
            for (int f = 0; f < t.Frames; f++) if (t.Shots[f] == kind) return t.StartTime + f * Frame;
            return double.NaN;
        }

        static IEnumerable<(SceneBrief brief, CameraTrack track)> Goals()
        {
            foreach (var (_, brief, _, track) in Built().Tracks)
                if (brief.Role == SceneRole.GoalMain) yield return (brief, track);
        }

        [Test]
        public void NetGoal_NoCutBefore1s()
        {
            int n = 0;
            foreach (var (brief, track) in Goals())
            {
                if (double.IsNaN(brief.NetContactTime)) continue;
                n++;
                double cut = FirstShot(track, ShotKind.Closeup);
                double expect = System.Math.Min(brief.NetContactTime + 1.0d, brief.EventTime + 1.5d);
                Assert.GreaterOrEqual(cut, expect - Frame, $"골 {brief.SceneId}: 첫 그물 접촉 {brief.NetContactTime:F2} + 1.0 s 전에 클로즈업");
            }
            Assert.Greater(n, 0, "그물에 닿는 골이 참조 경기에 있어야 한다");
        }

        [Test]
        public void NoNetGoal_Cut05()
        {
            foreach (var (brief, track) in Goals())
            {
                if (!double.IsNaN(brief.NetContactTime)) continue;
                double cut = FirstShot(track, ShotKind.Closeup);
                Assert.AreEqual(brief.EventTime + 0.5d, cut, Frame + 1e-6, $"골 {brief.SceneId}: 그물 안 닿은 골은 골 + 0.5 s");
            }
        }

        [Test]
        public void CutWithin15()
        {
            foreach (var (brief, track) in Goals())
            {
                double cut = FirstShot(track, ShotKind.Closeup);
                Assert.IsFalse(double.IsNaN(cut), $"골 {brief.SceneId}: 클로즈업 없음");
                Assert.LessOrEqual(cut, brief.EventTime + 1.5d + Frame, $"골 {brief.SceneId}: 골 + 1.5 s 뒤 컷");
            }
        }

        /// <summary>메인 → 클로즈업 → 세리머니 순서 (042 AC-04, 058 테스트와 같은 판정).</summary>
        [Test]
        public void Order_Unchanged()
        {
            foreach (var (brief, track) in Goals())
            {
                var order = new List<ShotKind>();
                for (int f = 0; f < track.Frames; f++) if (order.Count == 0 || order[order.Count - 1] != track.Shots[f]) order.Add(track.Shots[f]);
                Assert.That(order, Is.EqualTo(new[] { ShotKind.Main, ShotKind.Closeup, ShotKind.Celebration }), $"골 {brief.SceneId}");
            }
        }

        /// <summary>골대 뒤 하이앵글 리플레이: 첫 그물 접촉 프레임에서 접촉점이 뷰포트 안쪽 5 % 여백 안 (FR-33).</summary>
        [Test]
        public void ReplayHigh_ContactInViewport5pct()
        {
            var box = GoalBox.Default(PitchSpec.Default);
            var contacts = new List<NetContact>();
            int checkedCount = 0;
            foreach (var (_, brief, data, track) in Built().Tracks)
            {
                if (brief.Role != SceneRole.ReplayAngle || GoalSequenceDirector.ReplayKind(brief) != ShotKind.ReplayHigh) continue;
                NetContactDetector.Find(data, box, NetContactSettings.Default, contacts);
                if (contacts.Count == 0) continue;
                var c = contacts[0];
                var sample = track.Sample(c.Time);
                if (sample.Shot != ShotKind.ReplayHigh) continue;
                checkedCount++;
                var vp = CameraMath.Viewport(sample.Pose, Aspect, sample.FovMin, sample.FovMax, c.Point);
                string tag = $"리플레이 {brief.SceneId} 접촉 {c.Time:F2} s ({vp.X:F3}, {vp.Y:F3}, z {vp.Z:F1})";
                Assert.Greater(vp.Z, 0f, tag + ": 카메라 뒤");
                Assert.That(vp.X, Is.InRange(Margin, 1f - Margin), tag);
                Assert.That(vp.Y, Is.InRange(Margin, 1f - Margin), tag);
            }
            Assert.Greater(checkedCount, 0, "그물 접촉이 있는 하이앵글 리플레이가 참조 경기에 있어야 한다");
        }
    }
}
