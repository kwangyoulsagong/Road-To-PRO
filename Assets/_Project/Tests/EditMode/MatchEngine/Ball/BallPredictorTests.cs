using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>AC-09: 예측 = 실제 (오차 0), FR-30~32.</summary>
    public class BallPredictorTests
    {
        static BallState[] Kicks(BallParams p) => new[]
        {
            BallState.InAir(new Vec3(30f, 34f, p.Radius), BallFlightKit.Launch(22f, 25f), new Vec3(0f, -20f, 30f)),
            BallState.InAir(new Vec3(20f, 20f, p.Radius), BallFlightKit.Launch(28f, 8f, 180f), new Vec3(0f, 0f, 50f)), // 골대 쪽
            BallState.RollingAt(new Vec2(10f, 34f), new Vec2(-12f, 0.3f), p.Radius), // 골망
            BallState.RollingAt(new Vec2(50f, 30f), new Vec2(8f, 3f), p.Radius),
            BallState.InAir(new Vec3(0.6f, 34f, 2f), new Vec3(-6f, 0f, 4f), Vec3.Zero), // 크로스바
        };

        [TestCase(0.04f)]
        [TestCase(0.1f)]
        public void Prediction_EqualsActual_10s_Exact(float interval)
        {
            var p = BallTestKit.Params();
            var pred = new BallPrediction(250);
            foreach (var k in Kicks(p))
            {
                BallPredictor.Predict(k, p, interval, 10f, pred);
                var s = k;
                var ev = new BallEventBuffer();
                var per = (int)Math.Round(interval / 0.01f);
                for (int n = 0; n < pred.Count; n++)
                {
                    for (int j = 0; j < per; j++)
                    {
                        ev.Clear();
                        BallStepper.Step(ref s, p, ev, out _);
                    }
                    Assert.AreEqual(s.Position, pred.Positions[n], $"샘플 {n}");
                    Assert.AreEqual(s.Velocity, pred.Velocities[n]);
                    Assert.AreEqual(s.Phase, pred.Phases[n]);
                    Assert.AreEqual((n + 1) * per * 0.01f, pred.Times[n], 1e-5f);
                }
                Assert.Greater(pred.Count, 0);
            }
        }

        [Test]
        public void Prediction_ReportsLandingRestBoundary()
        {
            var p = BallTestKit.Params();
            var pred = new BallPrediction(250);
            BallPredictor.Predict(Kicks(p)[0], p, 0.04f, 10f, pred);
            Assert.IsTrue(pred.HasLanding);
            Assert.AreEqual(p.Radius, pred.LandingPoint.Z, 1e-5f);
            Assert.Greater(pred.LandingTime, 0.5f);
            var (land, t) = BallFlightKit.FlyToFirstBounce(Kicks(p)[0], p);
            Assert.AreEqual(land, pred.LandingPoint);
            Assert.AreEqual(t, pred.LandingTime, 1e-6f);

            BallPredictor.Predict(Kicks(p)[3], p, 0.1f, 10f, pred);
            Assert.IsTrue(pred.HasRest);
            BallPredictor.Predict(Kicks(p)[2], p, 0.1f, 10f, pred);
            Assert.IsTrue(pred.HasBoundary);
            Assert.AreEqual(BallBoundary.GoalLineLow, pred.Boundary);
        }

        [Test]
        public void Prediction_DoesNotMutateState()
        {
            var p = BallTestKit.Params();
            var k = Kicks(p)[0];
            var copy = k;
            BallPredictor.Predict(k, p, 0.04f, 10f, new BallPrediction(250));
            Assert.AreEqual(copy.Position, k.Position);
            Assert.AreEqual(copy.Velocity, k.Velocity);
            Assert.AreEqual(copy.Spin, k.Spin);
            Assert.AreEqual(copy.Phase, k.Phase);
        }

        [Test]
        public void Prediction_InvalidArguments_Throw()
        {
            var p = BallTestKit.Params();
            var k = Kicks(p)[0];
            Assert.Throws<ArgumentException>(() => BallPredictor.Predict(k, p, 0.045f, 10f, new BallPrediction(250)));
            Assert.Throws<ArgumentOutOfRangeException>(() => BallPredictor.Predict(k, p, 0.02f, 10f, new BallPrediction(500)));
            Assert.Throws<ArgumentOutOfRangeException>(() => BallPredictor.Predict(k, p, 0.04f, 11f, new BallPrediction(500)));
            Assert.Throws<ArgumentException>(() => BallPredictor.Predict(k, p, 0.04f, 10f, new BallPrediction(100)));
        }
    }
}
