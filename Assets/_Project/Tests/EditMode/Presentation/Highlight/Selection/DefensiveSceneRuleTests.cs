using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Highlight.Selection;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Selection
{
    /// <summary>specs/081 AC-05 (FR-30): 경기에 수비 액션 후보가 있으면 하이라이트에 수비 장면이 최소 1개 (고정 이벤트 로그).</summary>
    public class DefensiveSceneRuleTests
    {
        static readonly PitchSpec Pitch = PitchSpec.Default;
        static SceneScoreTable T => new SceneScoreTable();

        sealed class Ev
        {
            readonly List<SimEvent> _l = new List<SimEvent>();
            long _seq;
            public Ev() => Add(0, SimEventKind.PeriodStart, -1, -1);
            public Ev Add(double t, SimEventKind k, int team, int player, float x = 0.5f, float y = 0.5f, int a = 0)
            {
                _l.Add(new SimEvent { Seq = ++_seq, Tick = (long)Math.Round(t * 10), Substep = SimEvent.TickStart, Kind = k, Team = (sbyte)team, PlayerId = player, OtherId = -1, X = x, Y = y, A = a });
                return this;
            }
            public SimEvent[] Arr => _l.ToArray();
        }

        static LiveSceneList Select(Ev e) => LiveSceneSelector.Select(e.Arr, 54_000, Pitch, -1, T);

        /// <summary>팀 0(전반 +x 공격)의 상대 박스 안 슈팅 n개, 300 s 간격.</summary>
        static Ev Shots(int n, double start = 100)
        {
            var e = new Ev();
            for (int i = 0; i < n; i++) e.Add(start + i * 300, SimEventKind.Shot, 0, 9, 0.95f, 0.5f);
            return e;
        }

        static bool Covers(LiveSceneList l, double t, SceneRole? role = null)
        {
            foreach (var s in l.Scenes)
                if (!s.IsReplay && s.Start <= t && t <= s.End && (role == null || l.Info[s.Id].Role == role)) return true;
            return false;
        }

        static int Live(LiveSceneList l)
        {
            var n = 0;
            foreach (var s in l.Scenes) if (!s.IsReplay) n++;
            return n;
        }

        [Test]
        public void DefensiveRule_CandidatePresent_AtLeastOne()
        {
            var e = Shots(16);
            e.Add(5_000, SimEventKind.Tackle, 1, 104, 0.9f, 0.5f); // 팀 1 자기 진영 1/3 태클 (점수 25 < 슈팅 35)
            var l = Select(e);
            Assert.IsTrue(Covers(l, 5_000), "수비 후보가 있으면 수비 장면 최소 1개");
            var kinds = 0;
            foreach (var kv in l.Info) if (kv.Value.Kind == LiveSceneKind.Defensive) kinds++;
            Assert.AreEqual(1, kinds, "규칙으로 넣은 장면의 대표 사건 = 수비 액션");
            Assert.AreEqual(T.SceneMax, Live(l), "장면 상한 안에서 바꾼다");
        }

        [Test]
        public void DefensiveRule_NoCandidate_Unchanged()
        {
            var e = Shots(16);
            e.Add(5_000, SimEventKind.Tackle, 1, 104, 0.3f, 0.5f); // 상대 진영 태클 = 수비 후보 아님
            var a = Select(Shots(16));
            var b = Select(e);
            Assert.AreEqual(Live(a), Live(b));
            Assert.IsFalse(Covers(b, 5_000), "수비 후보가 없으면 규칙이 적용되지 않는다 (빈 장면을 만들지 않는다)");
        }

        [Test]
        public void DefensiveRule_AlreadyPresent_Unchanged()
        {
            var e = Shots(5);
            e.Add(4_000, SimEventKind.Interception, 1, 105, 0.9f, 0.5f);
            e.Add(5_000, SimEventKind.Tackle, 1, 104, 0.9f, 0.5f);
            var l = Select(e);
            Assert.IsTrue(Covers(l, 5_000) && Covers(l, 4_000), "이미 들어간 수비 장면은 그대로");
            for (int i = 0; i < 5; i++) Assert.IsTrue(Covers(l, 100 + i * 300), $"슈팅 {i} 장면 유지");
        }

        [Test]
        public void DefensiveRule_NeverRemovesGoalOrMust()
        {
            var e = new Ev();
            for (int i = 0; i < 14; i++) e.Add(100 + i * 300, SimEventKind.Goal, 0, 9, 0.99f, 0.5f);
            e.Add(4_400, SimEventKind.Shot, 0, 9, 0.95f, 0.5f);
            e.Add(4_700, SimEventKind.Shot, 0, 9, 0.95f, 0.5f);
            e.Add(5_000, SimEventKind.Tackle, 1, 104, 0.9f, 0.5f);
            var l = Select(e);
            for (int i = 0; i < 14; i++) Assert.IsTrue(Covers(l, 100 + i * 300, SceneRole.GoalMain), $"골 {i} 장면은 빠지지 않는다");
            Assert.IsTrue(Covers(l, 5_000), "비필수(슈팅) 장면과 바꿔 수비 장면");
        }
    }
}
