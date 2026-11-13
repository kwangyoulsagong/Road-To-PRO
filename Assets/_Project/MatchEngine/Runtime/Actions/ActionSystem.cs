using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Replay;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 경기당 액션 시스템 (specs/008). 단계 5의 공 동작을 예약 킥으로 받아 킥 준비 시간 뒤 접촉 서브스텝에 실행하고(FR-13·65·66),
    /// 서브스텝마다 수신·인터셉트를 판정하고 첫 터치를 실행한다(FR-45·67·70). 액션 RNG 스트림(<see cref="RngStreamId.Action"/>)만 쓰고,
    /// 소비는 패스 4 · 컨트롤 판정 1 · 첫 터치 3으로 고정(FR-61). 상태: 예약 킥·판정 표시·터치 쿨다운·상세 기록·RNG → 해시·스냅샷.
    /// </summary>
    public sealed class ActionSystem
    {
        public readonly ActionParams P;
        readonly BallParams _bp;
        public readonly SeedRng Rng;
        /// <summary>평가 입력 (틱마다 <see cref="Refresh"/>로 채운다).</summary>
        public readonly ActionWorld World = new ActionWorld();
        public readonly ActionDetailLog Details = new ActionDetailLog();
        /// <summary>물리 xPass (키 모먼트·디버그용, 틱 기본 경로 아님).</summary>
        public readonly PhysicsXPass Physics = new PhysicsXPass();
        /// <summary>FR-63 진단 싱크. null이면 호출 0.</summary>
        public IActionDiagnostics Diagnostics;

        PendingKick _pending;
        byte _pendingFlow;
        int _flightTarget = -1;
        readonly bool[] _attempted = new bool[ActionWorld.N];
        readonly long[] _touchCooldown = new long[ActionWorld.N];

        // 파생 캐시 (상태 아님)
        readonly Dictionary<int, MatchPlayer> _players = new Dictionary<int, MatchPlayer>();
        readonly int[] _boundId = new int[ActionWorld.N];
        long _worldTick = long.MinValue;
        readonly Vec2[] _oppPos = new Vec2[ActionWorld.PerTeam], _oppVel = new Vec2[ActionWorld.PerTeam];
        readonly float[] _runnerX = new float[ActionWorld.N];
        readonly BallEventBuffer _scratch = new BallEventBuffer(32);

        /// <summary>테스트·진단용 누계 (상태 아님).</summary>
        public long KicksExecuted, KicksCancelled, ControlChecks, FirstTouches;

        public ActionSystem(ActionParams p, BallParams bp, MatchSetup setup)
        {
            P = p;
            _bp = bp;
            Rng = RngStreams.Create(setup.Seed, RngStreamId.Action);
            foreach (var t in new[] { setup.Home, setup.Away })
                if (t?.Players != null)
                    foreach (var mp in t.Players) _players[mp.Id] = mp; // 조회 전용 (순회 결과에 의존하지 않음)
            for (int i = 0; i < _boundId.Length; i++) _boundId[i] = int.MinValue;
            World.AttackDir[0] = 1;
            World.AttackDir[1] = -1;
            World.PrepareRoll(p, bp);
        }

        public ref readonly PendingKick Pending => ref _pending;
        /// <summary>지금 날아가는 패스의 받는 슬롯 (실행된 패스만, 수신·다른 킥이면 −1). T14a 받는 선수 추격이 읽는다.</summary>
        public int FlightTarget => _flightTarget;
        public bool IsAttempted(int slot) => _attempted[slot];

        // ── 평가 입력 ──

        /// <summary>틱마다 1회: 22 슬롯 위치·속도·TTI·능력치를 월드에 채운다 (같은 틱 재호출은 무시).</summary>
        public void Refresh(MatchContext ctx)
        {
            if (_worldTick == ctx.Tick) return;
            _worldTick = ctx.Tick;
            var w = World;
            w.PitchL = ctx.Pitch.LengthM;
            w.PitchW = ctx.Pitch.WidthM;
            w.AttackDir[0] = ctx.AttackDir(0);
            w.AttackDir[1] = ctx.AttackDir(1);
            var k = ctx.Kinematics;
            for (int s = 0; s < ActionWorld.N; s++)
            {
                w.Active[s] = ctx.IsSlotControllable(s);
                if (!w.Active[s]) continue;
                ref readonly var st = ref k.State(s);
                w.Pos[s] = st.Position;
                w.Vel[s] = st.Velocity;
                w.Profile[s] = k.Profile(s);
                w.Tti[s] = new Kinematics.TimeToIntercept.Context(in w.Profile[s], st.Position, st.Velocity, st.W, st.C, ctx.KinParams);
                var id = ctx.PlayerAtSlot(s);
                if (id != _boundId[s]) Bind(s, id);
            }
        }

        /// <summary>테스트·고정 상황: 같은 틱에 선수를 옮긴 뒤 월드를 다시 채우게 한다.</summary>
        public void InvalidateWorld() => _worldTick = long.MinValue;

        void Bind(int s, int id)
        {
            _boundId[s] = id;
            if (id >= 0 && _players.TryGetValue(id, out var mp))
            {
                var mind = mp.Mind.OrAverage();
                var lvl = mp.Style != null ? mp.Style.HabitLevels[(int)UtilityAI.Habit.OneTouchMaster] : (byte)0;
                World.SetSkills(s, mp.Technical.OrAverage(), mind.Vision, mind.Composure, lvl);
            }
            else World.SetSkills(s, TechnicalAttributes.Average, 50f, 50f, 0);
        }

        /// <summary>FR-02·03: 슬롯 s를 목표로 한 상대 팀 압박 (월드 기준). 볼 소유자면 공 목표와 큰 값.</summary>
        public float PressureOn(MatchContext ctx, int s)
        {
            Refresh(ctx);
            var w = World;
            var opp = 1 - ActionWorld.TeamOf(s);
            var n = 0;
            for (int o = opp * ActionWorld.PerTeam; o < (opp + 1) * ActionWorld.PerTeam; o++)
            {
                if (!w.Active[o]) continue;
                _oppPos[n] = w.Pos[o];
                _oppVel[n] = w.Vel[o];
                n++;
            }
            ref readonly var ball = ref ctx.Ball;
            if (ball.Phase == BallPhase.Controlled && ball.OwnerId == s)
                return PressureModel.Carrier(P, w.Pos[s], w.Vel[s], ball.Position.XY, ball.Velocity.XY, _oppPos, _oppVel, n, w.PitchW);
            return PressureModel.Team(P, w.Pos[s], w.Vel[s], _oppPos, _oppVel, n, w.PitchW);
        }

        /// <summary>
        /// 대체 온볼·시나리오 공용 패스 평가 (FR-11·12): 수신자 receiver(조준점 aim)로의 최선 속도 후보와 ΔxT(007 전 대체 xT 차).
        /// RNG 없음.
        /// </summary>
        public PassOption EvaluatePass(MatchContext ctx, int passer, int receiver, Vec2 aim, out float deltaXt, out float pressure,
            PassModel.LoftMode mode = PassModel.LoftMode.Any)
        {
            pressure = PressureOn(ctx, passer);
            return EvaluatePass(ctx, passer, receiver, aim, pressure, out deltaXt, mode);
        }

        /// <summary>패서 압박을 이미 구했을 때 (후보 여러 명 평가 — 압박은 틱당 1회).</summary>
        public PassOption EvaluatePass(MatchContext ctx, int passer, int receiver, Vec2 aim, float pressure, out float deltaXt,
            PassModel.LoftMode mode = PassModel.LoftMode.Any)
        {
            Refresh(ctx);
            var from = ctx.Ball.Position.XY;
            var o = PassModel.Best(P, _bp, World, passer, receiver, from, aim, pressure, mode);
            var team = ActionWorld.TeamOf(passer);
            deltaXt = World.Threat(P, team, aim) - World.Threat(P, team, from);
            return o;
        }

        /// <summary>
        /// FR-14 스루볼 후보: 앞으로 뛰는 러너(공격 방향 속도 > 3 m/s)면 lead 후보(0 제외)마다 조준점을 풀어 xPass가 가장 큰 것. 없으면 Valid = false.
        /// </summary>
        public PassOption EvaluateThrough(MatchContext ctx, int passer, int receiver, out float deltaXt, out float pressure)
        {
            pressure = PressureOn(ctx, passer);
            return EvaluateThrough(ctx, passer, receiver, pressure, out deltaXt);
        }

        public PassOption EvaluateThrough(MatchContext ctx, int passer, int receiver, float pressure, out float deltaXt)
        {
            Refresh(ctx);
            deltaXt = 0f;
            var best = default(PassOption);
            var team = ActionWorld.TeamOf(passer);
            var dir = World.AttackDir[team];
            var vr = World.Vel[receiver];
            if (vr.X * dir < P.LeadMinRunnerSpeed) return best;
            var from = ctx.Ball.Position.XY;
            for (int li = 1; li < 4; li++)
            {
                if (!PassModel.ThroughAim(P, _bp, World, 2, from, World.Pos[receiver], vr, dir, PassModel.Lead(P, li), out var aim, out _)) continue;
                if (aim.X < 1f || aim.X > World.PitchL - 1f || aim.Y < 1f || aim.Y > World.PitchW - 1f) continue;
                var o = PassModel.Best(P, _bp, World, passer, receiver, from, aim, pressure, PassModel.LoftMode.ThroughSpeed);
                if (!o.Valid) continue;
                if (!best.Valid || o.XPass > best.XPass)
                {
                    best = o;
                    deltaXt = World.Threat(P, team, aim) - World.Threat(P, team, from);
                }
            }
            return best;
        }

        /// <summary>평가 결과를 의도 공 동작으로 (조준점·초속·발사각·xPass). Velocity는 명목값(오차 없음).</summary>
        public static BallAction IntentAction(int kicker, int target, in PassOption o, Vec2 from, bool oneTouch)
        {
            var d = o.Aim - from;
            var len = d.Length();
            var u = len > 1e-5f ? d / len : new Vec2(1f, 0f);
            var c = DMath.Cos(o.Elevation);
            return new BallAction
            {
                Kind = BallActionKind.Pass, KickerSlot = kicker, TargetSlot = target,
                Velocity = new Vec3(u.X * o.Speed * c, u.Y * o.Speed * c, o.Speed * DMath.Sin(o.Elevation)),
                HasIntent = true, Aim = o.Aim, Speed = o.Speed, Elevation = o.Elevation, XPass = o.XPass, IsOneTouch = oneTouch,
            };
        }

        // ── 예약 킥 (FR-65·66) ──

        /// <summary>
        /// 단계 5 공 동작을 예약한다(물리 서브스텝 0 전). 이미 예약이 있거나 공을 가진 선수가 아니면(대체 태클은 상대가 가졌을 때) 무시하고 false.
        /// 킥 기술·발·준비 시간을 정하고, 준비 시간이 지난 첫 서브스텝을 Due로 둔다.
        /// </summary>
        public bool Schedule(MatchContext ctx, in BallAction a, long abs)
        {
            if (_pending.Active || a.Kind == BallActionKind.None) return false;
            ref readonly var ball = ref ctx.Ball;
            if (ball.Phase != BallPhase.Controlled) return false;
            var tackle = a.Kind == BallActionKind.Clearance && ball.OwnerId != a.KickerSlot;
            if (!tackle && ball.OwnerId != a.KickerSlot) return false;
            Refresh(ctx);
            ref readonly var me = ref ctx.SlotState(a.KickerSlot);
            var from = ball.Position.XY;
            var dir = a.HasIntent ? a.Aim - from : a.Velocity.XY;
            var len = dir.Length();
            dir = len > 1e-5f ? dir / len : me.Facing;
            var lofted = a.HasIntent ? a.Elevation > 0f : a.Velocity.Z > 1f;
            var dist = a.HasIntent ? len : 0f;
            var speed = a.HasIntent ? a.Speed : a.Velocity.Length();
            var plan = KickTechnique.Plan(P, me.Facing, dir, lofted, dist, speed, World.PreferredFoot[a.KickerSlot], World.WeakFoot[a.KickerSlot]);
            if (tackle) plan.Windup = P.WindupOutside; // 대체 태클(070 전): 짧은 찌르기
            var dt = _bp.Dt;
            var n = (long)DMath.Max(1f, DMath.Floor(plan.Windup / dt + 0.999f));
            _pending = new PendingKick
            {
                Active = true, Action = a, Victim = tackle ? ball.OwnerId : -1, Plan = plan,
                Due = abs + n, Expire = abs + n + (long)(P.KickTimeout / dt + 0.5f), Tick = ctx.Tick,
            };
            _pendingFlow = (byte)ctx.Flow.State;
            return true;
        }

        /// <summary>
        /// 서브스텝마다(공 적분 전): 예약 킥이 Due를 지났고 사용 발 도달 거리 안이면 실행한다. 그 사이 공을 잃었거나 경기 흐름이 바뀌었으면 취소,
        /// 도달 거리 밖이 Expire까지 이어지면 취소. 실행했으면 true.
        /// </summary>
        public bool OnSubstep(MatchContext ctx, long abs, int sub)
        {
            if (!_pending.Active || abs < _pending.Due) return false;
            ref var ball = ref ctx.Ball;
            ref readonly var a = ref _pending.Action;
            var tackle = _pending.Victim >= 0;
            var owner = ball.Phase == BallPhase.Controlled ? ball.OwnerId : -1;
            if ((byte)ctx.Flow.State != _pendingFlow || owner != (tackle ? _pending.Victim : a.KickerSlot) || !ctx.IsSlotActive(a.KickerSlot))
            {
                Cancel();
                return false;
            }
            var kp = ctx.SlotState(a.KickerSlot).Position;
            var reach = tackle ? ctx.Params.TackleRange : P.KickReach;
            var d2 = (ball.Position.XY - kp).LengthSq();
            if (d2 > reach * reach)
            {
                if (abs >= _pending.Expire) Cancel();
                return false;
            }
            Execute(ctx, abs, sub);
            return true;
        }

        void Cancel()
        {
            _pending = default;
            KicksCancelled++;
        }

        void Execute(MatchContext ctx, long abs, int sub)
        {
            var pk = _pending;
            _pending = default;
            KicksExecuted++;
            var a = pk.Action;
            ref var ball = ref ctx.Ball;
            var tackle = pk.Victim >= 0;
            var victim = pk.Victim;
            var team = Squad.TeamOfSlot(a.KickerSlot);
            var velocity = a.HasIntent ? IntentVelocity(ctx, in pk) : a.Velocity;
            var detail = -1;
            if (a.Kind == BallActionKind.Pass) detail = SnapshotOffside(ctx, a.KickerSlot, team);
            BallCommands.Kick(ref ball, velocity, a.Spin, ctx.BallParams, ctx.BallEvents);
            ctx.Keepers.NotifyFlight(); // 011: 골문을 향하는지 다음 감시에서 예측
            if (a.IsKickoff)
            {
                var ko = SimEvent.Make(SimEventKind.Kickoff, team, ctx.PlayerAtSlot(a.KickerSlot));
                ko.Substep = (sbyte)sub;
                ctx.SetPosition(ref ko, ball.Position);
                ctx.Emit(ko);
            }
            var kind = tackle ? SimEventKind.Tackle : a.Kind == BallActionKind.Shot ? SimEventKind.Shot : a.Kind == BallActionKind.Pass ? SimEventKind.Pass : SimEventKind.Clearance;
            var e = SimEvent.Make(kind, team, ctx.PlayerAtSlot(a.KickerSlot),
                tackle ? ctx.PlayerAtSlot(victim) : (a.TargetSlot >= 0 ? ctx.PlayerAtSlot(a.TargetSlot) : -1));
            e.Substep = (sbyte)sub;
            e.Part = tackle || !a.HasIntent ? (a.Part == BodyPart.None ? pk.Plan.Foot : a.Part) : pk.Plan.Foot;
            e.BallPhase = ball.Phase;
            e.F0 = velocity.Length();
            if (a.Kind == BallActionKind.Pass)
            {
                e.A = ActionOutcome.PassFlags(pk.Plan.Tech, a.IsOneTouch, pk.Plan.WeakFoot);
                e.F1 = a.XPass;
                e.B = detail + 1;
            }
            else e.A = a.IsPenalty ? 1 : 0;
            ctx.SetPosition(ref e, ball.Position);
            ctx.Emit(e);
            _flightTarget = a.Kind == BallActionKind.Pass ? a.TargetSlot : -1;
            ctx.LastKickSlot = a.KickerSlot;
            ctx.LastKickSubstep = abs;
            ctx.LastTackleVictim = tackle ? victim : -1;
            ctx.LastKickKind = (byte)a.Kind;
            ctx.LastTouchTeam = team;
            ctx.LastTouchSlot = a.KickerSlot;
            ctx.Recorder.MarkAction(a.KickerSlot, tackle ? MotionHint.Tackle : MotionHint.Kick);
            if (tackle)
            {
                ctx.Kinematics.ApplyContact(a.KickerSlot);
                if (victim >= 0 && victim < Squad.Slots && ctx.IsSlotActive(victim)) ctx.Kinematics.ApplyContact(victim);
            }
            else SlowAfterTouch(ctx, a.KickerSlot, abs);
            ResetAttempts();
            Diagnostics?.OnContact(ctx.Tick, sub, a.KickerSlot, kind, a.XPass);
            if (a.IsRestart) ctx.Flow.OnRestartTaken();
        }

        /// <summary>의도 패스의 실행 초속: 지금 공 위치에서 조준점으로 다시 겨누고(준비 시간 동안 공이 움직임) FR-13 오차를 넣는다. RNG 정확히 4.</summary>
        Vec3 IntentVelocity(MatchContext ctx, in PendingKick pk)
        {
            var a = pk.Action;
            var k = a.KickerSlot;
            var from = ctx.Ball.Position.XY;
            var d = a.Aim - from;
            var dist = d.Length();
            var dir = dist > 1e-5f ? d / dist : ctx.SlotState(k).Facing;
            Vec3 v;
            if (a.Elevation > 0f && dist > 0.5f)
            {
                var inv = BallInverse.LoftedSpeedFor(_bp, dist, a.Elevation, Vec3.Zero, P.LoftMaxApex, _scratch);
                var sp = inv.Status == BallInverseStatus.Unreachable ? a.Speed : inv.Speed;
                v = new Vec3(dir.X * sp * DMath.Cos(a.Elevation), dir.Y * sp * DMath.Cos(a.Elevation), sp * DMath.Sin(a.Elevation));
            }
            else v = new Vec3(dir.X * a.Speed, dir.Y * a.Speed, 0f);
            var pressure = PressureOn(ctx, k);
            var habit = PassExecution.HabitMul(P, a.IsOneTouch, World.OneTouchLevel[k]);
            var sa = PassExecution.SigmaAngleDeg(P, World.Passing[k], pressure, World.Composure[k], pk.Plan.SigmaMul, habit);
            var ss = PassExecution.SigmaSpeed(P, World.Passing[k], pk.Plan.SigmaMul, habit);
            return PassExecution.Apply(v, sa, ss, Rng, out _, out _);
        }

        /// <summary>FR-15: 패스 순간 22 슬롯 x·공 x·수비 팀 오프사이드 라인(뒤에서 두 번째 수비수 x). 판정은 012.</summary>
        int SnapshotOffside(MatchContext ctx, int passer, int team)
        {
            var def = 1 - team;
            var dir = ctx.AttackDir(team);
            float last = float.MinValue, second = float.MinValue;
            for (int s = 0; s < Squad.Slots; s++)
            {
                var active = ctx.IsSlotActive(s);
                var x = active ? ctx.SlotState(s).Position.X : float.NaN;
                _runnerX[s] = x;
                if (!active || Squad.TeamOfSlot(s) != def) continue;
                var depth = x * dir; // 공격 방향으로 깊을수록 큼 (수비 팀 골 쪽)
                if (depth > last) { second = last; last = depth; }
                else if (depth > second) second = depth;
            }
            var line = second == float.MinValue ? (dir > 0 ? ctx.Pitch.LengthM : 0f) : second * dir;
            return Details.AddOffside(ctx.Tick, passer, ctx.Ball.Position.X, line, _runnerX);
        }

        void SlowAfterTouch(MatchContext ctx, int slot, long abs)
        {
            if (P.AfterDuration <= 0f) return;
            var cap = ctx.Kinematics.Profile(slot).MaxSpeed * P.AfterKickSlow;
            ctx.Kinematics.SetSpeedCap(slot, cap, abs + (long)(P.AfterDuration / _bp.Dt + 0.5f));
        }

        void ResetAttempts() => System.Array.Clear(_attempted, 0, _attempted.Length);

        // ── 수신 · 인터셉트 (FR-45·67·70) ──

        /// <summary>
        /// 느슨한 공 판정 (011 이전 <c>PhysicsStage.TryClaim</c> 대체). 선수 id 순으로, 공이 컨트롤 반경 안에서 가장 가까워지는 순간(멀어지기 시작·느린 공) 1회
        /// <see cref="BallControl.Prob"/>을 액션 RNG 1개로 굴리고 첫 성공자가 잡는다(같은 서브스텝 두 번째 이후는 판정하지 않음). 잡으면 첫 터치(RNG 3)로
        /// 이격을 정해 가까우면 컨트롤, 멀면 공을 그만큼 굴려 보낸다. GK가 자기 박스에서 손을 쓰면 첫 터치 없이 잡는다(011).
        /// </summary>
        public void Resolve(MatchContext ctx, long abs, int sub)
        {
            ref var ball = ref ctx.Ball;
            if (!ctx.Flow.InPlay || ball.Phase == BallPhase.Controlled) return;
            var pos = ball.Position;
            var rb = ctx.BallParams.Radius;
            // 공 전체가 라인 밖일 때만 잡기 금지 (라인 위 공은 인플레이 — 011 발견)
            if (pos.X < -rb || pos.X > ctx.Pitch.LengthM + rb || pos.Y < -rb || pos.Y > ctx.Pitch.WidthM + rb) return;
            for (int i = 0; i < ctx.BallEvents.Count; i++)
                if (ctx.BallEvents[i].Kind == BallEventKind.GoalFrameHit) { ResetAttempts(); break; } // 골대에 꺾인 공은 새 경로
            var speed = ball.Velocity.Length();
            var k = ctx.Kinematics;
            var lastKick = (BallActionKind)ctx.LastKickKind;
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (!ctx.IsSlotControllable(s)) continue;
                if ((s == ctx.LastKickSlot || s == ctx.LastTackleVictim) && abs - ctx.LastKickSubstep < 25) continue;
                if (abs < _touchCooldown[s]) continue;
                var isKeeper = Squad.IsKeeperSlot(s);
                if (isKeeper && ctx.Keepers.ExcludedFromClaim(s)) continue;
                var hands = isKeeper && Match.Stages.PhysicsStage.InOwnBox(ctx, Squad.TeamOfSlot(s), pos);
                if (hands && pos.Z > ctx.Params.ClaimMaxZ) hands = false;
                ref readonly var st = ref k.State(s);
                var rel = pos.XY - st.Position;
                var d = rel.Length();
                var radius = P.CtrlRadius * (hands ? P.CtrlKeeperMul : 1f);
                if (d > radius) { _attempted[s] = false; continue; }
                if (_attempted[s] && speed >= P.CtrlFreeSpeed) continue; // 느린 공(확률 1)은 판정 표시와 무관 — 놓친 공이 반경 안에서 멈추면 영영 안 잡히는 버그 방지
                if (!hands && pos.Z > P.CtrlMaxZ) continue; // 머리 위로 지나는 공: 판정 기회를 쓰지 않는다 (확률 0 — 결정론 리뷰)
                // 최근접 순간: 상대 속도가 멀어지는 쪽(또는 접선)이거나 느린 공
                var rv = ball.Velocity.XY - st.Velocity;
                if (speed >= P.CtrlFreeSpeed && Vec2.Dot(rel, rv) < 0f) continue;
                _attempted[s] = true;
                var team = Squad.TeamOfSlot(s);
                var opp = team != ctx.LastTouchTeam && lastKick == BallActionKind.Pass;
                var pressure = PressureOn(ctx, s);
                var prob = BallControl.Prob(P, d, speed, pos.Z, World.FirstTouch[s], pressure, opp, hands);
                ControlChecks++;
                if (!(Rng.NextFloat() < prob)) continue;
                Claim(ctx, s, abs, sub, speed, hands, pressure);
                return;
            }
        }

        void Claim(MatchContext ctx, int s, long abs, int sub, float speed, bool hands, float pressure)
        {
            ref var ball = ref ctx.Ball;
            var pos = ball.Position;
            var ballDir = ball.Velocity.XY;
            var team = Squad.TeamOfSlot(s);
            var prevTeam = ctx.LastTouchTeam;
            var prevKicker = ctx.LastKickSlot;
            var kickKind = (BallActionKind)ctx.LastKickKind;
            var kind = ActionOutcome.Reception(team, prevTeam, kickKind, prevKicker, s, speed);
            var touch = default(FirstTouchResult);
            var loose = false;
            if (!hands)
            {
                touch = FirstTouchModel.Sample(P, World.FirstTouch[s], speed, pressure, ballDir, Rng);
                FirstTouches++;
                loose = touch.Distance > P.TouchControlDist;
            }
            BallCommands.TakeControl(ref ball, s, pos, ctx.BallEvents);
            if (loose)
            {
                // 이격: 공을 그 거리만큼 굴려 보낸다 (느슨한 공, 수신자 짧은 쿨다운)
                var inv = BallInverse.GroundSpeedFor(ctx.BallParams, touch.Distance);
                var v = inv.Status == BallInverseStatus.Ok ? inv.Speed : 1f;
                BallCommands.Kick(ref ball, new Vec3(touch.Direction.X * v, touch.Direction.Y * v, 0f), Vec3.Zero, ctx.BallParams, ctx.BallEvents);
                _touchCooldown[s] = abs + (long)(P.TouchCooldown / ctx.BallParams.Dt + 0.5f);
            }
            var e = SimEvent.Make(kind, team, ctx.PlayerAtSlot(s), prevKicker >= 0 ? ctx.PlayerAtSlot(prevKicker) : -1);
            e.Substep = (sbyte)sub;
            e.Part = hands ? BodyPart.Hand : Match.Stages.PhysicsStage.PartFor(ctx.SlotState(s), pos);
            e.BallPhase = ball.Phase;
            e.F0 = speed;
            e.F1 = touch.Distance;
            e.B = ActionOutcome.ReceptionFlags(touch.Heavy, TouchKind.Deliberate);
            ctx.SetPosition(ref e, pos);
            ctx.Emit(e);
            if (team != prevTeam)
            {
                var pc = SimEvent.Make(SimEventKind.PossessionChange, team, ctx.PlayerAtSlot(s));
                pc.Substep = (sbyte)sub;
                ctx.SetPosition(ref pc, pos);
                ctx.Emit(pc);
            }
            ctx.LastTouchTeam = team;
            ctx.LastTouchSlot = s;
            ctx.LastKickKind = 0;
            _flightTarget = -1;
            ctx.PossessionStartTick = ctx.Tick;
            if (ctx.Flow.PenaltyKickTick >= 0) ctx.Flow.ClearPenalty();
            if (!hands) SlowAfterTouch(ctx, s, abs);
            ResetAttempts();
            Diagnostics?.OnContact(ctx.Tick, sub, s, kind, touch.Distance);
        }

        // ── 발행 뒤 ──

        /// <summary>이번 틱 발행분(로그 from 이후)의 패스 이벤트 B로 상세 기록에 이벤트 Seq를 연결한다 (FR-72).</summary>
        public void AfterPublish(SimEventLog log, int from)
        {
            for (int i = from; i < log.Count; i++)
            {
                ref readonly var e = ref log[i];
                if (e.Kind == SimEventKind.Pass && e.B > 0) Details.Bind(e.B - 1, e.Seq);
            }
        }

        // ── 해시 · 스냅샷 ──

        public void AddTo(ref StableHasher h)
        {
            var r = Rng.SaveState(); h.Add(r.S0); h.Add(r.S1); h.Add(r.S2); h.Add(r.S3);
            _pending.AddTo(ref h);
            h.Add(_pendingFlow);
            h.Add(_flightTarget);
            for (int s = 0; s < ActionWorld.N; s++) { h.Add(_attempted[s]); h.Add(_touchCooldown[s]); }
            Details.AddTo(ref h);
        }

        public void Write(StateWriter w)
        {
            w.Write(Rng.SaveState());
            _pending.Write(w);
            w.Write(_pendingFlow);
            w.Write(_flightTarget);
            for (int s = 0; s < ActionWorld.N; s++) { w.Write(_attempted[s]); w.Write(_touchCooldown[s]); }
            Details.Write(w);
        }

        public void Read(StateReader r)
        {
            Rng.RestoreState(r.ReadRngState());
            _pending = PendingKick.Read(r);
            _pendingFlow = r.ReadByte();
            _flightTarget = r.ReadInt();
            for (int s = 0; s < ActionWorld.N; s++) { _attempted[s] = r.ReadBool(); _touchCooldown[s] = r.ReadLong(); }
            Details.Read(r);
            _worldTick = long.MinValue;
        }
    }
}
