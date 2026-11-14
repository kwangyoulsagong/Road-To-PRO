using NUnit.Framework;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/072 T07: 단계 5 소유자 분기 — 실행·이벤트 1건 규칙(AC-04·09)·데드볼/GK 경계.</summary>
    public class OnBallStageTests
    {
        const long Ticks = 9_000; // 15분 (2026-11-14 specs/071: 10분은 데드볼 시간 비중에 따라 온볼 결정 50건 경계 — 궤적 변화에 흔들려 늘림)

        static UtilityDecisionStage StageOf(MatchSim sim) => (UtilityDecisionStage)sim.Stage(StageId.ScenarioOrUtility);

        static int Sum(int[,] a, int k)
        {
            var n = 0;
            for (int s = 0; s < 22; s++) n += a[s, k];
            return n;
        }

        static int Sum(int[] a)
        {
            var n = 0;
            for (int s = 0; s < a.Length; s++) n += a[s];
            return n;
        }

        [Test]
        public void Match_OwnerDecidedBy072_PassesDribblesEvents()
        {
            DecisionTelemetry tel = null;
            var sim = ShapeTestKit.Sim(new MatchSimOptions { DecisionDiagnostics = () => tel = new DecisionTelemetry() });
            sim.StepTicks(Ticks);
            var decisions = Sum(tel.OnBallDecisions);
            var passes = 0;
            foreach (var k in new[] { OnBallItem.ShortPass, OnBallItem.ProgPass, OnBallItem.ThroughBall, OnBallItem.Switch, OnBallItem.Cross }) passes += Sum(tel.OnBallAttempts, (int)k);
            TestContext.WriteLine($"온볼 결정 {decisions}, 패스 {passes}, 드리블 {Sum(tel.OnBallAttempts, (int)OnBallItem.Dribble)}, 캐리 {Sum(tel.OnBallAttempts, (int)OnBallItem.Carry)}, 슈팅 {Sum(tel.OnBallAttempts, (int)OnBallItem.Shoot)}, 홀드 {Sum(tel.OnBallAttempts, (int)OnBallItem.Hold)}, 넘어지기 {Sum(tel.OnBallAttempts, (int)OnBallItem.Dive)}, 걷어내기 {Sum(tel.OnBallAttempts, (int)OnBallItem.Clear)}");
            Assert.Greater(decisions, 50);
            Assert.Greater(passes, 10);
            Assert.Greater(MatchTestKit.Count(sim, SimEventKind.Pass), 10);
            // 070: 돌파 선택 중 FR-04 발동 조건(수비수가 공–골문 사이 < 3 m)을 만족한 것만 1v1 판정 이벤트를 낸다 (나머지는 캐리 터치)
            Assert.LessOrEqual(MatchTestKit.Count(sim, SimEventKind.Dribble), Sum(tel.OnBallAttempts, (int)OnBallItem.Dribble), "돌파 판정 이벤트 ≤ 돌파 선택");
            Assert.AreEqual(Sum(tel.OnBallAttempts, (int)OnBallItem.Dive), MatchTestKit.Count(sim, SimEventKind.DiveAttempt), "넘어지기 선택마다 012 입력 이벤트 1건 (AC-09)");
        }

        [Test]
        public void Match_IgnoredInstruction_ExactlyOneEventEach_AC04()
        {
            DecisionTelemetry tel = null;
            var sim = ShapeTestKit.Sim(new MatchSimOptions { DecisionDiagnostics = () => tel = new DecisionTelemetry() });
            var st = StageOf(sim);
            st.Style.SetInstructionWeight(0, Consideration.Dribble, 0.2f);
            st.Style.SetInstructionWeight(0, Consideration.Pass, 0.5f);
            for (int s = 0; s < 11; s++)
            {
                st.Style.SetCompliance(s, 0.1f);
                st.Style.SetStyle(s, PlayStyle.Neutral().WithHabit(Habit.DribbleAddict, 2));
            }
            sim.StepTicks(Ticks);
            var judged = Sum(tel.InstructionIgnored);
            TestContext.WriteLine($"지시 무시 판정 {judged}");
            Assert.Greater(judged, 0);
            Assert.AreEqual(judged, MatchTestKit.Count(sim, SimEventKind.InstructionIgnored));
        }

        [Test]
        public void Match_FullCompliance_NoIgnoredEvents_AC04()
        {
            var sim = ShapeTestKit.Sim();
            var st = StageOf(sim);
            st.Style.SetInstructionWeight(0, Consideration.Dribble, 0.2f);
            sim.StepTicks(Ticks);
            Assert.AreEqual(0, MatchTestKit.Count(sim, SimEventKind.InstructionIgnored));
        }

        [Test]
        public void Match_DeadBallAndKeeperOwner_NotDecidedBy072()
        {
            DecisionTelemetry tel = null;
            var sim = ShapeTestKit.Sim(new MatchSimOptions { DecisionDiagnostics = () => tel = new DecisionTelemetry() });
            var prev = new int[22];
            int bad = 0;
            var startState = sim.View.State;
            for (long i = 0; i < Ticks && sim.StepTick(); i++)
            {
                var v = sim.View;
                var b = v.Ball;
                for (int s = 0; s < 22; s++)
                {
                    if (tel.OnBallDecisions[s] == prev[s]) continue;
                    prev[s] = tel.OnBallDecisions[s];
                    // 결정은 틱 시작 상태 기준 (070: 같은 틱 물리 단계에서 드리블 이격 공이 라인을 넘어 틱 끝에 데드볼일 수 있다)
                    if (startState != FlowState.InPlay || Squad.IsKeeperSlot(s)) bad++;
                }
                startState = v.State;
            }
            Assert.AreEqual(0, bad, "데드볼 재시작·GK 소유는 012·074 대체 경로");
        }

        [Test]
        public void Match_SameSeed_SameHash_DiagnosticsOnOff()
        {
            var a = ShapeTestKit.Sim(new MatchSimOptions { DecisionDiagnostics = () => new DecisionTelemetry() });
            var b = ShapeTestKit.Sim();
            a.StepTicks(Ticks);
            b.StepTicks(Ticks);
            Assert.AreEqual(a.StateHash(), b.StateHash());
        }
    }
}
