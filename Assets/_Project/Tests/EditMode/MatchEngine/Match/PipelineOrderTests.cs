using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-02·03: FR-01 단계 순서, 서브스텝 10회, 같은 틱 뒤 단계 값 읽기 검출.</summary>
    public class PipelineOrderTests
    {
        [Test]
        public void RecordingStage_OrderMatchesFr01()
        {
            var order = new List<StageId>();
            var o = new MatchSimOptions
            {
                Stages = () =>
                {
                    var st = MatchSim.DefaultStages();
                    for (int i = 0; i < st.Length; i++) st[i] = new RecordingStage(st[i], order);
                    return st;
                },
            };
            var sim = Sim(3UL, o);
            sim.StepTicks(300);
            Assert.That(order.Count, Is.EqualTo(300 * Stages.Count));
            for (int i = 0; i < order.Count; i++) Assert.That((int)order[i], Is.EqualTo(i % Stages.Count), $"틱 {i / Stages.Count}");
        }

        [Test]
        public void PhysicsSubsteps_ExactlyTenPerTick()
        {
            var sim = Sim(4UL);
            sim.StepTicks(1234);
            var phys = (PhysicsStage)sim.Stage(StageId.Physics);
            Assert.That(phys.SubstepsRun, Is.EqualTo(12340L));
            Assert.That(sim.Context.Kinematics.Substep, Is.EqualTo(12340L));
        }

        [Test]
        public void ReadingLaterStageSlot_Throws()
        {
            var b = new TickBoard();
            b.Clear();
            b.Enter(StageId.Phase);
            Assert.Throws<StageOrderViolation>(() => b.ShapeTarget(0), "단계 1이 단계 2 값 읽기");
            Assert.Throws<StageOrderViolation>(() => b.ShapeTargetsForWrite(), "남의 슬롯 쓰기");
            b.Enter(StageId.Shape);
            Assert.DoesNotThrow(() => b.ShapeTargetsForWrite());
            Assert.Throws<StageOrderViolation>(() => b.ShapeTarget(0), "같은 단계가 자기 슬롯을 읽는 것도 '앞 단계 결과'가 아니다");
            b.Enter(StageId.SteeringTargets);
            Assert.DoesNotThrow(() => b.ShapeTarget(0));
            Assert.Throws<StageOrderViolation>(() => { var _ = b.PhysicsCallCount; });
        }

        sealed class BadPhaseStage : StatelessStage
        {
            public override StageId Id => StageId.Phase;
            public override void Execute(MatchContext ctx)
            {
                if (ctx.Tick == 50) { var _ = ctx.Board.ShapeTarget(0); }
            }
        }

        [Test]
        public void BadStageInMatch_FailsWithViolation()
        {
            var o = new MatchSimOptions
            {
                Stages = () =>
                {
                    var st = MatchSim.DefaultStages();
                    st[1] = new BadPhaseStage();
                    return st;
                },
            };
            var sim = Sim(5UL, o);
            var r = sim.RunToEnd();
            Assert.That(r.Reason, Is.EqualTo(EndReason.Failed));
            Assert.That(r.FailedTick, Is.EqualTo(50L));
            Assert.That(r.FailedStage, Is.EqualTo((int)StageId.Phase));
            StringAssert.Contains("StageOrderViolation", r.FailureMessage);
        }

        [Test]
        public void StagesInWrongOrder_Rejected()
        {
            var o = new MatchSimOptions
            {
                Stages = () =>
                {
                    var st = MatchSim.DefaultStages();
                    (st[2], st[3]) = (st[3], st[2]);
                    return st;
                },
            };
            Assert.Throws<System.ArgumentException>(() => Sim(5UL, o));
        }
    }
}
