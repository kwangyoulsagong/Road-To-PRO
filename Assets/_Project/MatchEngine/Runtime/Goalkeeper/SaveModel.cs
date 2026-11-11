using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>선방 결과 종류 (specs/011 FR-10·12).</summary>
    public enum SaveOutcomeKind : byte
    {
        None = 0,
        Catch = 1,
        ParrySafe = 2,
        ParryDanger = 3,
        Punch = 4,
        /// <summary>도달 실패 — 공은 그대로 간다 (골이면 실점).</summary>
        Miss = 5,
    }

    /// <summary>GK 상태 스냅샷 (슈팅 순간).</summary>
    public struct KeeperState
    {
        public Vec2 Position;
        public Vec2 Velocity;
    }

    /// <summary>판정 입력 문맥: 자기 골문과 혼잡도.</summary>
    public struct SaveContext
    {
        public Vec2 Goal;
        public float DirIn;
        /// <summary>가림·굴절로 늘어난 반응 지연 (s, FR-11).</summary>
        public float ExtraDelay;
        /// <summary>접촉 예정점 반경 안 다른 선수 수 (펀칭 조건 '혼잡', FR-10).</summary>
        public int Crowd;
    }

    /// <summary>
    /// RNG 없이 계산한 판정 (FR-10 1단계 + 2단계 확률, FR-14). <see cref="SaveModel.Evaluate"/>의 결과이고
    /// <see cref="SaveModel.Resolve"/>가 이것만 보고 결과를 굴린다 — 평가와 실제 판정이 같은 계수를 쓴다(AC-19).
    /// </summary>
    public struct SaveJudgement
    {
        public bool OnTarget;
        /// <summary>골라인 통과 예정 시각 (슛 기준 s).</summary>
        public float CrossTime;
        public float ReactTime;
        public float PReach;
        public float MaxMargin;
        /// <summary>접촉 예정 샘플 (경로 버퍼 인덱스, 슛 기준 시각 = (i + 1)·dt).</summary>
        public int ContactIndex;
        public float ContactTime;
        public Vec3 ContactPoint;
        public Vec3 ContactVelocity;
        public float RequiredReach, Reach, Difficulty;
        public float PCatch, PParrySafe;
        public bool Punch;
        /// <summary>GK 기준 접촉점 방향 (−1 왼쪽 · 0 정면 · +1 오른쪽, 골문을 바라본 공격 방향 기준 아님 — 골문 y 증가 쪽이 +1).</summary>
        public int Direction;
        public DiveHeight Height;
        /// <summary>몸 반폭을 넘는 좌우 이동이 필요해 다이빙하는가.</summary>
        public bool Dives;
        /// <summary>정지 자세가 아니어서 reach 패널티를 받았는가 (FR-09).</summary>
        public bool MovingPenalty;

        /// <summary>FR-14: P_beatGK = 1 − p_reach × p_처리성공. 처리(잡기·쳐내기·펀칭)는 모두 실점을 막으므로 p_처리성공 = 1.</summary>
        public float BeatProbability => OnTarget ? 1f - PReach : 0f;
    }

    /// <summary>판정 결과 (RNG 적용 뒤).</summary>
    public struct SaveOutcome
    {
        public SaveOutcomeKind Kind;
        /// <summary>쳐내기·펀칭 후 공 속도 (004에 넘김).</summary>
        public Vec3 ReboundVelocity;
        /// <summary>위험한 쳐내기의 리바운드 목표 지점(골라인 앞 거리 = 6 − 3n m ± 퍼짐, FR-12).</summary>
        public Vec2 ReboundTarget;
        /// <summary>리바운드 목표의 골라인에서 깊이 (m).</summary>
        public float ReboundDepth;
        /// <summary>실점이고 손끝이 닿았나 (FR-92).</summary>
        public bool Fingertip;
    }

    /// <summary>
    /// 선방 모델 (specs/011 FR-03·09~14). 슛 직후 공 상태로 004 스테퍼를 복사본에서 돌려 경로(서브스텝마다)를 만들고,
    /// 경로 전체에서 margin(τ) = R(τ − t_react_eff) − d_req(τ) (− 이동 패널티)의 최댓값으로 p_reach = σ(max margin / s_reach)를 낸다.
    /// 처리 확률은 난이도 h = α·v + β·(d_req/R) + γ·[높이 극단] + δ·[바운드 직전 낮은 공]으로 잡기 σ(A − h), 안전한 쳐내기 σ(b·H + c·P − h).
    /// 공중볼이고 혼잡하거나 빠르면 펀칭. 경로 버퍼는 생성 때 잡는다(할당 0). 경기 하나에 하나, 단일 스레드.
    /// </summary>
    public sealed class SaveModel
    {
        public const int MaxSteps = 300;
        const float Eps = 1e-5f;

        readonly GoalkeeperParams _p;
        readonly KeeperPositioner _pos;
        readonly Vec3[] _path = new Vec3[MaxSteps];
        readonly Vec3[] _vel = new Vec3[MaxSteps];
        readonly float[] _bounce = new float[8];
        readonly BallEventBuffer _ev = new BallEventBuffer(16);
        int _count, _bounceCount;
        bool _onTarget;
        float _crossTime, _dt;

        public SaveModel(GoalkeeperParams p, KeeperPositioner positioner)
        {
            _p = p;
            _pos = positioner;
        }

        public GoalkeeperParams Params => _p;
        public KeeperPositioner Positioner => _pos;
        public int PathCount => _count;
        public Vec3 PathPoint(int i) => _path[i];
        public bool PathOnTarget => _onTarget;
        public float PathCrossTime => _crossTime;

        /// <summary>
        /// 경로 예측: 골라인(자기 골) 통과·정지·컨트롤·상한 3 s에서 끝. 골문 입구로 통과하면 유효(on target).
        /// 입력 상태는 바꾸지 않는다. 004 스테퍼 그대로라 실제 궤적과 비트 동일.
        /// </summary>
        public bool Predict(in BallState start, BallParams bp, Vec2 goal, float dirIn)
        {
            _count = 0;
            _bounceCount = 0;
            _onTarget = false;
            _crossTime = 0f;
            _dt = bp.Dt;
            var s = start;
            var lowGoal = goal.X < bp.Pitch.LengthM * 0.5f;
            for (int i = 0; i < MaxSteps; i++)
            {
                if (s.Phase == BallPhase.Rest || s.Phase == BallPhase.Controlled) break;
                _ev.Clear();
                BallStepper.Step(ref s, bp, _ev, out _);
                var crossed = false;
                for (int k = 0; k < _ev.Count; k++)
                {
                    ref readonly var e = ref _ev[k];
                    if (e.Kind == BallEventKind.Bounce && _bounceCount < _bounce.Length) _bounce[_bounceCount++] = (i + e.Fraction) * bp.Dt;
                    if (e.Kind == BallEventKind.BoundaryCrossed)
                    {
                        var own = lowGoal ? e.Boundary == BallBoundary.GoalLineLow : e.Boundary == BallBoundary.GoalLineHigh;
                        crossed = true;
                        if (own && e.InGoalMouth)
                        {
                            _onTarget = true;
                            _crossTime = (i + e.Fraction) * bp.Dt;
                        }
                    }
                }
                if (crossed) break; // 라인 통과 서브스텝은 경로에 넣지 않는다 — 접촉은 항상 통과 전 서브스텝
                _path[_count] = s.Position;
                _vel[_count] = s.Velocity;
                _count++;
            }
            return _onTarget;
        }

        /// <summary>
        /// RNG 없는 판정 (FR-10·14). 직전 <see cref="Predict"/> 경로를 쓴다. 유효 슛이 아니면 OnTarget false.
        /// </summary>
        public SaveJudgement Evaluate(in KeeperState gk, in KeeperProfile k, in SaveContext ctx)
        {
            var j = new SaveJudgement { OnTarget = _onTarget, CrossTime = _crossTime };
            var tReact = k.ReactTime + ctx.ExtraDelay;
            j.ReactTime = tReact;
            var moving = gk.Velocity.LengthSq() > _p.StillSpeed * _p.StillSpeed;
            j.MovingPenalty = moving;
            var penalty = moving ? _p.StillMovingPenalty : 0f;
            var inDir = new Vec2(ctx.DirIn, 0f);
            var backScale = _p.ReachBackSpeed > Eps ? DMath.Max(1f, k.DiveSpeed / _p.ReachBackSpeed) : 1f;

            float best = float.MinValue;
            int bestI = -1;
            float bestReq = 0f, bestReach = 0f;
            var n = _count;
            // 가지치기 (AC-17): margin ≤ reachMax − (수평 거리 − 몸 반폭)이므로 이 상한이 지금 최댓값보다 작은 샘플은 계산하지 않는다 (결과 동일)
            var reachMax = DMath.Max(_p.ReachR0, k.MaxReach) - penalty + _p.ReachBodyHalf;
            // 골 쪽(GK 근처)부터 거꾸로 돌아 가지치기가 일찍 걸리게 한다. 같은 마진이면 앞(이른) 샘플을 고른다(>=) — 정방향 결과와 같다
            for (int i = n - 1; i >= 0; i--)
            {
                var b = _path[i];
                var rel = new Vec2(b.X, b.Y) - gk.Position;
                var depthComp = Vec2.Dot(rel, inDir);
                var lat = rel.Y; // 골라인과 평행한 성분 (골문은 x축에 수직)
                var dz = depthComp < 0f ? depthComp * backScale : depthComp;
                var d2 = lat * lat + dz * dz;
                if (bestI >= 0)
                {
                    var bound = reachMax - best;
                    if (bound < 0f || d2 > bound * bound) continue; // 같으면 계산 (동률은 이른 샘플)
                }
                var t = (i + 1) * _dt;
                var horiz = DMath.Sqrt(d2) - _p.ReachBodyHalf;
                if (horiz < 0f) horiz = 0f;
                var vex = b.Z - k.StandReachZ;
                if (vex < 0f) vex = 0f;
                var req = DMath.Sqrt(horiz * horiz + vex * vex);
                var reach = _pos.ReachAfterReact(t - tReact, b.Z, in k) - penalty;
                var m = reach - req;
                if (m >= best) { best = m; bestI = i; bestReq = req; bestReach = reach; }
            }
            if (bestI < 0)
            {
                j.ContactIndex = -1;
                j.MaxMargin = float.MinValue;
                return j;
            }
            j.MaxMargin = best;
            j.ContactIndex = bestI;
            j.ContactTime = (bestI + 1) * _dt;
            j.ContactPoint = _path[bestI];
            j.ContactVelocity = _vel[bestI];
            j.RequiredReach = bestReq;
            j.Reach = bestReach;
            j.PReach = j.OnTarget ? KeeperPositioner.Sigmoid(best / _p.ReachSReach) : 0f;

            var lateral = j.ContactPoint.Y - gk.Position.Y;
            var horizontal = (new Vec2(j.ContactPoint.X, j.ContactPoint.Y) - gk.Position).Length();
            // 몸 반폭 + 여유를 넘으면 접촉점으로 몸을 날린다 (옆이든 앞이든 — 앞 공을 제자리에서 잡으면 공이 손으로 순간이동)
            j.Dives = horizontal > _p.ReachBodyHalf + _p.DiveMinMove || j.ContactPoint.Z > k.StandReachZ - 0.1f;
            j.Direction = DMath.Abs(lateral) <= _p.ReachBodyHalf ? 0 : (lateral > 0f ? 1 : -1);
            j.Height = j.ContactPoint.Z < _p.DiveLowZ ? DiveHeight.Low : (j.ContactPoint.Z < _p.DiveHighZ ? DiveHeight.Mid : DiveHeight.High);

            // 2단계 처리 확률 (FR-10)
            var speed = j.ContactVelocity.Length();
            var ratio = bestReach > Eps ? bestReq / bestReach : 1f;
            var extreme = j.ContactPoint.Z < _p.SaveExtremeLowZ || j.ContactPoint.Z > _p.SaveExtremeHighZ ? 1f : 0f;
            var lowBounce = 0f;
            if (j.ContactPoint.Z < _p.DiveLowZ)
                for (int i = 0; i < _bounceCount; i++)
                {
                    var dtb = j.ContactTime - _bounce[i];
                    if (dtb >= 0f && dtb <= _p.SaveBounceWindow) { lowBounce = 1f; break; }
                }
            var h = _p.SaveAlpha * speed + _p.SaveBeta * ratio + _p.SaveGamma * extreme + _p.SaveDelta * lowBounce;
            j.Difficulty = h;
            j.Punch = j.ContactPoint.Z >= _p.SavePunchZ && (ctx.Crowd >= (int)_p.SaveCrowdCount || speed >= _p.SavePunchSpeed);
            j.PCatch = j.Punch ? 0f : KeeperPositioner.Sigmoid(k.HandlingA - h);
            j.PParrySafe = KeeperPositioner.Sigmoid(_p.SaveB * k.HandlingN + _p.SaveC * k.PositioningN - h);
            return j;
        }

        /// <summary>
        /// 결과 추첨 (FR-10·12): 난수 3개 고정 소비 — u1 결과 구간, u2 리바운드 방향, u3 리바운드 거리.
        /// u1 &lt; p_reach·p_catch → 잡기, &lt; p_reach·(p_catch + (1 − p_catch)·p_safe) → 안전한 쳐내기, &lt; p_reach → 위험한 쳐내기, 아니면 실패.
        /// 펀칭 조건이면 u1 &lt; p_reach → 펀칭.
        /// </summary>
        public SaveOutcome Resolve(in SaveJudgement j, in KeeperProfile k, in SaveContext ctx, SeedRng rng)
        {
            var u1 = rng.NextFloat();
            var u2 = rng.NextFloat();
            var u3 = rng.NextFloat();
            var o = new SaveOutcome();
            if (!j.OnTarget || j.ContactIndex < 0) { o.Kind = SaveOutcomeKind.None; return o; }
            var pr = j.PReach;
            if (j.Punch)
            {
                o.Kind = u1 < pr ? SaveOutcomeKind.Punch : SaveOutcomeKind.Miss;
            }
            else
            {
                var pc = pr * j.PCatch;
                var ps = pr * (j.PCatch + (1f - j.PCatch) * j.PParrySafe);
                o.Kind = u1 < pc ? SaveOutcomeKind.Catch : u1 < ps ? SaveOutcomeKind.ParrySafe : u1 < pr ? SaveOutcomeKind.ParryDanger : SaveOutcomeKind.Miss;
            }
            var goal = ctx.Goal;
            var dirIn = ctx.DirIn;
            var c = j.ContactPoint;
            var half = BallParams.GoalWidth * 0.5f;
            var side = c.Y > goal.Y ? 1f : (c.Y < goal.Y ? -1f : (u2 < 0.5f ? -1f : 1f));
            var speedIn = j.ContactVelocity.Length();
            switch (o.Kind)
            {
                case SaveOutcomeKind.ParrySafe:
                {
                    // 측면 골문 밖(포스트 바깥) 또는 높은 공은 크로스바 위로
                    var tip = c.Z > _p.SaveExtremeHighZ;
                    var target = new Vec2(goal.X - dirIn * 1.0f, goal.Y + side * (half + _p.SaveParryWide * (0.5f + u2)));
                    if (!tip) target = new Vec2(goal.X + dirIn * (0.5f + 2f * u3), goal.Y + side * (half + _p.SaveParryWide * (0.5f + u2)));
                    var dir = (target - new Vec2(c.X, c.Y)).Normalized();
                    if (dir == Vec2.Zero) dir = new Vec2(0f, side);
                    var sp = DMath.Max(_p.SaveDangerSpeedMin, speedIn * _p.SaveSafeSpeed);
                    o.ReboundVelocity = new Vec3(dir.X * sp, dir.Y * sp, tip ? 4f : 1.5f);
                    break;
                }
                case SaveOutcomeKind.ParryDanger:
                {
                    var contactDepth = (c.X - goal.X) * dirIn;
                    // 평균 6 − 3n 둘레 대칭 균등 — 3~8 m 경계 안에서 반폭을 줄여 평균을 지킨다 (AC-06)
                    var mean = Interp.Clamp(k.SpillMean, _p.SaveReboundMin, _p.SaveReboundMax);
                    var w = DMath.Min(_p.SaveReboundSpread, DMath.Min(mean - _p.SaveReboundMin, _p.SaveReboundMax - mean));
                    var d = mean + (2f * u3 - 1f) * w;
                    o.ReboundDepth = d;
                    var depth = DMath.Max(d, contactDepth + 1f);
                    var y = c.Y + (2f * u2 - 1f) * 2f;
                    o.ReboundTarget = new Vec2(goal.X + dirIn * depth, y);
                    var delta = o.ReboundTarget - new Vec2(c.X, c.Y);
                    var dist = delta.Length();
                    var dir = dist > Eps ? delta / dist : new Vec2(dirIn, 0f);
                    var inv = BallInverse.GroundSpeedFor(_bpForRebound, DMath.Max(dist, 0.5f));
                    var sp = inv.Ok ? inv.Speed : _p.SaveDangerSpeedMin;
                    if (sp < _p.SaveDangerSpeedMin) sp = _p.SaveDangerSpeedMin;
                    o.ReboundVelocity = new Vec3(dir.X * sp, dir.Y * sp, 0f);
                    break;
                }
                case SaveOutcomeKind.Punch:
                {
                    var target = new Vec2(goal.X + dirIn * _p.SavePunchDist * (0.7f + 0.6f * u3), c.Y + side * _p.SavePunchDist * (0.2f + 0.4f * u2));
                    var dir = (target - new Vec2(c.X, c.Y)).Normalized();
                    if (dir == Vec2.Zero) dir = new Vec2(dirIn, 0f);
                    o.ReboundVelocity = new Vec3(dir.X * 12f, dir.Y * 12f, 5f);
                    break;
                }
                case SaveOutcomeKind.Miss:
                    o.Fingertip = j.Dives && j.MaxMargin >= -_p.SaveFingertipBand;
                    break;
            }
            return o;
        }

        BallParams _bpForRebound;

        /// <summary>위험한 쳐내기 속도 역산에 쓸 공 파라미터 (경기 설정의 공). 판정 전에 한 번 넣는다.</summary>
        public void Bind(BallParams bp) => _bpForRebound = bp;

        /// <summary>
        /// FR-14 슛 평가 함수 (RNG 없음): 출발점 origin에서 목표 셀 cell 중심으로 속도 speed의 슛을 가정해 P_beatGK를 돌려준다.
        /// 발사 속도는 중력만 고려한 근사 조준(무회전)이고, 판정은 실제 004 경로로 한다(같은 초기 상태면 실제 판정과 같은 값).
        /// </summary>
        public float BeatProbability(Vec3 origin, int cell, float speed, in KeeperState gk, in KeeperProfile k, in SaveContext ctx, BallParams bp)
        {
            var state = AimedShot(origin, cell, speed, ctx.Goal, bp);
            if (!Predict(in state, bp, ctx.Goal, ctx.DirIn)) return 0f;
            var j = Evaluate(in gk, in k, in ctx);
            return j.BeatProbability;
        }

        /// <summary>셀 중심을 겨냥한 슛 초기 상태 (근사 포물선, 무회전).</summary>
        public BallState AimedShot(Vec3 origin, int cell, float speed, Vec2 goal, BallParams bp)
        {
            var grid = _pos.Grid;
            var target = new Vec3(goal.X, goal.Y + grid.OffsetY(cell), grid.Height(cell));
            var dx = target.X - origin.X;
            var dy = target.Y - origin.Y;
            var horiz = DMath.Sqrt(dx * dx + dy * dy);
            if (horiz < Eps) horiz = Eps;
            var t = horiz / speed;
            var vz = (target.Z - origin.Z + 0.5f * bp.Gravity * t * t) / t;
            var vh = speed;
            return BallState.InAir(origin, new Vec3(dx / horiz * vh, dy / horiz * vh, vz), Vec3.Zero);
        }
    }
}
