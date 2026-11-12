using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>
    /// 골문을 향하는 공 감시 (specs/011 FR-11·15·17). 물리 서브스텝마다 불린다(<see cref="GoalkeeperSystem.OnSubstep"/>).
    /// 공의 비행이 바뀔 때(킥·굴절·쳐내기 — <see cref="NotifyFlight"/>) 004 예측으로 골라인 통과를 보고, 유효면 거리와 상관없이
    /// 판정을 예약한다(FR-15): 반응 시각(슛 서브스텝 + t_react_eff, 가림 지연 포함)에 다이빙 시작, 접촉 서브스텝에 결과 적용.
    /// 비행이 다시 바뀌면(굴절) 예약을 버리고 그 시각부터 반응 타이머를 다시 잰다(FR-11).
    /// </summary>
    public sealed class ShotWatcher
    {
        readonly GoalkeeperParams _p;
        readonly SaveModel _save;
        readonly BallEventBuffer _scratch = new BallEventBuffer(8);

        int _epoch, _seenEpoch = -1;
        bool _active, _reactDone;
        int _team;
        long _start, _react, _contact;
        SaveJudgement _j;
        float _psxg;

        /// <summary>마지막 실점용 표시 (심판 단계가 Goal 이벤트에 붙인다): 수비 팀, B 플래그, PSxG.</summary>
        public int PendingGoalTeam { get; private set; } = -1;
        public int PendingGoalFlags { get; private set; }
        public float PendingGoalPsxg { get; private set; }

        public ShotWatcher(GoalkeeperParams p, SaveModel save)
        {
            _p = p;
            _save = save;
        }

        public bool Active => _active;
        public int DefendingTeam => _active ? _team : -1;
        public long ReactSubstep => _react;
        public long ContactSubstep => _contact;
        public long ShotSubstep => _start;
        public ref readonly SaveJudgement Judgement => ref _j;

        /// <summary>공의 비행이 바뀌었다 (킥·굴절·쳐내기·펀칭). 다음 서브스텝 감시에서 다시 예측한다. 이전 실패 판정의 실점 표시는 버린다(리뷰 M2).</summary>
        public void NotifyFlight()
        {
            _epoch++;
            ClearPendingGoal();
        }

        void ClearPendingGoal()
        {
            PendingGoalTeam = -1;
            PendingGoalFlags = 0;
            PendingGoalPsxg = 0f;
        }

        /// <summary>추적 중인 슛의 GK인가 (물리 단계 잡기 판정에서 제외).</summary>
        public bool Tracks(int keeperSlot) => _active && keeperSlot == _team * Squad.SlotsPerTeam;

        public void ConsumeGoal(int defendingTeam, out int flags, out float psxg)
        {
            if (PendingGoalTeam == defendingTeam) { flags = PendingGoalFlags; psxg = PendingGoalPsxg; }
            else { flags = 0; psxg = 0f; }
            PendingGoalTeam = -1;
            PendingGoalFlags = 0;
            PendingGoalPsxg = 0f;
        }

        internal void Step(MatchContext ctx, GoalkeeperSystem gk, long abs, int sub)
        {
            ref var ball = ref ctx.Ball;
            if (PendingGoalTeam >= 0 && (ball.Phase == BallPhase.Controlled || ball.Phase == BallPhase.Rest || !ctx.Flow.InPlay)) ClearPendingGoal(); // 리뷰 M2: 실패 판정 뒤 골이 안 나면 버림
            if (_epoch != _seenEpoch)
            {
                _seenEpoch = _epoch;
                _active = false; // 굴절·새 킥: 예약 취소, 반응 타이머 리셋
                if (ctx.Flow.InPlay) TryStart(ctx, gk, abs);
            }
            if (!_active) return;
            if (ball.Phase == BallPhase.Controlled || ball.Phase == BallPhase.Rest || !ctx.Flow.InPlay) { _active = false; return; }
            var slot = _team * Squad.SlotsPerTeam;
            if (!ctx.IsSlotActive(slot)) { _active = false; return; }

            if (!_reactDone && abs >= _react)
            {
                _reactDone = true;
                gk.StartDive(ctx, _team, in _j, abs, sub, _contact);
            }
            if (abs >= _contact) Resolve(ctx, gk, abs, sub);
        }

        void TryStart(MatchContext ctx, GoalkeeperSystem gk, long abs)
        {
            ref readonly var ball = ref ctx.Ball;
            if (ball.Phase == BallPhase.Controlled || ball.Phase == BallPhase.Rest) return;
            var vx = ball.Velocity.X;
            if (vx == 0f) return;
            var lowGoal = vx < 0f;
            int team = -1;
            for (int t = 0; t < 2; t++)
                if ((ctx.OwnGoal(t).X < ctx.Pitch.LengthM * 0.5f) == lowGoal) team = t;
            if (team < 0) return;
            var slot = team * Squad.SlotsPerTeam;
            if (!ctx.IsSlotActive(slot)) return; // 빈 골문 (퇴장 직후 등)
            var goal = ctx.OwnGoal(team);
            var dirIn = (float)ctx.AttackDir(team);
            if (!_save.Predict(in ball, ctx.BallParams, goal, dirIn)) return;

            ref readonly var st = ref ctx.SlotState(slot);
            var state = new KeeperState { Position = st.Position, Velocity = st.Velocity };
            var busy = gk.IsBusy(team);
            var extra = busy ? 10f : OcclusionDelay(ctx, slot, goal);
            var crowd = 0;
            var sctx = new SaveContext { Goal = goal, DirIn = dirIn, ExtraDelay = extra };
            var prof = gk.Profile(ctx, team);
            var j = _save.Evaluate(in state, in prof, in sctx);
            if (j.ContactIndex < 0) return;
            crowd = Crowd(ctx, slot, j.ContactPoint);
            if (crowd > 0)
            {
                sctx.Crowd = crowd;
                j = _save.Evaluate(in state, in prof, in sctx);
            }
            if (ctx.LastTouchTeam == team) j.PCatch = 0f; // 같은 팀 킥: 손으로 잡지 않는다 (백패스 판정은 012)
            if (busy) j.Dives = false;
            var avg = gk.LeagueAverage;
            var ja = _save.Evaluate(in state, in avg, new SaveContext { Goal = goal, DirIn = dirIn, ExtraDelay = extra, Crowd = crowd });
            _psxg = 1f - ja.PReach;

            _active = true;
            _reactDone = false;
            _team = team;
            _j = j;
            _start = abs;
            _contact = abs + j.ContactIndex + 1;
            var reactSteps = (long)DMath.Floor(j.ReactTime / ctx.BallParams.Dt + 0.5f);
            _react = abs + (reactSteps < 1 ? 1 : reactSteps);
            if (_react >= _contact) { _j.Dives = false; _react = _contact; }
        }

        /// <summary>
        /// 가림 지연 (FR-11): 슛 직후 반응 시간 안에서 GK–공 시야선이 다른 선수 몸(반지름 r)에 가린 시간 비율로 +min~max s.
        /// 선수 위치는 지금 위치로 고정(슛 순간 스냅샷). 경로는 직전 예측.
        /// </summary>
        float OcclusionDelay(MatchContext ctx, int keeperSlot, Vec2 goal)
        {
            var gkPos = ctx.SlotState(keeperSlot).Position;
            var dt = ctx.BallParams.Dt;
            var window = _p.OcclusionFullTime;
            var steps = (int)DMath.Floor(window / dt + 0.5f);
            if (steps > _save.PathCount) steps = _save.PathCount;
            var r2 = _p.OcclusionRadius * _p.OcclusionRadius;
            int blocked = 0;
            for (int i = 0; i < steps; i++)
            {
                var b = _save.PathPoint(i);
                var bxy = new Vec2(b.X, b.Y);
                for (int s = 0; s < Squad.Slots; s++)
                {
                    if (s == keeperSlot || !ctx.IsSlotActive(s)) continue;
                    if (DistSqToSegment(ctx.SlotState(s).Position, gkPos, bxy) < r2) { blocked++; break; }
                }
            }
            return OcclusionExtra(_p, blocked * dt);
        }

        /// <summary>가린 시간 → 추가 반응 지연: 0이면 0, 아니면 min + (max − min)·min(1, 가린 시간 / 기준 시간).</summary>
        public static float OcclusionExtra(GoalkeeperParams p, float occludedSeconds)
        {
            if (occludedSeconds <= 0f) return 0f;
            var u = p.OcclusionFullTime > 0f ? occludedSeconds / p.OcclusionFullTime : 1f;
            if (u > 1f) u = 1f;
            return p.OcclusionMin + (p.OcclusionMax - p.OcclusionMin) * u;
        }

        int Crowd(MatchContext ctx, int keeperSlot, Vec3 contact)
        {
            var c = new Vec2(contact.X, contact.Y);
            var r2 = _p.SaveCrowdRadius * _p.SaveCrowdRadius;
            int n = 0;
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (s == keeperSlot || !ctx.IsSlotActive(s)) continue;
                if ((ctx.SlotState(s).Position - c).LengthSq() < r2) n++;
            }
            return n;
        }

        public static float DistSqToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            var len2 = ab.LengthSq();
            var t = len2 > 1e-8f ? Vec2.Dot(p - a, ab) / len2 : 0f;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return (a + ab * t - p).LengthSq();
        }

        void Resolve(MatchContext ctx, GoalkeeperSystem gk, long abs, int sub)
        {
            _active = false;
            var team = _team;
            var slot = team * Squad.SlotsPerTeam;
            var goal = ctx.OwnGoal(team);
            var dirIn = (float)ctx.AttackDir(team);
            var sctx = new SaveContext { Goal = goal, DirIn = dirIn };
            var prof = gk.Profile(ctx, team);
            var o = _save.Resolve(in _j, in prof, in sctx, gk.Rng);
            ref var ball = ref ctx.Ball;
            var pos = ball.Position;
            var pid = ctx.PlayerAtSlot(slot);
            var shooter = ctx.LastKickSlot >= 0 ? ctx.PlayerAtSlot(ctx.LastKickSlot) : -1;
            var heightBand = (int)_j.Height;
            _scratch.Clear();
            switch (o.Kind)
            {
                case SaveOutcomeKind.Catch:
                {
                    BallCommands.TakeControl(ref ball, slot, pos, _scratch);
                    Contact(ctx, SimEventKind.Catch, team, pid, shooter, sub, pos, CatchMotion(_j), heightBand);
                    AfterTouch(ctx, team, slot, abs, sub, keep: true);
                    break;
                }
                case SaveOutcomeKind.ParrySafe:
                case SaveOutcomeKind.ParryDanger:
                case SaveOutcomeKind.Punch:
                {
                    BallCommands.TakeControl(ref ball, slot, pos, _scratch);
                    BallCommands.Kick(ref ball, o.ReboundVelocity, Vec3.Zero, ctx.BallParams, _scratch);
                    if (o.Kind == SaveOutcomeKind.Punch)
                        Contact(ctx, SimEventKind.Punch, team, pid, shooter, sub, pos,
                            TwoHands(_j) ? KeeperMotionKind.PunchTwoHands : KeeperMotionKind.PunchOneHand, heightBand);
                    else
                    {
                        var motion = pos.Z > _p.SaveExtremeHighZ && o.Kind == SaveOutcomeKind.ParrySafe ? KeeperMotionKind.TipOverBar
                            : (_j.Direction == 0 ? KeeperMotionKind.ParryFront : KeeperMotionKind.ParrySide);
                        Contact(ctx, SimEventKind.Parry, team, pid, shooter, sub, pos, motion,
                            heightBand | (o.Kind == SaveOutcomeKind.ParryDanger ? EventCodes.ParryDanger : 0));
                    }
                    AfterTouch(ctx, team, slot, abs, sub, keep: false);
                    NotifyFlight();
                    break;
                }
                case SaveOutcomeKind.Miss:
                    PendingGoalTeam = team;
                    PendingGoalFlags = o.Fingertip ? EventCodes.Fingertip : 0;
                    PendingGoalPsxg = _psxg;
                    return;
                default:
                    return;
            }
            // 선방 집계 (FR-80, 015 입력)
            var save = SimEvent.Make(SimEventKind.Save, team, pid, shooter);
            save.Substep = (sbyte)sub;
            save.F0 = _psxg;
            save.F1 = _j.PReach;
            save.A = (int)o.Kind;
            ctx.SetPosition(ref save, pos);
            ctx.Emit(save);
        }

        /// <summary>정면 펀칭은 두 손, 측면은 한 손.</summary>
        static bool TwoHands(in SaveJudgement j) => j.Direction == 0;

        static KeeperMotionKind CatchMotion(in SaveJudgement j)
        {
            if (j.Dives) return KeeperMotionKind.Dive;
            if (j.ContactPoint.Z < 0.5f) return KeeperMotionKind.CatchGround;
            if (j.ContactPoint.Z > 1.7f) return KeeperMotionKind.CatchOverhead;
            return KeeperMotionKind.CatchChest;
        }

        void Contact(MatchContext ctx, SimEventKind kind, int team, int pid, int shooter, int sub, Vec3 pos, KeeperMotionKind motion, int b)
        {
            var e = SimEvent.Make(kind, team, pid, shooter);
            e.Substep = (sbyte)sub;
            e.Part = BodyPart.Hand;
            e.BallPhase = ctx.Ball.Phase;
            e.A = (int)motion;
            e.B = b;
            e.F0 = _j.PReach;
            e.F1 = _j.Difficulty;
            ctx.SetPosition(ref e, pos);
            ctx.Emit(e);
        }

        static void AfterTouch(MatchContext ctx, int team, int slot, long abs, int sub, bool keep)
        {
            var prevTeam = ctx.LastTouchTeam;
            ctx.LastTouchTeam = team;
            ctx.LastTouchSlot = slot;
            if (keep)
            {
                ctx.LastKickKind = 0;
                ctx.PossessionStartTick = ctx.Tick;
                if (prevTeam != team)
                {
                    var pc = SimEvent.Make(SimEventKind.PossessionChange, team, ctx.PlayerAtSlot(slot));
                    pc.Substep = (sbyte)sub;
                    ctx.SetPosition(ref pc, ctx.Ball.Position);
                    ctx.Emit(pc);
                }
            }
            else
            {
                ctx.LastKickSlot = slot;
                ctx.LastKickSubstep = abs;
                ctx.LastKickKind = (byte)Match.Pipeline.BallActionKind.Clearance;
                ctx.LastTackleVictim = -1;
            }
        }

        public void Write(StateWriter w)
        {
            w.Write(_epoch); w.Write(_seenEpoch); w.Write(_active); w.Write(_reactDone); w.Write(_team);
            w.Write(_start); w.Write(_react); w.Write(_contact); w.Write(_psxg);
            WriteJ(w, in _j);
            w.Write(PendingGoalTeam); w.Write(PendingGoalFlags); w.Write(PendingGoalPsxg);
        }

        public void Read(StateReader r)
        {
            _epoch = r.ReadInt(); _seenEpoch = r.ReadInt(); _active = r.ReadBool(); _reactDone = r.ReadBool(); _team = r.ReadInt();
            _start = r.ReadLong(); _react = r.ReadLong(); _contact = r.ReadLong(); _psxg = r.ReadFloat();
            _j = ReadJ(r);
            PendingGoalTeam = r.ReadInt(); PendingGoalFlags = r.ReadInt(); PendingGoalPsxg = r.ReadFloat();
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_epoch); h.Add(_seenEpoch); h.Add(_active); h.Add(_reactDone); h.Add(_team);
            h.Add(_start); h.Add(_react); h.Add(_contact); h.Add(_psxg);
            AddJ(ref h, in _j);
            h.Add(PendingGoalTeam); h.Add(PendingGoalFlags); h.Add(PendingGoalPsxg);
        }

        /// <summary>판정 해시 — <see cref="WriteJ"/>와 같은 필드 (리뷰 L1).</summary>
        static void AddJ(ref StableHasher h, in SaveJudgement j)
        {
            h.Add(j.OnTarget); h.Add(j.CrossTime); h.Add(j.ReactTime); h.Add(j.PReach); h.Add(j.MaxMargin); h.Add(j.ContactIndex);
            h.Add(j.ContactTime); h.Add(j.ContactPoint.X); h.Add(j.ContactPoint.Y); h.Add(j.ContactPoint.Z);
            h.Add(j.ContactVelocity.X); h.Add(j.ContactVelocity.Y); h.Add(j.ContactVelocity.Z); h.Add(j.RequiredReach); h.Add(j.Reach); h.Add(j.Difficulty);
            h.Add(j.PCatch); h.Add(j.PParrySafe); h.Add(j.Punch); h.Add(j.Direction); h.Add((byte)j.Height); h.Add(j.Dives); h.Add(j.MovingPenalty);
        }

        static void WriteJ(StateWriter w, in SaveJudgement j)
        {
            w.Write(j.OnTarget); w.Write(j.CrossTime); w.Write(j.ReactTime); w.Write(j.PReach); w.Write(j.MaxMargin); w.Write(j.ContactIndex);
            w.Write(j.ContactTime); w.Write(j.ContactPoint); w.Write(j.ContactVelocity); w.Write(j.RequiredReach); w.Write(j.Reach); w.Write(j.Difficulty);
            w.Write(j.PCatch); w.Write(j.PParrySafe); w.Write(j.Punch); w.Write(j.Direction); w.Write((byte)j.Height); w.Write(j.Dives); w.Write(j.MovingPenalty);
        }

        static SaveJudgement ReadJ(StateReader r) => new SaveJudgement
        {
            OnTarget = r.ReadBool(), CrossTime = r.ReadFloat(), ReactTime = r.ReadFloat(), PReach = r.ReadFloat(), MaxMargin = r.ReadFloat(), ContactIndex = r.ReadInt(),
            ContactTime = r.ReadFloat(), ContactPoint = r.ReadVec3(), ContactVelocity = r.ReadVec3(), RequiredReach = r.ReadFloat(), Reach = r.ReadFloat(), Difficulty = r.ReadFloat(),
            PCatch = r.ReadFloat(), PParrySafe = r.ReadFloat(), Punch = r.ReadBool(), Direction = r.ReadInt(), Height = (DiveHeight)r.ReadByte(), Dives = r.ReadBool(), MovingPenalty = r.ReadBool(),
        };
    }
}
