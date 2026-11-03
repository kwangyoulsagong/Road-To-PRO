using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-05 킥오프·진영, AC-07 7명 미만 중단.</summary>
    public class MatchFlowTests
    {
        [Test]
        public void Kickoff_AfterGoal_ConcedingTeam()
        {
            var sim = Reference();
            var log = sim.Log;
            int checkedGoals = 0;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind != SimEventKind.Goal) continue;
                var k = FirstIndex(sim, SimEventKind.Kickoff, i + 1);
                var pe = FirstIndex(sim, SimEventKind.PeriodEnd, i + 1);
                if (k < 0 || (pe >= 0 && pe < k)) continue; // 기간 끝 직전 골은 다음 기간 규칙
                Assert.That(log[k].Team, Is.EqualTo(1 - log[i].Team), $"골 #{log[i].Seq} 뒤 킥오프는 실점 팀");
                checkedGoals++;
            }
            Assert.That(checkedGoals, Is.GreaterThan(0));
        }

        [Test]
        public void OwnGoal_ConcedingTeamKicksOff()
        {
            // 홈 선수가 자기 골문(전반 x = 0)으로 굴린 공
            var sim = Scripted(ctx =>
            {
                if (ctx.Tick == 0) ctx.Flow.OnRestartTaken();
                if (ctx.Tick == 20)
                {
                    ctx.Ball = BallState.RollingAt(new Vec2(1.5f, 31.5f), new Vec2(-8f, 0f), ctx.BallParams.Radius);
                    ctx.LastTouchTeam = 0;
                    ctx.LastTouchSlot = 3;
                }
            });
            sim.StepTicks(40);
            var goals = Events(sim, SimEventKind.Goal);
            Assert.That(goals.Count, Is.EqualTo(1));
            Assert.That(goals[0].Team, Is.EqualTo(1), "득점 팀은 원정");
            Assert.That(goals[0].A, Is.EqualTo(1), "자책골 표시");
            Assert.That(goals[0].PlayerId, Is.EqualTo(sim.Context.Squad.PlayerAt(3)));
            Assert.That(sim.Context.Flow.Restart, Is.EqualTo(RestartKind.Kickoff));
            Assert.That(sim.Context.Flow.RestartTeam, Is.EqualTo(0), "실점 팀(자책한 선수의 팀)이 킥오프");
            Assert.That(sim.Context.Flow.Goals(1), Is.EqualTo(1));
        }

        [Test]
        public void SecondHalf_OtherTeamKicksOff_SidesSwap()
        {
            var sim = Reference();
            var starts = Events(sim, SimEventKind.PeriodStart);
            Assert.That(starts.Count, Is.EqualTo(2));
            Assert.That(starts[1].B, Is.EqualTo(1 - starts[0].B), "후반 킥오프는 반대 팀");
            var kickoffs = Events(sim, SimEventKind.Kickoff);
            Assert.That(kickoffs[0].Team, Is.EqualTo(starts[0].B));

            var s2 = Sim(MatchTestKit.ReferenceSeed);
            Assert.That(s2.View.AttackDir(0), Is.EqualTo(1));
            for (int slot = 1; slot < 11; slot++) Assert.That(s2.View.SlotState(slot).Position.X, Is.LessThan(52.5f), "전반 홈은 x 작은 쪽");
            s2.RunUntilTick(starts[1].Tick + 1);
            Assert.That(s2.View.Period, Is.EqualTo(1));
            Assert.That(s2.View.AttackDir(0), Is.EqualTo(-1), "후반 진영 교체");
            Assert.That(s2.View.SlotState(0).Position.X, Is.GreaterThan(90f), "홈 골키퍼가 반대쪽 골문");
        }

        [Test]
        public void BelowSeven_AbandonsSameTick()
        {
            var script = new RefereeScript();
            for (int i = 1; i <= 5; i++) script.Add(new RefereeAction(500, RefereeActionKind.SendOff, i));
            var sim = Sim(31UL, new MatchSimOptions { Referee = script });
            var r = sim.RunToEnd();
            Assert.That(r.Reason, Is.EqualTo(EndReason.Abandoned));
            Assert.That(r.EndTick, Is.EqualTo(500L));
            var below = Events(sim, SimEventKind.PlayersBelowMinimum);
            Assert.That(below.Count, Is.EqualTo(1));
            Assert.That(below[0].Tick, Is.EqualTo(500L));
            Assert.That(below[0].A, Is.EqualTo(6));
            Assert.That(Events(sim, SimEventKind.MatchAbandoned)[0].Tick, Is.EqualTo(500L));
            Assert.That(r.Cards.Length, Is.EqualTo(5));
            Assert.That(r.Validate(), Is.Empty);
        }

        [Test]
        public void BothTeamsBelowSeven_SameTick()
        {
            var script = new RefereeScript();
            for (int i = 1; i <= 5; i++) script.Add(new RefereeAction(800, RefereeActionKind.SendOff, i));
            for (int i = 101; i <= 105; i++) script.Add(new RefereeAction(800, RefereeActionKind.SendOff, i));
            var sim = Sim(32UL, new MatchSimOptions { Referee = script });
            var r = sim.RunToEnd();
            Assert.That(r.Reason, Is.EqualTo(EndReason.Abandoned));
            Assert.That(Events(sim, SimEventKind.PlayersBelowMinimum).Count, Is.EqualTo(2));
        }

        [Test]
        public void SentOff_TeamContinuesWithTen()
        {
            var script = new RefereeScript();
            script.Add(new RefereeAction(300, RefereeActionKind.SendOff, 4));
            var sim = Sim(33UL, new MatchSimOptions { Referee = script });
            sim.RunUntilTick(400);
            Assert.That(sim.Context.Squad.OnPitchCount(0), Is.EqualTo(10));
            Assert.That(sim.View.IsSlotActive(3), Is.False);
            Assert.That(sim.IsFinished, Is.False);
        }
    }
}
