using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/072 T09: 90분 결정론(AC-11), 100경기 측정값(리포트용 — 판정 아님, 017 보정 대상).</summary>
    public class OnBallMatchTests
    {
        [Test, Category("Determinism")]
        public void Determinism_90min_OnBallHashSame_DiagnosticsOnOff_AC11()
        {
            DecisionRecorder ra = null, rb = null;
            var a = ShapeTestKit.Sim(new MatchSimOptions { DecisionDiagnostics = () => ra = new DecisionRecorder() }).RunToEnd();
            var b = ShapeTestKit.Sim(new MatchSimOptions { DecisionDiagnostics = () => rb = new DecisionRecorder() }).RunToEnd();
            var c = ShapeTestKit.Sim().RunToEnd(); // 진단 끔
            Assert.Greater(ra.OnBallDecisions, 300);
            Assert.AreEqual(ra.OnBallDecisions, rb.OnBallDecisions);
            Assert.AreEqual(ra.Hash, rb.Hash, "온볼 결정(선수·틱·후보 집합·점수·선택) 해시");
            Assert.AreEqual(a.EventLogHash, b.EventLogHash);
            Assert.AreEqual(a.FinalStateHash, b.FinalStateHash);
            Assert.AreEqual(a.EventLogHash, c.EventLogHash, "진단 켬/끔이 이벤트 해시를 바꾸지 않는다");
            Assert.AreEqual(a.FinalStateHash, c.FinalStateHash);
        }

        sealed class One
        {
            public double Goals, Shots, Passes, Received, Dribbles, DribbleOk, Dives, Ignored, Possessions;
            public readonly long[] Items = new long[OnBallItems.Count];
        }

        static One RunOne(int i, MatchTuning tu)
        {
            DecisionTelemetry tel = null;
            var sim = new MatchSim(ShapeTestKit.Setup(seed: 7200UL + (ulong)i), tu, new MatchSimOptions { DecisionDiagnostics = () => tel = new DecisionTelemetry(1) });
            var r = sim.RunToEnd();
            var o = new One { Goals = r.RegularGoals[0] + r.RegularGoals[1] };
            o.Shots = MatchTestKit.Count(sim, SimEventKind.Shot);
            o.Passes = MatchTestKit.Count(sim, SimEventKind.Pass);
            o.Received = MatchTestKit.Count(sim, SimEventKind.PassReceived);
            o.Dives = MatchTestKit.Count(sim, SimEventKind.DiveAttempt);
            o.Ignored = MatchTestKit.Count(sim, SimEventKind.InstructionIgnored);
            o.Possessions = MatchTestKit.Count(sim, SimEventKind.PossessionChange);
            var log = sim.Log;
            for (int k = 0; k < log.Count; k++)
            {
                var e = log[k];
                if (e.Kind != SimEventKind.Dribble) continue;
                o.Dribbles++;
                if ((e.B & EventCodes.DribbleSuccess) != 0) o.DribbleOk++;
            }
            for (int s = 0; s < 22; s++)
                for (int k = 0; k < OnBallItems.Count; k++) o.Items[k] += tel.OnBallAttempts[s, k];
            return o;
        }

        /// <summary>100경기 측정 (리포트 표용, 판정 없음). 경기 하나는 단일 스레드, 경기 여러 개만 병렬(006 FR-71).</summary>
        [Test, Explicit("리포트용 측정"), Category("Slow"), Timeout(1_200_000)]
        public void Report_100Matches_OnBallMeasurements()
        {
            const int n = 100;
            var tu = ShapeTestKit.TuningWithShape();
            ShapeTestKit.Setup(seed: 1UL);
            var res = new One[n];
            System.Threading.Tasks.Parallel.For(0, n, i => res[i] = RunOne(i, tu));
            var sum = new One();
            foreach (var x in res)
            {
                sum.Goals += x.Goals; sum.Shots += x.Shots; sum.Passes += x.Passes; sum.Received += x.Received;
                sum.Dribbles += x.Dribbles; sum.DribbleOk += x.DribbleOk; sum.Dives += x.Dives; sum.Ignored += x.Ignored; sum.Possessions += x.Possessions;
                for (int k = 0; k < OnBallItems.Count; k++) sum.Items[k] += x.Items[k];
            }
            TestContext.WriteLine($"경기당: 득점 {sum.Goals / n:F2}, 슈팅 {sum.Shots / n:F1}, 패스 {sum.Passes / n:F0}, 패스 수신 {sum.Received / n:F0} (성공 {(sum.Passes > 0 ? sum.Received / sum.Passes : 0):P0}), " +
                                  $"돌파 {sum.Dribbles / n:F1} (성공 {(sum.Dribbles > 0 ? sum.DribbleOk / sum.Dribbles : 0):P0}), 넘어지기 {sum.Dives / n:F2}, 지시 무시 {sum.Ignored / n:F2}, 소유 변경 {sum.Possessions / n:F0}");
            var line = "온볼 선택 경기당:";
            for (int k = 0; k < OnBallItems.Count; k++) line += $" {(OnBallItem)k} {sum.Items[k] / (double)n:F1}";
            TestContext.WriteLine(line);
            Assert.Greater(sum.Passes, 0);
        }
    }
}
