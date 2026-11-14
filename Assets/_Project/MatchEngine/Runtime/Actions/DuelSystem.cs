using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Replay;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 결투 하위 시스템 (specs/070): 드리블 터치 사이클·이격 탈취(FR-01~03), 테이크온(FR-04~06), 선수별 예약 태클(FR-07·10·11·31),
    /// 몸싸움(FR-20), 접촉 발행(FR-13). 무엇을 할지는 010·072가 정하고 여기서는 결과만 판정한다. 액션 RNG(<see cref="ActionSystem.Rng"/>)만 쓰고
    /// 소비는 터치 4+1 · 테이크온 2 · 태클 2 · 몸싸움 1로 고정(FR-61). <see cref="Enabled"/>가 false(006 대체 파이프라인)면 아무것도 하지 않는다.
    /// 서브스텝 순서(PhysicsStage): 예약 킥 → <see cref="OnTackles"/> → 운동 적분 → <see cref="OnDribble"/> → 컨트롤 공 위치 → 공 적분 → … → 수신 → <see cref="OnShield"/>.
    /// </summary>
    public sealed class DuelSystem
    {
        const int N = ActionWorld.N;

        readonly ActionSystem _a;
        readonly BallParams _bp;
        ActionParams P => _a.P;
        SeedRng Rng => _a.Rng;

        /// <summary>010 판단 파이프라인(UtilityDecisionStage)이 켠다. 006 대체 파이프라인은 끈 채로 둬 해시가 그대로다 (경기 내내 고정 — 상태 아님).</summary>
        public bool Enabled;

        DribbleState _d = DribbleState.None;
        readonly PendingTackle[] _tk = new PendingTackle[N];
        readonly long[] _lastAttempt = new long[N];
        readonly long[] _lostAbs = new long[N];
        long _shieldNext;
        // 대기 중인 테이크온 (072가 돌파를 골랐지만 아직 3 m 밖): 공격수·수비수·만료 서브스텝
        int _toAtt = -1, _toDef = -1;
        long _toExpire;


        /// <summary>테스트·진단 누계 (상태 아님).</summary>
        public long Touches, StealChecks, Steals, TakeOns, TacklesResolved, TacklesWon, LateTackles, ShieldChecks, ShieldLosses, Contacts, GapReleases;
        /// <summary>마지막 터치의 표본 거리 (진단·테스트, 상태 아님).</summary>
        public float LastTouchDist;
        /// <summary>마지막 탈취 판정 확률·고른 상대 (진단·테스트, 상태 아님).</summary>
        public float LastStealProb;
        public int LastStealCandidate = -1;

        public DuelSystem(ActionSystem a, BallParams bp)
        {
            _a = a;
            _bp = bp;
            for (int i = 0; i < N; i++) { _lastAttempt[i] = long.MinValue / 4; _lostAbs[i] = long.MinValue / 4; }
        }

        public ref readonly DribbleState Dribble => ref _d;
        /// <summary>테스트 고정 상황 전용: 드리블 상태를 비운다 (같은 소유자로 상황을 다시 놓을 때).</summary>
        public void ResetDribbleForTest() => _d = DribbleState.None;
        public ref readonly PendingTackle TackleOf(int slot) => ref _tk[slot];
        long Ticks(float seconds) => (long)(seconds / _bp.Dt + 0.5f);

        /// <summary>slot이 공을 가졌고 터치 이격이 열려 있는가.</summary>
        public bool GapOpenFor(int slot) => Enabled && _d.Open && _d.Owner == slot;

        /// <summary>이격 중 소유자가 따라갈 지점: 지금 공 위치에서 터치 방향 0.5 m 앞.</summary>
        public Vec2 ChasePoint(MatchContext ctx) => ctx.Ball.Position.XY + _d.Dir * 0.5f;

        /// <summary>FR-01 볼 운반 최고속도.</summary>
        public float CarrySpeed(MatchContext ctx, int slot)
        {
            _a.Refresh(ctx);
            return DribbleModel.VmaxBall(P, ctx.Kinematics.Profile(slot).MaxSpeed, _a.World.Dribbling[slot]);
        }

        static bool Immobile(MatchContext ctx, int slot) => ctx.Kinematics.IsImmobile(slot);

        // ── 드리블 (FR-01~03) ──

        /// <summary>운동 적분 뒤: 소유자 변경 추적, 이격 닫힘·탈취·안전 해제, 새 터치.</summary>
        public void OnDribble(MatchContext ctx, long abs, int sub)
        {
            if (!Enabled) return;
            ref var ball = ref ctx.Ball;
            var owner = ball.Phase == BallPhase.Controlled ? ball.OwnerId : -1;
            if (owner != _d.Owner)
            {
                if (_d.Owner >= 0 && _d.Owner < N) _lostAbs[_d.Owner] = abs;
                _d = DribbleState.None;
                _d.Owner = owner;
                _d.Reglue = owner >= 0;
            }
            if (owner < 0 || owner >= N || !ctx.Flow.InPlay) { if (_d.Open) _d.Open = false; return; }
            ref readonly var o = ref ctx.SlotState(owner);
            if (_d.Open)
            {
                var t = (abs - _d.Start + 1) * _bp.Dt;
                var bp = RollPos(t, out var bspeed);
                if (!_d.Contested)
                {
                    // FR-03: 이격이 열린 동안 상대가 공 컨트롤 반경에 들어오면 그 순간 소유자·상대의 공 도달 시간(005 TTI)을 비교해 탈취 (터치당 1회)
                    var ch = NearestOpponentAt(ctx, owner, bp);
                    if (ch >= 0)
                    {
                        _d.Contested = true;
                        _a.Refresh(ctx);
                        var w = _a.World;
                        // 상대는 이미 컨트롤 반경 안 = 도달 시간은 반응 시간만, 소유자는 005 TTI(반응 + 이동). 여유(stealMargin) = 소유자가 다시 발을 대는 데 걸리는 시간
                        var tOwn = TimeToIntercept.Estimate(in w.Tti[owner], bp.X, bp.Y);
                        var prob = DribbleModel.StealProb(P, tOwn, w.Tti[ch].Reaction);
                        LastStealProb = prob;
                        LastStealCandidate = ch;
                        StealChecks++;
                        if (_d.StealU < prob)
                        {
                            Steal(ctx, abs, sub, owner, ch, bp);
                            return;
                        }
                    }
                }
                var gap2 = (o.Position - bp).LengthSq();
                if (gap2 > P.KickReach * P.KickReach) _d.Far = true;
                // 닫힘: 이격이 도달 거리를 넘었다가 돌아왔거나, 한 번도 넘지 않은 짧은 터치면 공이 소유자보다 느려진 뒤
                if (t >= P.DribCloseMin && gap2 <= P.KickReach * P.KickReach && (_d.Far || bspeed <= o.Velocity.Length()))
                {
                    _d.Open = false;
                    _d.Reglue = true;
                    BallCommands.SetControlledPosition(ref ball, new Vec3(bp.X, bp.Y, _bp.Radius));
                }
                else if (t > P.DribMaxGapTime)
                {
                    // 안전장치: 소유자가 공을 끝내 못 따라가면 느슨한 공으로 놓는다 (마지막 터치 = 소유자)
                    BallCommands.SetControlledPosition(ref ball, new Vec3(bp.X, bp.Y, _bp.Radius));
                    BallCommands.Kick(ref ball, new Vec3(_d.Dir.X * bspeed, _d.Dir.Y * bspeed, 0f), Vec3.Zero, _bp, ctx.BallEvents);
                    _d.Open = false;
                    GapReleases++;
                    return;
                }
                else return;
            }
            // 닫힘: 새 터치 조건
            if (Squad.IsKeeperSlot(owner) || ctx.Keepers.IsBusySlot(owner)) return;
            var speed = o.Velocity.Length();
            if (speed < P.DribMinSpeed || _d.Reglue) return;
            ref readonly var pk = ref _a.Pending;
            if (pk.Active && pk.Action.KickerSlot == owner) return;
            if ((ball.Position.XY - o.Position).LengthSq() > P.KickReach * P.KickReach) return;
            StartTouch(ctx, abs, owner, in o, speed);
        }

        void StartTouch(MatchContext ctx, long abs, int owner, in PlayerMotionState o, float speed)
        {
            _a.Refresh(ctx);
            var w = _a.World;
            var pressure = _a.PressureOn(ctx, owner);
            var mean = DribbleModel.TouchDist(P, speed, w.Dribbling[owner], w.FirstTouch[owner]);
            var sigma = DribbleModel.SigmaDeg(P, w.Dribbling[owner], pressure);
            var touch = DribbleModel.SampleTouch(P, mean, sigma, o.Velocity, Rng); // RNG 4
            ref readonly var prof = ref ctx.Kinematics.Profile(owner);
            var v0 = SolveTouchSpeed(speed, prof.MaxSpeed, prof.MaxAccel, touch.Distance);
            var table = _bp.RollTable;
            _d.Open = true;
            _d.Start = abs;
            _d.Origin = ctx.Ball.Position.XY;
            _d.Dir = touch.Direction;
            _d.V0 = v0;
            _d.X0 = table.DistanceAtSpeed(v0);
            _d.S0 = table.StepAtDistance(_d.X0);
            _d.Dist = touch.Distance;
            Touches++;
            LastTouchDist = touch.Distance;

            _d.StealU = Rng.NextFloat(); // FR-03 탈취 판정 난수 (RNG 1, 판정이 없어도 소비)
            _d.Contested = false;
            _d.Far = false;
        }

        /// <summary>
        /// 터치 초속: 공은 004 굴림 표로, 소유자는 지금 속도에서 최고속도까지 005 모노-지수로 가속(τ = vmax/a0)하며 공을 쫓는다고 볼 때 최대 이격(발 앞 0.45 m 포함)이
        /// 터치 거리 d가 되는 초속을 이분법 10회로 푼다 (0.05 s 격자, 6 s 한도). RNG 없음, 할당 없음.
        /// </summary>
        float SolveTouchSpeed(float speed, float vmax, float accel, float d)
        {
            var table = _bp.RollTable;
            float lo = speed, hi = _bp.LimitSpeed - 0.1f;
            if (hi <= lo) return hi;
            const float dt = 0.05f;
            for (int it = 0; it < 10; it++)
            {
                var v0 = 0.5f * (lo + hi);
                var x0 = table.DistanceAtSpeed(v0);
                var s0 = table.StepAtDistance(x0);
                float own = 0f, u = speed, peak = 0f;
                for (int k = 1; k <= 120; k++)
                {
                    var t = k * dt;
                    u += (vmax - u) * accel / DMath.Max(vmax, 0.1f) * dt; // 005 모노-지수 가속 (τ = vmax/a0)
                    own += u * dt;
                    var gap = table.DistanceAtStep(s0 + t / _bp.Dt) - x0 + PhysicsCarryOffset - own;
                    if (gap > peak) peak = gap;
                    else if (gap < peak - 0.5f) break; // 정점 지남
                }
                if (peak < d) lo = v0; else hi = v0;
            }
            return 0.5f * (lo + hi);
        }

        const float PhysicsCarryOffset = Match.Stages.PhysicsStage.BallCarryOffset;

        /// <summary>공 컨트롤 반경 안의 가장 가까운 상대 (다운·다이빙 제외, 동률 낮은 슬롯). 없으면 −1.</summary>
        int NearestOpponentAt(MatchContext ctx, int owner, Vec2 ball)
        {
            var opp = 1 - Squad.TeamOfSlot(owner);
            var lim = P.CtrlRadius * P.CtrlRadius;
            var best = float.MaxValue;
            var who = -1;
            for (int s = opp * Squad.SlotsPerTeam; s < (opp + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!ctx.IsSlotControllable(s) || Immobile(ctx, s) || ctx.Keepers.IsBusySlot(s)) continue;
                var d2 = (ctx.SlotState(s).Position - ball).LengthSq();
                if (d2 <= lim && d2 < best) { best = d2; who = s; } // 동률 낮은 슬롯 (결정론 리뷰 L2)
            }
            return who;
        }

        /// <summary>이격 중 공 위치 (터치 원점 + 방향 × 004 굴림 거리).</summary>
        Vec2 RollPos(float t, out float speed)
        {
            var table = _bp.RollTable;
            var x = table.DistanceAtStep(_d.S0 + t / _bp.Dt);
            speed = table.SpeedAtDistance(x);
            if (x >= table.TotalDistance) speed = 0f;
            return _d.Origin + _d.Dir * (x - _d.X0);
        }

        /// <summary>
        /// 컨트롤 공 위치: 이격이 열렸으면 굴림 위치, 돌아가는 중이면 발 앞 위치로 서브스텝당 최대 reglueStep, 아니면 발 앞 위치(기존 규칙).
        /// 꺼져 있으면 false(호출자가 기존 규칙).
        /// </summary>
        public bool ControlledBallPosition(MatchContext ctx, long abs, Vec3 glue, out Vec3 pos)
        {
            pos = glue;
            if (!Enabled) return false;
            if (_d.Open && _d.Owner == ctx.Ball.OwnerId)
            {
                var p2 = RollPos((abs - _d.Start + 1) * _bp.Dt, out _);
                pos = new Vec3(p2.X, p2.Y, _bp.Radius);
                return true;
            }
            if (ctx.Ball.OwnerId != _d.Owner) _d.Reglue = true; // 같은 서브스텝 소유 이전(탈취·태클·테이크온): 순간이동 금지
            if (_d.Reglue)
            {
                var cur = ctx.Ball.Position;
                var d = glue - cur;
                var len = d.Length();
                if (len > P.DribReglueStep) pos = cur + d * (P.DribReglueStep / len);
                else _d.Reglue = false;
            }
            return true;
        }

        void Steal(MatchContext ctx, long abs, int sub, int owner, int stealer, Vec2 at)
        {
            Steals++;
            _d.Open = false;
            var sp = ctx.SlotState(stealer).Position;
            var op = ctx.SlotState(owner).Position;
            var tackle = (op - sp).LengthSq() <= P.TackleReach * P.TackleReach; // FR-70: 캐리어가 도달 거리 안이면 태클, 아니면 공 회수
            ref var ball = ref ctx.Ball;
            BallCommands.SetControlledPosition(ref ball, new Vec3(at.X, at.Y, _bp.Radius));
            var team = Squad.TeamOfSlot(stealer);
            var e = SimEvent.Make(tackle ? SimEventKind.Tackle : SimEventKind.BallRecovered, team, ctx.PlayerAtSlot(stealer), ctx.PlayerAtSlot(owner));
            e.Substep = (sbyte)sub;
            e.A = tackle ? EventCodes.TackleWon : 0;
            e.F1 = (op - sp).Length();
            e.BallPhase = BallPhase.Controlled;
            ctx.SetPosition(ref e, new Vec3(at.X, at.Y, _bp.Radius));
            ctx.Emit(e);
            if (tackle)
            {
                var c = ContactBuilder.Make(P, _a.World, ContactKind.Tackle, ctx.Tick, sub, owner, stealer, at, 1f,
                    TackleModel.AngleFromBehind(op, Heading(ctx, owner), sp), 0f, false, 0.2f, 0f);
                EmitContact(ctx, in c);
                ctx.Kinematics.ApplyContact(stealer);
                ctx.Kinematics.ApplyContact(owner);
                ctx.Recorder.MarkAction(stealer, MotionHint.Tackle);
            }
            _lostAbs[owner] = abs; // 결정론 리뷰 L1
            _tk[stealer] = default; // 공을 뺏은 선수의 예약 태클 취소 (M1)
            _a.TransferPossession(ctx, stealer, at);
        }

        static Vec2 Heading(MatchContext ctx, int slot)
        {
            ref readonly var s = ref ctx.SlotState(slot);
            return s.Velocity.LengthSq() > 0.25f ? s.Velocity : s.Facing;
        }

        // ── 테이크온 (FR-04~06) ──

        /// <summary>FR-04: 돌파를 골랐지만 아직 발동 조건 밖이면 armTime 동안 대기시킨다 (다가가는 동안 3 m 안에 들면 그때 판정).</summary>
        public void ArmTakeOn(MatchContext ctx, int att, int def)
        {
            _toAtt = att;
            _toDef = def;
            _toExpire = ctx.Kinematics.Substep + Ticks(P.TakeOnArmTime);
        }

        /// <summary>slot의 대기 테이크온이 지금 발동하는가 (소유자 그대로·수비수 활성·만료 전·발동 조건). 만료·소유 변경이면 지운다. RNG 없음.</summary>
        public bool TakeOnReady(MatchContext ctx, int slot, out int def)
        {
            def = -1;
            if (!Enabled || _toAtt < 0) return false;
            ref readonly var ball = ref ctx.Ball;
            var owns = ball.Phase == BallPhase.Controlled && ball.OwnerId == _toAtt;
            if (!owns || ctx.Kinematics.Substep > _toExpire || !ctx.IsSlotControllable(_toDef) || Immobile(ctx, _toDef)) { _toAtt = -1; _toDef = -1; return false; }
            if (_toAtt != slot || _d.Open || !TakeOnTriggered(ctx, _toAtt, _toDef)) return false;
            def = _toDef;
            _toAtt = -1;
            _toDef = -1;
            return true;
        }

        /// <summary>FR-04 발동 조건 (공격수 att의 상대 골문 기준).</summary>
        public bool TakeOnTriggered(MatchContext ctx, int att, int def)
            => TakeOnModel.Triggered(P, ctx.SlotState(att).Position, ctx.SlotState(def).Position, ctx.OpponentGoal(Squad.TeamOfSlot(att)));

        /// <summary>
        /// 결정 단계에서 1v1 테이크온을 판정하고 결과를 적용한다 (RNG 2). 성공 → 수비수 회복 지연(이동 불가 0.4 s), 실패 → 탈취(수비수 소유 + Tackle·접촉)·
        /// 터치라인 아웃·파울 획득(접촉만 — 판정은 012)·루즈볼. prob = 판정 확률.
        /// </summary>
        public TakeOnOutcome TakeOn(MatchContext ctx, int att, int def, out float prob)
        {
            _a.Refresh(ctx);
            var w = _a.World;
            var goal = ctx.OpponentGoal(Squad.TeamOfSlot(att));
            ref readonly var a = ref ctx.SlotState(att);
            ref readonly var d = ref ctx.SlotState(def);
            var angle = TakeOnModel.AngleAdv(a.Position, a.Velocity, d.Position);
            var cover = TakeOnModel.Cover(P, w, att, def, goal);
            var set = TakeOnModel.DefSetFactor(P, d.Velocity);
            prob = TakeOnModel.Probability(P, w, att, def, angle, cover, set);
            var outcome = TakeOnModel.Resolve(P, prob, Rng);
            TakeOns++;
            var abs = ctx.Kinematics.Substep;
            var at = ctx.Ball.Position.XY;
            var behind = TackleModel.AngleFromBehind(a.Position, Heading(ctx, att), d.Position);
            ref var ball = ref ctx.Ball;
            switch (outcome)
            {
                case TakeOnOutcome.Beat:
                    ctx.Kinematics.SetImmobile(def, abs + Ticks(P.TakeOnRecover));
                    _tk[def] = default; // 제쳐진 수비수의 예약 태클 취소 (결정론 리뷰 M1)
                    break;
                case TakeOnOutcome.Steal:
                {
                    // FR-70: 캐리어가 태클 도달 거리 안이면 태클, 아니면 공 회수 (테이크온 발동 거리 3 m > 스탠딩 도달 거리)
                    var dist = (a.Position - d.Position).Length();
                    var isTackle = dist <= P.TackleReach;
                    var tk = SimEvent.Make(isTackle ? SimEventKind.Tackle : SimEventKind.BallRecovered, Squad.TeamOfSlot(def), ctx.PlayerAtSlot(def), ctx.PlayerAtSlot(att));
                    tk.A = isTackle ? EventCodes.TackleWon : 0;
                    tk.F1 = dist;
                    tk.BallPhase = ball.Phase;
                    ctx.SetPosition(ref tk, d.Position);
                    ctx.Emit(tk);
                    var c = ContactBuilder.Make(P, w, ContactKind.TakeOn, ctx.Tick, 0, att, def, at, 1f, behind, 0f, false, 0.3f, 0f);
                    EmitContact(ctx, in c);
                    if (isTackle) ctx.Recorder.MarkAction(def, MotionHint.Tackle);
                    _lostAbs[att] = abs;
                    _tk[def] = default;
                    _a.TransferPossession(ctx, def, at);
                    break;
                }
                case TakeOnOutcome.Out:
                {
                    // 공격수가 터치라인 밖으로 흘림 (마지막 터치 = 공격수)
                    var lowSide = at.Y < ctx.Pitch.WidthM * 0.5f;
                    var dist = (lowSide ? at.Y : ctx.Pitch.WidthM - at.Y) + 1.5f;
                    var v = Loose(ctx, att, new Vec2(0.35f * ctx.AttackDir(Squad.TeamOfSlot(att)), lowSide ? -1f : 1f), dist, abs);
                    break;
                }
                case TakeOnOutcome.FoulWon:
                {
                    var c = ContactBuilder.Make(P, w, ContactKind.TakeOn, ctx.Tick, 0, att, def, at, 0f, behind, 0f, false, 0.4f, 1f);
                    EmitContact(ctx, in c);
                    ctx.Kinematics.ApplyContact(att);
                    ctx.Kinematics.ApplyContact(def);
                    break;
                }
                default: // 루즈볼: 수비수 발에 맞고 옆으로 튐 (마지막 터치 = 수비수)
                {
                    var rel = d.Position - a.Position;
                    var side = a.Velocity.X * rel.Y - a.Velocity.Y * rel.X >= 0f ? 1f : -1f;
                    var perp = new Vec2(-rel.Y * side, rel.X * side);
                    Loose(ctx, def, perp, 3f, abs);
                    var c = ContactBuilder.Make(P, w, ContactKind.TakeOn, ctx.Tick, 0, att, def, at, 1f, behind, 0f, false, 0.3f, 0.3f);
                    EmitContact(ctx, in c);
                    break;
                }
            }
            return outcome;
        }

        /// <summary>공을 toucher가 dir 방향으로 dist m 굴려 보낸다 (느슨한 공, 마지막 터치·킥 쿨다운 기록).</summary>
        float Loose(MatchContext ctx, int toucher, Vec2 dir, float dist, long abs)
        {
            ref var ball = ref ctx.Ball;
            if (ball.Phase != BallPhase.Controlled) return 0f;
            var u = dir.Normalized();
            if (u == Vec2.Zero) u = new Vec2(1f, 0f);
            var inv = BallInverse.GroundSpeedFor(_bp, dist);
            var v = inv.Status == BallInverseStatus.Ok ? inv.Speed : 3f;
            BallCommands.Kick(ref ball, new Vec3(u.X * v, u.Y * v, 0f), Vec3.Zero, _bp, ctx.BallEvents);
            ctx.LastTouchTeam = Squad.TeamOfSlot(toucher);
            ctx.LastTouchSlot = toucher;
            ctx.LastKickSlot = toucher;
            ctx.LastKickSubstep = abs;
            ctx.LastKickKind = (byte)BallActionKind.Clearance;
            ctx.LastTackleVictim = -1;
            _d.Open = false;
            return v;
        }

        // ── 태클 (FR-07·10·11·31) ──

        /// <summary>
        /// 010 압박 결정의 태클 시도 (결정 RNG: 시도 1 + 스타일 1, 시도가 통과했을 때만 스타일 소비). 대상이 지금 공을 가진 상대이고 최대 도달 거리 안,
        /// 저킹 조건(FR-07)을 만족할 때만 시도 확률을 굴린다. 고른 스타일 도달 거리 밖이면 시도하지 않는다. 예약했으면 true.
        /// </summary>
        public bool TryTackleAttempt(MatchContext ctx, int s, int target, float attemptProb, SeedRng decisionRng)
        {
            if (!Enabled || target < 0 || Squad.TeamOfSlot(target) == Squad.TeamOfSlot(s)) return false;
            ref readonly var ball = ref ctx.Ball;
            if (ball.Phase != BallPhase.Controlled || ball.OwnerId != target) return false;
            if (_tk[s].Active || Immobile(ctx, s) || Squad.IsKeeperSlot(s)) return false;
            var abs = ctx.Kinematics.Substep;
            if (abs - _lastAttempt[s] < Ticks(P.TackleAttemptGap)) return false;
            ref readonly var me = ref ctx.SlotState(s);
            ref readonly var tg = ref ctx.SlotState(target);
            var d2 = (tg.Position - me.Position).LengthSq();
            var maxReach = TackleModel.Reach(P, true);
            if (d2 > maxReach * maxReach) return false;
            var jockey = me.Active.Kind == SteeringKind.OffsetArrive;
            if (jockey)
            {
                _a.Refresh(ctx);
                var gap = (ball.Position.XY - tg.Position).Length();
                var mine = TimeToIntercept.Estimate(in _a.World.Tti[s], ball.Position.X, ball.Position.Y);
                var theirs = TimeToIntercept.Estimate(in _a.World.Tti[target], ball.Position.X, ball.Position.Y);
                if (!TackleModel.CanAttempt(P, true, gap, mine < theirs)) return false;
            }
            var pr = attemptProb < 0f ? 0f : (attemptProb > 1f ? 1f : attemptProb);
            if (!decisionRng.Bernoulli(pr)) return false;
            _a.Refresh(ctx);
            var sliding = TackleModel.ChooseSliding(P, _a.World.Temperament[s], _a.World.SlideLevel[s], decisionRng);
            var reach = TackleModel.Reach(P, sliding);
            if (d2 > reach * reach) return false;
            return ScheduleTackle(ctx, s, target, sliding);
        }

        /// <summary>예약 태클 (준비 시간 뒤 접촉 서브스텝에 판정). 시나리오 Engage(013)도 쓴다.</summary>
        public bool ScheduleTackle(MatchContext ctx, int s, int target, bool sliding)
        {
            if (!Enabled || _tk[s].Active || Immobile(ctx, s) || Squad.IsKeeperSlot(s)) return false;
            var abs = ctx.Kinematics.Substep;
            if (abs - _lastAttempt[s] < Ticks(P.TackleAttemptGap)) return false; // 시나리오 Engage도 같은 간격 (결정론 리뷰 L6)
            var due = abs + System.Math.Max(1L, Ticks(sliding ? P.TackleWindupSlide : P.TackleWindupStand));
            _tk[s] = new PendingTackle { Active = true, Target = target, Sliding = sliding, Due = due, Expire = due + Ticks(P.TackleLateWindow) };
            _lastAttempt[s] = abs;
            ctx.Recorder.MarkAction(s, MotionHint.Tackle);
            return true;
        }

        /// <summary>
        /// 서브스텝마다(운동 적분 전) 슬롯 오름차순으로 준비가 끝난 예약 태클을 판정한다. 같은 서브스텝에 먼저 성공한 태클이 있으면 뒤 태클은 무효(RNG는 같게 소비).
        /// 대상이 아직 공을 가졌고 도달 거리 안 → FR-10·11 판정. 공이 떠난 뒤 창 안·도달 거리 안 → 늦은 태클(접촉만). 도달 거리 밖이 만료까지 → 취소(접촉 없음).
        /// </summary>
        public void OnTackles(MatchContext ctx, long abs, int sub)
        {
            if (!Enabled) return;
            var won = false;
            for (int s = 0; s < N; s++)
            {
                ref var t = ref _tk[s];
                if (!t.Active || abs < t.Due) continue;
                if (!ctx.IsSlotControllable(s) || !ctx.Flow.InPlay || !ctx.IsSlotActive(t.Target)) { t = default; continue; }
                ref var ball = ref ctx.Ball;
                // 결정론 리뷰 M1: 그 사이 다운·회복 지연이 걸렸거나 태클러가 이미 공을 가졌으면 취소 (RNG 0)
                if (Immobile(ctx, s) || (ball.Phase == BallPhase.Controlled && ball.OwnerId == s)) { t = default; continue; }
                var target = t.Target;
                var sliding = t.Sliding;
                var reach = TackleModel.Reach(P, sliding);
                ref readonly var me = ref ctx.SlotState(s);
                ref readonly var tg = ref ctx.SlotState(target);
                var d2 = (tg.Position - me.Position).LengthSq();
                var owns = ball.Phase == BallPhase.Controlled && ball.OwnerId == target;
                if (d2 > reach * reach)
                {
                    if (abs >= t.Expire) t = default;
                    continue;
                }
                var lostAt = !owns && _d.Owner == target ? abs : _lostAbs[target]; // 결정론 리뷰 M2: 같은 서브스텝 킥으로 막 떠난 공 (OnDribble이 아직 못 봄)
                var lateSec = owns ? 0f : (abs - lostAt) * _bp.Dt;
                var voided = !owns && won; // 같은 서브스텝 앞 태클이 이미 따냄 → 무효 (판정·RNG 소비는 같게)
                if (!owns && !voided && (lateSec > P.TackleLateWindow || lostAt < abs - Ticks(P.TackleLateWindow) - 1))
                {
                    t = default; // 공이 떠난 지 오래 — 태클 아님
                    continue;
                }
                t = default;
                _a.Refresh(ctx);
                var w = _a.World;
                var gap = (ball.Position.XY - tg.Position).Length();
                var exposure = TackleModel.BallExposure(P, gap, (ball.Position.XY - me.Position).Length(), reach);
                var behind = TackleModel.AngleFromBehind(tg.Position, Heading(ctx, target), me.Position);
                var prob = TackleModel.WinProbability(P, w, s, target, exposure, behind, sliding);
                var result = TackleModel.Resolve(P, prob, sliding, Rng); // RNG 2 (무효·늦은 태클도 같게)
                TacklesResolved++;
                if (!owns || won) result = TackleResult.Miss;
                var lateness = voided ? 0f : TackleModel.Lateness(P, lateSec);
                var at = owns ? ball.Position.XY : tg.Position;
                var e = SimEvent.Make(SimEventKind.Tackle, Squad.TeamOfSlot(s), ctx.PlayerAtSlot(s), ctx.PlayerAtSlot(target));
                e.Substep = (sbyte)sub;
                var late = !owns && !voided;
                e.A = (sliding ? EventCodes.TackleSliding : 0) | (result != TackleResult.Miss ? EventCodes.TackleWon : 0) | (late ? EventCodes.TackleLate : 0);
                e.F0 = prob;
                e.F1 = DMath.Sqrt(d2); // 판정 순간 태클러–캐리어 거리 (FR-70 검증용)
                e.BallPhase = ball.Phase;
                ctx.SetPosition(ref e, new Vec3(at.X, at.Y, _bp.Radius));
                ctx.Emit(e);
                if (late) LateTackles++;
                var c = ContactBuilder.Make(P, w, ContactKind.Tackle, ctx.Tick, sub, target, s, at,
                    result != TackleResult.Miss ? 1f : 0f, behind, lateness, sliding, sliding ? 0.2f : 0.3f, owns ? 1f - exposure : 0f);
                EmitContact(ctx, in c);
                ctx.Kinematics.ApplyContact(s);
                ctx.Kinematics.ApplyContact(target);
                switch (result)
                {
                    case TackleResult.WinKeep:
                        won = true;
                        TacklesWon++;
                        _lostAbs[target] = abs;
                        _a.TransferPossession(ctx, s, ball.Position.XY);
                        break;
                    case TackleResult.WinPoke:
                    {
                        won = true;
                        TacklesWon++;
                        _lostAbs[target] = abs;
                        var dir = (ball.Position.XY - tg.Position) + (ball.Position.XY - me.Position);
                        var v = P.TacklePokeSpeed;
                        var u = dir.Normalized();
                        if (u == Vec2.Zero) u = me.Facing;
                        BallCommands.Kick(ref ball, new Vec3(u.X * v, u.Y * v, 0f), Vec3.Zero, _bp, ctx.BallEvents);
                        ctx.LastTouchTeam = Squad.TeamOfSlot(s);
                        ctx.LastTouchSlot = s;
                        ctx.LastKickSlot = s;
                        ctx.LastKickSubstep = abs;
                        ctx.LastKickKind = (byte)BallActionKind.Clearance;
                        ctx.LastTackleVictim = target;
                        _d.Open = false;
                        break;
                    }
                    default:
                        if (sliding) ctx.Kinematics.SetImmobile(s, abs + Ticks(P.TackleSlideDown)); // FR-11 다운 1.2 s
                        break;
                }
            }
        }

        // ── 몸싸움 (FR-20) ──

        /// <summary>
        /// 수신 판정 뒤: 이격이 닫힌(볼 보호) 소유자에게 상대가 접촉 거리 안이면 주기마다 볼 지키기 판정(RNG 1). 지면 공이 도전자 쪽으로 흐른다(루즈볼).
        /// Duel 이벤트 + 접촉(Shield).
        /// </summary>
        public void OnShield(MatchContext ctx, long abs, int sub)
        {
            if (!Enabled || abs < _shieldNext || !ctx.Flow.InPlay) return;
            ref var ball = ref ctx.Ball;
            if (ball.Phase != BallPhase.Controlled) return;
            var owner = ball.OwnerId;
            if (owner < 0 || owner >= N || _d.Open || Squad.IsKeeperSlot(owner) || ctx.Keepers.IsBusySlot(owner)) return;
            ref readonly var pk = ref _a.Pending;
            if (pk.Active && pk.Action.KickerSlot == owner) return;
            ref readonly var o = ref ctx.SlotState(owner);
            if (o.Velocity.LengthSq() >= P.DribMinSpeed * P.DribMinSpeed) return;
            var opp = 1 - Squad.TeamOfSlot(owner);
            var ch = -1;
            var lim = P.ShieldDist * P.ShieldDist;
            var best = float.MaxValue;
            for (int s = opp * Squad.SlotsPerTeam; s < (opp + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!ctx.IsSlotControllable(s) || Immobile(ctx, s) || Squad.IsKeeperSlot(s)) continue;
                var d2 = (ctx.SlotState(s).Position - o.Position).LengthSq();
                if (d2 <= lim && d2 < best) { best = d2; ch = s; } // 동률 낮은 슬롯 (결정론 리뷰 L2)
            }
            if (ch < 0) return;
            _shieldNext = abs + Ticks(P.ShieldPeriod);
            _a.Refresh(ctx);
            var keep = ShieldModel.Resolve(ShieldModel.KeepProbability(P, _a.World, owner, ch), Rng); // RNG 1
            ShieldChecks++;
            var at = ball.Position.XY;
            var e = SimEvent.Make(SimEventKind.Duel, Squad.TeamOfSlot(ch), ctx.PlayerAtSlot(ch), ctx.PlayerAtSlot(owner));
            e.Substep = (sbyte)sub;
            e.A = keep ? 0 : 1;
            e.BallPhase = ball.Phase;
            ctx.SetPosition(ref e, ball.Position);
            ctx.Emit(e);
            var cp = ctx.SlotState(ch).Position;
            var c = ContactBuilder.Make(P, _a.World, ContactKind.Shield, ctx.Tick, sub, owner, ch, at, keep ? 0f : 1f,
                TackleModel.AngleFromBehind(o.Position, o.Facing, cp), 0f, false, 1.2f, keep ? 1f : 0f);
            EmitContact(ctx, in c);
            ctx.Kinematics.ApplyContact(owner);
            ctx.Kinematics.ApplyContact(ch);
            if (!keep)
            {
                ShieldLosses++;
                Loose(ctx, ch, cp - o.Position + (at - o.Position), 2f, abs);
            }
        }

        void EmitContact(MatchContext ctx, in ContactDetail c)
        {
            _a.EmitContact(ctx, in c);
            Contacts++;
        }

        // ── 해시 · 스냅샷 ──

        public void AddTo(ref StableHasher h)
        {
            if (!Enabled) return; // 006 대체 파이프라인: 해시 그대로
            _d.AddTo(ref h);
            for (int s = 0; s < N; s++) { _tk[s].AddTo(ref h); h.Add(_lastAttempt[s]); h.Add(_lostAbs[s]); }
            h.Add(_shieldNext);
            h.Add(_toAtt); h.Add(_toDef); h.Add(_toExpire);
        }

        public void Write(StateWriter w)
        {
            w.Write(Enabled); // 복원한 실행이 첫 결정 단계 전에도 같은 해시를 내도록
            _d.Write(w);
            for (int s = 0; s < N; s++) { _tk[s].Write(w); w.Write(_lastAttempt[s]); w.Write(_lostAbs[s]); }
            w.Write(_shieldNext);
            w.Write(_toAtt); w.Write(_toDef); w.Write(_toExpire);
        }

        public void Read(StateReader r)
        {
            Enabled = r.ReadBool();
            _d = DribbleState.Read(r);
            for (int s = 0; s < N; s++) { _tk[s] = PendingTackle.Read(r); _lastAttempt[s] = r.ReadLong(); _lostAbs[s] = r.ReadLong(); }
            _shieldNext = r.ReadLong();
            _toAtt = r.ReadInt(); _toDef = r.ReadInt(); _toExpire = r.ReadLong();
        }
    }
}
