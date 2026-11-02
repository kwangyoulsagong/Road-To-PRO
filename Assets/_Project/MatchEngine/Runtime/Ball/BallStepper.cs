using System;
using System.Runtime.CompilerServices;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 공 서브스텝 적분 (specs/004 FR-10~23, 40~42). 실제 시뮬·궤적 예측·역산이 모두 이 함수를 쓴다 (FR-30).
    /// dt = 0.01 s 고정, semi-implicit Euler: v ← v + a(v)·dt, p ← p + v·dt.
    /// 처리 순서 (plan "결정론"): ① 적분 ② 골대(경로 꺾임) ③ 지면 바운스 ④ 구름·정지 ⑤ 경계 통과 ⑥ 골망 ⑦ 수치 검사.
    /// 골대·바운스·경계 이벤트의 시각 비율은 모두 같은 꺾인 경로(<see cref="BallPath"/>) 시간축이다.
    /// 난수 없음. 할당 없음.
    /// <para>이벤트 버퍼 계약: 호출자는 서브스텝마다 Clear한다. 한 번의 Step이 남기는 이벤트는 최대 7개
    /// (바운스 전이 2 + 바운스 1 + 골대 2 + 경계 2: 골대 근처에선 y 경계가 없고 x 경계는 나감→들어옴→나감으로 최대 2회).
    /// 수치 보정 시에는 이번 스텝 이벤트를 버리고 보정 1건만 남긴다. 기본 용량 16.</para>
    /// </summary>
    public static class BallStepper
    {
        public static void Step(ref BallState s, BallParams p, BallEventBuffer events, out BallSegment segment) =>
            Step(ref s, p, events, out segment, null);

        /// <summary>진단 출력 포함 (FR-43). diag가 null이면 위 오버로드와 같다. 진단은 결과를 바꾸지 않는다.</summary>
        public static void Step(ref BallState s, BallParams p, BallEventBuffer events, out BallSegment segment, IBallDiagnostics diag)
        {
            var r = p.Radius;
            if (s.Phase == BallPhase.Controlled || s.Phase == BallPhase.Rest)
            {
                segment = new BallSegment(s.Position, s.Position);
                if (diag != null) diag.OnSubstep(in s, in segment);
                return;
            }
            // 지면 아래 Air 입력은 시작점부터 지면으로 보정 (경계 상황, 리뷰 NN-2: 선분 시작도 보정 후 값)
            if (s.Phase == BallPhase.Air && s.Position.Z < r) s.Position = new Vec3(s.Position.X, s.Position.Y, r);

            var start = s.Position;
            var firstEvent = events.Count;
            bool boundaryDone;
            switch (s.Phase)
            {
                case BallPhase.Air:
                    boundaryDone = StepAir(ref s, p, events);
                    break;
                case BallPhase.Rolling:
                    boundaryDone = StepRolling(ref s, p, events);
                    break;
                default:
                    throw new InvalidOperationException($"저장될 수 없는 공 상태: {s.Phase}");
            }

            var end = s.Position;
            // 꺾인 경로면 StepAir/StepRolling이 경계를 판정했다. 직선이면 경계 근처일 때만 판정 (빠른 배제).
            if (!boundaryDone && !(InsideField(start, p, r) && InsideField(end, p, r)))
                GoalFrame.DetectBoundarySegment(start, end, 0f, 1f, s.Phase, p, events);
            if ((end.X < 0f || end.X > p.Pitch.LengthM) && (s.Phase == BallPhase.Air || s.Phase == BallPhase.Rolling)) GoalFrame.ApplyNet(ref s, p);

            if (!AllFinite(in s)) Sanitize(ref s, start, p, events, firstEvent);

            if (events.Count - firstEvent > 1) SortByFraction(events, firstEvent);
            segment = new BallSegment(start, s.Position);
            if (diag != null)
            {
                diag.OnSubstep(in s, in segment);
                for (int i = firstEvent; i < events.Count; i++) diag.OnEvent(in events[i]);
            }
        }

        /// <returns>경계 판정까지 마쳤으면 true (골대에 맞아 꺾인 경로).</returns>
        static bool StepAir(ref BallState s, BallParams p, BallEventBuffer events)
        {
            var r = p.Radius;
            var start = s.Position;
            if (start.Z <= r && s.Velocity.Z <= 0f)
            {
                // 지면 위(또는 아래에서 보정됨)에서 내려가는 중: 같은 스텝에서 바운스
                Bounce(ref s, p, events, 0f, start);
                return false;
            }

            var dt = p.Dt;
            var a = BallAero.AirAcceleration(s.Velocity, s.Spin, p);
            var vx = s.Velocity.X + a.X * dt;
            var vy = s.Velocity.Y + a.Y * dt;
            var vz = s.Velocity.Z + a.Z * dt;
            s.Velocity = new Vec3(vx, vy, vz);
            s.Position = new Vec3(start.X + vx * dt, start.Y + vy * dt, start.Z + vz * dt);
            var k = p.SpinDecayFactor;
            s.Spin = new Vec3(s.Spin.X * k, s.Spin.Y * k, s.Spin.Z * k);

            if (NearGoalLine(start.X, s.Position.X, p)) return AirNearGoal(ref s, start, p, events);

            // 직선 경로 지면 접촉
            var end = s.Position;
            if (end.Z <= r)
            {
                var denom = start.Z - end.Z;
                var t = denom > 0f ? (start.Z - r) / denom : 0f;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                Bounce(ref s, p, events, t, new Vec3(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t, r));
            }
            return false;
        }

        /// <summary>
        /// 골라인 근처 비행: 골대 접촉으로 꺾이는 경로. 골대 판정은 지면 접촉보다 늦은 접촉을 무시한다 (리뷰 NB-1: 가장 이른 사건만).
        /// 이어서 지면 접촉·경계를 같은 경로 시간축으로 판정한다.
        /// </summary>
        static bool AirNearGoal(ref BallState s, Vec3 start, BallParams p, BallEventBuffer events)
        {
            var r = p.Radius;
            var path = BallPath.Line(start, s.Position);
            GoalFrame.Collide(ref s, ref path, p, events, r);

            // 지면 접촉: 경로에서 처음으로 z ≤ r이 되는 구간
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var pa = path.Point(i);
                var pb = path.Point(i + 1);
                if (pb.Z > r) continue;
                var denom = pa.Z - pb.Z;
                var t = denom > 0f ? (pa.Z - r) / denom : 0f;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                var f0 = path.Fraction(i);
                var f = f0 + t * (path.Fraction(i + 1) - f0);
                var c = pa + (pb - pa) * t;
                var contact = new Vec3(c.X, c.Y, r);
                path.TruncateAt(i + 1, contact, f);
                Bounce(ref s, p, events, f, contact);
                break;
            }
            if (!PathInsideField(in path, p, r)) GoalFrame.DetectBoundary(in path, s.Phase, p, events);
            return true;
        }

        /// <summary>
        /// 지면 바운스 (FR-12·23, 리서치 01 A-5). 법선: vz' = −e·vz. 접선: 미끄럼 Δv_t = −μ(1+e)|v_n|(상대 미끄럼 방향)과
        /// 그립 v_t' = keep·v_t + spin·rω 중 변화량이 작은 쪽. 그립이면 ω를 구름 조건으로(ω' = v_t'/r),
        /// 미끄럼이면 얇은 껍질(I = 2/3·m·r²) 충격량으로 ω를 바꾼다. 남은 시간은 적분하지 않는다.
        /// </summary>
        static void Bounce(ref BallState s, BallParams p, BallEventBuffer events, float fraction, Vec3 contact)
        {
            var r = p.Radius;
            var v = s.Velocity;
            var w = s.Spin;
            var vn = v.Z < 0f ? v.Z : 0f;
            var vzOut = -p.Restitution * vn;

            var vt = new Vec2(v.X, v.Y);
            // 구름 조건 접선 속도 rω (ω × (−r·ẑ) 의 반대): (r·ωy, −r·ωx)
            var rw = new Vec2(r * w.Y, -r * w.X);
            var grip = vt * p.GripKeep + rw * p.GripSpin;
            var dGrip = grip - vt;

            var rel = vt - rw;
            var relLen = rel.Length();
            var slipMag = p.Mu * (1f + p.Restitution) * DMath.Abs(vn);
            var dSlip = relLen > 1e-6f ? rel * (-slipMag / relLen) : Vec2.Zero;

            Vec2 vtOut;
            Vec3 wOut;
            if (dSlip.LengthSq() < dGrip.LengthSq())
            {
                vtOut = vt + dSlip;
                var k = 3f / (2f * r);
                wOut = new Vec3(w.X + k * dSlip.Y, w.Y - k * dSlip.X, w.Z);
            }
            else
            {
                vtOut = grip;
                wOut = new Vec3(-vtOut.Y / r, vtOut.X / r, w.Z);
            }

            events.Add(BallEvent.Transition(fraction, contact, BallPhase.Air, BallPhase.Bounce, BallTransitionReason.GroundContact));
            var high = vzOut >= p.ToRollVz;
            var vOut = new Vec3(vtOut, high ? vzOut : 0f);
            events.Add(new BallEvent(BallEventKind.Bounce, fraction, contact, v, vOut, BallPhase.Air, BallPhase.Bounce,
                BallTransitionReason.GroundContact, BallBoundary.None, false, GoalFramePart.None));

            s.Position = contact;
            s.Velocity = vOut;
            s.Spin = wOut;
            s.Phase = high ? BallPhase.Air : BallPhase.Rolling;
            events.Add(BallEvent.Transition(fraction, contact, BallPhase.Bounce, s.Phase,
                high ? BallTransitionReason.ReboundHigh : BallTransitionReason.ReboundLow));
        }

        /// <returns>경계 판정까지 마쳤으면 true (골라인 근처 경로).</returns>
        static bool StepRolling(ref BallState s, BallParams p, BallEventBuffer events)
        {
            var r = p.Radius;
            var dt = p.Dt;
            var sx = s.Position.X;
            var sy = s.Position.Y;
            var start = new Vec3(sx, sy, r);
            var vx = s.Velocity.X;
            var vy = s.Velocity.Y;
            var speed = DMath.Sqrt(vx * vx + vy * vy);
            var newSpeed = RollSpeedStep(speed, p);

            if (newSpeed < p.ToRestSpeed)
            {
                var ex = sx;
                var ey = sy;
                if (newSpeed > 0f && speed > 0f)
                {
                    var m = newSpeed / speed * dt;
                    ex += vx * m;
                    ey += vy * m;
                }
                s.Position = new Vec3(ex, ey, r);
                s.Velocity = Vec3.Zero;
                s.Spin = Vec3.Zero;
                s.Phase = BallPhase.Rest;
                events.Add(BallEvent.Transition(1f, s.Position, BallPhase.Rolling, BallPhase.Rest, BallTransitionReason.SpeedBelowRest));
            }
            else
            {
                var q = newSpeed / speed;
                var nvx = vx * q;
                var nvy = vy * q;
                s.Velocity = new Vec3(nvx, nvy, 0f);
                s.Position = new Vec3(sx + nvx * dt, sy + nvy * dt, r);
                // 구름 조건 스핀 (진단용), 수직축 스핀은 감쇠만
                s.Spin = new Vec3(-nvy / r, nvx / r, s.Spin.Z * p.SpinDecayFactor);
            }

            if (!NearGoalLine(sx, s.Position.X, p)) return false;
            return RollNearGoal(ref s, start, p, events);
        }

        static bool RollNearGoal(ref BallState s, Vec3 start, BallParams p, BallEventBuffer events)
        {
            var r = p.Radius;
            var path = BallPath.Line(start, s.Position);
            GoalFrame.Collide(ref s, ref path, p, events, float.NegativeInfinity);
            s.Position = new Vec3(s.Position.X, s.Position.Y, r);
            s.Velocity = new Vec3(s.Velocity.X, s.Velocity.Y, 0f);
            if (!PathInsideField(in path, p, r)) GoalFrame.DetectBoundary(in path, s.Phase, p, events);
            return true;
        }

        /// <summary>구름 속도 1스텝: v − (a + b·v + β·Cd(v)·v²)·dt (FR-06). 음수가 될 수 있다(호출자가 정지 처리).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RollSpeedStep(float speed, BallParams p) =>
            speed - BallAero.RollDeceleration(speed, p) * p.Dt;

        /// <summary>이동 구간이 골라인 근처(골대 접촉 가능 거리)인지. 아니면 골대 판정을 건너뛴다.</summary>
        static bool NearGoalLine(float x0, float x1, BallParams p)
        {
            var reach = p.Radius + p.PostRadius;
            var min = x0 < x1 ? x0 : x1;
            var max = x0 < x1 ? x1 : x0;
            return min <= reach || max >= p.Pitch.LengthM - reach;
        }

        static bool InsideField(Vec3 v, BallParams p, float r) =>
            v.X >= -r && v.X <= p.Pitch.LengthM + r && v.Y >= -r && v.Y <= p.Pitch.WidthM + r;

        static bool PathInsideField(in BallPath path, BallParams p, float r)
        {
            for (int i = 0; i < path.Count; i++)
            {
                var v = path.Point(i);
                if (!(v.X >= -r && v.X <= p.Pitch.LengthM + r && v.Y >= -r && v.Y <= p.Pitch.WidthM + r)) return false;
            }
            return true;
        }

        /// <summary>9개 값이 모두 유한한지 (빠른 경로: 합이 유한하면 각각도 유한. 합이 넘치면 개별 검사로).</summary>
        static bool AllFinite(in BallState s)
        {
            var sum = s.Position.X + s.Position.Y + s.Position.Z + s.Velocity.X + s.Velocity.Y + s.Velocity.Z + s.Spin.X + s.Spin.Y + s.Spin.Z;
            if (sum - sum == 0f) return true;
            return FloatGuard.IsFinite(s.Position.X) && FloatGuard.IsFinite(s.Position.Y) && FloatGuard.IsFinite(s.Position.Z)
                && FloatGuard.IsFinite(s.Velocity.X) && FloatGuard.IsFinite(s.Velocity.Y) && FloatGuard.IsFinite(s.Velocity.Z)
                && FloatGuard.IsFinite(s.Spin.X) && FloatGuard.IsFinite(s.Spin.Y) && FloatGuard.IsFinite(s.Spin.Z);
        }

        /// <summary>
        /// 수치 오류 (경계 상황 "NaN·무한대 → 즉시 실패"). 디버그(Strict)는 예외. 릴리스(Sanitize)는 서브스텝 시작 위치에
        /// 정지 상태로 되돌리고 <see cref="BallEventKind.NumericSanitized"/> 이벤트를 남긴다 (조용히 넘어가지 않음, 리뷰 R-5).
        /// </summary>
        static void Sanitize(ref BallState s, Vec3 start, BallParams p, BallEventBuffer events, int firstEvent)
        {
            if (IsStrict())
                throw new NumericException($"공 상태에 유한하지 않은 값: 위치 {s.Position}, 속도 {s.Velocity}, 스핀 {s.Spin}");
            // 이번 스텝 이벤트(NaN 좌표가 섞였을 수 있음)를 버리고 보정 이벤트 1건만 남긴다. Rest는 지면 위 (리뷰 NR-1).
            // NumericSanitized는 Transition 종류가 아니라 전이 그래프 검사 대상이 아니다.
            events.TruncateTo(firstEvent);
            var from = s.Phase;
            var safe = AllFiniteVec(start) ? new Vec3(start.X, start.Y, p.Radius) : new Vec3(0f, 0f, p.Radius);
            events.Add(new BallEvent(BallEventKind.NumericSanitized, 1f, safe, Vec3.Zero, Vec3.Zero, from, BallPhase.Rest,
                BallTransitionReason.None, BallBoundary.None, false, GoalFramePart.None));
            s.Position = safe;
            s.Velocity = Vec3.Zero;
            s.Spin = Vec3.Zero;
            s.Phase = BallPhase.Rest;
        }

        /// <summary>빌드 구성 상수를 메서드로 감싸 분기 양쪽을 컴파일한다 (CS0162 방지).</summary>
        static bool IsStrict() => FloatGuard.DefaultMode == GuardMode.Strict;

        static bool AllFiniteVec(Vec3 v) => FloatGuard.IsFinite(v.X) && FloatGuard.IsFinite(v.Y) && FloatGuard.IsFinite(v.Z);

        /// <summary>이번 스텝 이벤트를 시각 비율 순으로 안정 정렬 (최대 8개, 삽입 정렬).</summary>
        static void SortByFraction(BallEventBuffer events, int first)
        {
            for (int i = first + 1; i < events.Count; i++)
            {
                for (int j = i; j > first && events[j].Fraction < events[j - 1].Fraction; j--)
                    events.Swap(j, j - 1);
            }
        }
    }
}
