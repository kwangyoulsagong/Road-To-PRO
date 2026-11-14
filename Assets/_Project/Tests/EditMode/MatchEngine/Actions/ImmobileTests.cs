using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 AC-07: 슬라이딩 실패 다운 1.2 s — 이동 명령 0건, 005 한계 위반 0, 다운 상태는 해시에 들어간다.</summary>
    public class ImmobileTests
    {
        [Test]
        public void SlideFail_NoCommands12s_NoLimitBreach_HashChanges()
        {
            var f = new DuelFixture(7090UL, "{\"act.tackle.b0\": -3}");
            var k = f.Ctx.Kinematics;
            k.Monitor = new MotionLimitMonitor();
            f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
            f.Put(13, new Vec2(46f, 34.3f), new Vec2(1f, 0f));
            f.Own(9);
            f.Seek(13, new Vec2(60f, 34f));
            f.Ticks(6); // 13이 달려와 도달 거리 안
            Assert.IsTrue(f.Duels.ScheduleTackle(f.Ctx, 13, 9, true));
            var guard = 0;
            while (!k.IsImmobile(13) && guard++ < 20) f.Tick();
            Assert.IsTrue(k.IsImmobile(13), "빗나간 슬라이딩 → 다운");
            var until = k.ImmobileUntil(13);
            Assert.AreEqual(120, until - k.Substep, 10, "다운 ≈ 1.2 s");
            var h0 = Hash(k);
            var ign0 = k.IgnoredCommands;
            var sent = 0;
            while (k.Substep < until)
            {
                k.SetCommand(13, SteeringCommand.Seek(new Vec2(80f, 34f)));
                sent++;
                f.Tick();
            }
            Assert.AreEqual(sent, k.IgnoredCommands - ign0, "다운 동안 이동 명령 0건 적용");
            Assert.AreEqual(0, k.Monitor.TotalViolations, "005 한계 위반 0");
            Assert.AreEqual(1, f.Count(SimEventKind.Tackle));

            // 다운 상태를 바꾸면 해시가 바뀐다
            var g = new DuelFixture(7090UL);
            var a = Hash(g.Ctx.Kinematics);
            g.Ctx.Kinematics.SetImmobile(13, g.Ctx.Kinematics.Substep + 120);
            Assert.AreNotEqual(a, Hash(g.Ctx.Kinematics));
        }

        static ulong Hash(KinematicsSystem k)
        {
            var h = StableHasher.Create();
            k.AddTo(ref h);
            return h.Value;
        }
    }
}
