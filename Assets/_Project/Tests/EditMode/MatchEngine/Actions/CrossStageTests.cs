using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 T07: 크로스 타입 선택·Cross 이벤트·뜬 크로스 착지 오차·경기 안 헤더.</summary>
    public class CrossStageTests
    {
        [Test]
        public void CrossIntent_AerialType_LandingErrorApplied_CrossEvent()
        {
            var f = new DuelFixture(7191UL);
            var ctx = f.Ctx;
            var dir = ctx.AttackDir(0);
            var goal = ctx.OpponentGoal(0);
            f.Put(7, new Vec2(goal.X - dir * 14f, 6f), new Vec2(dir, 0f));
            f.Own(7);
            f.Put(9, new Vec2(goal.X - dir * 8f, goal.Y), new Vec2(dir, 0f));
            var aim = new Vec2(goal.X - dir * 8f, goal.Y);
            var dist = (aim - ctx.Ball.Position.XY).Length();
            var a = new BallAction
            {
                Kind = BallActionKind.Pass, KickerSlot = 7, TargetSlot = 9, HasIntent = true, Aim = aim, Speed = 17f,
                Elevation = CrossModel.Elevation(ctx.Actions.P, CrossType.Lofted, dist), CrossType = (byte)((int)CrossType.Lofted + 1),
            };
            Assert.IsTrue(ctx.Actions.Schedule(ctx, in a, ctx.Kinematics.Substep));
            SimEvent? cross = null;
            for (int i = 0; i < 20 && cross == null; i++) { f.Tick(); foreach (var e in f.Events) if (e.Kind == SimEventKind.Cross) cross = e; }
            Assert.IsTrue(cross.HasValue, "Cross 이벤트");
            Assert.AreEqual((int)CrossType.Lofted + 1, cross.Value.A >> ActionSystem.CrossTypeShift);
            Assert.IsTrue(ctx.Actions.Aerials.IsArmed || ctx.Actions.Aerials.IsDecided || f.Count(SimEventKind.Header) > 0, "뜬 크로스 → 경합 예약");
            Assert.Greater(ctx.Ball.Velocity.Z, 0f);
        }

        [Test]
        [Category("Slow")]
        public void Match30min_CrossTypes_And_Headers()
        {
            var sim = ShapeTestKit.Sim();
            sim.StepTicks(18_000);
            var ctx = sim.Context;
            var types = new int[5];
            int headers = 0, headShots = 0;
            for (int i = 0; i < ctx.Log.Count; i++)
            {
                var e = ctx.Log[i];
                if (e.Kind == SimEventKind.Cross)
                {
                    var t = e.A >> ActionSystem.CrossTypeShift;
                    Assert.That(t, Is.InRange(1, 4));
                    types[t]++;
                }
                else if (e.Kind == SimEventKind.Header)
                {
                    Assert.That(e.A, Is.InRange(0, 5));
                    if (e.A > 2) continue; // GK 승리·놓침 기록 (헤더 없음)
                    headers++;
                    if (e.A == 0) headShots++;
                }
            }
            var aer = ctx.Actions.Aerials;
            TestContext.WriteLine($"크로스 Early {types[1]} Driven {types[2]} Lofted {types[3]} Cutback {types[4]}, 경합 {aer.Contests}(빈 {aer.EmptyContests}, GK {aer.KeeperWins}), 헤더 {headers}(슛 {headShots}), 블록 {ctx.Actions.Shots.Blocks}");
            Assert.Greater(aer.Contests, 0, "30분 안 공중볼 경합");
            Assert.AreEqual(aer.Headers, headers);
        }
    }
}
