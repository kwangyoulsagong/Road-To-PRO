using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Referee;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>
    /// 단계 8 심판 (specs/012). 순서: 예정 사건 스크립트 → 이번 틱 사건(이벤트 + 물리 경계)을 발생 순서대로 →
    /// 어드밴티지 보류 갱신 → GK 보유 → 보류 카드 집행 → 7명 미만 → 심판 이동.
    /// 사건마다: 진짜 판정(RNG 없음) → 심판 인지(<see cref="MatchContext.RefereeRng"/>) → 어드밴티지 → 휘슬·카드·재시작 → VAR(0틱, 정지 시간만).
    /// 휘슬로 데드볼이 되면 그 틱의 나머지 사건은 판정하지 않는다. 할당 없음(고정 배열).
    /// </summary>
    public sealed class RefereeStage : IMatchStage
    {
        const int Tps = (int)SimClock.TicksPerSecond;
        const int MaxItems = EventPublisher.MaxPendingPerTick;

        readonly RefereeParams _p;
        readonly RefereeProfile _profile;
        readonly RuleSet _rules;
        readonly bool _var;
        readonly RefereeState _s = new RefereeState();
        readonly RefereeWorld _w = new RefereeWorld();
        readonly int[] _order = new int[MaxItems];
        readonly float[] _runnerX = new float[RefereeWorld.N];
        readonly float[] _speed = new float[RefereeWorld.N];
        readonly float[] _front = new float[RefereeWorld.N];
        readonly Vec2[] _pos = new Vec2[RefereeWorld.N];

        public StageId Id => StageId.Referee;
        public RefereeState State => _s;
        public RefereeParams Params => _p;
        public RefereeProfile Profile => _profile;
        /// <summary>판정 누계 (측정용, 해시 밖).</summary>
        public readonly RefereeTelemetry Telemetry = new RefereeTelemetry();
        /// <summary>FR-97 진단 (널이면 호출 0).</summary>
        public IRefereeDiagnostics Diagnostics { get; set; }

        public RefereeStage(MatchSetup setup, MatchTuning tuning)
        {
            _p = RefereeParams.Create(tuning?.Match);
            _profile = setup.EffectiveReferee;
            _rules = setup.EffectiveRuleSet;
            _var = setup.VarEnabled;
        }

        /// <summary>경기 생성 때 한 번: 심판 체력 → 이동 속도 (FR-80).</summary>
        public void ConfigureOfficials(Officials o)
        {
            var v = OfficialsMotion.MaxSpeed(_p, _profile.Fitness);
            for (int i = 0; i < Officials.Count; i++) o.SetMaxSpeed(i, v);
        }

        // ───────────────────────── 실행 ─────────────────────────

        public void Execute(MatchContext ctx)
        {
            RefereeCommon.RunScript(ctx);
            _w.Fill(ctx);
            _w.Pressure = Pressure(ctx);
            var n = ctx.Publisher.Pending;
            SortPending(ctx, n);
            var board = ctx.Board;
            var calls = board.PhysicsCallCount;
            int ei = 0, ci = 0;
            var stopped = false;
            while (ei < n || ci < calls)
            {
                // 물리 경계는 같은 서브스텝 이벤트 뒤
                var takeCall = ci < calls && (ei >= n || ctx.Publisher.PendingAt(_order[ei]).Substep > board.PhysicsCallAt(ci).Substep);
                if (takeCall)
                {
                    ref readonly var c = ref board.PhysicsCallAt(ci++);
                    if (c.Kind == PhysicsCallKind.WoodworkHit)
                    {
                        if (stopped) continue; // 대체 단계와 같게: 첫 경계 뒤 골대 사건은 내지 않는다 (결정론 리뷰 F4)
                        RefereeCommon.EmitWoodwork(ctx, in c);
                        _s.Offside.OnRebound();
                        continue;
                    }
                    if (stopped || !ctx.Flow.InPlay) continue;
                    Boundary(ctx, in c);
                    stopped = true;
                    continue;
                }
                ref readonly var e = ref ctx.Publisher.PendingAt(_order[ei++]);
                if (stopped || !ctx.Flow.InPlay) continue;
                OnEvent(ctx, in e);
                if (!ctx.Flow.InPlay) stopped = true;
            }
            UpdateAdvantage(ctx);
            KeeperHold(ctx);
            if (!ctx.Flow.InPlay) OnDeadBall(ctx);
            _s.BelowReported = RefereeCommon.CheckMinimum(ctx, _s.BelowReported);
            MoveOfficials(ctx);
            _s.PrevRestart = ctx.Flow.State == FlowState.DeadBall ? ctx.Flow.Restart : RestartKind.None;
        }

        float Pressure(MatchContext ctx)
        {
            var imp = ctx.Setup.Importance - 1f;
            var p = imp > 0f ? imp * _p.PressureImportanceK : 0f;
            var flow = ctx.Flow;
            if (flow.Period >= 1 && flow.Elapsed > flow.NominalTicks - (long)(_p.PressureLateMinutes * SimClock.TicksPerMinute)) p += _p.PressureLateAdd;
            return p > 1f ? 1f : p;
        }

        void SortPending(MatchContext ctx, int n)
        {
            var pub = ctx.Publisher;
            for (int i = 0; i < n; i++)
            {
                var x = i;
                int j = i - 1;
                while (j >= 0 && SimEvent.CompareKey(in pub.PendingAt(_order[j]), in pub.PendingAt(x)) > 0)
                {
                    _order[j + 1] = _order[j];
                    j--;
                }
                _order[j + 1] = x;
            }
        }

        static int SlotOf(MatchContext ctx, int id)
        {
            if (id < 0 || !ctx.Squad.Has(id) || ctx.Squad.StatusOf(id) != PlayerStatus.OnPitch) return -1;
            return ctx.Squad.SlotOf(id);
        }

        Vec2 Meters(MatchContext ctx, in SimEvent e) => new Vec2(e.X * ctx.Pitch.LengthM, e.Y * ctx.Pitch.WidthM);

        // ───────────────────────── 이벤트 ─────────────────────────

        void OnEvent(MatchContext ctx, in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.PossessionChange:
                    if (e.Team != _s.PhaseTeam) EndPhase(ctx, e.Substep);
                    _s.PhaseTeam = e.Team;
                    break;
                case SimEventKind.Pass:
                case SimEventKind.Cross:
                    OnPass(ctx, in e);
                    break;
                case SimEventKind.Shot:
                    OnShot(ctx, in e);
                    break;
                case SimEventKind.PassReceived:
                    Touch(ctx, in e, ((e.B >> 1) & 3) == 0 ? OffsideTouch.Deliberate : OffsideTouch.Deflection);
                    break;
                case SimEventKind.Interception:
                case SimEventKind.BallRecovered:
                case SimEventKind.Clearance:
                case SimEventKind.Header:
                    Touch(ctx, in e, OffsideTouch.Deliberate);
                    break;
                case SimEventKind.Tackle:
                    if ((e.A & EventCodes.TackleWon) != 0) Touch(ctx, in e, OffsideTouch.Deliberate);
                    break;
                case SimEventKind.Block:
                    Touch(ctx, in e, OffsideTouch.Deflection);
                    break;
                case SimEventKind.Save:
                case SimEventKind.Parry:
                case SimEventKind.Punch:
                    Touch(ctx, in e, OffsideTouch.Save);
                    break;
                case SimEventKind.Catch:
                    OnCatch(ctx, in e);
                    if (ctx.Flow.InPlay) Touch(ctx, in e, OffsideTouch.Save);
                    break;
                case SimEventKind.Contact:
                    OnContact(ctx, in e);
                    break;
                case SimEventKind.DiveAttempt:
                    OnDive(ctx, in e);
                    break;
            }
        }

        // ── 오프사이드 ──

        void OnPass(MatchContext ctx, in SimEvent e)
        {
            var passer = SlotOf(ctx, e.PlayerId);
            if (passer < 0) return;
            // 패스도 패서의 터치 — 이전 단계 정리
            if (_s.Offside.Open) { Touch(ctx, passer, OffsideTouch.Deliberate, Meters(ctx, in e), e.Substep); if (!ctx.Flow.InPlay) return; }
            var details = ctx.Actions.Details;
            var exempt = OffsideJudge.ExemptRestart(_s.PrevRestart);
            _s.PrevRestart = RestartKind.None; // 재시작 킥은 이번 틱 첫 패스 하나
            var team = Squad.TeamOfSlot(passer);
            if (e.B > 0 && details.IsAvailable(e.B - 1) && details.KindOf(e.B - 1) == ActionDetailLog.KindOffsideSnapshot)
            {
                var k = e.B - 1;
                for (int s = 0; s < RefereeWorld.N; s++) { _runnerX[s] = details.RunnerX(k, s); _speed[s] = _w.Active[s] ? _w.Vel[s].Length() : 0f; }
                _s.Offside.Snapshot(_p, ctx.Tick, team, passer, details.BallX(k), _runnerX, _speed, ctx.AttackDir(team), ctx.Pitch.LengthM, exempt);
                Diag(ctx, team);
            }
            else SnapshotNow(ctx, team, passer, e.X * ctx.Pitch.LengthM, e.Substep, exempt);
            _s.LastPassTeam = team; _s.LastPasser = passer; _s.LastPassHeaded = e.Part == BodyPart.Head; // FR-64 백패스 입력
        }

        /// <summary>스냅샷 상세가 없으면(슛·헤딩 슛) 위치를 서브스텝 시각으로 되돌려 쓴다: 위치 − 속도 × 남은 서브스텝 시간.</summary>
        void SnapshotNow(MatchContext ctx, int team, int kicker, float ballX, sbyte substep, bool exempt)
        {
            var back = substep < 0 ? 0f : (SimClock.SubstepsPerDecision - 1 - substep) * SimClock.PhysicsDt;
            if (back < 0f) back = 0f;
            for (int s = 0; s < RefereeWorld.N; s++)
            {
                if (!_w.Active[s]) { _runnerX[s] = float.NaN; _speed[s] = 0f; continue; }
                _runnerX[s] = _w.Pos[s].X - _w.Vel[s].X * back;
                _speed[s] = _w.Vel[s].Length();
            }
            _s.Offside.Snapshot(_p, ctx.Tick, team, kicker, ballX, _runnerX, _speed, ctx.AttackDir(team), ctx.Pitch.LengthM, exempt);
            Diag(ctx, team);
        }

        void Diag(MatchContext ctx, int team)
        {
            if (Diagnostics == null) return;
            for (int s = 0; s < RefereeWorld.N; s++)
                _front[s] = _s.Offside.IsCandidate(s) ? OffsideJudge.FrontX(_runnerX[s], ctx.AttackDir(team), _p.OffsideBodyReach) : float.NaN;
            Diagnostics.OnOffsideSnapshot(ctx.Tick, team, _s.Offside.LineX, _front);
        }

        void OnShot(MatchContext ctx, in SimEvent e)
        {
            var shooter = SlotOf(ctx, e.PlayerId);
            if (shooter < 0) return;
            var team = Squad.TeamOfSlot(shooter);
            var from = Meters(ctx, in e);
            // OFF-09: 이전 패스 스냅샷의 후보가 GK 시야를 가림
            for (int s = 0; s < RefereeWorld.N; s++) _pos[s] = _w.Pos[s];
            var gk = _w.KeeperSlot(1 - team);
            if (_s.Offside.TrySightBlock(_p, shooter, from, ctx.OpponentGoal(team), _w.Active[gk] ? _w.Pos[gk] : ctx.OpponentGoal(team), _pos, _w.Active, out var o))
            {
                Offside(ctx, in o, e.Substep);
                if (!ctx.Flow.InPlay) return;
            }
            else if (_s.Offside.Open && Squad.TeamOfSlot(shooter) == _s.Offside.Team && _s.Offside.IsCandidate(shooter))
            {
                Touch(ctx, shooter, OffsideTouch.Deliberate, from, e.Substep);
                if (!ctx.Flow.InPlay) return;
            }
            // OFF-07: 리바운드 판정 기준 = 슛 순간
            SnapshotNow(ctx, team, shooter, from.X, e.Substep, false);
        }

        void Touch(MatchContext ctx, in SimEvent e, OffsideTouch kind)
        {
            _s.LastPassTeam = -1; // 패스 뒤 다른 터치 → 백패스 아님
            var slot = SlotOf(ctx, e.PlayerId);
            if (slot < 0) return;
            Touch(ctx, slot, kind, Meters(ctx, in e), e.Substep);
        }

        void Touch(MatchContext ctx, int slot, OffsideTouch kind, Vec2 at, sbyte substep)
        {
            if (!_s.Offside.Open) return;
            var team = Squad.TeamOfSlot(slot);
            if (team != _s.Offside.Team)
            {
                // ② 상대 관여: 상대가 공을 플레이하는 순간 가까운 후보
                for (int s = 0; s < RefereeWorld.N; s++) _pos[s] = _w.Pos[s];
                if (_s.Offside.TryInterfere(_p, at, _pos, _w.Active, out var oi)) { Offside(ctx, in oi, substep); if (!ctx.Flow.InPlay) return; }
            }
            if (_s.Offside.TryTouch(slot, kind, out var o)) Offside(ctx, in o, substep);
        }

        void Offside(MatchContext ctx, in OffsideOffence o, sbyte substep)
        {
            Telemetry.OffsideInvolvements++;
            var align = Perception.AssistantAlign(_w, o.LineX);
            var perceived = Perception.AssistantPerceive(_p, o.D, align, o.Speed, ctx.RefereeRng);
            var truth = o.D > 0f;
            var flag = perceived > 0f;
            var tight = DMath.Abs(o.D) < _p.TightMargin;
            Telemetry.Major(flag == truth);
            var team = Squad.TeamOfSlot(o.Slot);
            var spot = _w.Active[o.Slot] ? _w.Pos[o.Slot] : _w.Ball.XY;
            if (!flag)
            {
                if (truth) _s.Phase.Add(new PhaseIncident { Kind = PhaseIncidentKind.MissedOffside, Tick = ctx.Tick, AttackTeam = team, Offender = o.Slot, OffenderId = ctx.PlayerAtSlot(o.Slot), Spot = spot, TrueValue = o.D, Perceived = perceived, Tight = tight });
                return;
            }
            if (DMath.Abs(o.D) <= _p.LateFlagMargin && !_s.Held.Active)
            {
                _s.Held = new HeldFlag { Active = true, Team = team, Slot = o.Slot, PlayerId = ctx.PlayerAtSlot(o.Slot), Type = o.Type, D = o.D, Perceived = perceived, Spot = spot, Tick = ctx.Tick };
                return;
            }
            RaiseOffside(ctx, team, o.Slot, ctx.PlayerAtSlot(o.Slot), o.Type, o.D, perceived, spot, substep, false, false);
        }

        void RaiseOffside(MatchContext ctx, int team, int slot, int playerId, byte type, float d, float perceived, Vec2 spot, sbyte substep, bool late, bool varCorrected)
        {
            Telemetry.Offsides++;
            if (d <= 0f) Telemetry.AssistantErrors++;
            var e = SimEvent.Make(SimEventKind.Offside, team, playerId);
            e.Substep = substep;
            e.A = type;
            e.B = (DMath.Abs(d) < _p.TightMargin ? EventCodes.OffTight : 0) | (late ? EventCodes.OffLateFlag : 0) | (d <= 0f ? EventCodes.OffAssistantError : 0) | (varCorrected ? EventCodes.OffVarCorrected : 0);
            e.F0 = d;
            e.F1 = perceived;
            ctx.SetPosition(ref e, spot);
            ctx.Emit(e);
            if (!ctx.Flow.InPlay) return;
            var def = 1 - team;
            var dec = RestartRules.Foul(spot, def, ctx.OpponentGoal(def), ctx.AttackDir(def), false, ctx.Pitch.LengthM, ctx.Pitch.WidthM);
            ResolveAdvantageOnStop(ctx);
            RefereeCommon.Restart(ctx, in dec, substep, true);
        }

        /// <summary>공격 단계 끝 (점유 변경·데드볼): 늦은 깃발 올림, 놓친 PK·퇴장 VAR(다음 정지 근사).</summary>
        void EndPhase(MatchContext ctx, sbyte substep)
        {
            if (_s.Held.Active && ctx.Flow.InPlay)
            {
                var h = _s.Held;
                _s.Held = default;
                RaiseOffside(ctx, h.Team, h.Slot, h.PlayerId, h.Type, h.D, h.Perceived, h.Spot, substep, true, false);
            }
            if (ctx.Flow.InPlay) ReviewMissed(ctx, substep);
            _s.Phase.Clear();
            _s.Offside.Close();
        }

        // ── 파울 ──

        void OnContact(MatchContext ctx, in SimEvent e)
        {
            if (e.B <= 0) return;
            var log = ctx.Actions.Details.Contacts;
            var k = e.B - 1;
            if (!log.IsAvailable(k)) return;
            var c = log[k];
            Telemetry.Contacts++;
            if (c.Defender < 0 || c.Attacker < 0 || !_w.Active[c.Defender] || !_w.Active[c.Attacker]) return; // 결정론 리뷰 F1
            if (c.Kind == ContactKind.Arm) { OnHandball(ctx, in c); return; }
            var offender = c.Defender;
            var victim = c.Attacker;
            var victimTeam = Squad.TeamOfSlot(victim);
            var at = new Vec2(c.X, c.Y);
            var score = FoulJudge.Score(_p, in c);
            var trueDec = FoulJudge.Decide(score, FoulJudge.Base(_p));
            var goalCos = DogsoJudge.GoalCos(_w.Pos[victim], _w.Vel[victim], _w.Facing[victim], ctx.OpponentGoal(victimTeam));
            var kind = DogsoJudge.Classify(_p, in c, goalCos);
            var genuine = DogsoJudge.GenuineChallenge(in c);
            var trueFinal = DogsoJudge.Apply(trueDec, kind, c.InBox, genuine, false, _rules, out _);
            if (c.Kind == ContactKind.Tackle)
            {
                if (c.Sliding) Telemetry.SlidingTackles++; else Telemetry.StandingTackles++;
            }
            if (trueDec != EventCodes.DecisionNone) Telemetry.TrueFouls++;
            var sight = Perception.SightQuality(_p, _w, at, offender, victim, _w.Vel[victim]);
            _s.LastSight = sight;
            var homeVictim = victimTeam == _w.HomeTeam;
            var perceived = Perception.Perceive(_p, _profile, score, sight, homeVictim, _w.Pressure, ctx.RefereeRng);
            var offenderId = ctx.PlayerAtSlot(offender);
            var th = FoulJudge.ForReferee(_p, _profile, _s.Discipline.Fouls(offenderId));
            var refDec = FoulJudge.Decide(perceived, th);
            var refFinal = DogsoJudge.Apply(refDec, kind, c.InBox, genuine, false, _rules, out var reason);
            if (trueDec != EventCodes.DecisionNone || refDec != EventCodes.DecisionNone) Telemetry.Major(refFinal == trueFinal);
            if (refDec == EventCodes.DecisionNone)
            {
                if (trueDec == EventCodes.DecisionNone) return;
                var inc = new PhaseIncident { Tick = ctx.Tick, Offender = offender, OffenderId = offenderId, Victim = victim, VictimId = ctx.PlayerAtSlot(victim), Spot = at, TrueValue = score, Perceived = perceived, TrueDecision = (byte)trueFinal };
                inc.Kind = PhaseIncidentKind.MissedAttackFoul; inc.AttackTeam = Squad.TeamOfSlot(offender);
                _s.Phase.Add(inc);
                if (c.InBox) { inc.Kind = PhaseIncidentKind.MissedPenaltyFoul; inc.AttackTeam = victimTeam; _s.Phase.Add(inc); }
                if (trueFinal == EventCodes.DecisionRed) { inc.Kind = PhaseIncidentKind.MissedRed; inc.AttackTeam = victimTeam; _s.Phase.Add(inc); }
                return;
            }
            Telemetry.Whistles++;
            if (c.Kind == ContactKind.Tackle)
            {
                if (c.Sliding) Telemetry.SlidingTackleFouls++; else Telemetry.StandingTackleFouls++;
            }
            _s.Discipline.AddFoul(offenderId);
            var flags = (refFinal != trueFinal ? EventCodes.RefError : 0) | (c.InBox ? EventCodes.RefInBox : 0) | (kind == OpportunityKind.Dogso ? EventCodes.RefDogso : 0)
                | (kind == OpportunityKind.Spa ? EventCodes.RefSpa : 0) | (c.Sliding ? EventCodes.RefSliding : 0) | (homeVictim ? EventCodes.RefHomeVictim : 0);
            // 어드밴티지 (FR-40): 반칙당한 팀이 공을 갖고 있고 이득 점수가 기준 초과
            var kept = ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId >= 0 && Squad.TeamOfSlot(ctx.Ball.OwnerId) == victimTeam;
            var adv = AdvantageQueue.Score(_p, kept, (_w.Ball.XY - ctx.OpponentGoal(victimTeam)).Length());
            var pending = new PendingFoul
            {
                Tick = ctx.Tick, Offender = offender, Victim = victim, OffenderId = offenderId, VictimId = ctx.PlayerAtSlot(victim), VictimTeam = victimTeam, Spot = at,
                Decision = (byte)refDec, FinalDecision = (byte)refFinal, Reason = (byte)reason, InBox = c.InBox, Dogso = kind != OpportunityKind.None, Score = score,
            };
            if (AdvantageQueue.ShouldPlay(_profile, adv) && _s.Advantage.IndexOfOffender(offenderId) < 0 && _s.Advantage.Add(in pending))
            {
                Telemetry.Advantages++;
                EmitFoul(ctx, SimEventKind.Foul, offender, victim, refFinal, trueFinal, flags | EventCodes.RefAdvantage, score, sight, at, c.Substep);
                var a = SimEvent.Make(SimEventKind.AdvantagePlayed, victimTeam, offenderId, pending.VictimId);
                a.Substep = c.Substep;
                a.F0 = adv;
                ctx.SetPosition(ref a, at);
                ctx.Emit(a);
                return;
            }
            // FR-42: 보류 중 같은 선수 두 번째 반칙 → 보류 반칙 카드도 같이
            var prev = _s.Advantage.IndexOfOffender(offenderId);
            if (prev >= 0)
            {
                var pf = _s.Advantage[prev];
                _s.Advantage.RemoveAt(prev);
                EmitAdvantageEnd(ctx, in pf, 2);
                if (pf.FinalDecision >= EventCodes.DecisionYellow) Card(ctx, pf.OffenderId, pf.FinalDecision == EventCodes.DecisionRed ? EventCodes.CardRed : EventCodes.CardYellow, pf.Reason);
            }
            WhistleFoul(ctx, in c, offender, victim, refFinal, trueFinal, flags, reason, score, perceived, sight, at, true);
        }

        void EmitFoul(MatchContext ctx, SimEventKind kind, int offender, int victim, int given, int truth, int flags, float score, float sight, Vec2 at, sbyte substep)
        {
            var f = SimEvent.Make(kind, Squad.TeamOfSlot(offender), ctx.PlayerAtSlot(offender), victim >= 0 ? ctx.PlayerAtSlot(victim) : -1);
            f.Substep = substep;
            f.A = given * 10 + truth;
            f.B = flags | ((given >= EventCodes.DecisionRed || truth >= EventCodes.DecisionRed || (flags & EventCodes.RefInBox) != 0) ? EventCodes.RefVarReviewable : 0);
            f.F0 = score;
            f.F1 = sight;
            ctx.SetPosition(ref f, at);
            ctx.Emit(f);
        }

        /// <summary>휘슬 반칙: VAR(PK·퇴장) → 이벤트 → 카드 → 재시작.</summary>
        void WhistleFoul(MatchContext ctx, in ContactDetail c, int offender, int victim, int refFinal, int trueFinal, int flags, int reason, float score, float perceived, float sight, Vec2 at, bool direct)
        {
            var victimTeam = Squad.TeamOfSlot(victim);
            var dec = RestartRules.Foul(at, victimTeam, ctx.OpponentGoal(victimTeam), ctx.AttackDir(victimTeam), direct, ctx.Pitch.LengthM, ctx.Pitch.WidthM);
            var given = refFinal;
            var noFoul = false;
            if (_var)
            {
                // PK 사안: 반칙 없음인데 PK
                if (dec.Kind == RestartKind.Penalty && trueFinal == EventCodes.DecisionNone)
                {
                    var err = VarReview.ClearError(_p, perceived, score, given, trueFinal);
                    if (VarCheck(ctx, EventCodes.VarPenaltyIncident, false, false, err, DMath.Abs(perceived - score), c.Substep))
                    {
                        VarOverturnEvent(ctx, victimTeam, EventCodes.VarResultPenaltyOverturned, c.Substep);
                        noFoul = true;
                    }
                }
                // 직접 퇴장 사안
                if (!noFoul && (given == EventCodes.DecisionRed || trueFinal == EventCodes.DecisionRed) && given != trueFinal)
                {
                    var err = VarReview.ClearError(_p, perceived, score, given, trueFinal);
                    if (VarCheck(ctx, EventCodes.VarRedIncident, false, false, err, DMath.Abs(perceived - score), c.Substep))
                    {
                        VarOverturnEvent(ctx, victimTeam, given == EventCodes.DecisionRed ? EventCodes.VarResultRedDowngrade : EventCodes.VarResultRedUpgrade, c.Substep);
                        given = trueFinal;
                        if (given == EventCodes.DecisionRed) reason = EventCodes.ReasonSeriousFoulPlay;
                    }
                }
            }
            if (noFoul)
            {
                ResolveAdvantageOnStop(ctx);
                var drop = RestartRules.DropBall(at, Squad.TeamOfSlot(offender), ctx.AttackDir(0), ctx.Pitch.LengthM, ctx.Pitch.WidthM);
                ctx.Flow.BeginRestart(drop.Kind, drop.Team, drop.Spot);
                return;
            }
            if (given != trueFinal) Telemetry.ErrorsAfterVar++;
            EmitFoul(ctx, SimEventKind.Foul, offender, victim, given, trueFinal, flags, score, sight, at, c.Substep);
            if (given >= EventCodes.DecisionYellow) Card(ctx, ctx.PlayerAtSlot(offender), given == EventCodes.DecisionRed ? EventCodes.CardRed : EventCodes.CardYellow, reason);
            if (dec.Kind == RestartKind.Penalty) Telemetry.PenaltiesAwarded++;
            ResolveAdvantageOnStop(ctx);
            if (ctx.Flow.InPlay) RefereeCommon.Restart(ctx, in dec, c.Substep, true);
        }

        void Card(MatchContext ctx, int playerId, int color, int reason)
        {
            var outcome = _s.Discipline.Book(playerId, color);
            if (outcome == CardOutcome.Ignored) return;
            var slot = SlotOf(ctx, playerId);
            var team = slot >= 0 ? Squad.TeamOfSlot(slot) : (ctx.Squad.Has(playerId) ? ctx.Squad.TeamOf(playerId) : -1);
            var e = SimEvent.Make(SimEventKind.Card, team, playerId);
            e.Substep = SimEvent.TickEnd;
            e.A = color;
            e.B = reason;
            if (slot >= 0) ctx.SetPosition(ref e, ctx.SlotState(slot).Position);
            ctx.Emit(e);
            if (outcome == CardOutcome.Yellow) Telemetry.Yellows++;
            if (outcome == CardOutcome.SecondYellow)
            {
                Telemetry.Yellows++;
                Telemetry.SecondYellows++;
                var red = SimEvent.Make(SimEventKind.Card, team, playerId);
                red.Substep = SimEvent.TickEnd;
                red.A = EventCodes.CardRed;
                red.B = EventCodes.ReasonSecondYellow;
                if (slot >= 0) ctx.SetPosition(ref red, ctx.SlotState(slot).Position);
                ctx.Emit(red);
            }
            if (outcome == CardOutcome.Red || outcome == CardOutcome.SecondYellow)
            {
                Telemetry.Reds++;
                if (slot >= 0) ctx.Flow.RemoveFromPitch(slot, PlayerStatus.SentOff);
            }
            Stop(ctx, EventCodes.StopCard, (long)(ctx.RefereeRng.Range(_p.CardStopMin, _p.CardStopMax) * Tps + 0.5f));
        }

        /// <summary>FR-90: 012 정지 시간을 이벤트 데이터로 내고 006 집계에 넣는다.</summary>
        static void Stop(MatchContext ctx, int kind, long ticks)
        {
            var e = SimEvent.Make(SimEventKind.Stoppage);
            e.Substep = SimEvent.TickEnd;
            e.A = kind;
            e.B = (int)ticks;
            ctx.Emit(e);
            ctx.Flow.AddStoppage(kind, ticks);
        }

        // ── 핸드볼 · 다이빙 ──

        void OnHandball(MatchContext ctx, in ContactDetail c)
        {
            var handler = c.Defender;
            var shooter = c.Attacker;
            var at = new Vec2(c.X, c.Y);
            var score = HandballJudge.Score(_p, in c);
            var truth = score > _p.HandThreshold;
            var sight = Perception.SightQuality(_p, _w, at, handler, shooter, Vec2.Zero);
            _s.LastSight = sight;
            var perceived = Perception.Perceive(_p, _profile, score, sight, false, _w.Pressure, ctx.RefereeRng);
            var whistle = perceived > _p.HandThreshold;
            if (truth || whistle) Telemetry.Major(truth == whistle);
            var inBox = RestartRules.InBoxOfGoal(at, ctx.OwnGoal(Squad.TeamOfSlot(handler)), -ctx.AttackDir(Squad.TeamOfSlot(handler)));
            if (!whistle)
            {
                if (truth && inBox)
                    _s.Phase.Add(new PhaseIncident { Kind = PhaseIncidentKind.MissedPenaltyFoul, Tick = ctx.Tick, AttackTeam = Squad.TeamOfSlot(shooter), Offender = handler, OffenderId = ctx.PlayerAtSlot(handler), Victim = shooter, VictimId = ctx.PlayerAtSlot(shooter), Spot = at, TrueValue = score, Perceived = perceived, TrueDecision = EventCodes.DecisionYellow });
                return;
            }
            Telemetry.Handballs++;
            var victimTeam = 1 - Squad.TeamOfSlot(handler);
            var dec = RestartRules.Foul(at, victimTeam, ctx.OpponentGoal(victimTeam), ctx.AttackDir(victimTeam), true, ctx.Pitch.LengthM, ctx.Pitch.WidthM);
            if (_var && dec.Kind == RestartKind.Penalty && !truth)
            {
                var err = DMath.Abs(perceived - score) > _p.VarClearError;
                if (VarCheck(ctx, EventCodes.VarPenaltyIncident, false, false, err, DMath.Abs(perceived - score), c.Substep))
                {
                    VarOverturnEvent(ctx, victimTeam, EventCodes.VarResultPenaltyOverturned, c.Substep);
                    ResolveAdvantageOnStop(ctx);
                    var drop = RestartRules.DropBall(at, Squad.TeamOfSlot(handler), ctx.AttackDir(0), ctx.Pitch.LengthM, ctx.Pitch.WidthM);
                    ctx.Flow.BeginRestart(drop.Kind, drop.Team, drop.Spot);
                    return;
                }
            }
            var h = SimEvent.Make(SimEventKind.HandballOffence, Squad.TeamOfSlot(handler), ctx.PlayerAtSlot(handler), ctx.PlayerAtSlot(shooter));
            h.Substep = c.Substep;
            h.A = EventCodes.DecisionYellow * 10 + (truth ? EventCodes.DecisionYellow : EventCodes.DecisionNone);
            h.B = (truth ? 0 : EventCodes.RefError) | (inBox ? EventCodes.RefInBox | EventCodes.RefVarReviewable : 0);
            h.F0 = score;
            h.F1 = sight;
            ctx.SetPosition(ref h, at);
            ctx.Emit(h);
            // 슛을 팔로 막음 = 유망한 공격 저지 → 경고 [제안]
            Card(ctx, ctx.PlayerAtSlot(handler), EventCodes.CardYellow, EventCodes.ReasonHandball);
            if (dec.Kind == RestartKind.Penalty) Telemetry.PenaltiesAwarded++;
            ResolveAdvantageOnStop(ctx);
            if (ctx.Flow.InPlay) RefereeCommon.Restart(ctx, in dec, c.Substep, true);
        }

        void OnDive(MatchContext ctx, in SimEvent e)
        {
            var diver = e.B;
            if (diver < 0 || diver >= RefereeWorld.N || !_w.Active[diver]) return;
            Telemetry.Dives++;
            var at = Meters(ctx, in e);
            var team = Squad.TeamOfSlot(diver);
            var sight = Perception.SightQuality(_p, _w, at, diver, -1, _w.Vel[diver]);
            _s.LastSight = sight;
            var fooled = ctx.RefereeRng.Bernoulli(Perception.DiveFoolProb(_p, _profile, sight, e.A == 1));
            Telemetry.Major(!fooled);
            if (!fooled)
            {
                if (!ctx.RefereeRng.Bernoulli(_p.DiveCardProb)) return;
                Telemetry.DiveCards++;
                Card(ctx, ctx.PlayerAtSlot(diver), EventCodes.CardYellow, EventCodes.ReasonSimulation);
                var opp = 1 - team;
                var ifk = RestartRules.Foul(at, opp, ctx.OpponentGoal(opp), ctx.AttackDir(opp), false, ctx.Pitch.LengthM, ctx.Pitch.WidthM);
                ResolveAdvantageOnStop(ctx);
                if (ctx.Flow.InPlay) RefereeCommon.Restart(ctx, in ifk, e.Substep, true);
                return;
            }
            // 속음: 가장 가까운 상대(3 m 안)의 반칙으로 판정
            var offender = -1;
            var best = _p.DiveOffenderRadius * _p.DiveOffenderRadius;
            for (int s = (1 - team) * RefereeWorld.PerTeam; s < (2 - team) * RefereeWorld.PerTeam; s++)
            {
                if (!_w.Active[s]) continue;
                var d2 = (_w.Pos[s] - at).LengthSq();
                if (d2 < best) { best = d2; offender = s; }
            }
            if (offender < 0) return;
            Telemetry.DivesFooled++;
            var dec = RestartRules.Foul(at, team, ctx.OpponentGoal(team), ctx.AttackDir(team), true, ctx.Pitch.LengthM, ctx.Pitch.WidthM);
            if (_var && dec.Kind == RestartKind.Penalty && VarCheck(ctx, EventCodes.VarPenaltyIncident, false, false, true, 1f, e.Substep))
            {
                VarOverturnEvent(ctx, team, EventCodes.VarResultPenaltyOverturned, e.Substep);
                VarOverturnEvent(ctx, team, EventCodes.VarResultSimulationCaution, e.Substep);
                Card(ctx, ctx.PlayerAtSlot(diver), EventCodes.CardYellow, EventCodes.ReasonSimulation);
                var opp = 1 - team;
                var ifk = RestartRules.Foul(at, opp, ctx.OpponentGoal(opp), ctx.AttackDir(opp), false, ctx.Pitch.LengthM, ctx.Pitch.WidthM);
                ResolveAdvantageOnStop(ctx);
                if (ctx.Flow.InPlay) RefereeCommon.Restart(ctx, in ifk, e.Substep, true);
                return;
            }
            Telemetry.Whistles++;
            Telemetry.ErrorsAfterVar++;
            EmitFoul(ctx, SimEventKind.Foul, offender, diver, EventCodes.DecisionFoul, EventCodes.DecisionNone, EventCodes.RefError | EventCodes.RefDive | (dec.Kind == RestartKind.Penalty ? EventCodes.RefInBox : 0), 0f, sight, at, e.Substep);
            if (dec.Kind == RestartKind.Penalty) Telemetry.PenaltiesAwarded++;
            ResolveAdvantageOnStop(ctx);
            if (ctx.Flow.InPlay) RefereeCommon.Restart(ctx, in dec, e.Substep, true);
        }

        // ── VAR ──

        /// <summary>VAR 체크 (FR-71~73): 이벤트·정지 시간을 내고 번복 여부를 돌려준다. VAR 꺼짐이면 false(이벤트 없음).</summary>
        bool VarCheck(MatchContext ctx, int incident, bool factual, bool tight, bool error, float errorSize, sbyte substep)
        {
            if (!_var || !VarReview.Reviewable(incident)) return false;
            Telemetry.VarChecks++;
            var ticks = VarReview.CheckTicks(_p, factual, tight, ctx.RefereeRng, Tps);
            var e = SimEvent.Make(SimEventKind.VarCheck);
            e.Substep = substep;
            e.A = incident;
            e.B = (int)ticks;
            e.F0 = errorSize;
            ctx.Emit(e);
            ctx.Flow.AddStoppage(EventCodes.StopVar, ticks);
            var over = VarReview.Overturns(_p, factual, error, ctx.RefereeRng);
            if (over) Telemetry.VarOverturns++;
            return over;
        }

        void VarOverturnEvent(MatchContext ctx, int team, int result, sbyte substep)
        {
            var e = SimEvent.Make(SimEventKind.VarOverturn, team);
            e.Substep = substep;
            e.A = result;
            ctx.Emit(e);
        }

        /// <summary>놓친 PK·퇴장 (공격 단계 끝 = 다음 정지 근사): 명백한 오류면 VAR → PK·소급 퇴장.</summary>
        void ReviewMissed(MatchContext ctx, sbyte substep)
        {
            if (!_var) return;
            for (int i = 0; i < _s.Phase.Count; i++)
            {
                var x = _s.Phase[i];
                if (x.Kind == PhaseIncidentKind.MissedRed)
                {
                    var err = VarReview.ClearError(_p, x.Perceived, x.TrueValue, EventCodes.DecisionNone, x.TrueDecision);
                    if (err && VarCheck(ctx, EventCodes.VarRedIncident, false, false, true, DMath.Abs(x.Perceived - x.TrueValue), substep))
                    {
                        VarOverturnEvent(ctx, x.AttackTeam, EventCodes.VarResultRetroRed, substep);
                        Card(ctx, x.OffenderId, EventCodes.CardRed, EventCodes.ReasonSeriousFoulPlay);
                    }
                }
                else if (x.Kind == PhaseIncidentKind.MissedPenaltyFoul && ctx.Flow.InPlay)
                {
                    var err = VarReview.ClearError(_p, x.Perceived, x.TrueValue, EventCodes.DecisionNone, EventCodes.DecisionFoul);
                    if (err && VarCheck(ctx, EventCodes.VarPenaltyIncident, false, false, true, DMath.Abs(x.Perceived - x.TrueValue), substep))
                    {
                        VarOverturnEvent(ctx, x.AttackTeam, EventCodes.VarResultPenaltyAwarded, substep);
                        if (x.TrueDecision >= EventCodes.DecisionYellow) Card(ctx, x.OffenderId, x.TrueDecision == EventCodes.DecisionRed ? EventCodes.CardRed : EventCodes.CardYellow, EventCodes.ReasonReckless);
                        Telemetry.PenaltiesAwarded++;
                        var g = ctx.OpponentGoal(x.AttackTeam);
                        var pk = new RestartDecision(RestartKind.Penalty, x.AttackTeam, RestartRules.PenaltySpot(g, ctx.AttackDir(x.AttackTeam)));
                        ResolveAdvantageOnStop(ctx);
                        if (ctx.Flow.InPlay) RefereeCommon.Restart(ctx, in pk, substep, true);
                    }
                }
            }
        }

        // ── 경계 (골 · 아웃) ──

        void Boundary(MatchContext ctx, in PhysicsCall c)
        {
            var flow = ctx.Flow;
            if (flow.PenaltyKickTick >= 0) flow.ClearPenalty();
            if (!RefereeCommon.IsGoal(in c))
            {
                // 아웃: 보류 어드밴티지(이득 없이 정지) → 원래 반칙, 늦은 깃발 → 오프사이드가 아웃보다 먼저
                if (ResolveAdvantageOnStop(ctx)) return;
                if (_s.Held.Active)
                {
                    var h = _s.Held;
                    _s.Held = default;
                    RaiseOffside(ctx, h.Team, h.Slot, h.PlayerId, h.Type, h.D, h.Perceived, h.Spot, c.Substep, true, false);
                    return;
                }
                ReviewMissed(ctx, c.Substep);
                if (!flow.InPlay) return;
                RefereeCommon.Restart(ctx, RefereeCommon.OutDecision(ctx, in c), c.Substep, true);
                return;
            }
            var scoring = RefereeCommon.EmitGoal(ctx, in c);
            // 창 안 골 → 골 인정 + 징계 (FR-43, 2026/27 DOGSO 경고 없음)
            for (int i = _s.Advantage.Count - 1; i >= 0; i--)
            {
                var f = _s.Advantage[i];
                _s.Advantage.RemoveAt(i);
                if (f.VictimTeam != scoring) { EmitAdvantageEnd(ctx, in f, 1); continue; }
                EmitAdvantageEnd(ctx, in f, 3);
                var dec = f.Dogso && _rules.NoDogsoCautionAfterAdvantageGoal ? f.Decision : f.FinalDecision;
                if (dec >= EventCodes.DecisionYellow) _s.Discipline.Defer(f.OffenderId, dec == EventCodes.DecisionRed ? EventCodes.CardRed : EventCodes.CardYellow, f.Reason);
            }
            ReviewGoal(ctx, scoring, c.Substep);
            _s.Phase.Clear();
            _s.Offside.Close();
            _s.Held = default;
        }

        /// <summary>득점 VAR (FR-71·72): 늦은 깃발 → 놓친 오프사이드(사실) → 공격 측 핸드볼(사실) → 놓친 공격 반칙(주관, OFR).</summary>
        void ReviewGoal(MatchContext ctx, int scoring, sbyte substep)
        {
            if (_s.Held.Active && _s.Held.Team == scoring)
            {
                var h = _s.Held;
                if (_var)
                {
                    var wrong = h.D <= 0f; // 부심 깃발이 틀렸으면 골 인정
                    VarCheck(ctx, EventCodes.VarGoalIncident, true, DMath.Abs(h.D) < _p.TightMargin, wrong, DMath.Abs(h.Perceived - h.D), substep);
                    if (wrong) return;
                }
                DisallowForOffside(ctx, scoring, h.Slot, h.PlayerId, h.Type, h.D, h.Perceived, h.Spot, substep, true, false);
                return;
            }
            if (!_var) return;
            var i = _s.Phase.Find(PhaseIncidentKind.MissedOffside, scoring);
            if (i >= 0)
            {
                var x = _s.Phase[i];
                VarCheck(ctx, EventCodes.VarGoalIncident, true, x.Tight, true, x.TrueValue, substep);
                DisallowForOffside(ctx, scoring, x.Offender, x.OffenderId, OffsideInvolvement.Play, x.TrueValue, x.Perceived, x.Spot, substep, false, true);
                return;
            }
            i = _s.Phase.Find(PhaseIncidentKind.AttackHandball, scoring);
            if (i >= 0)
            {
                var x = _s.Phase[i];
                VarCheck(ctx, EventCodes.VarGoalIncident, true, false, true, 1f, substep);
                DisallowForFoul(ctx, scoring, in x, substep);
                return;
            }
            i = _s.Phase.Find(PhaseIncidentKind.MissedAttackFoul, scoring);
            if (i >= 0)
            {
                var x = _s.Phase[i];
                var err = VarReview.ClearError(_p, x.Perceived, x.TrueValue, EventCodes.DecisionNone, x.TrueDecision);
                if (err && VarCheck(ctx, EventCodes.VarGoalIncident, false, false, true, DMath.Abs(x.Perceived - x.TrueValue), substep))
                    DisallowForFoul(ctx, scoring, in x, substep);
            }
        }

        void DisallowForOffside(MatchContext ctx, int scoring, int slot, int playerId, byte type, float d, float perceived, Vec2 spot, sbyte substep, bool late, bool varCorrected)
        {
            VarOverturnEvent(ctx, scoring, EventCodes.VarResultGoalDisallowed, substep);
            ctx.Flow.DisallowGoal(scoring);
            Telemetry.Offsides++;
            var e = SimEvent.Make(SimEventKind.Offside, scoring, playerId);
            e.Substep = substep;
            e.A = type;
            e.B = (DMath.Abs(d) < _p.TightMargin ? EventCodes.OffTight : 0) | (late ? EventCodes.OffLateFlag : 0) | (varCorrected ? EventCodes.OffVarCorrected : 0);
            e.F0 = d;
            e.F1 = perceived;
            ctx.SetPosition(ref e, spot);
            ctx.Emit(e);
            var def = 1 - scoring;
            var dec = RestartRules.Foul(spot, def, ctx.OpponentGoal(def), ctx.AttackDir(def), false, ctx.Pitch.LengthM, ctx.Pitch.WidthM);
            RefereeCommon.Restart(ctx, in dec, substep, true);
        }

        void DisallowForFoul(MatchContext ctx, int scoring, in PhaseIncident x, sbyte substep)
        {
            VarOverturnEvent(ctx, scoring, EventCodes.VarResultGoalDisallowed, substep);
            ctx.Flow.DisallowGoal(scoring);
            if (x.Offender >= 0 && x.Offender < RefereeWorld.N)
                EmitFoul(ctx, SimEventKind.Foul, x.Offender, x.Victim, EventCodes.DecisionFoul, x.TrueDecision, EventCodes.RefVarReviewable, x.TrueValue, _s.LastSight, x.Spot, substep);
            if (x.TrueDecision >= EventCodes.DecisionYellow) Card(ctx, x.OffenderId, x.TrueDecision == EventCodes.DecisionRed ? EventCodes.CardRed : EventCodes.CardYellow, EventCodes.ReasonReckless);
            var def = 1 - scoring;
            var dec = RestartRules.Foul(x.Spot, def, ctx.OpponentGoal(def), ctx.AttackDir(def), true, ctx.Pitch.LengthM, ctx.Pitch.WidthM);
            if (dec.Kind == RestartKind.Penalty) dec = new RestartDecision(RestartKind.FreeKick, def, x.Spot, true); // 공격 반칙은 자기 박스 밖 기준이 아님
            RefereeCommon.Restart(ctx, in dec, substep, true);
        }

        // ── 어드밴티지 ──

        void EmitAdvantageEnd(MatchContext ctx, in PendingFoul f, int code)
        {
            var e = SimEvent.Make(SimEventKind.AdvantageEnd, f.VictimTeam, f.OffenderId, f.VictimId);
            e.Substep = SimEvent.TickEnd;
            e.A = code;
            ctx.SetPosition(ref e, f.Spot);
            ctx.Emit(e);
        }

        /// <summary>정지(휘슬·아웃) 시 보류 반칙: 이득이 안 났으므로 가장 오래된 것을 원래 지점 반칙으로. 처리했으면 true(재시작을 이미 정함).</summary>
        bool ResolveAdvantageOnStop(MatchContext ctx)
        {
            if (_s.Advantage.Count == 0) return false;
            var restarted = false;
            while (_s.Advantage.Count > 0)
            {
                var f = _s.Advantage[0];
                _s.Advantage.RemoveAt(0);
                if (!restarted && ctx.Flow.InPlay)
                {
                    EmitAdvantageEnd(ctx, in f, 2);
                    Telemetry.AdvantageReturned++;
                    ReturnToFoul(ctx, in f);
                    restarted = true;
                }
                else
                {
                    EmitAdvantageEnd(ctx, in f, 1);
                    if (f.FinalDecision >= EventCodes.DecisionYellow) _s.Discipline.Defer(f.OffenderId, f.FinalDecision == EventCodes.DecisionRed ? EventCodes.CardRed : EventCodes.CardYellow, f.Reason);
                }
            }
            return restarted;
        }

        void ReturnToFoul(MatchContext ctx, in PendingFoul f)
        {
            if (f.FinalDecision >= EventCodes.DecisionYellow) Card(ctx, f.OffenderId, f.FinalDecision == EventCodes.DecisionRed ? EventCodes.CardRed : EventCodes.CardYellow, f.Reason);
            var dec = RestartRules.Foul(f.Spot, f.VictimTeam, ctx.OpponentGoal(f.VictimTeam), ctx.AttackDir(f.VictimTeam), true, ctx.Pitch.LengthM, ctx.Pitch.WidthM);
            if (dec.Kind == RestartKind.Penalty) Telemetry.PenaltiesAwarded++;
            if (ctx.Flow.InPlay) RefereeCommon.Restart(ctx, in dec, SimEvent.TickEnd, true);
        }

        void UpdateAdvantage(MatchContext ctx)
        {
            if (_s.Advantage.Count == 0) return;
            var f = _s.Advantage[0];
            if (Diagnostics != null)
            {
                var left = _p.AdvantageWindow - (ctx.Tick - f.Tick) / (float)Tps;
                Diagnostics.OnAdvantage(ctx.Tick, f.VictimTeam, left > 0f ? left : 0f);
            }
            if (!ctx.Flow.InPlay) return;
            var lost = ctx.LastTouchTeam >= 0 && ctx.LastTouchTeam != f.VictimTeam;
            var r = AdvantageQueue.Evaluate(_p, in f, ctx.Tick, Tps, lost, false, false);
            if (r == AdvantageResult.Lost) { ResolveAdvantageOnStop(ctx); return; }
            if (r != AdvantageResult.Cleared) return;
            _s.Advantage.RemoveAt(0);
            EmitAdvantageEnd(ctx, in f, 1);
            if (f.FinalDecision >= EventCodes.DecisionYellow) _s.Discipline.Defer(f.OffenderId, f.FinalDecision == EventCodes.DecisionRed ? EventCodes.CardRed : EventCodes.CardYellow, f.Reason);
        }

        /// <summary>데드볼 틱: 보류 카드 집행 (FR-42), 남은 보류 반칙 정리, 늦은 깃발 끝.</summary>
        void OnDeadBall(MatchContext ctx)
        {
            while (_s.Advantage.Count > 0)
            {
                var f = _s.Advantage[0];
                _s.Advantage.RemoveAt(0);
                EmitAdvantageEnd(ctx, in f, 1);
                if (f.FinalDecision >= EventCodes.DecisionYellow) _s.Discipline.Defer(f.OffenderId, f.FinalDecision == EventCodes.DecisionRed ? EventCodes.CardRed : EventCodes.CardYellow, f.Reason);
            }
            var n = _s.Discipline.DeferredCount;
            for (int i = 0; i < n; i++)
            {
                _s.Discipline.DeferredAt(i, out var id, out var color, out var reason);
                Card(ctx, id, color, reason);
            }
            _s.Discipline.ClearDeferred();
            _s.Held = default;
            _s.Offside.Close();
        }

        // ── GK ──

        void OnCatch(MatchContext ctx, in SimEvent e)
        {
            var gk = SlotOf(ctx, e.PlayerId);
            if (gk < 0 || !Squad.IsKeeperSlot(gk)) return;
            var team = Squad.TeamOfSlot(gk);
            _s.HoldStart[team] = ctx.Tick;
            _s.HoldSlot[team] = gk;
            // 백패스 (FR-64): 같은 팀 의도적 발 패스·스로인을 자기 박스 안에서 손으로
            if (_rules.BackPassRule && _s.LastPassTeam == team && _s.LastPasser != gk && !_s.LastPassHeaded)
            {
                var at = _w.Pos[gk];
                if (RestartRules.InBoxOfGoal(at, ctx.OwnGoal(team), -ctx.AttackDir(team)))
                {
                    var dec = RestartRules.BackPass(team, at, ctx.OwnGoal(team), ctx.AttackDir(team), ctx.Pitch.LengthM, ctx.Pitch.WidthM);
                    ResolveAdvantageOnStop(ctx);
                    if (ctx.Flow.InPlay) RefereeCommon.Restart(ctx, in dec, e.Substep, true);
                    _s.HoldStart[team] = -1;
                }
            }
        }

        void KeeperHold(MatchContext ctx)
        {
            for (int t = 0; t < 2; t++)
            {
                if (_s.HoldStart[t] < 0) continue;
                var gk = _s.HoldSlot[t];
                var holding = ctx.Flow.InPlay && ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId == gk && ctx.IsSlotActive(gk);
                if (!holding) { _s.HoldStart[t] = -1; continue; }
                var sec = (ctx.Tick - _s.HoldStart[t]) / (float)Tps;
                if (!RestartRules.KeeperHoldViolation(sec, _p.KeeperHoldMax)) continue;
                _s.HoldStart[t] = -1;
                Telemetry.KeeperHolds++;
                var v = SimEvent.Make(SimEventKind.KeeperHoldViolation, t, ctx.PlayerAtSlot(gk));
                v.Substep = SimEvent.TickEnd;
                v.F0 = sec;
                ctx.SetPosition(ref v, ctx.SlotState(gk).Position);
                ctx.Emit(v);
                var dec = RestartRules.KeeperHold(_rules, t, ctx.SlotState(gk).Position, ctx.OwnGoal(t), ctx.AttackDir(t), ctx.Pitch.LengthM, ctx.Pitch.WidthM);
                RefereeCommon.Restart(ctx, in dec, SimEvent.TickEnd, true);
            }
        }

        // ── 행동 신고 (FR-26·28) ──

        /// <summary>
        /// 행동 신고 (항의·지연·셔츠 세리머니·허가 없는 출입·감독 항의). 행동을 만드는 쪽(050·060·010)이 단계 실행 중에 부른다 — 012 안에서는 발생 0 (플랜 위험 4).
        /// 결정론 리뷰 F3: RefereeRng·상태를 바꾸므로 시뮬 루프 밖(UI 이벤트 등)에서 부르면 리플레이가 깨진다 — 연결 스펙은 입력 로그나 단계 실행 안에서만 부를 것 (테스트는 틱 사이 호출로 결정론 대상 아님).
        /// 감독 항의는 playerId = −1, team 필수.
        /// </summary>
        public void ReportConduct(MatchContext ctx, ConductKind kind, int team, int playerId)
        {
            var color = _s.Discipline.Conduct(kind, team, out var reason);
            if (color == 0) return;
            if (kind == ConductKind.BenchDissent)
            {
                var e = SimEvent.Make(SimEventKind.Card, team, -1);
                e.Substep = SimEvent.TickEnd;
                e.A = color;
                e.B = reason;
                ctx.Emit(e);
                Stop(ctx, EventCodes.StopDissent, (long)(_p.DissentStop * Tps + 0.5f));
                return;
            }
            if (playerId >= 0) Card(ctx, playerId, color, reason);
            if (kind == ConductKind.Dissent) Stop(ctx, EventCodes.StopDissent, (long)(_p.DissentStop * Tps + 0.5f));
        }

        // ── 심판 이동 ──

        void MoveOfficials(MatchContext ctx)
        {
            var L = ctx.Pitch.LengthM;
            var W = ctx.Pitch.WidthM;
            var ball = _w.Ball.XY;
            var refT = OfficialsMotion.RefereeTarget(_p, ball, L, W);
            // 높은 x 골을 지키는 팀 = x 감소 쪽으로 공격하는 팀
            var hiDef = ctx.AttackDir(0) > 0 ? 1 : 0;
            for (int s = 0; s < RefereeWorld.N; s++) _runnerX[s] = _w.Active[s] ? _w.Pos[s].X : float.NaN;
            var hiLine = OffsideJudge.SecondLastDefenderX(_runnerX, hiDef, 1, L, 0f);
            var loLine = OffsideJudge.SecondLastDefenderX(_runnerX, 1 - hiDef, -1, L, 0f);
            var a1 = OfficialsMotion.AssistantHigh(_p, hiLine, ball.X, L);
            var a2 = OfficialsMotion.AssistantLow(_p, loLine, ball.X, L, W);
            ctx.Officials.SetTarget(0, refT);
            ctx.Officials.SetTarget(1, a1);
            ctx.Officials.SetTarget(2, a2);
            Diagnostics?.OnOfficials(ctx.Tick, refT, a1, a2, _s.LastSight);
        }

        // ───────────────────────── 상태 ─────────────────────────

        public void WriteState(StateWriter w) => _s.Write(w);
        public void ReadState(StateReader r) => _s.Read(r);
        public void AddTo(ref StableHasher h) => _s.AddTo(ref h);
    }
}
