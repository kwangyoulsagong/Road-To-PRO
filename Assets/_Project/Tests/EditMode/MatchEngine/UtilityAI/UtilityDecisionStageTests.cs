using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Scenarios;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 T12: 단계 연결 — 22명 기본 실행·배역 경계·마크 주기·키 모먼트·스냅샷 (AC-13·23·29).</summary>
    public class UtilityDecisionStageTests
    {
        static UtilityDecisionStage StageOf(MatchSim sim)
        {
            var st = sim.Stage(StageId.ScenarioOrUtility);
            if (st is ScenarioStage sc) return (UtilityDecisionStage)sc.DecisionStage;
            return (UtilityDecisionStage)st;
        }

        static MatchSim ScenarioSim(MatchSimOptions o = null) => ScenarioTestKit.Sim(ScenarioTestKit.DataOf(ScenarioTestKit.Samples()), o);

        [Test]
        public void Pipeline_TacticsUse010_StandInWithout()
        {
            Assert.That(ShapeTestKit.Sim().Stage(StageId.ScenarioOrUtility), Is.TypeOf<UtilityDecisionStage>());
            Assert.That(((ScenarioStage)ScenarioSim().Stage(StageId.ScenarioOrUtility)).DecisionStage, Is.TypeOf<UtilityDecisionStage>());
            Assert.That(Match.MatchTestKit.Sim().Stage(StageId.ScenarioOrUtility), Is.Not.TypeOf<UtilityDecisionStage>(), "전술 없는 006 대체 파이프라인은 그대로");
        }

        [Test, Category("Slow")]
        public void Headless_90min_All22HaveDecision_NoOpenPlayDefenceIntent_GkFrom011_AC29()
        {
            var sim = ScenarioSim();
            var stage = StageOf(sim);
            var rt = ScenarioTestKit.Runtime(sim);
            int missing = 0, defenceIntent = 0, gkWrong = 0, decidedGk = 0;
            sim.StepTicks(2);
            while (sim.StepTick())
            {
                var v = sim.View;
                var w = stage.World;
                for (int s = 0; s < 22; s++)
                {
                    if (!v.IsSlotActive(s)) continue;
                    if (stage.DecisionOf(s).Kind == DecisionKind.Keep) missing++;
                    if (w.Intent[s].Has && v.State == FlowState.InPlay && rt.TryGetCast(s, out var run, out _) && !run.Def.IsSetPiece && w.PossTeam >= 0 && DecisionWorld.TeamOf(s) != w.PossTeam)
                        defenceIntent++;
                    var b = v.Ball;
                    if (Squad.IsKeeperSlot(s) && v.State == FlowState.InPlay && !(b.Phase == BallPhase.Controlled && b.OwnerId == s) && stage.State(s).LastDecisionTick == stage.World.Tick)
                    {
                        decidedGk++;
                        if (stage.State(s).Source <= DecisionSource.Opportunistic || stage.State(s).Source == DecisionSource.KeyMoment) gkWrong++; // 010 오프볼이 GK를 정하면 안 된다 (공 찬 직후 틱은 대체 온볼)
                    }
                }
            }
            Assert.AreEqual(0, missing, "결정 누락");
            Assert.AreEqual(0, defenceIntent, "오픈플레이 수비 쪽 의도 0");
            Assert.Greater(decidedGk, 1000);
            Assert.AreEqual(0, gkWrong, "GK 결정은 011");
        }

        [Test]
        public void Cast_EveryTickDecision_SourceFlag_EndTickGeneralWithShape_AC23()
        {
            var sim = ScenarioTestKit.Forced(ScenarioTestKit.DataOf(ScenarioTestKit.Lib(ScenarioTestKit.Mini("RUN-01", ScenarioTestKit.RunCast))), "RUN-01");
            var stage = StageOf(sim);
            var rt = ScenarioTestKit.Runtime(sim);
            var wasCast = new bool[22];
            for (int s = 0; s < 22; s++) wasCast[s] = rt.IsCast(s);
            int castChecks = 0, endChecks = 0;
            // 070: 드리블 터치로 배역 캐리가 공을 라인 밖으로 내보내 첫 배역이 데드볼로 끝나는 시드가 있어 표본 구간을 400 → 1,500틱으로
            for (int k = 0; k < 1500 && (endChecks == 0 || k < 400); k++)
            {
                sim.StepTick();
                var v = sim.View;
                var b = v.Ball;
                for (int s = 0; s < 22; s++)
                {
                    var owns = b.Phase == BallPhase.Controlled && b.OwnerId == s;
                    var cast = rt.IsCast(s);
                    ref readonly var st = ref stage.State(s);
                    // 072: 이번 틱 시작에 공을 가졌던 배역(결정 뒤 차서 지금은 소유자가 아님)은 온볼 판단이 정한다
                    if (v.State != FlowState.InPlay || owns || Squad.IsKeeperSlot(s) || stage.World.Owner == s) { wasCast[s] = cast; continue; }
                    if (cast && stage.World.Intent[s].Has)
                    {
                        castChecks++;
                        Assert.AreEqual(stage.World.Tick, st.LastDecisionTick, $"배역 {s} 매 틱 결정");
                        Assert.IsTrue(st.Source == DecisionSource.IntentMatch || st.Source == DecisionSource.IntentIgnored || st.Source == DecisionSource.Opportunistic, st.Source.ToString());
                    }
                    if (wasCast[s] && !cast && st.Source != DecisionSource.Opportunistic && st.Source != DecisionSource.OnBall) // 공을 받아 소유자가 되면 대체 온볼
                    {
                        endChecks++;
                        Assert.AreEqual(stage.World.Tick, st.LastDecisionTick, $"배역 종료 {s} 같은 틱 결정");
                        Assert.AreEqual(DecisionSource.General, st.Source);
                        Assert.IsTrue(stage.World.HasShape[s], "종료 틱에 009 형태 목표");
                    }
                    wasCast[s] = cast;
                }
            }
            Assert.Greater(castChecks, 10, "배역 결정 표본");
            Assert.Greater(endChecks, 0, "배역 종료 표본");
        }

        [Test]
        public void Mark_NoSwitchBetweenReassign_AC23()
        {
            var sim = ShapeTestKit.Sim();
            var stage = StageOf(sim);
            var prev = new int[22];
            for (int s = 0; s < 22; s++) prev[s] = -2;
            long lastChange = -100;
            int lastPoss = -2, changes = 0;
            for (int k = 0; k < 3000; k++)
            {
                sim.StepTick();
                var w = stage.World;
                var changed = false;
                for (int s = 0; s < 22; s++) { if (w.MarkTarget[s] != prev[s]) changed = true; prev[s] = w.MarkTarget[s]; }
                if (!changed) continue;
                changes++;
                if (w.PossTeam == lastPoss && k > 0) Assert.GreaterOrEqual(k - lastChange, 5, $"tick {k}: 재배정 주기(0.5 s) 사이 변경");
                lastChange = k;
                lastPoss = w.PossTeam;
            }
            Assert.Greater(changes, 3);
        }

        [Test]
        public void KeyMoment_OverridesNextDecisionOnly_AC13()
        {
            var a = ShapeTestKit.Sim();
            var b = ShapeTestKit.Sim();
            a.StepTicks(600);
            b.StepTicks(600);
            var sa = StageOf(a);
            int slot = -1;
            var v = a.View;
            for (int s = 1; s < 22; s++)
            {
                if (Squad.IsKeeperSlot(s) || !v.IsSlotActive(s)) continue;
                var ball = v.Ball;
                if (ball.Phase == BallPhase.Controlled && ball.OwnerId == s) continue;
                slot = s;
                break;
            }
            Assume.That(v.State, Is.EqualTo(FlowState.InPlay));
            var target = new Vec2(52f, 30f);
            sa.KeyMoments.Set(slot, Consideration.DropToReceive, target);
            a.StepTick();
            b.StepTick();
            Assert.AreEqual(DecisionSource.KeyMoment, sa.State(slot).Source);
            Assert.AreEqual(Consideration.DropToReceive, sa.State(slot).Kind);
            Assert.AreEqual(target, sa.DecisionOf(slot).Point);
            var sb = StageOf(b);
            for (int s = 0; s < 22; s++)
                if (s != slot) Assert.AreEqual(sb.DecisionOf(s).Kind, sa.DecisionOf(s).Kind, $"다른 선수 {s} 결정 영향 없음");
            Assert.IsFalse(sa.KeyMoments.Has(slot), "1회만");
        }

        [Test]
        public void Snapshot_RoundTrip_DecisionState()
        {
            var o = new MatchSimOptions { Checkpoints = CheckpointMode.All };
            var a = ScenarioSim(o);
            a.RunUntilTick(9_000);
            var snap = a.NearestSnapshot(6_000);
            var mid = a.StateHash();
            var fork = a.Fork();
            fork.Restore(snap, a.Log, a.Checkpoints);
            fork.RunUntilTick(9_000);
            Assert.AreEqual(mid, fork.StateHash());
        }

        [Test]
        public void OffBall_NotAllSprinting_HoldShapeHasSpeedCap()
        {
            var sim = ShapeTestKit.Sim();
            var stage = StageOf(sim);
            int capped = 0, holds = 0, sprints = 0, offball = 0;
            for (int k = 0; k < 3000; k++)
            {
                sim.StepTick();
                for (int s = 0; s < 22; s++)
                {
                    ref readonly var st = ref stage.State(s);
                    if (st.LastDecisionTick != stage.World.Tick || st.Source != DecisionSource.General) continue;
                    offball++;
                    if (st.Intensity == Intensity.Sprint) sprints++;
                    ref readonly var d = ref stage.DecisionOf(s);
                    if (st.Kind != Consideration.HoldShape) continue; // 형태 유지 = 2 m 제한 목표점으로 MoveTo (FR-46)
                    holds++;
                    if (d.MaxSpeed > 0f && d.MaxSpeed < stage.World.Profile[s].MaxSpeed) capped++;
                }
            }
            Assert.Greater(holds, 100);
            Assert.AreEqual(holds, capped, "형태 유지는 최고 속도 미만 상한 (전력질주 기본값 아님)");
            Assert.Less(sprints, offball / 2, $"오프볼 질주 {sprints}/{offball}");
            TestContext.WriteLine($"오프볼 결정 {offball}, 질주 {sprints} ({100.0 * sprints / offball:F1} %), 형태 유지 {holds}");
        }
    }
}
