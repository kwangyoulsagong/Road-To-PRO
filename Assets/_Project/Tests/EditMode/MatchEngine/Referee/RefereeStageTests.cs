using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Result;
using RoadToPro.MatchEngine.Referee;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 T22·T24: 실경기 심판 단계 통합 (AC-09·16 + 재시작·오프사이드·어드밴티지·카드 불변식).</summary>
    public class RefereeStageTests
    {
        [Test, Category("Slow")]
        public void Live_90min_DecisionsFollowRules()
        {
            int fouls = 0, cards = 0, offsides = 0, fks = 0, pks = 0, adv = 0, varChecks = 0, overturns = 0;
            foreach (var sim in LiveReferee.Played())
            {
                Assert.AreEqual(EndReason.Normal, sim.Context.Flow.EndReason, "예외·실패 없이 90분");
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    ref readonly var e = ref log[i];
                    switch (e.Kind)
                    {
                        case SimEventKind.Foul:
                            fouls++;
                            if ((e.B & EventCodes.RefAdvantage) != 0) break;
                            Assert.IsTrue(RestartFollows(log, i, e.Tick), $"휘슬 반칙 뒤 같은 틱에 재시작·VAR·골 취소 ({e})");
                            break;
                        case SimEventKind.Card: cards++; break;
                        case SimEventKind.Offside:
                            offsides++;
                            Assert.IsTrue(RestartFollows(log, i, e.Tick), $"오프사이드 뒤 간접 FK ({e})");
                            break;
                        case SimEventKind.FreeKick:
                            fks++;
                            Assert.That(e.A, Is.EqualTo(1).Or.EqualTo(2), "직접·간접 표시");
                            break;
                        case SimEventKind.Penalty: pks++; break;
                        case SimEventKind.AdvantagePlayed: adv++; break;
                        case SimEventKind.VarCheck: varChecks++; break;
                        case SimEventKind.VarOverturn: overturns++; break;
                    }
                }
            }
            var n = LiveReferee.Matches;
            TestContext.WriteLine($"{n}경기 경기당: 파울 {fouls / (double)n:F1} · 카드 {cards / (double)n:F2} · 오프사이드 {offsides / (double)n:F2} · FK {fks / (double)n:F1} · PK {pks / (double)n:F2} · 어드밴티지 {adv / (double)n:F2} · VAR {varChecks / (double)n:F2} · 번복 {overturns / (double)n:F2}");
            Assert.Greater(fouls, 0, "실경기에서 파울이 분다");
            Assert.Greater(fks, 0);
        }

        static bool RestartFollows(SimEventLog log, int i, long tick)
        {
            for (int k = i + 1; k < log.Count && log[k].Tick == tick; k++)
            {
                var kk = log[k].Kind;
                if (kk == SimEventKind.FreeKick || kk == SimEventKind.Penalty || kk == SimEventKind.VarOverturn || kk == SimEventKind.Corner || kk == SimEventKind.ThrowIn || kk == SimEventKind.GoalKick) return true;
            }
            // 대체 진행: 이미 데드볼(같은 틱 앞 사건)일 수 있다 — 앞쪽에 재시작
            for (int k = i - 1; k >= 0 && log[k].Tick == tick; k--)
            {
                var kk = log[k].Kind;
                if (kk == SimEventKind.FreeKick || kk == SimEventKind.Penalty || kk == SimEventKind.Goal) return true;
            }
            return false;
        }

        [Test, Category("Slow")]
        public void Live_SecondYellows_AreSentOff()
        {
            foreach (var sim in LiveReferee.Played())
            {
                var log = sim.Log;
                foreach (var i in LiveReferee.Indices(log, SimEventKind.Card))
                {
                    ref readonly var e = ref log[i];
                    if (e.A != EventCodes.CardRed || e.PlayerId < 0) continue;
                    Assert.AreEqual(PlayerStatus.SentOff, sim.Context.Squad.StatusOf(e.PlayerId), $"퇴장 선수 상태 ({e})");
                }
            }
        }

        static MatchSim Tactical(ulong seed, RefereeScript script = null) =>
            new MatchSim(ShapeTestKit.Setup(seed: seed), ShapeTestKit.TuningWithShape(), script == null ? null : new MatchSimOptions { Referee = script });

        [Test]
        public void Law12_SecondYellow_SendOff_NoSub_CountDrops()
        {
            var sim = Tactical(7401);
            sim.StepTicks(300);
            var stage = LiveReferee.Stage(sim);
            var id = sim.Context.PlayerAtSlot(5);
            var bookedBefore = stage.State.Discipline.Yellows(id);
            if (bookedBefore > 0) Assert.Inconclusive("시드가 이미 경고를 줬다");
            stage.ReportConduct(sim.Context, ConductKind.Dissent, 0, id);
            stage.ReportConduct(sim.Context, ConductKind.Dissent, 0, id);
            sim.StepTicks(2);
            Assert.AreEqual(PlayerStatus.SentOff, sim.Context.Squad.StatusOf(id), "두 번째 경고 → 즉시 퇴장");
            Assert.AreEqual(10, sim.Context.Squad.OnPitchCount(0), "필드 인원 1 감소");
            var reds = 0;
            foreach (var i in LiveReferee.Indices(sim.Log, SimEventKind.Card))
                if (sim.Log[i].PlayerId == id && sim.Log[i].A == EventCodes.CardRed && sim.Log[i].B == EventCodes.ReasonSecondYellow) reds++;
            Assert.AreEqual(1, reds, "두 번째 경고 퇴장 카드 1장");
            // 교체 불가: 퇴장 선수를 빼는 교체는 거부
            var bench = sim.Setup.Home.Bench[0];
            sim.RequestSubstitution(0, id, bench);
            sim.StepTicks(600);
            Assert.AreEqual(PlayerStatus.SentOff, sim.Context.Squad.StatusOf(id));
            Assert.AreNotEqual(PlayerStatus.OnPitch, sim.Context.Squad.StatusOf(bench) == PlayerStatus.OnPitch && sim.Context.Squad.OnPitchCount(0) > 10 ? PlayerStatus.OnPitch : PlayerStatus.Bench, "퇴장 자리 교체로 11명 복구 안 됨");
        }

        [Test]
        public void Law3_SixPlayers_AbandonEventOnce()
        {
            var setup = ShapeTestKit.Setup(seed: 7402);
            var script = new RefereeScript();
            for (int k = 1; k <= 5; k++) script.Add(new RefereeAction(200 + k, RefereeActionKind.SendOff, setup.Home.Starters[k]));
            var sim = new MatchSim(setup, ShapeTestKit.TuningWithShape(), new MatchSimOptions { Referee = script });
            sim.RunToEnd();
            Assert.AreEqual(1, LiveReferee.Count(sim.Log, SimEventKind.PlayersBelowMinimum), "6명이 되는 순간 중단 이벤트 1개");
            Assert.AreEqual(EndReason.Abandoned, sim.Context.Flow.EndReason);
        }

        [Test]
        public void Script_StillRuns_UnderRealReferee()
        {
            var setup = ShapeTestKit.Setup(seed: 7403);
            var script = new RefereeScript();
            script.Add(new RefereeAction(150, RefereeActionKind.Booking, setup.Away.Starters[4]));
            var sim = new MatchSim(setup, ShapeTestKit.TuningWithShape(), new MatchSimOptions { Referee = script });
            sim.StepTicks(160);
            var found = false;
            foreach (var i in LiveReferee.Indices(sim.Log, SimEventKind.Card)) if (sim.Log[i].Tick == 150 && sim.Log[i].PlayerId == setup.Away.Starters[4]) found = true;
            Assert.IsTrue(found, "예정 사건 스크립트(006 대체 계약)는 실제 심판 단계에서도 실행");
        }
    }
}
