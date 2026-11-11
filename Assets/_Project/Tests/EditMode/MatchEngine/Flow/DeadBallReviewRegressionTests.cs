using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Flow
{
    /// <summary>058 determinism-reviewer 지적 회귀: 키커 이탈로 멈춤(H1·H2), 골 데드볼 중 정지 이중 계산(M1), 종료 직전 교체(M3).</summary>
    public class DeadBallReviewRegressionTests
    {
        static (long goalTick, int kickerId) FirstGoalKicker()
        {
            var g = Reference().Result.Goals[0];
            var sim = Sim(ReferenceSeed);
            sim.RunUntilTick(g.Tick + 10);
            return (g.Tick, sim.Context.PlayerAtSlot(sim.Context.Flow.RestartTaker));
        }

        [Test]
        public void GoalReturn_KickerSentOff_KickoffStillHappens()
        {
            var (goal, kicker) = FirstGoalKicker();
            var script = new RefereeScript();
            script.Add(new RefereeAction(goal + 90, RefereeActionKind.SendOff, kicker)); // 복귀 단계
            var sim = Sim(ReferenceSeed, new MatchSimOptions { Referee = script });
            var r = sim.RunToEnd();
            Assert.That(r.Reason, Is.EqualTo(EndReason.Normal), "키커 퇴장 뒤 경기가 끝나지 않았다");
            var ko = Events(sim, SimEventKind.Kickoff).Find(e => e.Tick > goal);
            Assert.That(ko.Kind, Is.EqualTo(SimEventKind.Kickoff), "골 뒤 킥오프 없음");
            Assert.That(ko.PlayerId, Is.Not.EqualTo(kicker));
        }

        [Test]
        public void Approach_TakerInjuredNoBench_RestartStillHappens()
        {
            // 공 근처 선수(재시작 키커)가 부상, 벤치 없음 → 걸어 나가고 다른 선수가 찬다
            var refSim = Sim(ReferenceSeed);
            refSim.RunUntilTick(3_000);
            var owner = refSim.Context.Ball.OwnerId;
            var victimSlot = owner >= 0 ? owner : 9;
            var victim = refSim.Context.PlayerAtSlot(victimSlot);
            var script = new RefereeScript();
            script.Add(new RefereeAction(3_000, RefereeActionKind.Injury, victim, 50));
            var sim = new MatchSim(Setup(ReferenceSeed, null, 0), Tuning(), new MatchSimOptions { Referee = script });
            sim.RunUntilTick(3_000 + 3_000);
            var ready = Events(sim, SimEventKind.DeadBallPhase).Find(e => e.Tick >= 3_000 && e.A == (int)DeadBallPhase.RestartReady);
            Assert.That(ready.Kind, Is.EqualTo(SimEventKind.DeadBallPhase), "부상 키커 뒤 재시작 준비 없음");
            Assert.That(sim.Context.Flow.InPlay || sim.Context.Flow.Restart != RestartKind.DropBall, Is.True, "드롭볼이 끝나지 않았다");
        }

        [Test]
        public void GoalDeadBall_Substitution_NotCountedTwice()
        {
            var g = Reference().Result.Goals[0];
            var inputs = new InputLog();
            inputs.Add(new ExternalInput(g.Tick + 20, 9, InputKind.Substitute, 12));
            inputs.Add(new ExternalInput(g.Tick + 20, 109, InputKind.Substitute, 112));
            var sim = Sim(ReferenceSeed, new MatchSimOptions { Inputs = inputs });
            sim.RunUntilTick(g.Tick);
            var subBefore = sim.Context.Flow.Stoppage.KindTicks(EventCodes.StopSubstitution);
            var goalBefore = sim.Context.Flow.Stoppage.KindTicks(EventCodes.StopGoal);
            sim.RunUntilTick(g.Tick + 1_500);
            var ko = Events(sim, SimEventKind.Kickoff).Find(e => e.Tick > g.Tick);
            Assert.That(ko.Kind, Is.EqualTo(SimEventKind.Kickoff));
            Assert.That(sim.Context.Flow.Stoppage.KindTicks(EventCodes.StopSubstitution), Is.EqualTo(subBefore), "골 데드볼 중 교체 정지가 따로 더해졌다");
            Assert.That(sim.Context.Flow.Stoppage.KindTicks(EventCodes.StopGoal) - goalBefore, Is.EqualTo(ko.Tick - g.Tick), "골 정지 = 골 ~ 킥오프");
        }

        [Test]
        public void SubstitutionWalking_AtMatchEnd_IsCompleted()
        {
            var full = Reference();
            var inputs = new InputLog();
            inputs.Add(new ExternalInput(full.Result.EndTick - 40, 9, InputKind.Substitute, 12));
            var sim = Sim(ReferenceSeed, new MatchSimOptions { Inputs = inputs });
            var r = sim.RunToEnd();
            var subs = Events(sim, SimEventKind.Substitution);
            var used = sim.Context.Flow.Subs.Used(0);
            Assert.That(subs.FindAll(e => e.Team == 0).Count, Is.EqualTo(used), "교체 횟수와 교체 이벤트 수가 다르다");
        }
    }
}
