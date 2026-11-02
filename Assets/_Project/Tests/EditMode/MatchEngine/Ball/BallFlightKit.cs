using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>비행·구름 측정 도우미.</summary>
    public static class BallFlightKit
    {
        public static Vec3 Launch(float speed, float elevationDeg, float headingDeg = 0f)
        {
            var e = elevationDeg * DMath.Pi / 180f;
            var h = headingDeg * DMath.Pi / 180f;
            return new Vec3(speed * DMath.Cos(e) * DMath.Cos(h), speed * DMath.Cos(e) * DMath.Sin(h), speed * DMath.Sin(e));
        }

        /// <summary>첫 지면 접촉까지 날린다: (착지 지점, 체공 시간 s). 착지 없으면 time = −1.</summary>
        public static (Vec3 landing, float time) FlyToFirstBounce(BallState s, BallParams p, int maxSteps = 2000)
        {
            var ev = new BallEventBuffer();
            for (int i = 0; i < maxSteps; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                for (int k = 0; k < ev.Count; k++)
                    if (ev[k].Kind == BallEventKind.Bounce)
                        return (ev[k].Position, (i + ev[k].Fraction) * p.Dt);
            }
            return (s.Position, -1f);
        }

        /// <summary>구름 공을 정지까지: (정지 위치, 정지 시간 s).</summary>
        public static (Vec3 rest, float time) RollToRest(BallState s, BallParams p, int maxSteps = 5000)
        {
            var ev = new BallEventBuffer();
            for (int i = 0; i < maxSteps; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                if (s.Phase == BallPhase.Rest) return (s.Position, (i + 1) * p.Dt);
            }
            return (s.Position, -1f);
        }
    }
}
