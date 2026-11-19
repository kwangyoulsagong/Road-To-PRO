using NUnit.Framework;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/074 FR-01·04 승부차기 판정: 같은 모델(071 키커 + 074 GK + 011 선방)을 한 틱 안에서 — 능력치·기억·피로가 결과에 들어간다.</summary>
    public class PenaltyShootoutSimTests
    {
        /// <summary>팀 1 GK를 표시 능력치 display로 바꾼 010 실경기를 몇 틱 돌린 상태 (판정 호출용 문맥).</summary>
        public static MatchSim Ready(float keeperDisplay, ulong seed = 7400UL)
        {
            var setup = ShapeTestKit.Setup(seed: seed);
            var team = setup.Away;
            var gkId = team.Starters[0];
            for (int i = 0; i < team.Players.Length; i++)
            {
                var mp = team.Players[i];
                if (mp.Id != gkId) continue;
                team.Players[i] = new MatchPlayer(mp.Id, in mp.Attributes, KeeperAttributes.Uniform(keeperDisplay), in mp.Mind, mp.Style, in mp.Technical);
            }
            var sim = new MatchSim(setup, ShapeTestKit.TuningWithShape());
            sim.RunUntilTick(20);
            return sim;
        }

        static int Kicker(MatchSim sim, int i) => sim.Context.Setup.Team(0).Starters[1 + i % 10];

        static int Goals(MatchSim sim, int kicks)
        {
            var judge = new SimPenaltyJudge();
            var ctx = sim.Context;
            var gk = ctx.Setup.Team(1).Starters[0];
            var g = 0;
            for (int i = 0; i < kicks; i++)
                if (judge.Kick(ctx, 0, Kicker(sim, i), gk).Scored) g++;
            return g;
        }

        [Test]
        public void Shootout_UsesKeeperAttributes()
        {
            const int n = 2000;
            var weak = Goals(Ready(1f), n);
            var strong = Goals(Ready(20f), n);
            TestContext.WriteLine($"GK 1: {weak}/{n} · GK 20: {strong}/{n}");
            double pa = weak / (double)n, pb = strong / (double)n, pp = (weak + strong) / (2.0 * n);
            var z = (pa - pb) / System.Math.Sqrt(pp * (1 - pp) * 2.0 / n);
            Assert.Greater(z, 2.33, "GK 20이 실점이 적다 (p < 0.01)");
            Assert.That(pa, Is.InRange(0.5, 0.98));
        }

        [Test]
        public void Memory_ShiftsBias_FatigueAccumulates()
        {
            var sim = Ready(10.5f);
            var ctx = sim.Context;
            var judge = new SimPenaltyJudge();
            var gk = ctx.Setup.Team(1).Starters[0];
            for (int i = 0; i < 10; i++) judge.Kick(ctx, 0, Kicker(sim, i), gk);
            var m = judge.Memory(1);
            Assert.Greater(m.Dives, 0, "다이빙 수 누적");
            Assert.LessOrEqual(System.Math.Abs(m.Balance), 10);
            var model = ctx.Keepers.PenaltyModel;
            Assert.AreNotEqual(model.Mean(1, 0), model.Mean(1, m.Balance == 0 ? 2 : m.Balance));
        }

        [Test]
        public void ShootoutKeeperReplaced_FatigueResets_MemoryKept()
        {
            var sim = Ready(10.5f);
            var ctx = sim.Context;
            var judge = new SimPenaltyJudge();
            var gk = ctx.Setup.Team(1).Starters[0];
            for (int i = 0; i < 6; i++) judge.Kick(ctx, 0, Kicker(sim, i), gk);
            var before = judge.Memory(1);
            judge.Kick(ctx, 0, Kicker(sim, 6), gk + 1000); // 다른 GK id (교체)
            var after = judge.Memory(1);
            Assert.LessOrEqual(after.Dives, 1, "새 GK 피로는 0부터");
            Assert.That(System.Math.Abs(after.Balance - before.Balance), Is.LessThanOrEqualTo(1), "방향 기억은 팀 것");
        }

        [Test]
        public void Shootout_Deterministic_SameSeedSameOutcomes()
        {
            string Run()
            {
                var sim = Ready(10.5f);
                var judge = new SimPenaltyJudge();
                var gk = sim.Context.Setup.Team(1).Starters[0];
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 20; i++)
                {
                    var o = judge.Kick(sim.Context, 0, Kicker(sim, i), gk);
                    sb.Append(o.Scored ? '1' : '0').Append(o.KeeperDir).Append(':').Append(o.Lead.ToString("R")).Append(':').Append(o.Lean.ToString("R")).Append(';');
                }
                return sb.ToString();
            }
            Assert.AreEqual(Run(), Run());
        }
    }
}
