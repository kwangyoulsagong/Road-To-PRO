using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/071 AC-11 대체 하네스 (017 전): 고정 상황별 슈팅 전환율·크로스 공중볼 공격 승률·헤딩 전환율을 리포트에 기록한다. 판정은 017에서.
    /// 골 = 공이 골문(포스트 안·크로스바 아래)으로 골라인을 넘음 (011 GK 선방 포함 물리 단계만).
    /// </summary>
    public class ShotRateHarnessTests
    {
        static bool InGoal(MatchContext ctx, int team)
        {
            var goal = ctx.OpponentGoal(team);
            var b = ctx.Ball.Position;
            var dir = ctx.AttackDir(team);
            return (b.X - goal.X) * dir > 0f && DMath.Abs(b.Y - goal.Y) < BallParams.GoalWidth * 0.5f && b.Z < BallParams.GoalHeight;
        }

        static double Conversion(float depth, float lateral, ulong seed, out double meanXg, int n = 200)
        {
            var f = new DuelFixture(seed);
            int goals = 0;
            double xg = 0;
            for (int i = 0; i < n; i++)
            {
                f.ClearPitch();
                f.Events.Clear();
                var ctx = f.Ctx;
                var dir = ctx.AttackDir(0);
                var goal = ctx.OpponentGoal(0);
                f.Put(9, new Vec2(goal.X - dir * depth, goal.Y + lateral), new Vec2(dir, 0f));
                f.Put(11, new Vec2(goal.X - dir * 1.0f, goal.Y + lateral * 0.1f), new Vec2(-dir, 0f));
                f.Own(9);
                var a = ctx.Actions.Shots.Plan(ctx, 9, 0, -1f, false, false, BodyPart.RightFoot);
                xg += a.ShotXg;
                ctx.Actions.Schedule(ctx, in a, ctx.Kinematics.Substep);
                for (int t = 0; t < 40; t++)
                {
                    f.Tick();
                    if (InGoal(ctx, 0)) { goals++; break; }
                    if (ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId != 9 || f.CrossedLastTick()) break;
                }
            }
            meanXg = xg / n;
            return goals / (double)n;
        }

        [Test]
        [Category("Slow")]
        public void FixedSituations_ShotConversion_Reported()
        {
            var spot = Conversion(11f, 0f, 7601UL, out var xs);
            var edge = Conversion(17f, 12f, 7602UL, out var xe);
            var far = Conversion(30f, 0f, 7603UL, out var xf);
            TestContext.WriteLine($"전환율(평가 xG 평균): 11 m 정면 {spot:P0}({xs:P0}), 박스 모서리 {edge:P0}({xe:P0}), 30 m {far:P0}({xf:P0}) — 평균 능력치·압박 없음·GK 1명");
            Assert.GreaterOrEqual(spot, far, "가까운 슛이 더 들어간다");
            Assert.Greater(xs, xf);
        }

        [Test]
        [Category("Slow")]
        public void Match_CrossAndHeaderRates_Reported()
        {
            var sim = ShapeTestKit.Sim();
            sim.StepTicks(27_000);
            var ctx = sim.Context;
            int crosses = 0, crossWonByAtt = 0, headShots = 0;
            for (int i = 0; i < ctx.Log.Count; i++)
            {
                var e = ctx.Log[i];
                if (e.Kind == SimEventKind.Cross) crosses++;
                if (e.Kind == SimEventKind.Header && e.A == (int)HeaderOutcome.Shot) headShots++;
                if (e.Kind == SimEventKind.Header && e.Team >= 0 && (e.A == 0 || e.A == 1)) crossWonByAtt++;
            }
            var aer = ctx.Actions.Aerials;
            TestContext.WriteLine($"전반 45분: 크로스 {crosses}, 경합 {aer.Contests}, 공격 쪽 헤더 승 {crossWonByAtt}, 헤딩슛 {headShots}, 블록 {ctx.Actions.Shots.Blocks}");
            Assert.GreaterOrEqual(aer.Contests, 0);
        }
    }
}
