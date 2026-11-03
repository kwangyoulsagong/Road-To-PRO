using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>지연 명령 큐 (FR-06·28, plan [제안] ③).</summary>
    public class CommandQueueTests
    {
        sealed class DropCounter : IKinematicsDiagnostics
        {
            public int Dropped;
            public void OnPlayerSubstep(in KinematicsSample s) { }
            public void OnCommandDropped(long substep, int playerId) => Dropped++;
        }

        [Test]
        public void Command_ActivatesAfterReactionDelay()
        {
            var k = One(KinematicAttributes.Uniform(10), new Vec2(50f, 34f));
            var pr = k.Profile(0);
            var delaySteps = (int)DMath.Floor(pr.ReactionDelay / k.Params.Dt + 0.5f);
            k.SetCommand(1, SteeringCommand.Seek(new Vec2(80f, 34f)));
            for (int i = 0; i < delaySteps; i++)
            {
                k.Step();
                Assert.That(k.State(0).Active.Kind, Is.EqualTo(SteeringKind.Idle), $"서브스텝 {i}: 아직 지연 중");
                Assert.That(k.State(0).Velocity.Length(), Is.EqualTo(0f));
            }
            k.Step();
            Assert.That(k.State(0).Active.Kind, Is.EqualTo(SteeringKind.Seek));
            Assert.That(k.State(0).Velocity.Length(), Is.GreaterThan(0f));
        }

        [Test]
        public void ReissuedEveryTick_StillActivatesInOrder()
        {
            var k = One(KinematicAttributes.Uniform(10), new Vec2(50f, 34f));
            for (int t = 0; t < 10; t++)
            {
                k.SetCommand(1, SteeringCommand.Arrive(new Vec2(50f + t, 34f)));
                Run(k, 10);
            }
            Assert.That(k.State(0).Active.Kind, Is.EqualTo(SteeringKind.Arrive));
            Assert.That(k.State(0).Active.Point.X, Is.GreaterThanOrEqualTo(55f), "10 Hz로 다시 내려도 명령이 굶지 않는다");
        }

        [Test]
        public void Overflow_DropsOldest_WithDiagnostic()
        {
            var k = One(KinematicAttributes.Uniform(1), new Vec2(50f, 34f));
            var diag = new DropCounter();
            k.Diagnostics = diag;
            for (int i = 0; i < CommandQueue.CapacityPerPlayer + 3; i++) k.SetCommand(1, SteeringCommand.Arrive(new Vec2(60f + i, 34f)));
            Assert.That(diag.Dropped, Is.EqualTo(3));
            Assert.That(k.PendingCommands(0), Is.EqualTo(CommandQueue.CapacityPerPlayer));
            Run(k, 100);
            Assert.That(k.State(0).Active.Point.X, Is.EqualTo(60f + CommandQueue.CapacityPerPlayer + 2), "가장 최근 명령이 마지막에 활성화");
        }

        [Test]
        public void Immediate_ClearsPending()
        {
            var k = One(KinematicAttributes.Uniform(10), new Vec2(50f, 34f));
            k.SetCommand(1, SteeringCommand.Seek(new Vec2(80f, 34f)));
            k.SetCommandImmediate(1, SteeringCommand.Arrive(new Vec2(40f, 34f)));
            Assert.That(k.PendingCommands(0), Is.Zero);
            Run(k, 100);
            Assert.That(k.State(0).Active.Kind, Is.EqualTo(SteeringKind.Arrive));
        }
    }
}
