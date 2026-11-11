using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>GK 역할 (specs/011 FR-71, 기획서 4.2.1). 역할 데이터는 003 — 그 전에는 Standard.</summary>
    public enum KeeperRole : byte
    {
        Standard = 0,
        Sweeper = 1,
        Traditional = 2,
    }

    /// <summary>위치 탐색 입력 (미터, 자기 골 기준).</summary>
    public struct PositionInput
    {
        /// <summary>자기 골문 중앙 (골라인 위).</summary>
        public Vec2 Goal;
        /// <summary>골라인에서 피치 안쪽 x 방향 (+1 / −1).</summary>
        public float DirIn;
        public Vec2 Ball;
        /// <summary>자기 골라인에서 009 수비 라인까지 거리 (m). 음수면 모름 → 기본값.</summary>
        public float LineHeight;
        /// <summary>009 국면이 DefHigh인가 (스위퍼 키퍼 전진 조건, FR-05).</summary>
        public bool HighBlock;
        public KeeperRole Role;
        /// <summary>위치 오차 (FR-06, 호출자가 재추첨 시점에만 바꿈). 0이면 노이즈 없음.</summary>
        public Vec2 Noise;
        public bool HasPrev;
        public Vec2 Prev;
    }

    /// <summary>
    /// GK 목표 위치 (specs/011 FR-01~07). 각 이등분선 위 후보의 슛 위험도 risk(P) = Σ_t w_t·σ((d_req − R(t_ball)) / s)에
    /// λ·|P − P_prev| + μ·max(0, depth − h_safe)를 더한 목적 함수를 soft-argmin(온도 κ)으로 줄여 공 위치의 연속 함수로 만든다(FR-07).
    /// 슈팅 가능 거리 경계에서는 대기 깊이 점과 3 m 구간에서 섞고, 골문이 거의 안 보이는 각이면 니어포스트 커버로 섞는다(FR-01).
    /// RNG를 쓰지 않는다. 할당 없음(후보 버퍼는 생성 때 잡음).
    /// </summary>
    public sealed class KeeperPositioner
    {
        public const float BallZ = 0.11f;
        const float Eps = 1e-4f;

        readonly GoalkeeperParams _p;
        readonly GoalGrid _grid;
        readonly Vec2[] _cand;
        readonly float[] _obj;

        public KeeperPositioner(GoalkeeperParams p, GoalGrid grid)
        {
            _p = p;
            _grid = grid;
            var max = (int)p.PosCandidates + 3;
            _cand = new Vec2[max];
            _obj = new float[max];
        }

        public GoalGrid Grid => _grid;

        /// <summary>
        /// 각 이등분선이 골라인과 만나는 점 Q (각의 이등분선 정리: Q = (d2·P1 + d1·P2)/(d1 + d2)).
        /// 보이는 각(rad)을 함께 돌려준다. 공이 골라인 위·뒤면 각 0, Q = 가까운 포스트.
        /// </summary>
        public static Vec2 BisectorFoot(Vec2 goal, float dirIn, Vec2 ball, out float angle)
        {
            var half = BallParams.GoalWidth * 0.5f;
            var p1 = new Vec2(goal.X, goal.Y - half);
            var p2 = new Vec2(goal.X, goal.Y + half);
            var depth = (ball.X - goal.X) * dirIn;
            if (depth <= Eps)
            {
                angle = 0f;
                return ball.Y < goal.Y ? p1 : p2;
            }
            var a = p1 - ball;
            var b = p2 - ball;
            var d1 = a.Length();
            var d2 = b.Length();
            angle = DMath.Abs(DMath.Atan2(Vec2.Cross(a, b), Vec2.Dot(a, b)));
            var s = d1 + d2;
            if (s < Eps) return goal;
            return new Vec2(goal.X, p1.Y + (p2.Y - p1.Y) * (d1 / s));
        }

        /// <summary>
        /// 공이 포스트 바깥에서 골라인과 이루는 각 (rad): atan2(공 깊이, 포스트 바깥 좌우 거리). 포스트 사이 앞이면 π/2, 골라인 위·뒤면 0.
        /// </summary>
        public static float GoalLineAngle(Vec2 goal, float dirIn, Vec2 ball)
        {
            var depth = (ball.X - goal.X) * dirIn;
            if (depth <= 0f) return 0f;
            var outside = DMath.Abs(ball.Y - goal.Y) - BallParams.GoalWidth * 0.5f;
            if (outside <= 0f) return (float)(System.Math.PI * 0.5);
            return DMath.Atan2(depth, outside);
        }

        /// <summary>이등분선(골라인 위 이등분점 → 공) 위, 이등분점에서 거리 d(m)인 점. 공까지 거리의 0.8배로 자른다.</summary>
        public static Vec2 OnBisector(Vec2 goal, float dirIn, Vec2 ball, Vec2 foot, float depth)
        {
            var v = ball - foot;
            var len = v.Length();
            if (len <= Eps || (ball.X - goal.X) * dirIn <= Eps) return new Vec2(goal.X + dirIn * depth, foot.Y);
            var d = DMath.Min(depth, len * 0.8f);
            if (d < 0f) d = 0f;
            return foot + v * (d / len);
        }

        /// <summary>대기 위치: 같은 이등분선 위 거리 d. 측면 공이어도 포스트 사이를 벗어나지 않게 좌우를 포스트 안으로 자른다(자르기는 단조).</summary>
        public static Vec2 WaitPoint(Vec2 goal, float dirIn, Vec2 ball, Vec2 foot, float depth)
        {
            var p = OnBisector(goal, dirIn, ball, foot, depth);
            var half = BallParams.GoalWidth * 0.5f;
            return new Vec2(p.X, Interp.Clamp(p.Y, goal.Y - half, goal.Y + half));
        }

        /// <summary>대기 깊이 depth_base (FR-05): clamp(0.3 × 라인 높이, 4, 20), 스위퍼는 DefHigh에서 라인 − 18 m까지(상한 20 우선), 전통 × 0.6.</summary>
        public float WaitDepth(float lineHeight, bool highBlock, KeeperRole role)
        {
            var line = lineHeight >= 0f ? lineHeight : _p.WaitDefaultLine;
            var d = Interp.Clamp(_p.WaitRatio * line, _p.WaitMin, _p.WaitMax);
            if (role == KeeperRole.Sweeper && highBlock) d = DMath.Min(DMath.Max(d, line - _p.WaitSweeperOffset), _p.WaitMax);
            else if (role == KeeperRole.Traditional) d *= _p.WaitTraditional;
            return d;
        }

        /// <summary>
        /// 후보 P에서의 슛 위험도 (FR-02). 공 → 셀 중심 직선이 P의 깊이 평면을 지나는 점까지의 비행 시간 t_ball(가정 속도)과
        /// 필요한 도달 거리 d_req(좌우 − 몸 반폭, 손 높이 초과분)를 FR-03 도달거리 R(t_ball)과 비교한다.
        /// </summary>
        public float Risk(Vec2 goal, float dirIn, Vec2 ball, Vec2 p, in KeeperProfile k)
        {
            Prepare(goal, ball);
            var bd = (ball.X - goal.X) * dirIn;
            var pd = (p.X - goal.X) * dirIn;
            if (bd <= Eps) bd = Eps;
            var f = 1f - pd / bd; // 공에서 셀까지 가는 비율 (P 평면)
            if (f < 0f) f = 0f;
            var invScale = 1f / _p.PosRiskScale;
            var invSpeed = f / _p.PosShotSpeed;
            var lateralBase = ball.Y - p.Y;
            var body = _p.ReachBodyHalf;
            var standZ = k.StandReachZ;
            var r0 = _p.ReachR0;
            var react = k.ReactTime;
            var dive = k.DiveSpeed;
            var highZ = _p.ReachHighZ;
            var fadeK = _p.ReachHighFade / DMath.Max(BallParams.GoalHeight - highZ, Eps);
            float risk = 0f;
            for (int t = 0; t < GoalGrid.Cells; t++)
            {
                // 공 → 셀 직선이 P 깊이 평면을 지나는 점: 좌우 = 공 + dy·f, 높이 = 공 + dz·f, 비행 거리 = |T − B|·f (셀별 상수는 Prepare)
                var lat = DMath.Abs(lateralBase + _cellDy[t] * f) - body;
                if (lat < 0f) lat = 0f;
                var cz = BallZ + _cellDz[t] * f;
                var vex = cz - standZ;
                var dReq = vex > 0f ? DMath.Sqrt(lat * lat + vex * vex) : lat;
                // ReachAfterReact 인라인 (같은 식)
                var after = _cellDist[t] * invSpeed - react;
                var reach = r0 + dive * (after > 0f ? after : 0f);
                var fade = cz <= highZ ? 1f : 1f - DMath.Min(_p.ReachHighFade, (cz - highZ) * fadeK);
                var rmax = k.MaxReach * fade;
                if (rmax < r0) rmax = r0;
                if (reach > rmax) reach = rmax;
                risk += _grid.Prior(t) * SoftStep((dReq - reach) * invScale);
            }
            return risk;
        }

        // 셀별 상수 캐시 (공·골이 같으면 재사용) — 위치 탐색 한 번에 후보 15개가 같은 값을 쓴다 (AC-17)
        readonly float[] _cellDy = new float[GoalGrid.Cells], _cellDz = new float[GoalGrid.Cells], _cellDist = new float[GoalGrid.Cells];
        Vec2 _cacheGoal = new Vec2(float.NaN, 0f), _cacheBall;

        void Prepare(Vec2 goal, Vec2 ball)
        {
            if (goal == _cacheGoal && ball == _cacheBall) return;
            _cacheGoal = goal;
            _cacheBall = ball;
            var dx = goal.X - ball.X;
            for (int t = 0; t < GoalGrid.Cells; t++)
            {
                var dy = goal.Y + _grid.OffsetY(t) - ball.Y;
                var dz = _grid.Height(t) - BallZ;
                _cellDy[t] = dy;
                _cellDz[t] = dz;
                _cellDist[t] = DMath.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        /// <summary>위험도용 부드러운 계단 0.5 + 0.5·x/(1 + |x|) — 시그모이드와 같은 모양(단조·대칭)이고 지수 함수가 없다 (AC-17).</summary>
        public static float SoftStep(float x) => 0.5f + 0.5f * x / (1f + DMath.Abs(x));

        /// <summary>필요 도달 거리: 좌우 |dy| − 몸 반폭(≥ 0)과 손 도달 높이 초과분의 합성 (FR-03).</summary>
        public float RequiredReach(float lateral, float z, in KeeperProfile k)
        {
            var lat = DMath.Abs(lateral) - _p.ReachBodyHalf;
            if (lat < 0f) lat = 0f;
            var vex = z - k.StandReachZ;
            if (vex < 0f) vex = 0f;
            return DMath.Sqrt(lat * lat + vex * vex);
        }

        /// <summary>FR-03 가용 도달거리 R(τ) = r0 + v_dive·max(0, τ − t_react), 상한 r_max(높은 구석 감쇄).</summary>
        public float Reach(float tau, float z, in KeeperProfile k) => ReachAfterReact(tau - k.ReactTime, z, in k);

        /// <summary>반응 뒤 남은 시간 τ'로 본 도달거리 (선방 판정은 t_react_eff를 직접 뺀 τ'를 쓴다).</summary>
        public float ReachAfterReact(float tauAfter, float z, in KeeperProfile k)
        {
            var r = _p.ReachR0 + k.DiveSpeed * (tauAfter > 0f ? tauAfter : 0f);
            var rmax = k.MaxReach * HighFade(z);
            if (rmax < _p.ReachR0) rmax = _p.ReachR0;
            return r < rmax ? r : rmax;
        }

        /// <summary>높은 구석 감쇄 (FR-03): highZ 위에서 크로스바 높이까지 1 → 1 − highFade 선형.</summary>
        public float HighFade(float z)
        {
            if (z <= _p.ReachHighZ) return 1f;
            var span = BallParams.GoalHeight - _p.ReachHighZ;
            var u = span > Eps ? (z - _p.ReachHighZ) / span : 1f;
            if (u > 1f) u = 1f;
            return 1f - _p.ReachHighFade * u;
        }

        /// <summary>
        /// 목표점 (FR-01~07). 노이즈(FR-06)·직전 목표 이동 상한(FR-07)은 입력으로 받는다. 진단 싱크는 null 가능.
        /// </summary>
        public Vec2 Target(in PositionInput inp, in KeeperProfile k, int team = 0, IKeeperDiagnostics diag = null)
        {
            var goal = inp.Goal;
            var dirIn = inp.DirIn;
            var ball = inp.Ball;
            var foot = BisectorFoot(goal, dirIn, ball, out var angle);

            // 대기 깊이 점 (같은 이등분선 위)
            var wait = WaitPoint(goal, dirIn, ball, foot, WaitDepth(inp.LineHeight, inp.HighBlock, inp.Role));

            // 슈팅 가능 거리 섞기 가중 (FR-07: 경계 ±band/2 선형 — smoothstep은 가운데 기울기가 1.5배라 경계 이동량 상한(AC-22)을 넘는다)
            var dist = (ball - goal).Length();
            var band = _p.PosBlendBand;
            float u;
            if (band <= Eps) u = dist <= _p.PosShootRange ? 1f : 0f;
            else u = Interp.Clamp((_p.PosShootRange + band * 0.5f - dist) / band, 0f, 1f);

            var target = wait;
            if (u > 0f)
            {
                var best = SearchSoft(in inp, foot, in k, team, diag);
                target = wait + (best - wait) * u;
            }

            // 니어포스트 우선 (FR-01): 공이 골라인 근처 측면(포스트 바깥에서 골라인과 이루는 각이 임계 미만)이면 니어포스트 커버 점으로 섞는다
            var thr = _p.PosNearPostAngle * (float)(System.Math.PI / 180.0);
            var half = BallParams.GoalWidth * 0.5f;
            var lineAngle = GoalLineAngle(goal, dirIn, ball);
            if (thr > Eps && lineAngle < thr)
            {
                var w = lineAngle / thr;
                var side = ball.Y < goal.Y ? -1f : 1f;
                var near = new Vec2(goal.X + dirIn * _p.PosDepthMin, goal.Y + side * (half - _p.ReachBodyHalf - 0.1f));
                target = near + (target - near) * w;
            }

            target += inp.Noise;
            target = Contain(goal, dirIn, target);

            if (inp.HasPrev)
            {
                var step = target - inp.Prev;
                var len = step.Length();
                if (len > _p.PosMaxStep && len > Eps) target = inp.Prev + step * (_p.PosMaxStep / len);
            }
            diag?.OnTarget(team, target, ball, foot);
            return target;
        }

        /// <summary>후보(깊이 오름차순 × 좌우 −, 0, +)의 목적 함수를 soft-argmin으로 섞는다.</summary>
        Vec2 SearchSoft(in PositionInput inp, Vec2 foot, in KeeperProfile k, int team, IKeeperDiagnostics diag)
        {
            var goal = inp.Goal;
            var dirIn = inp.DirIn;
            var ball = inp.Ball;
            var n = k.Candidates;
            if (n > _cand.Length) n = _cand.Length;
            var depths = (n + 2) / 3;
            var lateralAxis = new Vec2(-(ball - foot).Y, (ball - foot).X).Normalized();
            if (lateralAxis == Vec2.Zero) lateralAxis = new Vec2(0f, 1f);
            var hSafe = _p.PosHSafeBase + _p.PosHSafeRatio * (ball - goal).Length();
            int c = 0;
            float minObj = float.MaxValue;
            for (int i = 0; i < depths && c < n; i++)
            {
                var depth = depths == 1 ? _p.PosDepthMin : _p.PosDepthMin + (_p.PosDepthMax - _p.PosDepthMin) * i / (depths - 1);
                var basePt = OnBisector(goal, dirIn, ball, foot, depth);
                for (int j = -1; j <= 1 && c < n; j++)
                {
                    var pt = Contain(goal, dirIn, basePt + lateralAxis * (j * _p.PosLateral));
                    var risk = Risk(goal, dirIn, ball, pt, in k);
                    var d = (pt.X - goal.X) * dirIn;
                    var obj = risk + _p.PosMu * DMath.Max(0f, d - hSafe);
                    if (inp.HasPrev) obj += _p.PosLambda * (pt - inp.Prev).Length();
                    _cand[c] = pt;
                    _obj[c] = obj;
                    if (obj < minObj) minObj = obj;
                    diag?.OnRiskCandidate(team, pt, risk, obj);
                    c++;
                }
            }
            float wsum = 0f, x = 0f, y = 0f;
            var kappa = _p.PosSoftTemp;
            for (int i = 0; i < c; i++)
            {
                var w = DMath.Exp(-(_obj[i] - minObj) / kappa);
                wsum += w;
                x += _cand[i].X * w;
                y += _cand[i].Y * w;
            }
            return new Vec2(x / wsum, y / wsum);
        }

        /// <summary>골문 안·골라인 뒤 금지(깊이 ≥ 최소 깊이 0.5 m의 절반), 좌우는 포스트 바깥 1 m까지.</summary>
        Vec2 Contain(Vec2 goal, float dirIn, Vec2 p)
        {
            var minD = _p.PosDepthMin * 0.5f;
            var d = (p.X - goal.X) * dirIn;
            var x = d < minD ? goal.X + dirIn * minD : p.X;
            var lim = BallParams.GoalWidth * 0.5f + 1f;
            var y = Interp.Clamp(p.Y, goal.Y - lim, goal.Y + lim);
            return new Vec2(x, y);
        }

        public static float Sigmoid(float x)
        {
            if (x > 30f) return 1f;
            if (x < -30f) return 0f;
            return 1f / (1f + DMath.Exp(-x));
        }
    }
}
