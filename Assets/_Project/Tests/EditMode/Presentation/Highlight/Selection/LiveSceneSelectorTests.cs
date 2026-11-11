using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Highlight.Selection;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Selection
{
    /// <summary>specs/058 FR-40~48, AC-02·07·08·17(장면): 이벤트 기반 장면 선정 1단계.</summary>
    public class LiveSceneSelectorTests
    {
        static SceneScoreTable T => new SceneScoreTable();
        static readonly PitchSpec Pitch = PitchSpec.Default;

        static LiveSceneList Real(MatchSim sim, int my = -1) => LiveSceneSelector.Select(sim.Result.EventLog, sim.Result.EndTick, Pitch, my, T);

        static int Moments(LiveSceneList l)
        {
            int n = 0;
            foreach (var s in l.Scenes) if (l.Info.TryGetValue(s.Id, out var i) && (i.Role == SceneRole.GoalMain || i.Role == SceneRole.Live)) n++;
            return n;
        }

        // ── 합성 이벤트 ──
        sealed class Ev
        {
            readonly List<SimEvent> _l = new List<SimEvent>();
            long _seq;
            public Ev() => Add(0, SimEventKind.PeriodStart, -1, -1, 0.5f, 0.5f);
            public Ev Add(double t, SimEventKind k, int team, int player, float x = 0.5f, float y = 0.5f, int a = 0, int other = -1, int period = 0)
            {
                _l.Add(new SimEvent { Seq = ++_seq, Tick = (long)Math.Round(t * 10), Substep = SimEvent.TickStart, Kind = k, Team = (sbyte)team, PlayerId = player, OtherId = other, X = x, Y = y, A = a, Period = (byte)period });
                return this;
            }
            public SimEvent[] Arr => _l.ToArray();
        }

        static LiveSceneList Syn(Ev e, double end = 5_400, int my = -1) => LiveSceneSelector.Select(e.Arr, (long)(end * 10), Pitch, my, T);

        // ── 실경기 ──

        [Test]
        public void ScoreTable_DefaultsEqualFile()
        {
            var reg = TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning", "highlight.json"));
            var f = SceneScoreTable.From(TuningBundle.Defaults(reg));
            var d = new SceneScoreTable();
            Assert.That(new object[] { f.VarOverturn, f.Goal, f.RedCard, f.Penalty, f.Woodwork, f.Save, f.ShotInBox, f.ShotOutBox, f.Defensive, f.YellowCard, f.Foul, f.MultLate, f.MultClose, f.MultMyPlayer, f.SceneMin, f.SceneMax, f.Before, f.After, f.GoalAfter, f.MinShot, f.SubSceneSeconds, f.ConsecutiveGoalSeconds, f.FreeKickZone, f.Filler },
                Is.EqualTo(new object[] { d.VarOverturn, d.Goal, d.RedCard, d.Penalty, d.Woodwork, d.Save, d.ShotInBox, d.ShotOutBox, d.Defensive, d.YellowCard, d.Foul, d.MultLate, d.MultClose, d.MultMyPlayer, d.SceneMin, d.SceneMax, d.Before, d.After, d.GoalAfter, d.MinShot, d.SubSceneSeconds, d.ConsecutiveGoalSeconds, d.FreeKickZone, d.Filler }));
        }

        [Test]
        public void AllGoalsIncluded_Count8to15_MinLength()
        {
            var sim = Reference();
            var l = Real(sim);
            foreach (var g in sim.Result.Goals)
            {
                double t = g.Tick * 0.1d;
                bool found = false;
                foreach (var s in l.Scenes)
                    if (!s.IsReplay && l.Info[s.Id].Role == SceneRole.GoalMain && s.Start <= t && t <= s.End) found = true;
                Assert.That(found, Is.True, $"골 {t:0.0}s 장면 없음");
            }
            if (sim.Result.Goals.Length <= 15) Assert.That(Moments(l), Is.InRange(8, 15));
            foreach (var s in l.Scenes)
                if (!s.IsReplay) Assert.That(s.End - s.Start, Is.GreaterThanOrEqualTo(2.5d - 1e-9), $"장면 {s.Id} 2.5 s 미만");
        }

        [Test]
        public void Relocation_NotInsideLiveScene()
        {
            var l = Real(Reference());
            Assert.That(l.Relocations.Length, Is.GreaterThan(0));
            foreach (var r in l.Relocations)
            {
                long rt = (long)Math.Round(r * 10);
                foreach (var s in l.Scenes)
                {
                    if (s.IsReplay) continue;
                    long from = (long)Math.Floor(s.Start * 10), to = (long)Math.Ceiling(s.End * 10) + 1; // SceneClipBuilder 구간
                    Assert.That(from <= rt && to > rt + 1, Is.False, $"장면 {s.Id} [{s.Start:0.0},{s.End:0.0}]가 재배치 {r:0.0}s 앞뒤를 함께 담는다");
                }
            }
        }

        [Test, Category("Determinism")]
        public void Deterministic_SameInputSameScenes()
        {
            var a = Real(Reference());
            var b = Real(Reference());
            Assert.That(a.Scenes.Length, Is.EqualTo(b.Scenes.Length));
            for (int i = 0; i < a.Scenes.Length; i++)
            {
                Assert.That(a.Scenes[i].Id, Is.EqualTo(b.Scenes[i].Id));
                Assert.That(a.Scenes[i].Start, Is.EqualTo(b.Scenes[i].Start));
                Assert.That(a.Scenes[i].End, Is.EqualTo(b.Scenes[i].End));
            }
        }

        [Test]
        public void ReferenceMatch_GoalGroup_ThreeReplaysAndTail()
        {
            var l = Real(Reference());
            int mains = 0;
            foreach (var s in l.Scenes)
            {
                var i = l.Info[s.Id];
                if (i.Role != SceneRole.GoalMain) continue;
                mains++;
                int replays = 0;
                foreach (var o in l.Scenes) if (o.GroupId == s.Id && l.Info[o.Id].Role == SceneRole.ReplayAngle) replays++;
                Assert.That(replays, Is.EqualTo(3), $"골 장면 {s.Id} 리플레이 {replays}");
            }
            Assert.That(mains, Is.GreaterThan(0));
        }

        // ── 합성 ──

        [Test]
        public void GoalsOver15_GoalsOnly()
        {
            var e = new Ev();
            for (int i = 0; i < 16; i++) e.Add(100 + i * 200, SimEventKind.Goal, i & 1, 10 + i).Add(140 + i * 200, SimEventKind.Kickoff, (i + 1) & 1, 20);
            e.Add(3_300, SimEventKind.WoodworkHit, 0, 5);
            var l = Syn(e);
            int goals = 0;
            foreach (var s in l.Scenes)
            {
                var i = l.Info[s.Id];
                Assert.That(i.Kind, Is.EqualTo(LiveSceneKind.Goal), "골이 15개를 넘으면 골만");
                if (i.Role == SceneRole.GoalMain) goals++;
            }
            Assert.That(goals, Is.EqualTo(16));
        }

        [Test]
        public void GoallessDraw_AtLeast8_TwoKinds()
        {
            var e = new Ev();
            // 슈팅 2(박스 밖), 수비 2, 나머지는 채움 후보
            e.Add(300, SimEventKind.Shot, 0, 9, 0.75f, 0.5f);
            e.Add(900, SimEventKind.Shot, 1, 109, 0.3f, 0.5f);
            e.Add(1_500, SimEventKind.Tackle, 0, 3, 0.15f, 0.4f);
            e.Add(2_100, SimEventKind.Interception, 1, 103, 0.85f, 0.4f);
            for (int i = 0; i < 12; i++) e.Add(2_400 + i * 200, SimEventKind.PassReceived, 0, 8, 0.8f, 0.5f);
            var l = Syn(e);
            Assert.That(Moments(l), Is.GreaterThanOrEqualTo(8));
            var kinds = new HashSet<LiveSceneKind>();
            foreach (var s in l.Scenes) kinds.Add(l.Info[s.Id].Kind);
            int real = 0;
            foreach (var k in new[] { LiveSceneKind.Save, LiveSceneKind.ShotInBox, LiveSceneKind.ShotOutBox, LiveSceneKind.Defensive, LiveSceneKind.Foul, LiveSceneKind.YellowCard })
                if (kinds.Contains(k)) real++;
            Assert.That(real, Is.GreaterThanOrEqualTo(2), "선방·슈팅·수비·파울 중 2종류 이상");
        }

        [Test]
        public void ShotSaveRebound_Merged_TwoReplays()
        {
            var e = new Ev();
            e.Add(1_000, SimEventKind.Shot, 0, 9, 0.9f, 0.5f).Add(1_001, SimEventKind.Save, 1, 101, 0.98f, 0.5f).Add(1_003, SimEventKind.Shot, 0, 10, 0.9f, 0.45f);
            var l = Syn(e);
            int live = 0, replays = 0;
            foreach (var s in l.Scenes)
            {
                var i = l.Info[s.Id];
                if (s.Start > 1_010 || s.End < 990) continue;
                if (i.Role == SceneRole.Live) { live++; Assert.That(s.Start <= 1_000 && s.End >= 1_003, Is.True); }
                if (i.Role == SceneRole.ReplayAngle) replays++;
            }
            Assert.That(live, Is.EqualTo(1), "슈팅 → 선방 → 리바운드는 한 장면");
            Assert.That(replays, Is.EqualTo(2), "박스 안 유효 슈팅 → 선방 리플레이 2");
        }

        [Test]
        public void ReplayCounts_Woodwork2_Goal3()
        {
            var e = new Ev();
            e.Add(500, SimEventKind.WoodworkHit, 0, 9, 0.99f, 0.5f);
            e.Add(1_500, SimEventKind.Shot, 1, 109, 0.05f, 0.5f).Add(1_501, SimEventKind.Goal, 1, 109, 0f, 0.5f).Add(1_540, SimEventKind.Kickoff, 0, 9);
            var l = Syn(e);
            foreach (var s in l.Scenes)
            {
                var i = l.Info[s.Id];
                if (i.Role != SceneRole.Live && i.Role != SceneRole.GoalMain) continue;
                int r = 0;
                foreach (var o in l.Scenes) if (o.GroupId == s.Id && o.IsReplay) r++;
                if (i.Kind == LiveSceneKind.Woodwork) Assert.That(r, Is.EqualTo(2));
                if (i.Kind == LiveSceneKind.Goal) Assert.That(r, Is.EqualTo(3));
            }
        }

        [Test]
        public void VarOverturnedGoal_IsVarScene_NotGoal()
        {
            var e = new Ev();
            e.Add(1_000, SimEventKind.Shot, 0, 9, 0.9f, 0.5f).Add(1_001, SimEventKind.Goal, 0, 9, 1f, 0.5f);
            e.Add(1_060, SimEventKind.VarOverturn, 0, -1).Add(1_060, SimEventKind.DeadBallPhase, -1, -1, a: 4);
            var l = Syn(e);
            bool var = false;
            foreach (var s in l.Scenes)
            {
                var i = l.Info[s.Id];
                Assert.That(i.Kind, Is.Not.EqualTo(LiveSceneKind.Goal), "취소된 골은 골 장면이 아니다 (018)");
                if (i.Kind == LiveSceneKind.VarOverturn && i.Role == SceneRole.Live)
                {
                    var = true;
                    Assert.That(s.Start <= 1_001 && s.End >= 1_060, Is.True, "골 순간부터 번복까지");
                    int r = 0;
                    foreach (var o in l.Scenes) if (o.GroupId == s.Id && o.IsReplay) r++;
                    Assert.That(r, Is.EqualTo(2), "VAR 번복 즉시 리플레이 2");
                }
            }
            Assert.That(var, Is.True, "VAR 번복 장면(필수) 없음");
        }

        [Test]
        public void ConsecutiveGoals_FirstTailSkipped()
        {
            var e = new Ev();
            e.Add(1_000, SimEventKind.Goal, 0, 9).Add(1_025, SimEventKind.DeadBallPhase, -1, -1, a: 3).Add(1_026, SimEventKind.Kickoff, 1, 109);
            e.Add(1_060, SimEventKind.Goal, 1, 109).Add(1_085, SimEventKind.DeadBallPhase, -1, -1, a: 3).Add(1_086, SimEventKind.Kickoff, 0, 9);
            var l = Syn(e);
            var tails = new List<LiveSceneInfo>();
            foreach (var s in l.Scenes) if (l.Info[s.Id].Role == SceneRole.GoalTail) tails.Add(l.Info[s.Id]);
            Assert.That(tails.Count, Is.EqualTo(1), "연속 골은 앞 골 꼬리 생략");
            Assert.That(tails[0].EventTick, Is.EqualTo(10_600L));
        }

        [Test]
        public void GoalTail_EndsAtKickoff_AfterGoalSeven()
        {
            var e = new Ev();
            e.Add(1_000, SimEventKind.Goal, 0, 9).Add(1_024, SimEventKind.DeadBallPhase, -1, -1, a: 3).Add(1_025, SimEventKind.Kickoff, 1, 109);
            var l = Syn(e);
            foreach (var s in l.Scenes)
                if (l.Info[s.Id].Role == SceneRole.GoalTail)
                {
                    Assert.That(s.End, Is.EqualTo(1_024.8d).Within(1e-6), "킥 0.2 s 전에 끝난다");
                    Assert.That(s.Start, Is.GreaterThanOrEqualTo(1_007d));
                    return;
                }
            Assert.Fail("꼬리 없음");
        }

        [Test]
        public void MyPlayer_SubInAndOut_ScenesPresent()
        {
            var e = new Ev();
            e.Add(3_600, SimEventKind.Substitution, 0, 13, other: 9, period: 1);
            e.Add(4_000, SimEventKind.Shot, 0, 13, 0.08f, 0.5f, period: 1); // 후반 홈은 −x 방향 공격
            e.Add(4_500, SimEventKind.Substitution, 0, 14, other: 13, period: 1);
            var l = Syn(e, my: 13);
            bool sin = false, sout = false, shot = false;
            foreach (var s in l.Scenes)
            {
                var i = l.Info[s.Id];
                if (i.Kind == LiveSceneKind.SubIn) { sin = true; Assert.That(s.Start, Is.EqualTo(3_600d).Within(1e-6)); }
                if (i.Kind == LiveSceneKind.SubOut) { sout = true; Assert.That(s.End, Is.LessThanOrEqualTo(4_501d)); }
                if (i.Kind == LiveSceneKind.ShotInBox) { shot = true; Assert.That(i.MyPlayerInvolved, Is.True); }
            }
            Assert.That(sin && sout && shot, Is.True, $"투입 {sin} 아웃 {sout} 슈팅 {shot}");
        }
    }
}
