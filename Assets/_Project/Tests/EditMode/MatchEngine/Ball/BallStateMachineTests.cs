using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-08: 전이가 아키텍처 5 그래프 밖으로 나가지 않는다.</summary>
    public class BallStateMachineTests
    {
        [Test]
        public void AllTransitions_InAllowedGraph()
        {
            var p = BallTestKit.Params();
            var rng = new SeedRng(4004UL);
            var ev = new BallEventBuffer();
            int transitions = 0, bad = 0;
            for (int n = 0; n < 1000; n++)
            {
                var s = BallState.Controlled(7, new Vec3(rng.Range(0f, 105f), rng.Range(0f, 68f), p.Radius));
                var vel = new Vec3(rng.Range(-30f, 30f), rng.Range(-30f, 30f), rng.Range(-4f, 15f));
                var spin = new Vec3(rng.Range(-50f, 50f), rng.Range(-50f, 50f), rng.Range(-80f, 80f));
                ev.Clear();
                BallCommands.Kick(ref s, vel, spin, p, ev);
                var phase = BallPhase.Controlled;
                for (int i = 0; ; i++)
                {
                    for (int k = 0; k < ev.Count; k++)
                    {
                        if (ev[k].Kind != BallEventKind.Transition) continue;
                        transitions++;
                        if (ev[k].From != phase || !BallTransitions.IsAllowed(ev[k].From, ev[k].To)) bad++;
                        phase = ev[k].To;
                    }
                    Assert.AreNotEqual(BallPhase.Bounce, s.Phase, "Bounce가 상태로 남음");
                    if (s.Phase == BallPhase.Rest || i >= 3000) break;
                    if (i == 150 && n % 5 == 0)
                    {
                        // 일부는 중간에 컨트롤 판정 입력
                        ev.Clear();
                        BallCommands.TakeControl(ref s, 3, s.Position, ev);
                        continue;
                    }
                    if (s.Phase == BallPhase.Controlled) break;
                    ev.Clear();
                    BallStepper.Step(ref s, p, ev, out _);
                }
                Assert.AreEqual(s.Phase, phase, "이벤트로 추적한 상태 = 실제 상태");
            }
            Assert.Greater(transitions, 3000);
            Assert.AreEqual(0, bad, "허용되지 않은 전이");
        }
    }
}
