using System;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    public enum BallInverseStatus : byte
    {
        Ok = 0,
        /// <summary>속도 한도로도 닿지 않거나 입력이 잘못됨.</summary>
        Unreachable = 1,
        /// <summary>도달은 하지만 정점 높이 제약을 넘는다.</summary>
        ApexExceeded = 2,
    }

    public readonly struct BallInverseResult
    {
        public readonly BallInverseStatus Status;
        public readonly float Speed;
        /// <summary>땅볼: 목표 지점까지 시간, 뜬공: 체공 시간 (s).</summary>
        public readonly float Time;
        /// <summary>뜬공 정점 높이(공 중심, m). 땅볼은 0.</summary>
        public readonly float Apex;

        public BallInverseResult(BallInverseStatus status, float speed, float time, float apex)
        {
            Status = status;
            Speed = speed;
            Time = time;
            Apex = apex;
        }

        public bool Ok => Status == BallInverseStatus.Ok;
        public static BallInverseResult Unreachable => new BallInverseResult(BallInverseStatus.Unreachable, 0f, 0f, 0f);
    }

    /// <summary>
    /// 역산 (specs/004 FR-33·34). 땅볼은 <see cref="BallRollTable"/>(실제 모델로 한도 속도에서 한 번 굴린 궤적) 보간,
    /// 뜬공은 실제 적분기로 착지 거리를 맞춘다 (Illinois).
    /// </summary>
    public static class BallInverse
    {
        /// <summary>뜬공 역산 수렴 허용치 (AC-10 요구 0.2 m보다 엄격하게).</summary>
        public const float Tolerance = 0.1f;

        /// <summary>
        /// 땅볼 목표 거리 distance에서 잔여속도 residualSpeed(0이면 정지)가 되는 초속 (FR-33).
        /// 실제 구름 모델 궤적 표(<see cref="BallRollTable"/>)를 보간한다: x(v0) = x(vr) − distance.
        /// 오차 0.2 m 이내는 AC-10 테스트가 실제 적분기로 보장한다 (2026-11-02 결정: 런타임 재검증 없음).
        /// </summary>
        public static BallInverseResult GroundSpeedFor(BallParams p, float distance, float residualSpeed = 0f)
        {
            if (!(distance > 0f) || !(residualSpeed >= 0f) || residualSpeed >= p.LimitSpeed) return BallInverseResult.Unreachable;
            var table = p.RollTable;
            var vr = residualSpeed < p.ToRestSpeed ? 0f : residualSpeed;
            var xr = vr <= 0f ? table.TotalDistance : table.DistanceAtSpeed(vr);
            var x0 = xr - distance;
            if (x0 < 0f) return BallInverseResult.Unreachable;
            var v0 = table.SpeedAtDistance(x0);
            var stepsEnd = vr <= 0f ? table.RestStep : table.StepAtDistance(xr);
            var time = (stepsEnd - table.StepAtDistance(x0)) * p.Dt;
            return new BallInverseResult(BallInverseStatus.Ok, v0, time, 0f);
        }

        /// <summary>
        /// 뜬공: 발사각 elevationRad, 스핀 spin(킥 방향 +x 기준 프레임)으로 수평 거리 distance에 첫 착지하는 초속.
        /// 정점(공 중심 높이)이 maxApex를 넘으면 ApexExceeded.
        /// </summary>
        /// <param name="scratch">호출자 소유 이벤트 버퍼 (재사용, 할당 0). 공유 버퍼를 두지 않는다 (리뷰 B-1: 병렬 경기 안전).</param>
        public static BallInverseResult LoftedSpeedFor(BallParams p, float distance, float elevationRad, Vec3 spin, float maxApex, BallEventBuffer scratch)
        {
            if (scratch == null) throw new System.ArgumentNullException(nameof(scratch));
            if (!(distance > 0f) || !(elevationRad > 0f && elevationRad < DMath.HalfPi)) return BallInverseResult.Unreachable;
            var hi = p.LimitSpeed;
            var dHi = Carry(p, hi, elevationRad, spin, scratch, out _, out _);
            if (dHi < distance) return BallInverseResult.Unreachable;
            var lo = 0.5f;
            var dLo = Carry(p, lo, elevationRad, spin, scratch, out _, out _);
            if (dLo > distance) return BallInverseResult.Unreachable;

            // Illinois(regula falsi) — 착지 거리는 초속에 단조 증가
            var side = 0;
            var converged = false;
            float v = hi, t = 0f, apex = 0f;
            for (int it = 0; it < 40; it++)
            {
                v = (lo * (dHi - distance) - hi * (dLo - distance)) / ((dHi - distance) - (dLo - distance));
                var d = Carry(p, v, elevationRad, spin, scratch, out t, out apex);
                var err = d - distance;
                if (DMath.Abs(err) <= Tolerance)
                {
                    converged = true;
                    break;
                }
                if (err > 0f)
                {
                    hi = v;
                    dHi = d;
                    if (side == 1) dLo = distance + (dLo - distance) * 0.5f;
                    side = 1;
                }
                else
                {
                    lo = v;
                    dLo = d;
                    if (side == -1) dHi = distance + (dHi - distance) * 0.5f;
                    side = -1;
                }
            }
            // 수렴하지 못하면 틀린 초속 대신 도달 불가 (리뷰 R-2)
            if (!converged) return BallInverseResult.Unreachable;
            var status = apex > maxApex ? BallInverseStatus.ApexExceeded : BallInverseStatus.Ok;
            return new BallInverseResult(status, v, t, apex);
        }

        /// <summary>실제 적분기로 날려 첫 착지까지 수평 거리. 골대·경계에서 먼 곳(5, 5)에서 +x로 찬다.</summary>
        public static float Carry(BallParams p, float speed, float elevationRad, Vec3 spin, BallEventBuffer ev, out float time, out float apex)
        {
            var start = new Vec3(5f, 5f, p.Radius);
            var vel = new Vec3(speed * DMath.Cos(elevationRad), 0f, speed * DMath.Sin(elevationRad));
            var s = BallState.InAir(start, vel, spin);
            apex = start.Z;
            for (int i = 0; i < 2000; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                if (s.Position.Z > apex) apex = s.Position.Z;
                for (int k = 0; k < ev.Count; k++)
                    if (ev[k].Kind == BallEventKind.Bounce)
                    {
                        time = (i + ev[k].Fraction) * p.Dt;
                        return (ev[k].Position - start).XY.Length();
                    }
            }
            time = 0f;
            return (s.Position - start).XY.Length();
        }
    }
}
