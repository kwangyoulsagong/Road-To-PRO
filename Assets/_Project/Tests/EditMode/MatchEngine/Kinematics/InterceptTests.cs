using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-06: 공 인터셉트 탐색 (FR-22, 004 예측기 사용).</summary>
    public class InterceptTests
    {
        static BallParams BallP() => BallParams.Create(
            TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning", "ball.json"))),
            SurfaceCondition.Dry, PitchSpec.Default);

        /// <summary>
        /// 무작위 고정 시드: 땅볼 70%·낮은 뜬공 30%, 선수는 공 출발점에서 3~20 m, 초기 속도 0~6 m/s(반응 지연 동안 그대로 진행).
        /// 탐색이 지점을 찾은 케이스 1,000개에서, 공 도착 시각 + 0.1 s 안에 그 지점 컨트롤 반경 안에 들어오는 비율 ≥ 98%.
        /// (먼저 도착해 기다리는 것도 성공: ± 0.1 s 창 안에 반경 안에 있다.)
        /// </summary>
        [Test]
        public void ChosenPoint_ReachedOnTime_98Percent()
        {
            var p = Params;
            var bp = BallP();
            var pred = new BallPrediction(64);
            var rng = new SeedRng(2026UL);
            int found = 0, ok = 0, tried = 0;
            while (found < 1000 && tried < 100000)
            {
                tried++;
                var bpos = new Vec2(rng.Range(25f, 80f), rng.Range(15f, 53f));
                var ang = rng.Range(0f, DMath.TwoPi);
                var bs = rng.Range(6f, 22f);
                var dir = new Vec2(DMath.Cos(ang), DMath.Sin(ang));
                var ball = rng.NextFloat() < 0.7f
                    ? BallState.RollingAt(bpos, dir * bs, bp.Radius)
                    : BallState.InAir(new Vec3(bpos.X, bpos.Y, bp.Radius), new Vec3(dir.X * bs, dir.Y * bs, rng.Range(1f, 5f)), Vec3.Zero);
                BallPredictor.Predict(ball, bp, 0.04f, 2.0f, pred);

                var pa = rng.Range(0f, DMath.TwoPi);
                var ppos = bpos + new Vec2(DMath.Cos(pa), DMath.Sin(pa)) * rng.Range(3f, 20f);
                var va = rng.Range(0f, DMath.TwoPi);
                var vs = rng.Range(0f, 6f);
                var pvel = new Vec2(DMath.Cos(va), DMath.Sin(va)) * vs;
                var ability = (int)rng.Range(5f, 20.99f);
                var k = One(KinematicAttributes.Uniform(ability), ppos, pvel, default, p);
                k.Monitor = new MotionLimitMonitor();

                if (!TimeToIntercept.FindBallIntercept(k.Profile(0), ppos, pvel, 1f, 0f, p, pred, 2.5f, out var point, out var tBall)) continue;
                found++;
                if (vs > 0.01f) k.SetCommandImmediate(1, SteeringCommand.Seek(Ray(ppos, pvel.Normalized(), p), vs));
                k.SetCommand(1, SteeringCommand.InterceptBall(point));
                var steps = (int)DMath.Floor((tBall + 0.1f) / p.Dt + 0.5f);
                for (int i = 0; i < steps; i++)
                {
                    k.Step();
                    if ((k.State(0).Position - point).Length() <= p.ControlRadius) { ok++; break; }
                }
                Assert.That(k.Monitor.TotalViolations, Is.Zero);
            }
            Assert.That(found, Is.EqualTo(1000));
            Assert.That(ok / (double)found, Is.GreaterThanOrEqualTo(0.98), $"성공 {ok}/{found}");
        }

        [Test]
        public void Unreachable_ReturnsFalse_BeyondMaxT()
        {
            var p = Params;
            var bp = BallP();
            var pred = new BallPrediction(64);
            BallPredictor.Predict(BallState.RollingAt(new Vec2(30f, 34f), new Vec2(20f, 0f), bp.Radius), bp, 0.04f, 2.0f, pred);
            var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(10), p);
            Assert.That(TimeToIntercept.FindBallIntercept(pr, new Vec2(30f, 5f), Vec2.Zero, 1f, 0f, p, pred, 2.5f, out _, out _), Is.False);
        }
    }
}
