using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Flow
{
    /// <summary>
    /// specs/058 AC-15 (보류): 대량 시뮬 [제안] 200경기 평균 득점, 골 후 데드볼 평균 길이·재시작 평균 대기 시간 기록.
    /// 결과는 specs/058-highlight-live-match/evidence/mass-sim.json.
    /// </summary>
    public class MassSimDeadBallTests
    {
        const int Matches = 200;
        const ulong FirstSeed = 20261200UL;

        /// <remarks>200경기 ≈ 23분이라 전체 EditMode에서 빼고 수동 실행한다 (AC-15 보류, 결과는 evidence에 남김).</remarks>
        [Test, Explicit("AC-15 보류 — 수동 실행 (≈ 23분)"), Category("Slow"), Timeout(3_600_000)]
        public void Batch200_GoalsAndDeadBallStats()
        {
            long goals = 0, goalDead = 0, goalDeadN = 0, restartWait = 0, restartN = 0;
            for (int m = 0; m < Matches; m++)
            {
                var sim = Sim(FirstSeed + (ulong)m * 104729UL);
                var r = sim.RunToEnd();
                goals += r.Score(0) + r.Score(1);
                long goalTick = -1, approachTick = -1;
                for (int i = 0; i < sim.Log.Count; i++)
                {
                    var e = sim.Log[i];
                    if (e.Kind == SimEventKind.Goal) goalTick = e.Tick;
                    else if (e.Kind == SimEventKind.Kickoff && goalTick >= 0) { goalDead += e.Tick - goalTick; goalDeadN++; goalTick = -1; }
                    else if (e.Kind == SimEventKind.PeriodStart || e.Kind == SimEventKind.PeriodEnd) goalTick = -1;
                    if (e.Kind == SimEventKind.DeadBallPhase && e.A == (int)DeadBallPhase.Approach) approachTick = e.Tick;
                    else if (e.Kind == SimEventKind.DeadBallPhase && e.A == (int)DeadBallPhase.RestartReady && approachTick >= 0) { restartWait += e.Tick - approachTick; restartN++; approachTick = -1; }
                }
            }
            double avgGoals = goals / (double)Matches;
            double avgGoalDead = goalDeadN == 0 ? 0 : goalDead * 0.1d / goalDeadN;
            double avgRestart = restartN == 0 ? 0 : restartWait * 0.1d / restartN;
            var json = new StringBuilder();
            json.Append("{\n  \"matches\": ").Append(Matches)
                .Append(",\n  \"avgGoals\": ").Append(avgGoals.ToString("0.000", CultureInfo.InvariantCulture))
                .Append(",\n  \"avgGoalDeadBallSeconds\": ").Append(avgGoalDead.ToString("0.00", CultureInfo.InvariantCulture))
                .Append(",\n  \"avgRestartWaitSeconds\": ").Append(avgRestart.ToString("0.00", CultureInfo.InvariantCulture))
                .Append(",\n  \"goalDeadBalls\": ").Append(goalDeadN).Append(",\n  \"restarts\": ").Append(restartN).Append("\n}\n");
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "specs", "058-highlight-live-match", "evidence");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "mass-sim.json"), json.ToString());
            TestContext.WriteLine(json.ToString());
            // AC-15 보류 (2026-11-11 사용자 결정): 대체 판단(008·010 전)의 평균 득점이 058 이전부터 약 24골이라 범위 판정은 008·010·017 연결 때.
            // 지금은 수치만 남기고, 058이 득점을 늘리지 않았는지(main 30경기 24.5골)만 본다.
            Assert.That(avgGoals, Is.LessThanOrEqualTo(24.5d * 1.1d), $"평균 득점 {avgGoals:0.00} — 058 전(24.5)보다 10% 넘게 늘었다");
            Assert.That(goalDeadN, Is.GreaterThan(0));
            Assert.That(restartN, Is.GreaterThan(0));
        }
    }
}
