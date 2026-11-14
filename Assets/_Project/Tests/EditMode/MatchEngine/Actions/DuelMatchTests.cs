using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 T09: 90분 결정론, 태클 거리(AC-11 로직 부분), 경기 측정값(AC-10 보류 — 리포트용).</summary>
    public class DuelMatchTests
    {
        [Test, Category("Determinism")]
        public void Determinism_90min_TwoRuns_SameHashes()
        {
            var a = ShapeTestKit.Sim().RunToEnd();
            var b = ShapeTestKit.Sim().RunToEnd();
            Assert.AreEqual(a.EventLogHash, b.EventLogHash);
            Assert.AreEqual(a.FinalStateHash, b.FinalStateHash);
        }

        /// <summary>
        /// 태클 이벤트 순간 태클러–캐리어 거리(이벤트 F1, 판정 순간 값) ≤ 그 태클 도달 거리. 틱 끝 위치 거리는 참고로 최댓값만 기록. 주변에 상대가 없는 태클 0건 (AC-11 로직 부분, 008 FR-70).
        /// </summary>
        [Test, Timeout(900_000)]
        public void Tackles_CarrierWithinReach_20Matches()
        {
            var p = ActionTestKit.P;
            var bad = new int[20];
            var total = new int[20];
            var far = new float[20];
            System.Threading.Tasks.Parallel.For(0, 20, i =>
            {
                var sim = new MatchSim(ShapeTestKit.Setup(seed: 7300UL + (ulong)i), ShapeTestKit.TuningWithShape());
                var ctx = sim.Context;
                var from = 0;
                while (sim.StepTick())
                {
                    var log = ctx.Log;
                    for (int k = from; k < log.Count; k++)
                    {
                        var e = log[k];
                        if (e.Kind != SimEventKind.Tackle) continue;
                        total[i]++;
                        var s = ctx.Squad.SlotOf(e.PlayerId);
                        var t = e.OtherId >= 0 && ctx.Squad.Has(e.OtherId) ? ctx.Squad.SlotOf(e.OtherId) : -1;
                        if (s < 0 || t < 0) { bad[i]++; continue; }
                        var d = (ctx.SlotState(s).Position - ctx.SlotState(t).Position).Length();
                        var reach = TackleModel.Reach(p, (e.A & EventCodes.TackleSliding) != 0);
                        if (d > far[i]) far[i] = d;
                        if (!(e.F1 <= reach + 1e-3f)) bad[i]++;
                    }
                    from = log.Count;
                }
            });
            int sb = 0, st = 0;
            var mf = 0f;
            for (int i = 0; i < 20; i++) { sb += bad[i]; st += total[i]; mf = System.Math.Max(mf, far[i]); }
            TestContext.WriteLine($"태클 {st}건 (경기당 {st / 20.0:F1}), 틱 끝 최대 거리 {mf:F2} m, 거리 밖 {sb}");
            Assert.Greater(st, 0);
            Assert.AreEqual(0, sb, "캐리어가 도달 거리 밖인 태클 0건");
        }

        sealed class One
        {
            public double Goals, Shots, Passes, Received, TakeOns, TakeOnOk, Tackles, TacklesWon, Slides, Contacts, Interceptions, Duels, Possessions, Touches, Steals;
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
                    case SimEventKind.Shot: o.Shots++; break;
                    case SimEventKind.Pass: o.Passes++; break;
                    case SimEventKind.PassReceived: o.Received++; break;
                    case SimEventKind.Dribble: o.TakeOns++; if ((e.B & EventCodes.DribbleSuccess) != 0) o.TakeOnOk++; break;
                    case SimEventKind.Tackle: o.Tackles++; if ((e.A & EventCodes.TackleWon) != 0) o.TacklesWon++; if ((e.A & EventCodes.TackleSliding) != 0) o.Slides++; break;
                    case SimEventKind.Contact: o.Contacts++; break;
                    case SimEventKind.Interception: o.Interceptions++; break;
                    case SimEventKind.Duel: o.Duels++; break;
                    case SimEventKind.PossessionChange: o.Possessions++; break;
                }
            }
            o.Touches = sim.Context.Actions.Duels.Touches;
            o.Steals = sim.Context.Actions.Duels.Steals;
            return o;
        }

        /// <summary>경기 측정 (리포트 표용, 판정 없음 — AC-10은 017 뒤). 기본 100경기, `.farm/duel-n.txt`가 있으면 그 수.</summary>
        [Test, Explicit("리포트용 측정"), Category("Slow"), Timeout(3_600_000)]
        public void Report_100Matches_DuelMeasurements()
        {
            var n = 100;
            var np = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".farm", "duel-n.txt");
            if (System.IO.File.Exists(np)) n = int.Parse(System.IO.File.ReadAllText(np).Trim());
            ShapeTestKit.Setup(seed: 1UL);
            var res = new One[n];
            System.Threading.Tasks.Parallel.For(0, n, i => res[i] = RunOne(i));
            var s = new One();
            foreach (var x in res)
            {
                s.Goals += x.Goals; s.Shots += x.Shots; s.Passes += x.Passes; s.Received += x.Received; s.TakeOns += x.TakeOns; s.TakeOnOk += x.TakeOnOk;
                s.Tackles += x.Tackles; s.TacklesWon += x.TacklesWon; s.Slides += x.Slides; s.Contacts += x.Contacts; s.Interceptions += x.Interceptions;
                s.Duels += x.Duels; s.Possessions += x.Possessions; s.Touches += x.Touches; s.Steals += x.Steals;
            }
            var def = s.Tackles + s.Interceptions;
            TestContext.WriteLine($"경기 {n}, 경기당: 득점 {s.Goals / n:F2}, 슈팅 {s.Shots / n:F1}, 패스 {s.Passes / n:F0} (수신 {(s.Passes > 0 ? s.Received / s.Passes : 0):P0}), " +
                $"테이크온 {s.TakeOns / n:F1} (성공 {(s.TakeOns > 0 ? s.TakeOnOk / s.TakeOns : 0):P0}), 태클 {s.Tackles / n:F1} (성공 {(s.Tackles > 0 ? s.TacklesWon / s.Tackles : 0):P0}, 슬라이딩 {s.Slides / n:F1}), " +
                $"인터셉트 {s.Interceptions / n:F1}, 몸싸움 {s.Duels / n:F1}, 접촉 {s.Contacts / n:F1}, 드리블 터치 {s.Touches / n:F0} (이격 탈취 {s.Steals / n:F1}), 소유 변경 {s.Possessions / n:F0}, " +
                $"PPDA 근사(전 구역, 패스 ÷ 태클+인터셉트) {(def > 0 ? s.Passes / def : 0):F1}");
            Assert.Greater(s.Passes, 0);
        }
    }
}
