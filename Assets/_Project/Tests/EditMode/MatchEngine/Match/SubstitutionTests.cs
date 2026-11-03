using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-06: 5명·3창, 하프타임 창 미소모, 뇌진탕 별도, 재투입 금지, 연장 +1명·+1창 합산.</summary>
    public class SubstitutionTests
    {
        static void NewDeadBall(MatchContext ctx) => ctx.Flow.BeginRestart(RestartKind.ThrowIn, 0, new Vec2(30f, 0f));

        [Test]
        public void Limits_Windows_Halftime_Concussion_Reentry()
        {
            MatchSim sim = null;
            sim = Scripted(ctx =>
            {
                // 데드볼 1(킥오프)은 틱 0, 이후 틱 5·10·15·27100·27200에 새 데드볼
                if (ctx.Tick == 5 || ctx.Tick == 10 || ctx.Tick == 15 || ctx.Tick == 27_100 || ctx.Tick == 27_200 || ctx.Tick == 27_300) NewDeadBall(ctx);
            });
            // 홈: 선발 1~11, 벤치 12~18
            sim.RequestSubstitution(0, 2, 12);
            sim.RequestSubstitution(0, 3, 13);                  // 같은 데드볼 → 창 1개
            sim.RunUntilTick(5); sim.RequestSubstitution(0, 4, 14);  // 창 2
            sim.RunUntilTick(10); sim.RequestSubstitution(0, 5, 15); // 창 3
            sim.RunUntilTick(15); sim.RequestSubstitution(0, 6, 16); // 창 4 → 거부
            sim.RunUntilTick(26_999); sim.RequestSubstitution(0, 7, 17); // 전반 마지막 틱 → 하프타임 (창 미소모)
            sim.RunUntilTick(27_100); sim.RequestSubstitution(0, 8, 18); // 6번째 → 한도 거부
            sim.RunUntilTick(27_200); sim.RequestSubstitution(0, 9, 18, concussion: true); // 뇌진탕 → 별도 허용
            sim.RunUntilTick(27_300); sim.RequestSubstitution(0, 12, 2);  // 나간 2번 재투입 → 거부
            sim.RequestSubstitution(0, 10, 18, concussion: true);          // 뇌진탕 두 번째 → 거부 (18은 이미 들어감 → 재투입이 아닌 벤치 아님)
            sim.StepTicks(5);

            var subs = Events(sim, SimEventKind.Substitution);
            var rej = Events(sim, SimEventKind.SubstitutionRejected);
            Assert.That(subs.Count, Is.EqualTo(6), "정상 5 + 뇌진탕 1");
            var f = sim.Context.Flow.Subs;
            Assert.That(f.Used(0), Is.EqualTo(5));
            Assert.That(f.WindowsUsed(0), Is.EqualTo(3), "하프타임 교체는 창을 쓰지 않는다");
            Assert.That(f.ConcussionUsed(0), Is.EqualTo(1));
            Assert.That(rej.Count, Is.EqualTo(4));
            Assert.That(rej[0].A, Is.EqualTo(EventCodes.RejectWindows), "4번째 창");
            Assert.That(rej[0].PlayerId, Is.EqualTo(16));
            Assert.That(rej[1].A, Is.EqualTo(EventCodes.RejectLimit), "6번째 교체");
            // 같은 틱 요청은 입력 로그 순서(나가는 선수 id 순)로 처리: 10→18 먼저, 12→2 다음
            Assert.That(rej[2].A, Is.EqualTo(EventCodes.RejectNotOnBench), "이미 들어간 18");
            Assert.That(rej[3].A, Is.EqualTo(EventCodes.RejectReentry), "나간 선수 재투입");
            // 하프타임 교체는 전반 끝 시각에 기록
            var ht = subs.Find(e => e.PlayerId == 17);
            Assert.That(ht.Period, Is.EqualTo(0));
            Assert.That(ht.Tick, Is.EqualTo(26_999L));
            // 들어온 선수는 나간 선수 슬롯·위치에서 시작, 상태는 출전
            Assert.That(sim.Context.Squad.StatusOf(12), Is.EqualTo(PlayerStatus.OnPitch));
            Assert.That(sim.Context.Squad.StatusOf(2), Is.EqualTo(PlayerStatus.SubbedOff));
            Assert.That(sim.Context.Squad.PlayerAt(1), Is.EqualTo(12));
        }

        [Test]
        public void ExtraTime_AddsOneSubAndOneWindow_ToRemaining()
        {
            MatchSim sim = null;
            sim = Scripted(ctx =>
            {
                if (ctx.Tick == 5 || ctx.Tick == 10 || ctx.Tick == 60_000 || ctx.Tick == 60_100) NewDeadBall(ctx);
            }, rules: CompetitionRules.Cup());
            sim.RequestSubstitution(0, 2, 12);
            sim.RunUntilTick(5); sim.RequestSubstitution(0, 3, 13);
            sim.RunUntilTick(10); sim.RequestSubstitution(0, 4, 14); sim.RequestSubstitution(0, 5, 15); sim.RequestSubstitution(0, 6, 16); // 같은 창(3번째) → 5명
            sim.RunUntilTick(60_000);
            Assert.That(sim.View.Period, Is.EqualTo(2), "0-0 → 연장");
            sim.RequestSubstitution(0, 7, 17); // 연장: 6번째·4번째 창 허용
            sim.RunUntilTick(60_100);
            sim.RequestSubstitution(0, 8, 18); // 7번째 → 거부
            sim.StepTicks(3);
            var f = sim.Context.Flow.Subs;
            Assert.That(f.Used(0), Is.EqualTo(6));
            Assert.That(f.WindowsUsed(0), Is.EqualTo(4));
            var rej = Events(sim, SimEventKind.SubstitutionRejected);
            Assert.That(rej.Count, Is.EqualTo(1));
            Assert.That(rej[0].A, Is.EqualTo(EventCodes.RejectLimit));
        }

        [Test]
        public void InjuryWithNoSubsLeft_TeamPlaysWithTen()
        {
            var script = new RoadToPro.MatchEngine.Match.Input.RefereeScript();
            script.Add(new RoadToPro.MatchEngine.Match.Input.RefereeAction(100, RoadToPro.MatchEngine.Match.Input.RefereeActionKind.Injury, 9, 300));
            var sim = Scripted(null, bench: 0, options: new MatchSimOptions { Referee = script });
            sim.StepTicks(120);
            Assert.That(sim.Context.Squad.StatusOf(9), Is.EqualTo(PlayerStatus.InjuredOff));
            Assert.That(sim.Context.Squad.OnPitchCount(0), Is.EqualTo(10));
            Assert.That(Events(sim, SimEventKind.Injury).Count, Is.EqualTo(1));
            Assert.That(sim.Context.Flow.Stoppage.KindTicks(EventCodes.StopInjury), Is.EqualTo(300L));
        }

        [Test]
        public void InjuryWithBench_AutoReplaced()
        {
            var script = new RoadToPro.MatchEngine.Match.Input.RefereeScript();
            script.Add(new RoadToPro.MatchEngine.Match.Input.RefereeAction(100, RoadToPro.MatchEngine.Match.Input.RefereeActionKind.Injury, 9, 300, 1));
            var sim = Scripted(null, options: new MatchSimOptions { Referee = script });
            sim.StepTicks(120);
            var subs = Events(sim, SimEventKind.Substitution);
            Assert.That(subs.Count, Is.EqualTo(1));
            Assert.That(subs[0].OtherId, Is.EqualTo(9));
            Assert.That(subs[0].B, Is.EqualTo(EventCodes.SubConcussion));
            Assert.That(sim.Context.Squad.OnPitchCount(0), Is.EqualTo(11));
        }
    }
}
