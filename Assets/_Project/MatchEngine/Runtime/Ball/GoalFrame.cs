using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 골대·경계·골망 (specs/004 FR-40~42). 골대는 골라인(x = 0, x = 길이) 위의 원기둥:
    /// 포스트 중심 y = 중앙 ± (골 폭/2 + 포스트 반지름), 크로스바 중심 z = 골 높이 + 포스트 반지름.
    /// </summary>
    public static class GoalFrame
    {
        /// <summary>서브스텝당 골대 접촉 최대 횟수 (모서리 동시 접촉 포함, 무한 반복 방지).</summary>
        public const int MaxContactsPerSubstep = 2;

        /// <summary>
        /// 공 이동 경로(path: 시작 → s.Position)를 골대와 swept 판정하고 반발을 적용한다. 접촉마다 path가 꺾인다.
        /// 접촉은 시각 비율 오름차순, 같은 시각이면 골은 x=0 쪽 먼저, 부위는 PostLow → PostHigh → Crossbar.
        /// 같은 부위는 연속 두 번 잡지 않는다. 서브스텝당 최대 <see cref="MaxContactsPerSubstep"/>회.
        /// </summary>
        /// <param name="groundZ">이 높이 이하로 내려가는 시점 이후의 골대 접촉은 무시한다 (지면이 먼저, 리뷰 NB-1). 구름은 −∞.</param>
        public static void Collide(ref BallState s, ref BallPath path, BallParams p, BallEventBuffer events, float groundZ)
        {
            var R = p.Radius + p.PostRadius;
            var v = s.Velocity;
            var lastGoal = -1;
            var lastPart = GoalFramePart.None;

            for (int k = 0; k < MaxContactsPerSubstep; k++)
            {
                var segStart = path.Point(path.Count - 2);
                var segEnd = path.Last;
                var fracBase = path.Fraction(path.Count - 2);
                var fracSpan = 1f - fracBase;
                var minX = DMath.Min(segStart.X, segEnd.X);
                var maxX = DMath.Max(segStart.X, segEnd.X);
                var nearLow = minX <= R;
                var nearHigh = maxX >= p.Pitch.LengthM - R;
                if (!nearLow && !nearHigh) break;
                // 이 구간에서 지면에 닿는 비율 (닿지 않으면 2)
                var tGround = 2f;
                if (segEnd.Z <= groundZ)
                    tGround = segStart.Z > groundZ ? (segStart.Z - groundZ) / (segStart.Z - segEnd.Z) : 0f;

                var bestT = 2f;
                var bestGoal = -1;
                var bestPart = GoalFramePart.None;
                var bestN = Vec3.Zero;
                for (int g = 0; g < 2; g++)
                {
                    if (g == 0 && !nearLow) continue;
                    if (g == 1 && !nearHigh) continue;
                    var gx = g == 0 ? 0f : p.Pitch.LengthM;
                    for (var part = GoalFramePart.PostLow; part <= GoalFramePart.Crossbar; part++)
                    {
                        if (g == lastGoal && part == lastPart) continue;
                        if (!TryContact(segStart, segEnd, v, gx, part, p, R, out var t, out var n)) continue;
                        if (t < bestT && t < tGround)
                        {
                            bestT = t;
                            bestGoal = g;
                            bestPart = part;
                            bestN = n;
                        }
                    }
                }
                if (bestGoal < 0) break;

                var gxB = bestGoal == 0 ? 0f : p.Pitch.LengthM;
                // 원기둥 표면으로 밀어낸다 (축 방향 좌표는 유지)
                var c = PushOut(Lerp(segStart, segEnd, bestT), gxB, bestPart, bestN, p, R);
                var vIn = v;
                var vn = Vec3.Dot(v, bestN);
                v = v - bestN * ((1f + p.PostRestitution) * vn);
                var frac = fracBase + bestT * fracSpan;
                events.Add(new BallEvent(BallEventKind.GoalFrameHit, frac, c, vIn, v, s.Phase, s.Phase,
                    BallTransitionReason.None, BallBoundary.None, false, bestPart));

                path.TruncateAt(path.Count - 1, c, frac);
                path.Add(c + v * (p.Dt * (1f - frac)), 1f);
                lastGoal = bestGoal;
                lastPart = bestPart;
            }

            if (lastGoal < 0) return;
            s.Position = path.Last;
            s.Velocity = v;
        }

        static bool TryContact(Vec3 a, Vec3 b, Vec3 v, float gx, GoalFramePart part, BallParams p, float R, out float t, out Vec3 n)
        {
            n = Vec3.Zero;
            var cy = p.GoalCenterY;
            var half = BallParams.GoalWidth * 0.5f + p.PostRadius;
            var zc = BallParams.GoalHeight + p.PostRadius;
            if (part == GoalFramePart.Crossbar)
            {
                if (!Geometry.SweptSegmentCircle(new Vec2(a.X, a.Z), new Vec2(b.X, b.Z), new Vec2(gx, zc), R, out t)) return false;
                var c = Lerp(a, b, t);
                if (c.Y < cy - half || c.Y > cy + half) return false;
                n = Normal(c.X - gx, 0f, c.Z - zc, v);
            }
            else
            {
                var yc = part == GoalFramePart.PostLow ? cy - half : cy + half;
                if (!Geometry.SweptSegmentCircle(a.XY, b.XY, new Vec2(gx, yc), R, out t)) return false;
                var c = Lerp(a, b, t);
                if (c.Z > zc) return false;
                n = Normal(c.X - gx, c.Y - yc, 0f, v);
            }
            // 멀어지는 중(이미 밀려난 뒤)이면 접촉으로 보지 않는다
            return Vec3.Dot(v, n) < 0f;
        }

        static Vec3 Normal(float x, float y, float z, Vec3 v)
        {
            var n = new Vec3(x, y, z).Normalized();
            if (n == Vec3.Zero) n = new Vec3(-v.X, -v.Y, -v.Z).Normalized();
            return n;
        }

        static Vec3 PushOut(Vec3 c, float gx, GoalFramePart part, Vec3 n, BallParams p, float R)
        {
            var cy = p.GoalCenterY;
            var half = BallParams.GoalWidth * 0.5f + p.PostRadius;
            var zc = BallParams.GoalHeight + p.PostRadius;
            if (part == GoalFramePart.Crossbar) return new Vec3(gx + n.X * R, c.Y, zc + n.Z * R);
            var yc = part == GoalFramePart.PostLow ? cy - half : cy + half;
            return new Vec3(gx + n.X * R, yc + n.Y * R, c.Z);
        }

        static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        /// <summary>
        /// 경계 통과 신호 (FR-41): 공 중심이 경계(피치 바깥 가장자리)에서 반지름 이상 바깥으로 나간 순간.
        /// 선에 걸친 상태는 신호 없음. 꺾인 경로의 구간마다 판정해 같은 시간축 비율로 낸다 (나갔다 들어와 다시 나가면 두 번).
        /// 시각 정렬은 Step 끝에서 한다.
        /// </summary>
        public static void DetectBoundary(in BallPath path, BallPhase phase, BallParams p, BallEventBuffer events)
        {
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var f0 = path.Fraction(i);
                DetectBoundarySegment(path.Point(i), path.Point(i + 1), f0, path.Fraction(i + 1) - f0, phase, p, events);
            }
        }

        /// <summary>구간 하나(시간축 f0 ~ f0+span)의 경계 통과 판정.</summary>
        public static void DetectBoundarySegment(Vec3 a, Vec3 b, float f0, float span, BallPhase phase, BallParams p, BallEventBuffer events)
        {
            var r = p.Radius;
            var L = p.Pitch.LengthM;
            var W = p.Pitch.WidthM;
            if (a.X >= -r && b.X < -r) Emit(a, b, (a.X + r) / (a.X - b.X), f0, span, BallBoundary.GoalLineLow, phase, p, events);
            else if (a.X <= L + r && b.X > L + r) Emit(a, b, (L + r - a.X) / (b.X - a.X), f0, span, BallBoundary.GoalLineHigh, phase, p, events);
            if (a.Y >= -r && b.Y < -r) Emit(a, b, (a.Y + r) / (a.Y - b.Y), f0, span, BallBoundary.TouchlineLow, phase, p, events);
            else if (a.Y <= W + r && b.Y > W + r) Emit(a, b, (W + r - a.Y) / (b.Y - a.Y), f0, span, BallBoundary.TouchlineHigh, phase, p, events);
        }

        static void Emit(Vec3 a, Vec3 b, float t, float f0, float span, BallBoundary boundary, BallPhase phase, BallParams p, BallEventBuffer events)
        {
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            var c = Lerp(a, b, t);
            var mouth = (boundary == BallBoundary.GoalLineLow || boundary == BallBoundary.GoalLineHigh)
                        && DMath.Abs(c.Y - p.GoalCenterY) < BallParams.GoalWidth * 0.5f
                        && c.Z < BallParams.GoalHeight;
            events.Add(new BallEvent(BallEventKind.BoundaryCrossed, f0 + t * span, c, default, default, phase, phase,
                BallTransitionReason.None, boundary, mouth, GoalFramePart.None));
        }

        /// <summary>
        /// 골망 감속 영역 (FR-42): 골라인 뒤 깊이 netDepth, 골문 폭·높이 안에서 서브스텝마다 속도·스핀 ×damping.
        /// 뒤판을 넘지 않는다. 정확도 요구 낮음 (연출 일관성용).
        /// </summary>
        public static void ApplyNet(ref BallState s, BallParams p)
        {
            var pos = s.Position;
            if (DMath.Abs(pos.Y - p.GoalCenterY) >= BallParams.GoalWidth * 0.5f || pos.Z >= BallParams.GoalHeight) return;
            float back;
            if (pos.X < 0f) back = -p.NetDepth;
            else if (pos.X > p.Pitch.LengthM) back = p.Pitch.LengthM + p.NetDepth;
            else return;

            s.Velocity = s.Velocity * p.NetDamping;
            s.Spin = s.Spin * p.NetDamping;
            if ((back < 0f && pos.X < back) || (back > 0f && pos.X > back))
            {
                s.Position = new Vec3(back, pos.Y, pos.Z);
                s.Velocity = new Vec3(0f, s.Velocity.Y, s.Velocity.Z);
            }
        }
    }
}
