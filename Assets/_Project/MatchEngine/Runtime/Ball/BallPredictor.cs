using System;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 궤적 예측 (specs/004 FR-30~32). 상태 복사본으로 <see cref="BallStepper.Step(ref BallState, BallParams, BallEventBuffer, out BallSegment)"/>를
    /// 반복하므로 실제 시뮬과 비트 단위로 같다. 입력 상태는 바꾸지 않는다.
    /// </summary>
    public static class BallPredictor
    {
        public const float MaxHorizonSeconds = 10f;
        public const float MinSampleInterval = 0.04f;
        public const float MaxSampleInterval = 0.1f;

        /// <param name="sampleInterval">0.04~0.1 s, 서브스텝(0.01 s)의 정수배.</param>
        /// <param name="horizonSeconds">(0, 10] s, 서브스텝 정수배.</param>
        public static void Predict(in BallState start, BallParams p, float sampleInterval, float horizonSeconds, BallPrediction output,
            IBallDiagnostics diag = null)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            var perSample = ToSteps(sampleInterval, p.Dt, nameof(sampleInterval));
            if (sampleInterval < MinSampleInterval - 1e-6f || sampleInterval > MaxSampleInterval + 1e-6f)
                throw new ArgumentOutOfRangeException(nameof(sampleInterval), sampleInterval, "샘플 간격은 0.04~0.1 s");
            if (!(horizonSeconds > 0f && horizonSeconds <= MaxHorizonSeconds + 1e-6f))
                throw new ArgumentOutOfRangeException(nameof(horizonSeconds), horizonSeconds, "예측 길이는 (0, 10] s");
            var total = ToSteps(horizonSeconds, p.Dt, nameof(horizonSeconds));
            var needed = total / perSample;
            if (needed > output.Capacity) throw new ArgumentException($"예측 버퍼 용량 부족: 필요 {needed}, 용량 {output.Capacity}", nameof(output));

            output.Reset();
            var s = start;
            var ev = output.Scratch;
            for (int i = 1; i <= total; i++)
            {
                if (s.Phase == BallPhase.Rest || s.Phase == BallPhase.Controlled) break;
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                for (int k = 0; k < ev.Count; k++)
                {
                    ref readonly var e = ref ev[k];
                    var t = (i - 1 + e.Fraction) * p.Dt;
                    if (e.Kind == BallEventKind.Bounce && !output.HasLanding)
                    {
                        output.HasLanding = true;
                        output.LandingPoint = e.Position;
                        output.LandingTime = t;
                        if (diag != null) diag.OnPredictedLanding(e.Position, t);
                    }
                    else if (e.Kind == BallEventKind.BoundaryCrossed && !output.HasBoundary)
                    {
                        output.HasBoundary = true;
                        output.Boundary = e.Boundary;
                        output.BoundaryPoint = e.Position;
                        output.BoundaryTime = t;
                    }
                    else if (e.Kind == BallEventKind.Transition && e.To == BallPhase.Rest)
                    {
                        output.HasRest = true;
                        output.RestPoint = e.Position;
                        output.RestTime = i * p.Dt;
                    }
                }
                if (i % perSample == 0)
                {
                    var n = output.Count;
                    output.Times[n] = i * p.Dt;
                    output.Positions[n] = s.Position;
                    output.Velocities[n] = s.Velocity;
                    output.Phases[n] = s.Phase;
                    output.Count = n + 1;
                }
            }
        }

        static int ToSteps(float seconds, float dt, string name)
        {
            var n = (int)DMath.Floor(seconds / dt + 0.5f);
            if (n <= 0 || DMath.Abs(n * dt - seconds) > 1e-4f)
                throw new ArgumentException($"{name}는 서브스텝 {dt} s의 정수배여야 한다", name);
            return n;
        }
    }
}
