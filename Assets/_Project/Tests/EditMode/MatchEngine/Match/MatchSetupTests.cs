using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>FR-10 입력 검증 (경계: 선발 11명 미만·중복·벤치 0명).</summary>
    public class MatchSetupTests
    {
        /// <summary>specs/012 T04: 심판 프로필·규칙 세트·VAR 플래그는 복제에 들어가고, 없으면 관대함 환산·기본 규칙 세트를 쓴다.</summary>
        [Test]
        public void RefereeFields_Clone_AndEffectiveDefaults()
        {
            var s = Setup();
            Assert.AreSame(RoadToPro.MatchEngine.Referee.RefereeProfile.LeagueAverage, s.EffectiveReferee, "기본 = 리그 평균");
            Assert.AreSame(RoadToPro.MatchEngine.Referee.RuleSet.Default, s.EffectiveRuleSet);
            Assert.IsTrue(s.VarEnabled);
            s.RefereeLeniency = 3;
            Assert.AreEqual(0.2f, s.EffectiveReferee.Strictness, 1e-5f, "관대함 +3 → 엄격함 0.5 − 0.3");
            var p = RoadToPro.MatchEngine.Referee.RefereeProfile.LeagueAverage.With(strictness: 0.9f);
            s.Referee = p;
            s.RuleSet = RoadToPro.MatchEngine.Referee.RuleSet.Season2025_26;
            s.VarEnabled = false;
            var c = s.Clone();
            Assert.AreSame(p, c.EffectiveReferee);
            Assert.AreSame(RoadToPro.MatchEngine.Referee.RuleSet.Season2025_26, c.EffectiveRuleSet);
            Assert.IsFalse(c.VarEnabled);
        }

        [Test]
        public void TenStarters_Rejected()
        {
            var s = Setup();
            s.Home.Starters = s.Home.Starters[..10];
            Assert.Throws<MatchSetupException>(() => new MatchSim(s, Tuning()));
        }

        [Test]
        public void DuplicatePlayer_Rejected()
        {
            var s = Setup();
            s.Home.Starters[3] = s.Home.Starters[2];
            Assert.Throws<MatchSetupException>(() => new MatchSim(s, Tuning()), "선발 중복");
            s = Setup();
            s.Away.Bench[0] = s.Away.Starters[4];
            Assert.Throws<MatchSetupException>(() => new MatchSim(s, Tuning()), "선발·벤치 중복");
            s = Setup();
            s.Away.Players[0] = new MatchPlayer(s.Home.Players[0].Id, s.Away.Players[0].Attributes);
            Assert.Throws<MatchSetupException>(() => new MatchSim(s, Tuning()), "두 팀에 같은 id");
        }

        [Test]
        public void BenchOverLimit_Rejected()
        {
            var s = Setup(bench: 7);
            s.Rules.BenchSize = 5;
            Assert.Throws<MatchSetupException>(() => new MatchSim(s, Tuning()));
        }

        [Test]
        public void ZeroBench_Allowed_SubstitutionRejected()
        {
            var sim = Scripted(null, bench: 0);
            sim.RequestSubstitution(0, 2, 999);
            sim.StepTicks(2);
            var rej = Events(sim, SimEventKind.SubstitutionRejected);
            Assert.That(rej.Count, Is.EqualTo(1));
            Assert.That(rej[0].A, Is.EqualTo(EventCodes.RejectNotOnBench));
        }
    }
}
