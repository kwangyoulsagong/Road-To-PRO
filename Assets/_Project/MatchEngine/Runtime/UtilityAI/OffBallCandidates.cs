using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>오프볼 후보 하나 (FR-03). 점수 항은 <see cref="OffBallScorer"/>가 채운다.</summary>
    public struct OffBallCandidate
    {
        public Consideration Kind;
        public Vec2 Point;
        /// <summary>추격 대상 슬롯 (압박 = 공 소유자, 마킹 = 마크 대상, 없으면 −1).</summary>
        public int Target;
        /// <summary>기본 이득 항 (결합 배수 전).</summary>
        public float Gain;
        /// <summary>손실 항.</summary>
        public float Loss;
        // ── 점수 분해 (FR-60) ──
        public float Mult;
        public float Tactic;
        public float Utility;
        public float Match;
        public float Noise;
    }

    /// <summary>
    /// 오프볼 후보 생성 (specs/010 FR-03·04·16·18·19·19a·19b·85·96). 국면(공 소유 팀 기준)에 맞는 후보만 만들고, 기본 이득·손실과 목표점을 정한다.
    /// 상대·동료 위치는 지각 지연만큼 과거 값(FR-24). 피치 컨트롤 질의는 침투 목표점에만(FR-04). 할당 0.
    /// </summary>
    public sealed class OffBallCandidates
    {
        public const int Capacity = 12;

        readonly DecisionParams _p;
        readonly ISpaceEvaluator _space;
        readonly IStyleMultiplierSource _style;
        readonly OffBallCandidate[] _buf = new OffBallCandidate[Capacity];
        int _n;
        int _protected = -1;

        public OffBallCandidates(DecisionParams p, ISpaceEvaluator space, IStyleMultiplierSource style)
        {
            _p = p;
            _space = space;
            _style = style;
        }

        public int Count => _n;
        /// <summary>의도 후보 인덱스 (없으면 −1).</summary>
        public int IntentIndex => _protected;
        public ref OffBallCandidate this[int i] => ref _buf[i];

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>이탈 상한 (FR-85): 역할 자유도 × 규율.</summary>
        public float RoamRadius(DecisionWorld w, int s)
        {
            var m = w.Mind[s];
            var discipline = (m.Professionalism + (100f - m.Temperament)) / 200f;
            var f = _style.Freedom(s);
            return (_p.RoamMin + (_p.RoamMax - _p.RoamMin) * f) * (_p.RoamDisciplineBase - _p.RoamDisciplineCut * discipline);
        }

        /// <summary>선수 s의 후보를 만든다. intentActive면 의도 종류 후보의 목표점을 의도 지점으로 둔다(없으면 추가, FR-91).</summary>
        public void Generate(DecisionWorld w, int s, float delay, in IntentPacket intent, bool intentActive)
        {
            _n = 0;
            _protected = -1;
            var t = DecisionWorld.TeamOf(s);
            var dir = w.AttackDir[t];
            var me = w.Pos[s];
            var anchor = w.Anchor[s];
            var owner = w.Owner;
            var poss = w.PossTeam;
            var attacking = poss == t || (poss < 0 && w.Phase[t] <= Tactics.TeamPhase.AttTransition);

            Add(Consideration.HoldShape, anchor, -1, _p.WHold * (_p.HoldBase - _p.HoldFreedomCut * _style.Freedom(s)), 0f);

            if (owner < 0) AddLooseBall(w, s);

            if (attacking) GenerateAttack(w, s, delay, t, dir, me, anchor, owner);
            else GenerateDefence(w, s, delay, t, dir, me, anchor, owner);

            if (intentActive) ApplyIntent(w, s, in intent);

            ShapeTerms(w, s, anchor, intentActive);
            Prune();
        }

        void GenerateAttack(DecisionWorld w, int s, float delay, int t, int dir, Vec2 me, Vec2 anchor, int owner)
        {
            var W = w.PitchW;
            var hasMate = owner >= 0 && owner != s && DecisionWorld.TeamOf(owner) == t;
            var ownerPos = hasMate ? w.Perceived(owner, delay) : w.Ball;

            // 지원 (FR-19): 소유자 주변 8~15 m, 패스 라인이 열린 지점
            if (hasMate && (me - ownerPos).LengthSq() < (_p.SupportMax * _p.SupportReachMul) * (_p.SupportMax * _p.SupportReachMul))
            {
                Vec2 best = me;
                float bestV = -1f;
                var pts = (int)_p.SpacePoints;
                for (int k = 0; k < pts; k++)
                {
                    var a = (-0.5f + (k + 0.5f) / pts) * DMath.Pi * _p.SupportArc; // 진행 방향 기준 ±arc·90°
                    var r = (k & 1) == 0 ? _p.SupportMin : _p.SupportMax;
                    var d = new Vec2(dir * DMath.Cos(a), DMath.Sin(a)) * r;
                    var q = w.ClampToPitch(ownerPos + d, 2f);
                    var v = _space.Openness(w, s, q, delay) * LaneOpen(w, s, ownerPos, q, delay) - _p.SupportDistCost * (q - me).Length();
                    if (v > bestV) { bestV = v; best = q; }
                }
                Add(Consideration.Support, best, -1, _p.WSupport * Clamp01(bestV + _p.SupportFloor), 0f);
            }

            // 침투 (FR-18·19a): 오프사이드 라인 뒤
            var oppLine = w.OffsideLineX[1 - t];
            {
                var x = oppLine + dir * _p.RunDepth;
                x = DMath.Min(DMath.Max(x, _p.RunEdge), w.PitchL - _p.RunEdge);
                var goalY = W * 0.5f;
                var target = new Vec2(x, me.Y + (goalY - me.Y) * _p.RunGoalPull);
                var ahead = dir * (me.X - (owner >= 0 ? w.Pos[owner].X : w.Ball.X));
                if ((hasMate || w.Phase[t] == Tactics.TeamPhase.AttTransition) && ahead >= -_p.RunBehindLimit) // 공보다 너무 뒤면 후보·질의 없음 (FR-04 질의 예산)
                {
                    var control = _space.QueryCount < (int)_p.SpaceQueryMax ? _space.Control(w, s, target) : _space.Openness(w, s, target, delay); // 틱당 질의 상한 (FR-04)
                    var gain = _p.WRun * (_p.RunControlFloor + (1f - _p.RunControlFloor) * control) * Floor(_space.Threat(w, t, target));
                    var m = w.Mind[s];
                    var skill = (DecisionAttributes.N(m.OffBall) + DecisionAttributes.N(w.Kin[s].Anticipation)) * 0.5f;
                    // 출발 시점 (FR-19a·81): 소유자가 공을 잡은 뒤 지연 = 최대 × (1 − 기술) × (라인 브레이커면 감소)
                    var delayS = _p.RunDelayMax * (1f - skill) * (_style.HabitLevel(s, Habit.LineBreaker) > 0 ? 1f - _p.RunBreakerTiming : 1f);
                    var held = hasMate ? (w.Tick - w.OwnerSinceTick) * w.Dt : 1f;
                    if (held < delayS) gain *= _p.RunEarlyDamp;
                    // 오프사이드 판단 (FR-19a): 라인 판단 오차만큼 더 나가 있어도 괜찮다고 본다
                    var judged = oppLine + dir * _p.RunOffsideErr * (1f - skill);
                    var loss = dir * (me.X - judged) > 0f ? _p.LossOffside : 0f;
                    Add(Consideration.RunInBehind, target, -1, gain, loss);
                }
            }

            if (hasMate)
            {
                // 내려와 받기: 공보다 앞에 있는 선수
                var ahead = dir * (me.X - ownerPos.X);
                if (ahead > _p.DropAhead)
                {
                    var q = w.ClampToPitch(new Vec2(me.X - dir * _p.DropDepth, me.Y + (ownerPos.Y - me.Y) * _p.DropYPull), 2f);
                    Add(Consideration.DropToReceive, q, -1, _p.WDrop * Floor(_space.Openness(w, s, q, delay)), 0f);
                }
                // 오버래핑·언더래핑: 소유자 뒤 25 m 안, 같은 측면
                var behind = dir * (ownerPos.X - me.X);
                if (behind > 0f && behind < _p.OverlapBehind && DMath.Abs(ownerPos.Y - me.Y) < _p.OverlapLateral)
                {
                    var wide = ownerPos.Y >= W * 0.5f ? 1f : -1f;
                    var o = w.ClampToPitch(new Vec2(ownerPos.X + dir * _p.OverlapAhead, ownerPos.Y + wide * _p.OverlapWide), 2f);
                    var u = w.ClampToPitch(new Vec2(ownerPos.X + dir * _p.OverlapAhead, ownerPos.Y - wide * _p.UnderlapInside), 2f);
                    Add(Consideration.Overlap, o, -1, _p.WOverlap * Floor(_space.Openness(w, s, o, delay)), 0f);
                    Add(Consideration.Underlap, u, -1, _p.WOverlap * Floor(_space.Openness(w, s, u, delay)), 0f);
                }
            }

            // 폭 유지 / 안쪽 (FR-82): 측면 앵커만
            var side = anchor.Y - W * 0.5f;
            if (DMath.Abs(side) > _p.WidthMinSide)
            {
                var sg = side > 0f ? 1f : -1f;
                Add(Consideration.StayWide, new Vec2(anchor.X, W * 0.5f + sg * (W * 0.5f - _p.WidthTouch)), -1, _p.WWidth, 0f);
                Add(Consideration.Invert, new Vec2(anchor.X, W * 0.5f + sg * _p.InvertY), -1, _p.WWidth, 0f);
            }

            // 박스 침투: 공이 마지막 3분의 1
            var goal = w.OppGoal(t);
            if (DMath.Abs(goal.X - w.Ball.X) < _p.BoxBallDist)
            {
                var q = new Vec2(goal.X - dir * _p.BoxDepth, W * 0.5f + (me.Y >= W * 0.5f ? _p.BoxLateral : -_p.BoxLateral));
                Add(Consideration.BoxCrash, q, -1, _p.WBox * Floor(_space.Openness(w, s, q, delay)), 0f);
            }
        }

        void GenerateDefence(DecisionWorld w, int s, float delay, int t, int dir, Vec2 me, Vec2 anchor, int owner)
        {
            var ownGoal = w.OwnGoal(t);
            var oppOwns = owner >= 0 && DecisionWorld.TeamOf(owner) != t;
            var ballP = oppOwns ? w.Perceived(owner, delay) : w.Ball;
            var press = PressMult(w, t);
            var order = w.PresserOrder(s);
            var gegen = w.GegenActive[t] && (me - w.Ball).LengthSq() <= _p.GpRadius * _p.GpRadius;

            // 압박 (FR-03·100·101): 009 지정 1순위, 또는 게겐프레싱 창 안 가까운 선수
            if (order == 0 || (gegen && order < 0))
            {
                var g = _p.WPress * press * (order == 0 ? 1f : _p.PressNonDesignated) * (gegen ? 1f + _p.GpBoost : 1f);
                Add(Consideration.Press, ballP, oppOwns ? owner : -1, g, 0f);
            }

            // 커버 섀도: 2·3순위 압박 또는 공 20 m 안
            var nearest = NearestOpponent(w, s, delay, owner);
            if (oppOwns && nearest >= 0 && (order >= 1 || (me - ballP).LengthSq() < _p.CoverShadowRadius * _p.CoverShadowRadius))
            {
                var recv = w.Perceived(nearest, delay);
                var q = ballP + (recv - ballP) * _p.CoverShadowLerp;
                Add(Consideration.CoverShadow, q, -1, _p.WCoverShadow * press * (order >= 1 ? _p.CoverShadowDesignated : 1f) * (gegen ? 1f + _p.GpBoost * _p.CoverShadowGegen : 1f), 0f);
            }

            // 마킹 (FR-19b): 상대–골 선 위 1~2 m
            var mt = w.MarkTarget[s];
            if (mt >= 0 && w.Active[mt])
            {
                var op = w.Perceived(mt, delay);
                var q = op + (ownGoal - op).Normalized() * _p.MarkOffset;
                var danger = 1f - _p.MarkDangerCut * Clamp01((op - ownGoal).Length() / _p.MarkDangerDist);
                Add(Consideration.Mark, q, mt, _p.WMark * danger, 0f);
            }

            // 커버: 공과 골 사이 (FR-16)
            {
                var q = w.ClampToPitch(ballP + (ownGoal - ballP).Normalized() * _p.CoverDist, 2f);
                Add(Consideration.Cover, q, -1, _p.WCover * ((me - ballP).LengthSq() < _p.CoverNear * _p.CoverNear ? 1f : _p.CoverFar), 0f);
            }

            // 라인 유지: 앵커가 수비 라인 근처
            if (DMath.Abs(anchor.X - w.DefLineX[t]) < _p.LineHoldBand)
                Add(Consideration.LineHold, new Vec2(w.DefLineX[t], anchor.Y), -1, _p.WLine, 0f);

            // 길목 차단: 공 소유자와 가까운 상대 사이
            if (oppOwns && nearest >= 0 && (w.Perceived(nearest, delay) - me).LengthSq() < _p.CoverShadowRadius * _p.CoverShadowRadius)
                Add(Consideration.PassLaneBlock, (ballP + w.Perceived(nearest, delay)) * 0.5f, -1, _p.WLane, 0f);

            // 회복 달리기: 공보다 앞(골 반대쪽)에 있음
            var ahead = dir * (me.X - ballP.X);
            if (ahead > _p.RecoveryAhead)
                Add(Consideration.RecoveryRun, anchor, -1, _p.WRecovery * Clamp01(ahead / _p.RecoveryScale), 0f);
        }

        void AddLooseBall(DecisionWorld w, int s)
        {
            var q = w.Ball + w.BallVel * _p.LooseLookahead;
            var mine = Kinematics.TimeToIntercept.Estimate(in w.Tti[s], q.X, q.Y);
            var t = DecisionWorld.TeamOf(s);
            int rank = 0;
            for (int o = t * DecisionWorld.PerTeam + 1; o < (t + 1) * DecisionWorld.PerTeam; o++)
            {
                if (o == s || !w.Active[o]) continue;
                if (Kinematics.TimeToIntercept.Estimate(in w.Tti[o], q.X, q.Y) < mine) rank++;
            }
            var g = _p.WLoose / (1f + _p.LooseRankWeight * rank);
            var pt = w.PassTarget;
            if (pt == s) g = _p.WLoose * _p.PassReceiveBoost; // 나에게 온 패스: 순위와 무관하게 받으러 간다
            else if (pt >= 0 && DecisionWorld.TeamOf(pt) == t) g *= _p.PassMateDamp; // 동료에게 가는 패스는 덜 쫓는다
            Add(Consideration.LooseBall, q, -1, g, 0f);
        }

        int NearestOpponent(DecisionWorld w, int s, float delay, int exclude)
        {
            var opp = 1 - DecisionWorld.TeamOf(s);
            int best = -1;
            float bd = float.MaxValue;
            for (int o = opp * DecisionWorld.PerTeam + 1; o < (opp + 1) * DecisionWorld.PerTeam; o++)
            {
                if (o == exclude || !w.Active[o]) continue;
                var d = (w.Perceived(o, delay) - w.Pos[s]).LengthSq();
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        float LaneOpen(DecisionWorld w, int s, Vec2 from, Vec2 to, float delay)
        {
            var opp = 1 - DecisionWorld.TeamOf(s);
            var seg = to - from;
            var len2 = seg.LengthSq();
            float best = float.MaxValue;
            for (int o = opp * DecisionWorld.PerTeam; o < (opp + 1) * DecisionWorld.PerTeam; o++)
            {
                if (!w.Active[o]) continue;
                var p = w.Perceived(o, delay);
                var u = len2 > 1e-6f ? Vec2.Dot(p - from, seg) / len2 : 0f;
                u = u < 0f ? 0f : (u > 1f ? 1f : u);
                var d = (from + seg * u - p).LengthSq();
                if (d < best) best = d;
            }
            return best == float.MaxValue ? 1f : Clamp01(DMath.Sqrt(best) / _p.LaneClear);
        }

        float Floor(float x) => _p.OpenFloor + (1f - _p.OpenFloor) * x;

        float PressMult(DecisionWorld w, int t) => _p.PressBase + _p.PressSlope * w.PressIntensity[t];

        void ApplyIntent(DecisionWorld w, int s, in IntentPacket intent)
        {
            var q = w.ClampToPitch(intent.Point, 0.5f);
            for (int i = 0; i < _n; i++)
            {
                if (_buf[i].Kind != intent.Kind) continue;
                _buf[i].Point = q;
                if (intent.TargetSlot >= 0) _buf[i].Target = intent.TargetSlot;
                _protected = i;
                return;
            }
            if (_n >= Capacity) _n = Capacity - 1;
            _protected = _n;
            Add(intent.Kind, q, intent.TargetSlot, BaseWeight(intent.Kind) * Floor(_space.Openness(w, s, q, 0f)), 0f);
        }

        float BaseWeight(Consideration k)
        {
            switch (k)
            {
                case Consideration.HoldShape: return _p.WHold;
                case Consideration.Support: return _p.WSupport;
                case Consideration.RunInBehind: return _p.WRun;
                case Consideration.DropToReceive: return _p.WDrop;
                case Consideration.Overlap:
                case Consideration.Underlap: return _p.WOverlap;
                case Consideration.StayWide:
                case Consideration.Invert: return _p.WWidth;
                case Consideration.BoxCrash: return _p.WBox;
                case Consideration.LooseBall: return _p.WLoose;
                case Consideration.Press: return _p.WPress;
                case Consideration.CoverShadow: return _p.WCoverShadow;
                case Consideration.Mark: return _p.WMark;
                case Consideration.Cover: return _p.WCover;
                case Consideration.LineHold: return _p.WLine;
                case Consideration.PassLaneBlock: return _p.WLane;
                case Consideration.RecoveryRun: return _p.WRecovery;
                default: return _p.WSupport;
            }
        }

        /// <summary>형태 앵커 당김(FR-96)·이탈 상한(FR-85, 침투류 제외)·형태 이탈 손실.</summary>
        void ShapeTerms(DecisionWorld w, int s, Vec2 anchor, bool cast)
        {
            var roam = DMath.Max(RoamRadius(w, s), 0.5f);
            var pull = _p.AnchorPull * (cast ? _p.IntentAnchorScale : 1f);
            for (int i = 0; i < _n; i++)
            {
                ref var c = ref _buf[i];
                if (c.Kind == Consideration.HoldShape || Considerations.IsDefensiveDuty(c.Kind)) continue;
                c.Point += (anchor - c.Point) * pull;
                var off = c.Point - anchor;
                var d = off.Length();
                if (Considerations.IsRun(c.Kind)) { c.Loss += _p.LossShape * Clamp01(d / (_p.RunLossScale * roam)); continue; } // 침투류는 형태를 잠시 떠나는 행동
                if (!cast && d > roam) { c.Point = anchor + off * (roam / d); d = roam; }
                c.Loss += _p.LossShape * Clamp01(d / roam);
            }
        }

        /// <summary>상한 초과 시 기본 이득이 낮은 후보부터 뺀다(형태 유지·의도 후보는 남김, 순서 유지).</summary>
        void Prune()
        {
            var cap = (int)_p.CandMax;
            while (_n > cap)
            {
                int worst = -1;
                for (int i = 1; i < _n; i++)
                    if (i != _protected && (worst < 0 || _buf[i].Gain < _buf[worst].Gain)) worst = i;
                if (worst < 0) break; // 남은 후보가 형태 유지·의도뿐 (리뷰 L7)
                for (int i = worst; i < _n - 1; i++) _buf[i] = _buf[i + 1];
                if (_protected > worst) _protected--;
                _n--;
            }
        }

        void Add(Consideration k, Vec2 point, int target, float gain, float loss)
        {
            if (_n >= Capacity) return;
            _buf[_n++] = new OffBallCandidate { Kind = k, Point = point, Target = target, Gain = gain, Loss = loss };
        }
    }
}
