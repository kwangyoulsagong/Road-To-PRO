using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.PitchControl;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;
using RoadToPro.Tests.EditMode.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.PitchControl
{
    /// <summary>specs/007 AC-10 (질의 해시·입력 순서) · AC-11 (진단 켬/끔 해시 동일, 끔 할당 0).</summary>
    public class PitchControlDeterminismTests
    {
        static PitchControlParams P => PitchControlParams.Defaults;

        /// <summary>상태 1,000개 질의 해시. shuffle = 같은 배치를 슬롯 채우는 순서만 섞어서.</summary>
        static ulong QueryHash(bool shuffle)
        {
            var rng = new SeedRng(7010);
            var order = new SeedRng(99);
            var src = new ControlPlayers();
            var pl = new ControlPlayers();
            var q = new PitchControlQuery(P);
            var tr = new TrajectorySamples();
            var grid = new PitchControlGrid();
            var h = StableHasher.Create();
            var idx = new int[22];
            for (int i = 0; i < 1000; i++)
            {
                PitchControlTestKit.Random(src, rng, false);
                for (int k = 0; k < 22; k++) idx[k] = k;
                if (shuffle)
                    for (int k = 21; k > 0; k--) { var j = (int)(order.NextFloat() * (k + 1)) % (k + 1); var t = idx[k]; idx[k] = idx[j]; idx[j] = t; }
                pl.Clear();
                foreach (var s in idx)
                    if (src.Active[s]) pl.SetEngine(s, src.Team[s], src.IsKeeper[s], src.Pos[s], src.Vel[s], in src.Tti[s], src.LambdaAtt[s], src.LambdaDef[s]);
                var target = PitchControlTestKit.RandomPoint(rng);
                var r = q.Point(pl, i & 1, target, 2f * rng.NextFloat());
                h.Add(r.Att); h.Add(r.Def); h.Add(r.None); h.Add(r.Leader);
                var passer = -1;
                for (int s = 0; s < 22; s++) if (pl.Active[s]) { passer = s; break; }
                tr.Ground(ActionTestKit.Ball, pl.Pos[passer], PitchControlTestKit.RandomPoint(rng), 10f + 15f * rng.NextFloat(), P.Dt, P.Horizon, 105f, 68f);
                var t2 = q.Trajectory(pl, tr, passer, (passer + 1) % 22);
                h.Add(t2.Receiver); h.Add(t2.Teammate); h.Add(t2.Opponent); h.Add(t2.Out);
                if (i % 100 == 0)
                {
                    q.Grid(pl, 0, target, i, grid);
                    for (int c = 0; c < grid.Att.Length; c++) { h.Add(grid.Att[c]); h.Add(grid.Def[c]); }
                }
            }
            return h.Value;
        }

        [Test, Category("Determinism")]
        public void Queries_1000States_SameHash_Shuffled()
        {
            var a = QueryHash(false);
            var b = QueryHash(false);
            var c = QueryHash(true);
            Assert.AreEqual(a, b, "2회 실행 같음");
            Assert.AreEqual(a, c, "입력(채우는) 순서를 섞어도 같음 — 합산은 슬롯 순");
        }

        sealed class Recorder : IPitchControlDiagnostics
        {
            public int Count;
            public double Sum;
            public void OnPassRefined(long tick, int passer, int receiver, byte item, float logisticXPass, in TrajectoryControl trajectory, float judged)
            {
                Count++;
                Sum += judged - logisticXPass;
            }
        }

        [Test, Category("Determinism"), Timeout(900_000)]
        public void DiagnosticsOnOff_SameHashes()
        {
            Recorder r = null;
            var on = new MatchSim(ShapeTestKit.Setup(seed: 7400UL), ShapeTestKit.TuningWithShape(),
                new MatchSimOptions { PitchControlDiagnostics = () => r = new Recorder() }).RunToEnd();
            var off = new MatchSim(ShapeTestKit.Setup(seed: 7400UL), ShapeTestKit.TuningWithShape()).RunToEnd();
            TestContext.WriteLine($"재평가 {r.Count}회, 판단 P − 로지스틱 평균 {r.Sum / System.Math.Max(1, r.Count):F3}");
            Assert.Greater(r.Count, 0, "재평가가 실제로 돈다");
            Assert.AreEqual(on.EventLogHash, off.EventLogHash, "진단 켬/끔이 이벤트 해시를 바꾸지 않는다");
            Assert.AreEqual(on.FinalStateHash, off.FinalStateHash);
        }

        [Test]
        public void DiagnosticsOff_ZeroAlloc()
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(50f, 34f));
            f.GiveBall(6);
            var rng = new SeedRng(3);
            for (int i = 0; i < 50; i++) { f.F.ResetState(6); f.Decide(rng, 6, sync: i == 0); } // 워밍업
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            var refined = 0;
            for (int i = 0; i < 200; i++)
            {
                f.F.ResetState(6);
                f.Decide(rng, 6, sync: false);
                refined += f.Decider.LastRefined;
            }
            var bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Greater(refined, 0);
            Assert.AreEqual(0, bytes, "진단 끔 · 재평가 포함 온볼 결정 할당 0 B");
        }
    }
}
