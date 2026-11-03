using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Replay;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-10: 연장 → 승부차기, 5-5 뒤 서든데스, 키커 순환, 연장 없는 동점, 정규시간 승부, GK 부상.</summary>
    public class ShootoutTests
    {
        static MatchSim _cupBeforeEnd;
        static MatchSnapshot _snap;

        /// <summary>0-0 컵 경기를 연장 후반 마지막 틱 직전까지 (스냅샷으로 판정만 바꿔 다시 돈다).</summary>
        static MatchSim CupAtEnd(IPenaltyJudge judge)
        {
            if (_cupBeforeEnd == null)
            {
                var s = Scripted(null, rules: CompetitionRules.Cup(), options: new MatchSimOptions { Checkpoints = CheckpointMode.None });
                s.RunUntilTick(54_000 + 18_000 - 1);
                _snap = s.Capture();
                _cupBeforeEnd = s;
            }
            var sim = Scripted(null, rules: CompetitionRules.Cup());
            sim.Restore(_snap, _cupBeforeEnd.Log, _cupBeforeEnd.Checkpoints);
            if (judge != null) sim.Context.Flow.PenaltyJudge = judge;
            sim.RunToEnd();
            return sim;
        }

        sealed class Scripted5 : IPenaltyJudge
        {
            readonly System.Func<int, int, bool> _scores; // (team, kickNo) → 득점
            readonly int _injureAt;
            public readonly int[] Kicks = new int[2];
            public readonly List<int> Kickers = new List<int>();
            public Scripted5(System.Func<int, int, bool> scores, int injureAt = -1) { _scores = scores; _injureAt = injureAt; }
            public PenaltyOutcome Kick(MatchContext ctx, int team, int kickerId, int keeperId)
            {
                var n = ++Kicks[team];
                if (team == 0) Kickers.Add(kickerId);
                return new PenaltyOutcome { Scored = _scores(team, n), KeeperInjured = n == _injureAt && team == 0 };
            }
        }

        [Test]
        public void CupDraw_GoesExtraTimeThenShootout()
        {
            var sim = CupAtEnd(null);
            var r = sim.Result;
            Assert.That(Events(sim, SimEventKind.PeriodStart).Count, Is.EqualTo(4), "전·후반 + 연장 전·후반");
            Assert.That(r.ExtraTimePlayed && r.ShootoutPlayed);
            Assert.That(r.ShootoutGoals[0], Is.Not.EqualTo(r.ShootoutGoals[1]));
            Assert.That(r.Winner, Is.InRange(0, 1));
            var end = Events(sim, SimEventKind.ShootoutEnd);
            Assert.That(end.Count, Is.EqualTo(1));
            Assert.That(end[0].A, Is.EqualTo(r.ShootoutGoals[0]));
            Assert.That(r.Validate(), Is.Empty);
        }

        [Test]
        public void FiveFive_ThenSuddenDeath_UntilWinner()
        {
            var j = new Scripted5((team, n) => !(team == 1 && n == 7)); // 원정 7번째 실축
            var sim = CupAtEnd(j);
            var kicks = Events(sim, SimEventKind.ShootoutKick);
            Assert.That(kicks.Count, Is.EqualTo(14), "5-5 뒤 서든데스 2라운드");
            Assert.That(sim.Result.ShootoutGoals[0], Is.EqualTo(7));
            Assert.That(sim.Result.ShootoutGoals[1], Is.EqualTo(6));
            Assert.That(sim.Result.Winner, Is.EqualTo(0));
            Assert.That(kicks[kicks.Count - 1].B, Is.EqualTo(7));
        }

        [Test]
        public void AllElevenKicked_OrderCyclesFromStart()
        {
            var j = new Scripted5((team, n) => !(team == 1 && n == 13));
            var sim = CupAtEnd(j);
            Assert.That(j.Kickers.Count, Is.EqualTo(13));
            Assert.That(j.Kickers[11], Is.EqualTo(j.Kickers[0]), "12번째 키커 = 첫 키커 (11명 순환)");
            Assert.That(new HashSet<int>(j.Kickers.GetRange(0, 11)).Count, Is.EqualTo(11));
        }

        [Test]
        public void Decided_EarlyWhenUncatchable()
        {
            var j = new Scripted5((team, n) => team == 0);
            var sim = CupAtEnd(j);
            Assert.That(Events(sim, SimEventKind.ShootoutKick).Count, Is.LessThan(10));
            Assert.That(sim.Result.Winner, Is.EqualTo(0));
        }

        [Test]
        public void KeeperInjured_ReplacedWhenSubsLeft()
        {
            var j = new Scripted5((team, n) => !(team == 1 && n == 6), injureAt: 1);
            var sim = CupAtEnd(j);
            var subs = Events(sim, SimEventKind.Substitution);
            Assert.That(subs.Count, Is.EqualTo(1), "교체가 남아 있으면 GK 교체");
            Assert.That(subs[0].OtherId, Is.EqualTo(101), "원정 GK(선발 첫 선수) 교체");
        }

        [Test]
        public void NoExtraTime_DrawEnds()
        {
            var sim = Scripted(null);
            var r = sim.RunToEnd();
            Assert.That(r.Reason, Is.EqualTo(EndReason.Normal));
            Assert.That(r.Winner, Is.EqualTo(-1));
            Assert.That(r.ExtraTimePlayed || r.ShootoutPlayed, Is.False);
            Assert.That(Events(sim, SimEventKind.PeriodEnd).Count, Is.EqualTo(2));
        }

        [Test]
        public void CupDecidedInRegulation_NoExtraTime()
        {
            var sim = Scripted(ctx =>
            {
                if (ctx.Tick == 0) ctx.Flow.OnRestartTaken();
                if (ctx.Tick == 100)
                {
                    ctx.Ball = BallState.RollingAt(new Vec2(104f, 34f), new Vec2(10f, 0f), ctx.BallParams.Radius);
                    ctx.LastTouchTeam = 0;
                    ctx.LastTouchSlot = 9;
                }
            }, rules: CompetitionRules.Cup());
            var r = sim.RunToEnd();
            Assert.That(r.Score(0), Is.EqualTo(1));
            Assert.That(r.ExtraTimePlayed, Is.False);
            Assert.That(r.ShootoutPlayed, Is.False);
            Assert.That(r.Winner, Is.EqualTo(0));
        }
    }
}
