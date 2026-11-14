using NUnit.Framework;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 T11: 100경기 측정 (리포트 표용, 판정 없음 — AC-11은 017 뒤). 070 측정과 같은 시드 묶음(7400 + i). `.farm/shot-n.txt`가 있으면 그 수.</summary>
    public class ShotMatchTests
    {
        sealed class One
        {
            public double Goals, Shots, ShotXg, HeaderShots, Blocks, Crosses, CrossEarly, CrossDriven, CrossLofted, CrossCutback, Headers, Contests, Passes, Received, Saves, Possessions;
        }

        static One RunOne(int i)
        {
            var sim = new MatchSim(ShapeTestKit.Setup(seed: 7400UL + (ulong)i), ShapeTestKit.TuningWithShape());
            var r = sim.RunToEnd();
            var o = new One { Goals = r.RegularGoals[0] + r.RegularGoals[1] };
            var log = sim.Log;
            for (int k = 0; k < log.Count; k++)
            {
                var e = log[k];
                switch (e.Kind)
                {
                    case SimEventKind.Shot:
                        o.Shots++;
                        o.ShotXg += e.F1;
                        if ((e.A & ActionSystem.ShotFlagHeader) != 0) o.HeaderShots++;
                        break;
                    case SimEventKind.Block: o.Blocks++; break;
                    case SimEventKind.Cross:
                        o.Crosses++;
                        switch ((e.A >> ActionSystem.CrossTypeShift) - 1)
                        {
                            case (int)CrossType.Early: o.CrossEarly++; break;
                            case (int)CrossType.Driven: o.CrossDriven++; break;
                            case (int)CrossType.Lofted: o.CrossLofted++; break;
                            default: o.CrossCutback++; break;
                        }
                        break;
                    case SimEventKind.Header: o.Headers++; break;
                    case SimEventKind.Pass: o.Passes++; break;
                    case SimEventKind.PassReceived: o.Received++; break;
                    case SimEventKind.Save:
                    case SimEventKind.Catch:
                    case SimEventKind.Parry: o.Saves++; break;
                    case SimEventKind.PossessionChange: o.Possessions++; break;
                }
            }
            o.Contests = sim.Context.Actions.Aerials.Contests;
            return o;
        }

        [Test, Explicit("리포트용 측정"), Category("Slow"), Timeout(3_600_000)]
        public void Report_100Matches_ShotMeasurements()
        {
            var n = 100;
            var np = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".farm", "shot-n.txt");
            if (System.IO.File.Exists(np)) n = int.Parse(System.IO.File.ReadAllText(np).Trim());
            ShapeTestKit.Setup(seed: 1UL);
            var res = new One[n];
            System.Threading.Tasks.Parallel.For(0, n, i => res[i] = RunOne(i));
            var s = new One();
            foreach (var x in res)
            {
                s.Goals += x.Goals; s.Shots += x.Shots; s.ShotXg += x.ShotXg; s.HeaderShots += x.HeaderShots; s.Blocks += x.Blocks; s.Crosses += x.Crosses;
                s.CrossEarly += x.CrossEarly; s.CrossDriven += x.CrossDriven; s.CrossLofted += x.CrossLofted; s.CrossCutback += x.CrossCutback;
                s.Headers += x.Headers; s.Contests += x.Contests; s.Passes += x.Passes; s.Received += x.Received; s.Saves += x.Saves; s.Possessions += x.Possessions;
            }
            TestContext.WriteLine($"경기 {n}, 경기당: 득점 {s.Goals / n:F2}, 슈팅 {s.Shots / n:F1} (슈팅당 xG {(s.Shots > 0 ? s.ShotXg / s.Shots : 0):F3}, 경기당 xG {s.ShotXg / n:F2}, 헤딩슛 {s.HeaderShots / n:F1}, 블록 {s.Blocks / n:F1}, 선방 {s.Saves / n:F1}), " +
                $"크로스 {s.Crosses / n:F1} (Early {s.CrossEarly / n:F1} · Driven {s.CrossDriven / n:F1} · Lofted {s.CrossLofted / n:F1} · Cutback {s.CrossCutback / n:F1}), " +
                $"공중볼 경합 {s.Contests / n:F1}, 헤더 {s.Headers / n:F1}, 패스 {s.Passes / n:F0} (수신 {(s.Passes > 0 ? s.Received / s.Passes : 0):P0}), 소유 변경 {s.Possessions / n:F0}, 전환율 {(s.Shots > 0 ? s.Goals / s.Shots : 0):P1}");
            Assert.Greater(s.Shots, 0);
        }
    }
}
