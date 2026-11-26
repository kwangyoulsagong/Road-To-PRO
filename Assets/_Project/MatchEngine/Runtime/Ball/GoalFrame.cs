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
        /// 골망 그물 면 (specs/059 FR-41, 004 FR-42 감속 영역 대체). 그물 상자 = 골라인 뒤 깊이 netDepth · 폭 7.32 m · 높이 2.44 m.
        /// ① 골 안 공(<see cref="BallState.NetSide"/> ≠ 0 — 서브스텝 시작이 정지 그물 상자 안이면 표시, 피치로 돌아오면 지움):
        ///    뒤·옆·위 면(중심 기준 면 − 반지름)을 넘으면 그물을 밀고 들어간다(FR-41 ⑥, 2026-11-26 사용자 결정 "공이 그물을 밀고 들어감") —
        ///    면 바깥 방향으로 임계 감쇠 스프링(ω)이 되밀고, 접선 속도·스핀은 감쇠(slide), 깊이는 pocket × 테두리 감소(edgeBand 안쪽 smoothstep) 넘지 않음.
        ///    옆·위 면은 공이 골라인을 완전히 넘은 뒤(d ≥ r)만 — 넘는 중인 공은 골대 충돌이 다룬다 (결정론 리뷰 3).
        /// ② 바깥 공(표시 없음 — 빗나가 골라인 뒤로 나간 공·크로스바 위로 넘어간 공): 바깥 면(옆그물 바깥·뒤 그물 뒤·위 그물 윗면)에서
        ///    중심을 반지름만큼 바깥에 고정, 수직 속도 → −e·v⊥ (2026-11-26 사용자 결정 "지금 바깥 면에서 막기". 바깥 면 출렁임 화면 검증은 012, Q2 (a)).
        /// 면에 닿기 전에는 아무것도 하지 않는다(상태를 쓰지도 않음). 골라인 뒤 공만 다루므로 골 판정(골라인 통과, <see cref="DetectBoundarySegment"/>)에는 영향 없음.
        /// 알려진 한계(012 화면 검증 때): 위 그물 윗면에 멈춘 바깥 공은 Air 상태로 남는다(재시작 재배치 전까지).
        /// </summary>
        public static void ApplyNet(ref BallState s, Vec3 start, BallParams p)
        {
            var pos = s.Position;
            float line, sx;
            if (pos.X < 0f) { line = 0f; sx = -1f; }
            else if (pos.X > p.Pitch.LengthM) { line = p.Pitch.LengthM; sx = 1f; }
            else
            {
                if (s.NetSide != 0) s.NetSide = 0;   // 피치로 돌아옴
                return;
            }

            float half = BallParams.GoalWidth * 0.5f, H = BallParams.GoalHeight, D = p.NetDepth, r = p.Radius, cy = p.GoalCenterY;
            // 골 쪽 좌표: d = 골라인 뒤 깊이(+), w = 중심에서 옆 거리(부호), vd = 깊이 방향 속도
            float d = sx * (pos.X - line), w = pos.Y - cy, z = pos.Z;
            float ds = sx * (start.X - line), ws = start.Y - cy, zs = start.Z;
            var v = s.Velocity;
            float vd = sx * v.X, vw = v.Y, vz = v.Z;
            bool hit = false, moved = false;

            sbyte side = sx < 0f ? (sbyte)-1 : (sbyte)1;
            // 표시가 있어도 시작이 주머니 최대 상자 안일 때만 (결정론 리뷰 2b — 남은 표시로 바깥 공을 잡지 않게)
            float P = p.NetPocket;
            bool inside = (s.NetSide == side && DMath.Abs(ws) < half - r + P && zs < H - r + P && ds < D - r + P)
                          || (DMath.Abs(ws) < half && zs < H && ds < D);
            if (inside)
            {
                s.NetSide = side;   // 표시만 바뀐 경우 위치·속도는 쓰지 않는다 (결정론 리뷰 4)
                float slide = p.NetSlideFactor;
                bool pocket = false, pd = false, pw = false, pz = false;
                // 뒤 면 (면 위 테두리 거리: 옆·바닥·위)
                // 테두리 거리는 공 표면 기준(− r) — 골대·지지대에 묶인 줄 근처는 공이 거의 못 민다. 바닥은 공이 늘 r 떠 있어 중심 높이 그대로
                Pocket(ref d, ref vd, D - r, Edge(half - DMath.Abs(w) - r, z, H - z - r, float.MaxValue, p), p, ref pd);
                if (d >= r)
                {
                    float aw = DMath.Abs(w), sw = w >= 0f ? 1f : -1f;
                    float ow = aw, vo = vw * sw;
                    Pocket(ref ow, ref vo, half - r, Edge(d - r, D - d - r, z, H - z - r, p), p, ref pw);
                    w = sw * ow;
                    vw = sw * vo;
                    Pocket(ref z, ref vz, H - r, Edge(half - aw - r, d - r, D - d - r, float.MaxValue, p), p, ref pz);
                }
                pocket = pd || pw || pz;
                if (pocket)
                {
                    moved = hit = true;
                    // 주머니 안: 접선 감쇠(그물이 공을 잡는다). 수직 성분은 스프링 몫이라 감쇠하지 않는다 —
                    // 감쇠하면 굴러온 공이 되밀리기 전에 멈춰 주머니 안에 남는다(T18a)
                    if (pd) { vw *= slide; vz *= slide; }
                    if (pw) { vd *= slide; vz *= slide; }
                    if (pz) { vd *= slide; vw *= slide; }
                }
                if (hit) s.Spin = s.Spin * slide;
                if (!moved) return;
                s.Position = new Vec3(line + sx * d, cy + w, z);
                s.Velocity = new Vec3(sx * vd, vw, vz);
                return;
            }

            // 바깥 면 상자(그물 면 + 반지름) 안으로 들어오면, 시작이 바깥이었던 면 중 덜 파고든 면 하나로 밀어낸다
            {
                float aw = DMath.Abs(w);
                if (d > 0f && d < D + r && aw < half + r && z < H + r)
                {
                    float best = float.MaxValue;
                    int face = -1;
                    if (ds >= D && D + r - d < best) { best = D + r - d; face = 0; }
                    if (DMath.Abs(ws) >= half && half + r - aw < best) { best = half + r - aw; face = 1; }
                    if (zs >= H && H + r - z < best) { best = H + r - z; face = 2; }
                    moved = face >= 0;
                    if (face == 0) { d = D + r; if (vd < 0f) { Hit(ref vd, ref vw, ref vz, 0, p); hit = true; } }
                    else if (face == 1)
                    {
                        float sideW = ws >= 0f ? 1f : -1f;
                        w = sideW * (half + r);
                        if (vw * sideW < 0f) { Hit(ref vd, ref vw, ref vz, 1, p); hit = true; }
                    }
                    else if (face == 2) { z = H + r; if (vz < 0f) { Hit(ref vd, ref vw, ref vz, 2, p); hit = true; } }
                }
            }

            // 면에 안 닿은 공은 아무것도 쓰지 않는다 — 좌표 왕복(cy + w)의 1 ulp 차이도 남기지 않음 (리뷰 1)
            if (!moved) return;
            s.Position = new Vec3(line + sx * d, cy + w, z);
            s.Velocity = new Vec3(sx * vd, vw, vz);
            if (hit) s.Spin = s.Spin * p.NetTangentKeep;
        }

        /// <summary>
        /// 그물 주머니 한 면 (FR-41 ⑥): 바깥 방향 좌표 x가 한계 lim을 넘으면(깊이 pen) 임계 감쇠 스프링으로 바깥 속도 v를 줄이고
        /// (v += dt·(−ω²·pen − 2ω·v), 반암시적), 깊이가 허용(pocket × 테두리 감소) 넘으면 그 자리에 고정·바깥 속도 ≤ −dt·ω²·허용(되밀기).
        /// </summary>
        static void Pocket(ref float x, ref float vOut, float lim, float edgeFactor, BallParams p, ref bool touched)
        {
            float pen = x - lim;
            if (pen <= 0f) return;
            touched = true;
            float w = p.NetStiffness, dt = p.Dt;
            vOut += dt * (-w * w * pen - 2f * w * vOut);
            float allowed = p.NetPocket * edgeFactor;
            if (pen > allowed)
            {
                // 주머니 끝: 그 자리에 두고, 늘어난 그물이 되미는 한 스텝 속도를 준다 — 0으로 두면 굴러온 공이 다음 스텝에 정지해 주머니에 남는다
                x = lim + allowed;
                float back = -dt * w * w * allowed;
                if (vOut > back) vOut = back;
            }
        }

        /// <summary>면 위 테두리까지 거리(네 방향 중 최소) → 주머니 깊이 비율 smoothstep(거리 / edgeBand).</summary>
        static float Edge(float a, float b, float c, float e, BallParams p)
        {
            float m = DMath.Min(DMath.Min(a, b), DMath.Min(c, e));
            if (m <= 0f) return 0f;
            float t = m / p.NetEdgeBand;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }

        /// <summary>그물 면 접촉 응답: 축 axis(0 깊이·1 옆·2 높이)의 수직 속도 → −e·v⊥, 나머지 두 축 × keep.</summary>
        static void Hit(ref float vd, ref float vw, ref float vz, int axis, BallParams p)
        {
            float e = p.NetRestitution, k = p.NetTangentKeep;
            if (axis == 0) { vd = -vd * e; vw *= k; vz *= k; }
            else if (axis == 1) { vw = -vw * e; vd *= k; vz *= k; }
            else { vz = -vz * e; vd *= k; vw *= k; }
        }
    }
}
