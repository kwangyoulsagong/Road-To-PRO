using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Highlight.Selection;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    /// <summary>specs/058 AC-05·06·10(표현), FR-22·60·62: 실경기 골 시퀀스 카메라 순서, 샷 안 연속성, 장면·카메라 결정론.</summary>
    public class LiveGoalSequenceTests
    {
        static LiveHighlightKit.Built _built;
        static readonly object Gate = new object();

        static LiveHighlightKit.Built Built()
        {
            lock (Gate) return _built ??= LiveHighlightKit.Build(Reference());
        }

        static List<ShotKind> Order(CameraTrack t)
        {
            var l = new List<ShotKind>();
            for (int f = 0; f < t.Frames; f++) if (l.Count == 0 || l[l.Count - 1] != t.Shots[f]) l.Add(t.Shots[f]);
            return l;
        }

        [Test]
        public void GoalAndConceded_CameraOrder()
        {
            var b = Built();
            int groups = 0;
            var teams = new HashSet<int>();
            for (int i = 0; i < b.Tracks.Count; i++)
            {
                var (scene, brief, data, track) = b.Tracks[i];
                if (brief.Role != SceneRole.GoalMain) continue;
                groups++;
                teams.Add(b.Briefs.InfoOf(scene.Id).Team);
                var main = Order(track);
                Assert.That(main, Is.EqualTo(new[] { ShotKind.Main, ShotKind.Closeup, ShotKind.Celebration }), $"골 장면 {scene.Id} 메인 순서");
                // 059 메인 샷 유지: 클로즈업은 골 + 0.5 s 이후 (그물 접촉이 있으면 접촉 + 1.0 s, 상한 1.5 s)
                int closeup = track.Shots.Length;
                for (int f = 0; f < track.Frames; f++) if (track.Shots[f] == ShotKind.Closeup) { closeup = f; break; }
                double closeupAt = track.StartTime + closeup / 30d;
                Assert.That(closeupAt, Is.GreaterThanOrEqualTo(brief.EventTime + 0.5d - 0.05d), "그물 출렁임 전에 클로즈업");
                Assert.That(closeupAt, Is.LessThanOrEqualTo(brief.EventTime + 1.5d + 0.05d));
                var kinds = new List<ShotKind>();
                bool tail = false;
                for (int j = i + 1; j < b.Tracks.Count && b.Tracks[j].scene.GroupId == scene.Id; j++)
                {
                    var r = b.Tracks[j];
                    if (r.brief.Role == SceneRole.ReplayAngle) kinds.Add(Order(r.track)[0]);
                    if (r.brief.Role == SceneRole.GoalTail)
                    {
                        tail = true;
                        var o = Order(r.track);
                        Assert.That(o[0], Is.EqualTo(ShotKind.ReactionWide));
                        if (!r.brief.EndWide) Assert.That(o[o.Count - 1], Is.EqualTo(ShotKind.KickoffWide));
                    }
                }
                Assert.That(kinds, Is.EqualTo(new[] { ShotKind.ReplayHigh, ShotKind.ReplayShooter, ShotKind.ReplayReverse }), $"골 {scene.Id} 리플레이 3개");
                if (!tail) Assert.That(TailSkipAllowed(b.Briefs.InfoOf(scene.Id).EventTick), Is.True, $"골 {scene.Id} 꼬리(반응 → 킥오프 와이드) 없음");
            }
            Assert.That(groups, Is.GreaterThan(0));
            Assert.That(teams.Count, Is.EqualTo(2), "참조 경기에 양 팀 골(실점 장면)이 있어야 한다");
        }

        /// <summary>FR-62(킥오프 뒤 60 s 안 다음 골) 또는 FR-60(종료 와이드 2.5 s 미만)이면 꼬리 생략이 맞다.</summary>
        static bool TailSkipAllowed(long goalTick)
        {
            var log = Reference().Log;
            long kickoff = -1;
            for (int i = 0; i < log.Count; i++)
            {
                var e = log[i];
                if (e.Tick <= goalTick) continue;
                if (kickoff < 0 && e.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.PeriodEnd) return true;
                if (kickoff < 0 && e.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Kickoff) kickoff = e.Tick;
                else if (kickoff >= 0 && e.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Goal) return (e.Tick - kickoff) * 0.1d <= new SceneScoreTable().ConsecutiveGoalSeconds;
            }
            return false;
        }

        [Test]
        public void KickoffWide_PlayersInOwnHalf_BallAtCentre()
        {
            var b = Built();
            foreach (var (scene, brief, data, track) in b.Tracks)
            {
                if (brief.Role != SceneRole.GoalTail || brief.EndWide) continue;
                int f = data.Frames - 2;
                var ball = data.Ball[f];
                Assert.That(System.Math.Abs(ball.X) < 1f && System.Math.Abs(ball.Z) < 1f, Is.True, $"꼬리 {scene.Id}: 공이 센터 스폿에 없다 {ball}");
            }
        }

        [Test]
        public void FullMatch_CameraWithinLimits()
        {
            var b = Built();
            var report = new ContinuityReport();
            var t = new CameraTuning();
            int offscreen = 0;
            foreach (var (_, _, _, track) in b.Tracks)
            {
                CameraContinuity.Measure(track, t.ReplayBlendSeconds * PlaybackSettings.ReplayRate, t.MaxAngularSpeed + 0.5f, t.MaxAngularAccel + 2f, t.MaxFovSpeed + 0.1f, report);
                offscreen += track.BallOffscreenDiagnostics;
            }
            TestContext.WriteLine($"공 화면 밖 0.5 s 초과 진단 {offscreen}건 (FR-33)");
            TestContext.WriteLine($"최대 {report.MaxAngularSpeed:0}°/s, {report.MaxAngularAccel:0}°/s², 화각 {report.MaxFovSpeed:0.0}°/s, 프레임 {report.Frames}");
            Assert.That(report.Violations, Is.EqualTo(0), string.Join("\n", report.Samples));
        }

        [Test, Category("Determinism")]
        public void SameSeed_SceneListAndCameraHash_Equal()
        {
            var a = Built();
            var s2 = Sim(ReferenceSeed, WithCheckpoints(RoadToPro.MatchEngine.Match.Replay.CheckpointMode.All));
            s2.RunToEnd();
            var c = LiveHighlightKit.Build(s2);
            Assert.That(c.Tracks.Count, Is.EqualTo(a.Tracks.Count));
            for (int i = 0; i < a.Tracks.Count; i++)
            {
                Assert.That(c.Tracks[i].scene.Start, Is.EqualTo(a.Tracks[i].scene.Start));
                Assert.That(BroadcastTestKit.Hash(c.Tracks[i].track), Is.EqualTo(BroadcastTestKit.Hash(a.Tracks[i].track)), $"장면 {a.Tracks[i].scene.Id} 카메라");
            }
        }
    }
}
