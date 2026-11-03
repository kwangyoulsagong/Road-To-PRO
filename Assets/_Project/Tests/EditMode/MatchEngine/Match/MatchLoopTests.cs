using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-01: 대체 단계만으로 11 vs 11 경기가 킥오프부터 종료까지 돌고, 결과에 스코어와 이벤트 로그가 있다. FR-70 조건 실행.</summary>
    public class MatchLoopTests
    {
        [Test]
        public void StandInMatch_RunsToEnd_ScoreAndLog()
        {
            var sim = Reference();
            var r = sim.Result;
            Assert.That(sim.IsFinished);
            Assert.That(r, Is.Not.Null);
            Assert.That(r.Reason, Is.EqualTo(EndReason.Normal));
            Assert.That(sim.NextTick, Is.GreaterThanOrEqualTo(54_000L), "90분 이상");
            Assert.That(r.EventLog, Is.Not.Null.And.Not.Empty);
            Assert.That(r.EventLog[0].Kind, Is.EqualTo(SimEventKind.PeriodStart));
            Assert.That(r.EventLog[r.EventLog.Length - 1].Kind, Is.EqualTo(SimEventKind.MatchEnd));
            Assert.That(r.EventLog[r.EventLog.Length - 1].A, Is.EqualTo(r.Score(0)));
            Assert.That(r.EventLog[r.EventLog.Length - 1].B, Is.EqualTo(r.Score(1)));
            Assert.That(Count(sim, SimEventKind.Kickoff), Is.GreaterThanOrEqualTo(2));
            Assert.That(Count(sim, SimEventKind.Pass), Is.GreaterThan(100), "단순 패스");
            Assert.That(Count(sim, SimEventKind.Shot), Is.GreaterThan(0), "단순 슈팅");
            Assert.That(r.Goals.Length, Is.EqualTo(r.Score(0) + r.Score(1)));
            Assert.That(r.Validate(), Is.Empty);
            Assert.That(r.Players.Length, Is.EqualTo(36));
            foreach (var p in r.Players)
                if (p.FinalStatus == PlayerStatus.OnPitch) Assert.That(p.PlayedTicks, Is.EqualTo(r.EndTick + 1), "선발 풀타임 = 경기 틱 수");
        }

        [Test]
        public void RunUntil_GoalCondition_StopsAtThatTick()
        {
            var sim = Sim(MatchTestKit.ReferenceSeed);
            var idx = MatchRunner.RunUntil(sim, new EventCondition(SimEventKind.Goal));
            Assert.That(idx, Is.GreaterThanOrEqualTo(0));
            var goal = sim.Log[idx];
            Assert.That(goal.Kind, Is.EqualTo(SimEventKind.Goal));
            Assert.That(sim.NextTick, Is.EqualTo(goal.Tick + 1));
            Assert.That(FirstIndex(sim, SimEventKind.Goal), Is.EqualTo(idx), "첫 골에서 멈춤");
            Assert.That(goal.Seq, Is.EqualTo(Reference().Result.Goals[0].Seq), "참조 경기 첫 골과 같다");
            // 구역 조건: 상대 진영 패스
            var sim2 = Sim(MatchTestKit.ReferenceSeed);
            var i2 = MatchRunner.RunUntil(sim2, new EventCondition(SimEventKind.Pass, -1, 0.75f, 0f, 1f, 1f));
            Assert.That(sim2.Log[i2].X, Is.GreaterThanOrEqualTo(0.75f));
        }
    }
}
