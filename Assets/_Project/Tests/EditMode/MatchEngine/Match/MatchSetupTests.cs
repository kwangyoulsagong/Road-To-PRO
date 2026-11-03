using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>FR-10 입력 검증 (경계: 선발 11명 미만·중복·벤치 0명).</summary>
    public class MatchSetupTests
    {
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
