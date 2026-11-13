using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using BallParams = RoadToPro.MatchEngine.Ball.BallParams;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 온볼 후보 생성 (specs/072 FR-01·03·04·15·20). 고정 순서: 패스(시야 집합 거리순: 일반 → 스루 → 크로스) → 드리블·캐리(공격 방향부터 반시계 8방향)
    /// → 슈팅 → 홀드 → 걷어내기 → 넘어지기. 상한(24)을 넘으면 사전 점수 P·V 상위만 남기고 생성 순서를 유지한다. 0개면 홀드·걷어내기 강제 폴백.
    /// 패스 성공 확률은 008 <see cref="PassModel"/>(수신 지점 컨트롤 포함 로지스틱), 나머지는 <see cref="IOnBallEvaluator"/>. RNG 없음, 할당 0.
    /// </summary>
    public sealed class OnBallCandidates
    {
        /// <summary>생성 버퍼 (자르기 전): 패스 3 × 시야 9 + 드리블 8 + 의도 캐리 1 + 슈팅·홀드·걷어내기·넘어지기·여유.</summary>
        public const int RawCapacity = VisionSet.Capacity * 3 + 8 + 6;

        readonly DecisionParams _p;
        readonly IOnBallEvaluator _eval;
        readonly IChemistrySource _chem;
        readonly OnBallCandidate[] _raw = new OnBallCandidate[RawCapacity];
        readonly OnBallCandidate[] _c = new OnBallCandidate[UtilityChooser.MaxCandidates];
        readonly bool[] _keep = new bool[RawCapacity];
        readonly VisionSet _vision = new VisionSet();
        int _rawCount;

        public OnBallCandidates(DecisionParams p, IOnBallEvaluator eval, IChemistrySource chem)
        {
            _p = p;
            _eval = eval;
            _chem = chem;
        }

        public int Count { get; private set; }
        /// <summary>자르기 전 후보 수 (텔레메트리·테스트).</summary>
        public int RawCount => _rawCount;
        public VisionSet Vision => _vision;
        public ref OnBallCandidate this[int i] => ref _c[i];
        /// <summary>강제 폴백이 일어났는가 (FR-04).</summary>
        public bool Fallback { get; private set; }
        /// <summary>마지막 생성의 볼 소유자 압박 (0~1, 008 Carrier).</summary>
        public float Pressure { get; private set; }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        int CapMax => _p.OnBallCandMax < 1f ? 1 : ((int)_p.OnBallCandMax > UtilityChooser.MaxCandidates ? UtilityChooser.MaxCandidates : (int)_p.OnBallCandMax);

        ref OnBallCandidate Add(OnBallItem item, int target, Vec2 point)
        {
            ref var c = ref _raw[_rawCount++];
            c = default;
            c.Item = item;
            c.Target = target;
            c.Point = point;
            c.VisionIndex = -1;
            c.Defender = -1;
            if (OnBallItems.Primary(item, out var k)) c.Cons.Add(k);
            return ref c;
        }

        /// <summary>
        /// 후보 생성. aw = 008 평가 월드(같은 틱 위치), oneTouch = 받자마자(008 원터치 창 안), heldSeconds = 이번 소유 보유 시간.
        /// </summary>
        public void Generate(DecisionWorld w, ActionWorld aw, ActionParams ap, BallParams bp, int s, float pressure, bool oneTouch, float heldSeconds)
            => Generate(w, aw, ap, bp, s, pressure, oneTouch, heldSeconds, IntentPacket.None, false);

        public void Generate(DecisionWorld w, ActionWorld aw, ActionParams ap, BallParams bp, int s, float pressure, bool oneTouch, float heldSeconds,
            in IntentPacket intent, bool intentActive)
        {
            _rawCount = 0;
            Fallback = false;
            Pressure = pressure;
            var t = DecisionWorld.TeamOf(s);
            var opp = 1 - t;
            var dir = w.AttackDir[t];
            var me = w.Pos[s];
            var from = w.Ball;
            var xs = _p.OnBallXtScale;
            var threatFrom = aw.Threat(ap, t, from);
            var lossHere = xs * aw.Threat(ap, opp, me);
            var halfW = w.PitchW * 0.5f;

            _vision.Build(_p, w, s);

            // ── 패스 (FR-01·02·15) ──
            var wideZone = DMath.Abs(me.Y - halfW) >= halfW - _p.OnBallCrossWide
                           && (dir > 0 ? me.X >= w.PitchL * _p.OnBallCrossThird : me.X <= w.PitchL * (1f - _p.OnBallCrossThird));
            for (int i = 0; i < _vision.Count; i++)
            {
                var o = _vision.Slot(i);
                var seen = _vision.Seen(i);
                var dist = (seen - from).Length();
                if (dist < _p.OnBallPassMinDist) continue;
                var offside = _eval.OffsideFactor(w, s, o);
                var chem = _chem != null ? _chem.Chemistry(s, o) : 0f;
                var mateInBox = _eval.InOppBox(w, t, seen);

                var opt = PassModel.Best(ap, bp, aw, s, o, from, seen, pressure, PassModel.LoftMode.Any);
                if (opt.Valid)
                {
                    var lateral = DMath.Abs(seen.Y - from.Y);
                    var gain = (seen.X - from.X) * dir;
                    var item = lateral >= _p.OnBallSwitchLateral ? OnBallItem.Switch : (gain >= _p.OnBallProgGain ? OnBallItem.ProgPass : OnBallItem.ShortPass);
                    AddPass(item, o, i, in opt, offside, chem, mateInBox, oneTouch, aw, ap, t, opp, from, threatFrom);
                }

                // 스루볼 (리드 2/4/6 m, 008 FR-14): 앞으로 뛰는 러너만
                var vr = aw.Vel[o];
                if (vr.X * dir >= ap.LeadMinRunnerSpeed)
                {
                    var best = default(PassOption);
                    for (int li = 1; li < 4; li++)
                    {
                        if (!PassModel.ThroughAim(ap, bp, aw, 2, from, seen, vr, dir, PassModel.Lead(ap, li), out var aim, out _)) continue;
                        if (aim.X < 1f || aim.X > w.PitchL - 1f || aim.Y < 1f || aim.Y > w.PitchW - 1f) continue;
                        var th = PassModel.Best(ap, bp, aw, s, o, from, aim, pressure, PassModel.LoftMode.ThroughSpeed);
                        if (th.Valid && (!best.Valid || th.XPass > best.XPass)) best = th;
                    }
                    if (best.Valid) AddPass(OnBallItem.ThroughBall, o, i, in best, offside, chem, _eval.InOppBox(w, t, best.Aim), oneTouch, aw, ap, t, opp, from, threatFrom);
                }

                // 크로스 (측면 구역 → 박스 안 동료, 뜬 패스 — 플랜 대체 2)
                if (wideZone && mateInBox)
                {
                    var cr = PassModel.Best(ap, bp, aw, s, o, from, seen, pressure, PassModel.LoftMode.LoftedOnly);
                    if (cr.Valid)
                    {
                        ref var c = ref AddPass(OnBallItem.Cross, o, i, in cr, offside, chem, true, false, aw, ap, t, opp, from, threatFrom);
                        c.P = _eval.CrossSuccess(w, s, cr.XPass) * offside;
                        c.Pre = c.P * c.V;
                    }
                }
            }

            // ── 드리블·캐리 8방향 (FR-01) ──
            var dirs = (int)_p.OnBallDribbleDirs;
            if (dirs < 1) dirs = 1;
            var threatMe = aw.Threat(ap, t, me);
            for (int j = 0; j < dirs; j++)
            {
                var d = ActionMath.Rotate(new Vec2(dir, 0f), DMath.TwoPi * j / dirs);
                AddMove(w, aw, ap, s, me + d * _p.OnBallDribbleStep, d, pressure, threatMe, lossHere);
            }
            // 시나리오 단계 행동이 캐리·돌파면 의도 목표점 방향 후보 1개 (FR-14 — 단계 목표에 닿아야 013 단계가 끝난다)
            if (intentActive && (intent.Kind == Consideration.Dribble || intent.Kind == Consideration.CutIn))
            {
                var goalPt = w.ClampToPitch(intent.Point, 1f);
                var rel = goalPt - me;
                var len = rel.Length();
                if (len > 1f)
                {
                    var step = len < _p.OnBallDribbleStep ? len : _p.OnBallDribbleStep;
                    AddMove(w, aw, ap, s, me + rel * (step / len), rel / len, pressure, threatMe, lossHere);
                }
            }

            // ── 슈팅 ──
            var goal = w.OppGoal(t);
            var shotDist = (goal - from).Length();
            if (shotDist <= _p.OnBallShotRange)
            {
                ref var c = ref Add(OnBallItem.Shoot, -1, goal);
                c.P = 1f;
                c.V = _eval.ShotXg(w, s, from, pressure);
                if (shotDist > _p.OnBallLongShotDist) c.Cons.Add(Consideration.LongShot);
                c.LossXt = lossHere;
                c.Pre = c.V;
            }

            // ── 홀드 ──
            if (pressure < _p.OnBallHoldMaxPressure) AddHold(w, s, me, pressure, heldSeconds, lossHere);

            // ── 걷어내기 (자기 진영 압박) ──
            var ownGoalX = w.OwnGoal(t).X;
            var inOwnThird = DMath.Abs(me.X - ownGoalX) <= w.PitchL * _p.OnBallClearOwnThird;
            if (pressure >= _p.OnBallClearPressure && inOwnThird) AddClear(w, aw, ap, s, me, dir, opp);

            // ── 넘어지기 (FR-20): 접촉 직전 = 최근접 상대가 범위 안에서 다가오는 중 ──
            if (!DecisionWorld.IsKeeper(s))
            {
                int near = -1;
                float nearD2 = _p.OnBallDiveRange * _p.OnBallDiveRange;
                for (int o = opp * DecisionWorld.PerTeam; o < (opp + 1) * DecisionWorld.PerTeam; o++)
                {
                    if (!w.Active[o]) continue;
                    var d2 = (w.Pos[o] - me).LengthSq();
                    if (d2 <= nearD2) { nearD2 = d2; near = o; }
                }
                if (near >= 0)
                {
                    var rel = w.Pos[near] - me;
                    var rv = w.Vel[near] - w.Vel[s];
                    if (rel.X * rv.X + rel.Y * rv.Y < 0f)
                    {
                        ref var c = ref Add(OnBallItem.Dive, near, me);
                        c.P = _p.OnBallDiveAward;
                        c.V = _eval.FoulValue(w, s) * _p.OnBallDiveWeight;
                        c.LossXt = lossHere;
                        c.Pre = c.P * c.V;
                    }
                }
            }

            // ── 강제 폴백 (FR-04) ──
            if (_rawCount == 0) ForceFallback(w, aw, ap, s, pressure, heldSeconds);

            Prune();
        }

        /// <summary>점수 단계 뒤 포함 후보가 하나도 없을 때(금지 지시 등) 결정자가 부른다: 기존 후보를 버리고 홀드 또는 걷어내기 1개로.</summary>
        public void ForceFallback(DecisionWorld w, ActionWorld aw, ActionParams ap, int s, float pressure, float heldSeconds)
        {
            _rawCount = 0;
            Fallback = true;
            var t = DecisionWorld.TeamOf(s);
            var me = w.Pos[s];
            var lossHere = _p.OnBallXtScale * aw.Threat(ap, 1 - t, me);
            if (pressure < _p.OnBallClearPressure) AddHold(w, s, me, pressure, heldSeconds, lossHere);
            else AddClear(w, aw, ap, s, me, w.AttackDir[t], 1 - t);
            Prune();
        }

        /// <summary>드리블·캐리 후보 1개 (방향 d 단위 벡터): 경계 밖·동료 겹침이면 없음, 원뿔 안 최근접 상대가 있으면 돌파.</summary>
        void AddMove(DecisionWorld w, ActionWorld aw, ActionParams ap, int s, Vec2 target, Vec2 d, float pressure, float threatMe, float lossHere)
        {
            if (target.X < 1f || target.X > w.PitchL - 1f || target.Y < 1f || target.Y > w.PitchW - 1f) return;
            var t = DecisionWorld.TeamOf(s);
            var opp = 1 - t;
            var me = w.Pos[s];
            var clear2 = _p.OnBallDribbleMateClear * _p.OnBallDribbleMateClear;
            for (int o = t * DecisionWorld.PerTeam; o < (t + 1) * DecisionWorld.PerTeam; o++)
                if (o != s && w.Active[o] && (w.Pos[o] - target).LengthSq() < clear2) return;
            var cosCone = DMath.Cos(_p.OnBallDribbleCone * (DMath.Pi / 180f));
            var engage2 = _p.OnBallDribbleEngage * _p.OnBallDribbleEngage;
            int def = -1;
            float defD2 = float.MaxValue;
            for (int o = opp * DecisionWorld.PerTeam; o < (opp + 1) * DecisionWorld.PerTeam; o++)
            {
                if (!w.Active[o]) continue;
                var rel = w.Pos[o] - me;
                var d2 = rel.LengthSq();
                if (d2 > engage2 || d2 >= defD2) continue;
                var len = DMath.Sqrt(d2);
                if (len > 1e-4f && (rel.X * d.X + rel.Y * d.Y) < cosCone * len) continue;
                def = o;
                defD2 = d2;
            }
            ref var c = ref Add(def >= 0 ? OnBallItem.Dribble : OnBallItem.Carry, def, target);
            c.Defender = def;
            if (def >= 0)
            {
                c.P = _eval.DribbleSuccess(w, s, def, pressure);
                c.Cons.Add(Consideration.Trick);
            }
            else c.P = Clamp01(1f - _p.OnBallCarryLoss * pressure);
            var halfW = w.PitchW * 0.5f;
            var wide = DMath.Abs(me.Y - halfW) >= halfW - _p.OnBallCrossWide;
            if (wide && d.X * w.AttackDir[t] >= 0f && DMath.Abs(target.Y - halfW) < DMath.Abs(me.Y - halfW) - 1f)
            {
                c.CutIn = true;
                c.Cons.Add(Consideration.CutIn);
            }
            c.V = _p.OnBallXtScale * (aw.Threat(ap, t, target) - threatMe);
            c.LossXt = lossHere;
            c.Pre = c.P * c.V;
        }

        void AddHold(DecisionWorld w, int s, Vec2 me, float pressure, float heldSeconds, float lossHere)
        {
            var t = DecisionWorld.TeamOf(s);
            ref var c = ref Add(OnBallItem.Hold, -1, me);
            c.P = Clamp01(1f - _p.OnBallHoldLoss * pressure);
            var v = _p.OnBallHoldValue - _p.OnBallHoldDecay * (0.5f + w.Tempo[t]) * heldSeconds; // FR-11 Tempo
            if (w.GoalDiff[t] > 0 && w.MinutesLeft <= _p.OnBallTimeWasteMinutes) v += _p.OnBallTimeWasteHold * w.TimeWasting[t]; // FR-11 TimeWasting
            c.V = v;
            c.LossXt = lossHere;
            c.Pre = c.P * c.V;
        }

        void AddClear(DecisionWorld w, ActionWorld aw, ActionParams ap, int s, Vec2 me, int dir, int opp)
        {
            var x = me.X + dir * _p.OnBallClearDist;
            x = x < 1f ? 1f : (x > w.PitchL - 1f ? w.PitchL - 1f : x);
            var land = new Vec2(x, me.Y);
            ref var c = ref Add(OnBallItem.Clear, -1, land);
            c.P = Clamp01(1f - _p.OnBallClearLoss);
            c.V = 0f;
            c.LossXt = _p.OnBallXtScale * aw.Threat(ap, opp, land);
            c.Pre = 0f;
        }

        ref OnBallCandidate AddPass(OnBallItem item, int o, int vi, in PassOption opt, float offside, float chem, bool intoBox, bool oneTouch,
            ActionWorld aw, ActionParams ap, int t, int opp, Vec2 from, float threatFrom)
        {
            ref var c = ref Add(item, o, opt.Aim);
            c.Pass = opt;
            c.VisionIndex = vi;
            c.Chem = chem;
            if (oneTouch) c.Cons.Add(Consideration.OneTouchPass);
            if (intoBox) c.Cons.Add(Consideration.AssistPass);
            c.P = opt.XPass * offside;
            c.V = _p.OnBallXtScale * (aw.Threat(ap, t, opt.Aim) - threatFrom);
            var lossPoint = from + (opt.Aim - from) * _p.OnBallLossPoint;
            c.LossXt = _p.OnBallXtScale * aw.Threat(ap, opp, lossPoint);
            c.Pre = c.P * c.V;
            return ref c;
        }

        /// <summary>상한을 넘으면 사전 점수 상위만 (동점 = 생성 순서), 남은 후보는 생성 순서 유지.</summary>
        void Prune()
        {
            var n = _rawCount;
            var cap = CapMax;
            if (n <= cap)
            {
                for (int i = 0; i < n; i++) _c[i] = _raw[i];
                Count = n;
                return;
            }
            System.Array.Clear(_keep, 0, n);
            for (int k = 0; k < cap; k++)
            {
                int b = -1;
                for (int i = 0; i < n; i++)
                    if (!_keep[i] && (b < 0 || _raw[i].Pre > _raw[b].Pre)) b = i;
                _keep[b] = true;
            }
            var m = 0;
            for (int i = 0; i < n; i++) if (_keep[i]) _c[m++] = _raw[i];
            Count = m;
        }
    }
}
