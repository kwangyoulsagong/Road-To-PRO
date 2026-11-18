using NUnit.Framework;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine
{
    /// <summary>specs/081 AC-01: 측정 도구가 같은 시드 묶음에 같은 지표를 내고, FR-01 지표가 모두 있다. 지표 정의는 고정 이벤트 로그로 확인.</summary>
    public class MatchFeelMetricsTests
    {
        static readonly string[] Fr01 =
        {
            "goals", "shots", "passes", "passSuccess", "takeOns", "takeOnSuccess", "tackles", "tackleSuccess", "dribbleSteals", "stealPerTouch",
            "possessionChanges", "seqSeconds", "ppda", "contacts", "onTargetRatio", "conversion", "xgPerShot", "gkSaveRate", "crosses", "crossSuccess",
            "contests", "contestsBothTeams", "contestsEmpty", "headers", "headerConversion", "goalsWithoutShot", "ownGoals",
        };

        static void Add(SimEventLog log, SimEventKind kind, int team, long tick, int a = 0, int b = 0, float f1 = 0f, BodyPart part = BodyPart.None)
        {
            var e = SimEvent.Make(kind, team, 1, -1);
            e.Tick = tick;
            e.A = a;
            e.B = b;
            e.F1 = f1;
            e.Part = part;
            log.Append(ref e);
        }

        static double Get(MatchFeelMetrics.Totals t, string key)
        {
            foreach (var kv in MatchFeelMetrics.PerMatch(t)) if (kv.Key == key) return kv.Value;
            Assert.Fail("지표 없음: " + key);
            return 0;
        }

        [Test]
        public void MatchFeel_AllFr01MetricsPresent()
        {
            var m = MatchFeelMetrics.PerMatch(new MatchFeelMetrics.Totals { Matches = 1 });
            Assert.AreEqual(Fr01.Length, m.Count);
            for (int i = 0; i < Fr01.Length; i++) Assert.AreEqual(Fr01[i], m[i].Key);
            var json = MatchFeelMetrics.ToJson(new MatchFeelMetrics.Totals { Matches = 1 }, "t", 7400, "abc");
            foreach (var k in Fr01) StringAssert.Contains("\"" + k + "\"", json);
        }

        [Test]
        public void MatchFeel_ShotsAndKeeper_Definitions()
        {
            var log = new SimEventLog(64);
            Add(log, SimEventKind.Shot, 0, 10, f1: 0.1f);                                    // 선방
            Add(log, SimEventKind.Save, 1, 11);
            Add(log, SimEventKind.Shot, 0, 20, a: ActionSystem.ShotFlagHeader, f1: 0.3f, part: BodyPart.Head); // 헤딩 골
            Add(log, SimEventKind.Goal, 0, 21);
            Add(log, SimEventKind.Shot, 1, 30, f1: 0.2f);                                    // 빗나감
            Add(log, SimEventKind.Shot, 1, 40, a: ActionSystem.ShotFlagHeader, f1: 0.0f, part: BodyPart.Head); // 헤딩 빗나감
            Add(log, SimEventKind.GoalKick, 0, 41);
            Add(log, SimEventKind.Shot, 0, 50, f1: 0.2f);                                    // 선방 + 캐치 (이벤트 2개 = 유효 1)
            Add(log, SimEventKind.Save, 1, 51);
            Add(log, SimEventKind.Catch, 1, 51);
            Add(log, SimEventKind.Goal, 1, 60, a: 1);                                        // 슈팅 없는 자책골
            var t = MatchFeelMetrics.Count(log);
            Assert.AreEqual(5, Get(t, "shots"));
            Assert.AreEqual(2, Get(t, "goals"));
            Assert.AreEqual(0.6, Get(t, "onTargetRatio"), 1e-9);    // 슈팅별: 선방·골·선방 = 3 / 5
            Assert.AreEqual(0.2, Get(t, "conversion"), 1e-9);       // 슈팅 골 1 / 5
            Assert.AreEqual(0.16, Get(t, "xgPerShot"), 1e-6);
            Assert.AreEqual(2.0 / 3.0, Get(t, "gkSaveRate"), 1e-9); // 유효 3 중 막음 2
            Assert.AreEqual(0.5, Get(t, "headerConversion"), 1e-9); // 헤딩 골 1 / 헤딩슛 2
            Assert.AreEqual(1, Get(t, "goalsWithoutShot"));
            Assert.AreEqual(1, Get(t, "ownGoals"));
        }

        [Test]
        public void MatchFeel_PassAndCross_FatesAndSuccess()
        {
            var log = new SimEventLog(64);
            Add(log, SimEventKind.Pass, 0, 1);
            Add(log, SimEventKind.PassReceived, 0, 2);       // 받음
            Add(log, SimEventKind.Pass, 0, 3);
            Add(log, SimEventKind.Interception, 1, 4);       // 끊김
            Add(log, SimEventKind.Pass, 1, 5);
            Add(log, SimEventKind.ThrowIn, 0, 6);            // 라인 밖
            Add(log, SimEventKind.Pass, 1, 7);
            Add(log, SimEventKind.BallRecovered, 0, 8);      // 루즈볼 상대
            Add(log, SimEventKind.Cross, 0, 9);
            Add(log, SimEventKind.Header, 0, 10);            // 같은 팀 공중볼 → 크로스 성공
            Add(log, SimEventKind.Cross, 1, 11);
            Add(log, SimEventKind.Clearance, 0, 12, part: BodyPart.Head); // 상대 헤딩 클리어 → 실패
            var t = MatchFeelMetrics.Count(log);
            Assert.AreEqual(4, Get(t, "passes"));
            Assert.AreEqual(0.25, Get(t, "passSuccess"), 1e-9);
            Assert.AreEqual(2, Get(t, "crosses"));
            Assert.AreEqual(0.5, Get(t, "crossSuccess"), 1e-9);
            Assert.AreEqual(1, t.PassFates[(int)MatchFeelMetrics.PassFate.Received]);
            Assert.AreEqual(1, t.PassFates[(int)MatchFeelMetrics.PassFate.Intercepted]);
            Assert.AreEqual(1, t.PassFates[(int)MatchFeelMetrics.PassFate.Out]);
            Assert.AreEqual(1, t.PassFates[(int)MatchFeelMetrics.PassFate.LostLoose]);
        }

        static void AddAt(SimEventLog log, SimEventKind kind, int team, long tick, float x, int a = 0, int b = 0)
        {
            var e = SimEvent.Make(kind, team, 1, -1);
            e.Tick = tick;
            e.X = x;
            e.A = a;
            e.B = b;
            log.Append(ref e);
        }

        static void AddZ(SimEventLog log, SimEventKind kind, int team, long tick, float z)
        {
            var e = SimEvent.Make(kind, team, 1, -1);
            e.Tick = tick;
            e.Z = z;
            log.Append(ref e);
        }

        static double Diag(MatchFeelMetrics.Totals t, string key)
        {
            foreach (var kv in MatchFeelMetrics.Diagnostics(t)) if (kv.Key == key) return kv.Value;
            Assert.Fail("진단 칸 없음: " + key);
            return 0;
        }

        /// <summary>084 T02: 조준 행별 선방률, 크로스 타입·골라인 거리, 컷백 골 정의 (고정 로그).</summary>
        [Test]
        public void MatchFeel_084Diagnostics_RowSaveAndCutback()
        {
            var log = new SimEventLog(64);
            AddZ(log, SimEventKind.Shot, 0, 10, 0f);
            AddZ(log, SimEventKind.Save, 1, 11, 0.3f);    // 낮음 → 선방
            AddZ(log, SimEventKind.Shot, 0, 20, 0f);
            AddZ(log, SimEventKind.Goal, 0, 21, 2.1f);    // 높음 → 골
            AddZ(log, SimEventKind.Shot, 0, 30, 0f);
            AddZ(log, SimEventKind.Catch, 1, 31, 1.9f);   // 높음 → 선방
            var cut = ((int)CrossType.Cutback + 1) << ActionSystem.CrossTypeShift;
            var lofted = ((int)CrossType.Lofted + 1) << ActionSystem.CrossTypeShift;
            AddAt(log, SimEventKind.Cross, 0, 40, 101f / 105f, a: cut);       // 골라인 4 m, 컷백
            AddAt(log, SimEventKind.PassReceived, 0, 41, 95f);
            AddAt(log, SimEventKind.Shot, 0, 45, 95f);
            AddAt(log, SimEventKind.Goal, 0, 60, 105f);                       // 컷백 골
            AddAt(log, SimEventKind.Cross, 1, 70, 8f / 105f, a: lofted);      // 골라인 8 m (반대편), 띄운 크로스
            var t = MatchFeelMetrics.Count(log);
            Assert.AreEqual(0.5, Diag(t, "saveRateLow"), 1e-9);  // 낮음: 선방 1 + 컷백 골(Z 0) 1
            Assert.AreEqual(0.5, Diag(t, "saveRateHigh"), 1e-9);
            Assert.AreEqual(0.0, Diag(t, "onTargetMid"), 1e-9);
            Assert.AreEqual(1, Diag(t, "crossCutback"));
            Assert.AreEqual(1, Diag(t, "crossLofted"));
            Assert.AreEqual(1, Diag(t, "crossNear6"));
            Assert.AreEqual(2, Diag(t, "crossNear10"));
            Assert.AreEqual(1, Diag(t, "cutbackGoals"));
            StringAssert.Contains("\"diag\"", MatchFeelMetrics.ToJson(t, "t", 7400, "abc"));
        }

        [Test]
        public void MatchFeel_Sequences_AverageSeconds()
        {
            var log = new SimEventLog(16);
            var tps = SimClock.TicksPerSecond;
            Add(log, SimEventKind.PossessionChange, 0, 0);
            Add(log, SimEventKind.PossessionChange, 1, 4 * tps);
            Add(log, SimEventKind.PossessionChange, 0, 12 * tps);
            var t = MatchFeelMetrics.Count(log);
            Assert.AreEqual(3, Get(t, "possessionChanges"));
            Assert.AreEqual(6.0, Get(t, "seqSeconds"), 1e-9); // (4 + 8) / 2
        }

        [Test]
        public void MatchFeel_DuelsAndPpda_Definitions()
        {
            var log = new SimEventLog(16);
            Add(log, SimEventKind.Dribble, 0, 1, b: EventCodes.DribbleSuccess);
            Add(log, SimEventKind.Dribble, 0, 2);
            Add(log, SimEventKind.Tackle, 1, 3, a: EventCodes.TackleWon);
            Add(log, SimEventKind.Interception, 1, 4);
            Add(log, SimEventKind.Pass, 0, 5);
            Add(log, SimEventKind.Pass, 0, 6);
            var t = MatchFeelMetrics.Count(log);
            t.Touches = 10; t.Steals = 2;
            Assert.AreEqual(2, Get(t, "takeOns"));
            Assert.AreEqual(0.5, Get(t, "takeOnSuccess"), 1e-9);
            Assert.AreEqual(1, Get(t, "tackleSuccess"), 1e-9);
            Assert.AreEqual(0.2, Get(t, "stealPerTouch"), 1e-9);
            Assert.AreEqual(1.0, Get(t, "ppda"), 1e-9); // 패스 2 / (태클 1 + 인터셉트 1)
        }

        [Test, Timeout(1_200_000)]
        public void MatchFeel_SameSeeds_SameMetrics()
        {
            var a = MatchFeelReportTests.Run(4, 7400UL);
            var b = MatchFeelReportTests.Run(4, 7400UL);
            Assert.AreEqual(MatchFeelMetrics.ToJson(a, "x", 7400, "-"), MatchFeelMetrics.ToJson(b, "x", 7400, "-"));
            Assert.Greater(a.Passes, 0);
        }
    }
}
