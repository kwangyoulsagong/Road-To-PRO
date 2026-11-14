using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>공중볼 경합 승자 결과 (specs/071 FR-05). Header 이벤트 A 값.</summary>
    public enum HeaderOutcome : byte
    {
        Shot = 0,
        Pass = 1,
        Clear = 2,
        /// <summary>GK가 손으로 이김 — 헤더 없이 011 잡기 경로로 넘긴다(클레임·펀칭은 073).</summary>
        Keeper = 3,
        /// <summary>참여자 0명 — 루즈볼 (이벤트 없음).</summary>
        None = 4,
        /// <summary>결정된 승자가 공에 못 닿음(놓침·라인·다른 터치·이탈) — 헤더 없이 일반 수신.</summary>
        Missed = 5,
    }

    /// <summary>
    /// 경기당 공중볼 경합 (specs/071 FR-02~06, <see cref="ActionSystem.Aerials"/>).
    /// ① <see cref="OnLoftedKick"/>: 최고점이 컨트롤 높이(act.ctrl maxZ)를 넘는 킥(슛 제외)이면 경합을 예약한다.
    /// ② <see cref="OnSubstep"/>: 내려오는 공이 착지까지 act.aerial.decideLead(1 s) 이하로 남은 첫 서브스텝에 참여자(착지점 반경 2 m · TTI ≤ t_land + 0.3 s, 최대 6명 —
    /// 크로스가 아닌 뜬 공은 양 팀이 모두 있어야 경합, 아니면 일반 수신)의
    /// 점수(FR-03)로 승자를 RNG 1개, 결과 오차를 RNG 4개로 정한다(참여 0명이어도 같은 소비 — FR-61). 동작 정보(FR-06)를 기록한다.
    /// ③ 승자는 접촉 지점으로 달려가고(<see cref="TryChase"/>), 공에 닿는 서브스텝(수평 경합 반경 안 · 공 높이 ≤ min(reach, contactZ))에 헤더를 실행한다(준비 시간 없음).
    /// 공이 1 m 아래로 내려올 때까지 못 닿으면 헤더 없이 일반 수신으로 넘긴다.
    /// 결정~실행 사이 다른 선수는 공을 잡지 못한다(<see cref="HoldsBall"/>). 상태 → 해시(쓸 때만)·스냅샷.
    /// </summary>
    public sealed class AerialSystem
    {
        public const int MaxParticipants = 6;
        public const int RngPerContest = 5; // 승자 1 + 결과 4
        public const float FallbackZ = 1.0f;

        readonly ActionSystem _a;
        readonly ActionParams _p;
        readonly BallEventBuffer _scratch = new BallEventBuffer(8);

        // 예약
        bool _armed;
        int _kicker = -1;
        byte _crossType;
        // 결정
        bool _decided;
        int _winner = -1, _nearestOpp = -1, _passTarget = -1, _shotCell = -1, _detailFirst = -1;
        HeaderOutcome _outcome;
        float _winProb, _xg, _n1, _n2, _reach;
        Vec2 _contact;
        long _landAbs;

        readonly int[] _slot = new int[MaxParticipants];
        readonly float[] _score = new float[MaxParticipants], _prob = new float[MaxParticipants], _arrive = new float[MaxParticipants],
            _reachOf = new float[MaxParticipants], _jumpOf = new float[MaxParticipants], _timingOf = new float[MaxParticipants];

        /// <summary>테스트·진단 누계 (상태 아님).</summary>
        public long Armed, Contests, Headers, EmptyContests, KeeperWins, Uncontested, Missed;
        /// <summary>결정 뒤 실행 전 취소 사유 (진단): 0 데드볼 · 1 컨트롤됨 · 2 다른 터치 · 3 라인·골대 · 4 승자 이탈.</summary>
        public readonly long[] CancelReason = new long[5];
        /// <summary>마지막 경합 참여자 수·착지점 최근접 선수 거리·남은 착지 시간 (진단).</summary>
        public int LastParticipants;
        public float LastNearestDist, LastTLand, LastNearestTti;

        public AerialSystem(ActionSystem a)
        {
            _a = a;
            _p = a.P;
        }

        public bool IsArmed => _armed;
        public bool IsDecided => _decided;
        public int Winner => _decided ? _winner : -1;
        public HeaderOutcome Outcome => _outcome;
        /// <summary>헤더 실행 대기 중 — 다른 선수의 수신 판정을 막는다.</summary>
        public bool HoldsBall => _decided;

        /// <summary>결정된 경합이 헤더 없이 끝남 → Header 이벤트(A = 결과, B = 묶음 연결)로 남긴다 (결정론 리뷰 M2: 1초 공백 설명·054 동작 정보 연결).</summary>
        void EndWithoutHeader(MatchContext ctx, int sub, HeaderOutcome outcome)
        {
            if (_winner >= 0 && _detailFirst >= 0)
            {
                var team = Squad.TeamOfSlot(_winner);
                var he = SimEvent.Make(SimEventKind.Header, team, ctx.PlayerAtSlot(_winner), _nearestOpp >= 0 && ctx.IsSlotActive(_nearestOpp) ? ctx.PlayerAtSlot(_nearestOpp) : -1);
                he.Substep = (sbyte)sub;
                he.A = (int)outcome;
                he.B = _detailFirst + 1;
                he.F0 = _winProb;
                he.BallPhase = ctx.Ball.Phase;
                ctx.SetPosition(ref he, ctx.Ball.Position);
                ctx.Emit(he);
            }
            Cancel();
        }

        public void Cancel()
        {
            _armed = false;
            _decided = false;
            _kicker = -1;
            _winner = -1;
        }

        /// <summary>킥 실행 직후 (008 예약 킥). 뜬 공이면 경합 예약. crossType = BallAction.CrossType (0 = 크로스 아님).</summary>
        public void OnLoftedKick(MatchContext ctx, int kicker, BallActionKind kind, byte crossType)
        {
            Cancel();
            if (kind == BallActionKind.Shot) return;
            ref readonly var ball = ref ctx.Ball;
            var vz = ball.Velocity.Z;
            if (vz <= 0f) return;
            var g = ctx.BallParams.Gravity;
            var apex = ball.Position.Z + vz * vz / (2f * (g > 0.1f ? g : 9.81f));
            if (apex <= _p.CtrlMaxZ) return;
            _armed = true;
            _kicker = kicker;
            _crossType = crossType;
            Armed++;
        }

        /// <summary>공 적분 뒤(슛 블록 다음, GK·수신 앞) 서브스텝마다.</summary>
        public void OnSubstep(MatchContext ctx, long abs, int sub)
        {
            if (!_armed && !_decided) return;
            ref var ball = ref ctx.Ball;
            if (!ctx.Flow.InPlay || ball.Phase == BallPhase.Controlled || ctx.LastKickSlot != _kicker && !_decided || ctx.LastTouchSlot != _kicker)
            {
                if (_decided) { CancelReason[!ctx.Flow.InPlay ? 0 : ball.Phase == BallPhase.Controlled ? 1 : 2]++; EndWithoutHeader(ctx, sub, HeaderOutcome.Missed); }
                else Cancel();
                return;
            }
            for (int i = 0; i < ctx.BallEvents.Count; i++)
            {
                var k = ctx.BallEvents[i].Kind;
                if (k == BallEventKind.GoalFrameHit || k == BallEventKind.BoundaryCrossed)
                {
                    if (_decided) { CancelReason[3]++; EndWithoutHeader(ctx, sub, HeaderOutcome.Missed); } else Cancel();
                    return;
                }
            }
            if (_armed && !_decided)
            {
                if (ball.Velocity.Z < 0f && TimeToLand(ctx) <= _p.AerialDecideLead) Decide(ctx, abs, sub);
                return;
            }
            if (_decided)
            {
                if (!ctx.IsSlotControllable(_winner)) { CancelReason[4]++; EndWithoutHeader(ctx, sub, HeaderOutcome.Missed); return; }
                var wp = ctx.SlotState(_winner).Position;
                var near = (ball.Position.XY - wp).LengthSq() <= _p.AerialRadius * _p.AerialRadius && ball.Position.Z <= _reach && ball.Position.Z <= _p.AerialContactZ; // [대체] 머리 접촉 수평 허용 = 경합 반경 2 m (뻗기·점프 포함)
                if (near) Execute(ctx, abs, sub);
                else if (ball.Position.Z <= FallbackZ)
                {
                    // 승자가 끝내 공 밑에 못 옴 → 헤더 없이 일반 수신(008)으로 (RNG는 이미 고정 소비 — 순간이동 헤더 금지)
                    Missed++;
                    EndWithoutHeader(ctx, sub, HeaderOutcome.Missed);
                }
            }
        }

        // ── 결정 ──

        void Decide(MatchContext ctx, long abs, int sub)
        {
            _armed = false;
            Contests++;
            _a.Refresh(ctx);
            var w = _a.World;
            ref readonly var ball = ref ctx.Ball;
            var g = ctx.BallParams.Gravity > 0.1f ? ctx.BallParams.Gravity : 9.81f;
            var z = ball.Position.Z;
            var vz = ball.Velocity.Z;
            var tLand = (vz + DMath.Sqrt(vz * vz + 2f * g * (z > 0f ? z : 0f))) / g;
            var landing = ball.Position.XY + ball.Velocity.XY * tLand;
            var dt = ctx.BallParams.Dt;
            _landAbs = abs + (long)(tLand / dt + 0.5f);
            var n = Gather(ctx, w, landing, tLand, abs);
            LastParticipants = n;
            AerialModel.Softmax(_score, n, _prob);
            var u = _a.Rng.NextFloat();
            var pick = AerialModel.Pick(_prob, n, u);
            ShotExecution.SampleNormals(_a.Rng, out _n1, out _n2);
            if (pick < 0)
            {
                EmptyContests++;
                Cancel();
                return;
            }
            if (_crossType == 0 && !BothTeams(n))
            {
                // 크로스가 아닌 뜬 공을 한 팀만 다툼 → 경합 아님: 일반 수신(008)으로 (RNG 소비는 같음)
                Uncontested++;
                Cancel();
                return;
            }
            _detailFirst = Record(ctx, abs, sub, n, pick, tLand);
            _winner = _slot[pick];
            _winProb = _prob[pick];
            _reach = _reachOf[pick];
            _contact = ContactPoint(ctx, _reach < _p.AerialContactZ ? _reach : _p.AerialContactZ);
            var winTeam = Squad.TeamOfSlot(_winner);
            _nearestOpp = -1;
            var bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (Squad.TeamOfSlot(_slot[i]) == winTeam) continue;
                var d = (w.Pos[_slot[i]] - w.Pos[_winner]).LengthSq();
                if (d < bestD) { bestD = d; _nearestOpp = _slot[i]; }
            }
            if (Squad.IsKeeperSlot(_winner) && Match.Stages.PhysicsStage.InOwnBox(ctx, winTeam, ball.Position))
            {
                KeeperWins++;
                _outcome = HeaderOutcome.Keeper;
                EndWithoutHeader(ctx, sub, HeaderOutcome.Keeper); // 011 잡기 경로(Resolve hands)로 — 클레임·펀칭은 073
                return;
            }
            ChooseOutcome(ctx, w, winTeam, landing);
            _decided = true;
        }

        /// <summary>내려오는 공이 높이 h를 지나는 수평 위치 (중력만 근사).</summary>
        Vec2 ContactPoint(MatchContext ctx, float h)
        {
            ref readonly var ball = ref ctx.Ball;
            var g = ctx.BallParams.Gravity > 0.1f ? ctx.BallParams.Gravity : 9.81f;
            var dz = ball.Position.Z - h;
            var vz = ball.Velocity.Z;
            var disc = vz * vz + 2f * g * (dz > 0f ? dz : 0f);
            var t = (vz + DMath.Sqrt(disc)) / g;
            return ball.Position.XY + ball.Velocity.XY * (t > 0f ? t : 0f);
        }

        /// <summary>T08a: 경합 승자는 헤더 접촉 지점으로 간다 (010 결정 단계가 읽음 — 070 드리블 추격과 같은 방식).</summary>
        public bool TryChase(int slot, out Vec2 point)
        {
            point = _contact;
            return _decided && slot == _winner;
        }

        float TimeToLand(MatchContext ctx)
        {
            ref readonly var ball = ref ctx.Ball;
            var g = ctx.BallParams.Gravity > 0.1f ? ctx.BallParams.Gravity : 9.81f;
            var z = ball.Position.Z > 0f ? ball.Position.Z : 0f;
            var vz = ball.Velocity.Z;
            return (vz + DMath.Sqrt(vz * vz + 2f * g * z)) / g;
        }

        bool BothTeams(int n)
        {
            var t0 = false; var t1 = false;
            for (int i = 0; i < n; i++) { if (Squad.TeamOfSlot(_slot[i]) == 0) t0 = true; else t1 = true; }
            return t0 && t1;
        }

        /// <summary>참여자 모으기: 슬롯 오름차순으로 후보를 보고 TTI 작은 순 최대 6명(같으면 낮은 슬롯).</summary>
        int Gather(MatchContext ctx, ActionWorld w, Vec2 landing, float tLand, long abs)
        {
            var n = 0;
            LastNearestDist = float.MaxValue;
            LastNearestTti = float.MaxValue;
            LastTLand = tLand;
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (!w.Active[s] || !ctx.IsSlotControllable(s) || ctx.Kinematics.IsImmobile(s)) continue;
                if (s == _kicker) continue;
                var pos = w.Pos[s];
                var rel = pos - landing;
                var dist = rel.Length();
                var pull = dist < _p.AerialRadius ? dist : _p.AerialRadius;
                var pt = dist > 1e-4f ? landing + rel * (pull / dist) : landing;
                var tti = TimeToIntercept.Estimate(in w.Tti[s], pt.X, pt.Y);
                if (dist < LastNearestDist) LastNearestDist = dist;
                if (tti < LastNearestTti) LastNearestTti = tti;
                if (!(tti <= tLand + _p.AerialTtiSlack)) continue;
                var j = n < MaxParticipants ? n++ : MaxParticipants;
                if (j == MaxParticipants)
                {
                    if (tti >= _arrive[MaxParticipants - 1]) continue;
                    j = MaxParticipants - 1;
                }
                while (j > 0 && _arrive[j - 1] > tti)
                {
                    _arrive[j] = _arrive[j - 1]; _slot[j] = _slot[j - 1];
                    j--;
                }
                _arrive[j] = tti;
                _slot[j] = s;
            }
            for (int i = 0; i < n; i++)
            {
                var s = _slot[i];
                var toLand = landing - w.Pos[s];
                var tl = toLand.Length();
                var approach = tl > 1e-4f ? Vec2.Dot(w.Vel[s], toLand / tl) : 0f;
                if (approach < 0f) approach = 0f;
                var jump = AerialModel.JumpHeight(_p, w.Jumping[s], approach, w.Stamina[s]);
                var hands = Squad.IsKeeperSlot(s) && Match.Stages.PhysicsStage.InOwnBox(ctx, Squad.TeamOfSlot(s), new Vec3(landing.X, landing.Y, 0f));
                var reach = AerialModel.Reach(_p, w.HeightCm[s], jump, hands);
                var timing = AerialModel.Timing(_p, _arrive[i], tLand);
                _jumpOf[i] = jump;
                _reachOf[i] = reach;
                _timingOf[i] = timing;
                _score[i] = AerialModel.Score(_p, reach, timing, w.Strength[s], w.Heading[s], w.Positioning[s]);
            }
            return n;
        }

        /// <summary>FR-06: 참여자마다 도약·정점 시각·정점 높이 기록. 정점 = 지금 + 도착 시간(착지 ± 0.5 s로 자름), 도약 = 정점 − jumpLead.</summary>
        int Record(MatchContext ctx, long abs, int sub, int n, int pick, float tLand)
        {
            if (n == 0) return -1;
            var dt = ctx.BallParams.Dt;
            var log = _a.Details.Aerials;
            var first = -1;
            var groupFirst = log.Count;
            var half = (long)(0.5f / dt);
            for (int i = 0; i < n; i++)
            {
                var apex = abs + (long)(_arrive[i] / dt + 0.5f);
                if (apex < _landAbs - half) apex = _landAbs - half;
                if (apex > _landAbs + half) apex = _landAbs + half;
                if (apex < abs) apex = abs;
                var d = new AerialDetail
                {
                    Tick = ctx.Tick, Substep = (sbyte)sub, Slot = _slot[i], Team = (byte)Squad.TeamOfSlot(_slot[i]),
                    ApexSubstep = apex, JumpSubstep = apex - (long)(_p.AerialJumpLead / dt + 0.5f), LandSubstep = _landAbs,
                    ApexHeight = _jumpOf[i], Reach = _reachOf[i], Timing = _timingOf[i], WinProb = _prob[i], Won = i == pick,
                    GroupFirst = groupFirst, GroupCount = (byte)n,
                };
                var idx = log.Add(in d);
                if (idx < 0) break;
                if (first < 0) first = idx;
            }
            return first;
        }

        void ChooseOutcome(MatchContext ctx, ActionWorld w, int team, Vec2 landing)
        {
            _xg = 0f;
            _passTarget = -1;
            _shotCell = -1;
            var attacking = team == Squad.TeamOfSlot(_kicker);
            if (!attacking)
            {
                _outcome = HeaderOutcome.Clear;
                return;
            }
            var goal = ctx.OpponentGoal(team);
            var at = ctx.Ball.Position.XY;
            var shots = _a.Shots;
            var c = ShotContextBuilder.Build(_p, w.Pos, w.Active, goal, ctx.AttackDir(team), 1 - team, at, _a.PressureOn(ctx, _winner), 1f);
            _xg = ShotModel.Xg(_p, in c);
            if (InOppBox(ctx, team, at) && _xg >= _p.HeaderShotMinXg)
            {
                _outcome = HeaderOutcome.Shot;
                var speed = ShotExecution.SpeedFor(_p, _p.HeaderShotPower);
                var sigma = ShotExecution.SigmaAimDeg(_p, w.Finishing[_winner], speed, c.Pressure, w.Composure[_winner], 1f, true);
                _shotCell = shots.Aim.Choose(at, goal, speed, sigma, shots.KeeperOffset(team, goal), shots.Keeper);
                return;
            }
            _xg = 0f;
            // 헤딩 패스: 공격 방향으로 앞선(또는 옆) 가장 가까운 동료, passMax 안
            var dir = ctx.AttackDir(team);
            var best = -1;
            var bestD = _p.HeaderPassMax * _p.HeaderPassMax;
            for (int s = team * Squad.SlotsPerTeam; s < (team + 1) * Squad.SlotsPerTeam; s++)
            {
                if (s == _winner || !w.Active[s]) continue;
                var r = w.Pos[s] - at;
                if (r.X * dir < -2f) continue;
                var d = r.LengthSq();
                if (d < 4f || d >= bestD) continue;
                bestD = d;
                best = s;
            }
            _passTarget = best;
            _outcome = HeaderOutcome.Pass;
        }

        static bool InOppBox(MatchContext ctx, int team, Vec2 p) => Match.Stages.PhysicsStage.InOwnBox(ctx, 1 - team, new Vec3(p.X, p.Y, 0f));

        // ── 실행 ──

        void Execute(MatchContext ctx, long abs, int sub)
        {
            _a.Refresh(ctx); // 결정론 리뷰 H1
            _decided = false;
            if (_passTarget >= 0 && !ctx.IsSlotControllable(_passTarget)) _passTarget = -1; // 리뷰 L3: 결정 뒤 퇴장·교체
            if (_nearestOpp >= 0 && !ctx.IsSlotControllable(_nearestOpp)) _nearestOpp = -1;
            Headers++;
            var s = _winner;
            var team = Squad.TeamOfSlot(s);
            var w = _a.World;
            ref var ball = ref ctx.Ball;
            var from = ball.Position;
            var incoming = ball.Velocity.Length();
            Vec3 v;
            SimEventKind kind;
            BallActionKind kick;
            var target = -1;
            switch (_outcome)
            {
                case HeaderOutcome.Shot:
                {
                    var goal = ctx.OpponentGoal(team);
                    var shots = _a.Shots;
                    var speed = ShotExecution.SpeedFor(_p, _p.HeaderShotPower);
                    var intended = ShotExecution.Intended(from, new Vec2(goal.X, goal.Y + shots.Aim.OffsetY(_shotCell)), shots.Aim.Height(_shotCell), speed, ctx.BallParams.Gravity);
                    var sigma = ShotExecution.SigmaAimDeg(_p, w.Finishing[s], speed, _a.PressureOn(ctx, s), w.Composure[s], 1f, true);
                    v = ShotExecution.ApplyNormals(intended, sigma, _n1, _n2, out _, out _);
                    kind = SimEventKind.Shot;
                    kick = BallActionKind.Shot;
                    break;
                }
                case HeaderOutcome.Pass when _passTarget >= 0:
                {
                    var to = ctx.SlotState(_passTarget).Position;
                    var d = to - from.XY;
                    var dist = d.Length();
                    var sp = _p.HeaderPassBase + _p.HeaderPassK * dist;
                    if (sp > _p.HeaderPassVMax) sp = _p.HeaderPassVMax;
                    var dir = dist > 1e-4f ? d / dist : new Vec2(ctx.AttackDir(team), 0f);
                    var sa = PassExecution.SigmaAngleDeg(_p, w.Passing[s], _a.PressureOn(ctx, s), w.Composure[s], _p.ShotHeaderMul, 1f);
                    var h = ActionMath.Rotate(dir * sp, _n1 * sa * ActionMath.DegToRad);
                    v = new Vec3(h.X, h.Y, _p.HeaderPassVz + 0.5f * _n2);
                    kind = SimEventKind.Pass;
                    kick = BallActionKind.Pass;
                    target = _passTarget;
                    break;
                }
                default:
                {
                    // 클리어(수비 승자) / 헤딩 패스 대상 없음: 자기 골문 반대 방향, ±25°·속도 lerp(lo, hi, Φ(n2)), 위로 5 m/s
                    var away = new Vec2(ctx.AttackDir(team), 0f);
                    var h = ActionMath.Rotate(away, _n1 * _p.HeaderClearAngle * ActionMath.DegToRad);
                    var sp = ActionMath.Lerp(_p.HeaderClearLo, _p.HeaderClearHi, ActionMath.NormalCdf(_n2));
                    v = new Vec3(h.X * sp, h.Y * sp, _p.HeaderClearVz);
                    kind = SimEventKind.Clearance;
                    kick = BallActionKind.Clearance;
                    break;
                }
            }
            _scratch.Clear();
            BallCommands.TakeControl(ref ball, s, from, _scratch);
            BallCommands.Kick(ref ball, v, Vec3.Zero, ctx.BallParams, _scratch);
            var detail = kick == BallActionKind.Pass ? _a.SnapshotOffside(ctx, s, team) : -1; // 헤딩 패스도 008 FR-15 오프사이드 스냅샷
            var e = SimEvent.Make(kind, team, ctx.PlayerAtSlot(s), target >= 0 ? ctx.PlayerAtSlot(target) : -1);
            e.Substep = (sbyte)sub;
            e.Part = BodyPart.Head;
            e.BallPhase = ball.Phase;
            e.F0 = v.Length();
            if (kind == SimEventKind.Shot)
            {
                e.A = ActionSystem.ShotFlagPlanned | ActionSystem.ShotFlagHeader;
                e.B = _shotCell + 1;
                e.F1 = _xg;
            }
            else if (kind == SimEventKind.Pass) e.B = detail + 1;
            ctx.SetPosition(ref e, from);
            ctx.Emit(e);
            var he = SimEvent.Make(SimEventKind.Header, team, ctx.PlayerAtSlot(s), _nearestOpp >= 0 ? ctx.PlayerAtSlot(_nearestOpp) : -1);
            he.Substep = (sbyte)sub;
            he.A = _outcome == HeaderOutcome.Pass && target < 0 ? (int)HeaderOutcome.Clear : (int)_outcome;
            he.B = _detailFirst + 1;
            he.F0 = _winProb;
            he.F1 = _xg;
            he.BallPhase = ball.Phase;
            ctx.SetPosition(ref he, from);
            ctx.Emit(he);
            if (_nearestOpp >= 0)
            {
                var attacking = team == Squad.TeamOfSlot(_kicker);
                var att = attacking ? s : _nearestOpp;
                var def = attacking ? _nearestOpp : s;
                var cd = ContactBuilder.Make(_p, w, ContactKind.Aerial, ctx.Tick, sub, att, def, from.XY, 1f, 0f, 0f, false, from.Z, 0f);
                _a.EmitContact(ctx, in cd);
            }
            ctx.LastTouchTeam = team;
            ctx.LastTouchSlot = s;
            ctx.LastKickSlot = s;
            ctx.LastKickSubstep = abs;
            ctx.LastKickKind = (byte)kick;
            ctx.LastTackleVictim = -1;
            ctx.Recorder.MarkAction(s, Match.Replay.MotionHint.Kick);
            _a.SetFlightTarget(kick == BallActionKind.Pass ? target : -1);
            if (kick == BallActionKind.Shot) _a.Shots.OnKicked(ctx, s, abs);
            ctx.Keepers.NotifyFlight();
            _a.ResetAttempts();
            _a.Diagnostics?.OnContact(ctx.Tick, sub, s, kind, incoming);
            _winner = -1;
            _kicker = -1;
        }

        // ── 해시 · 스냅샷 ──

        public void AddTo(ref StableHasher h)
        {
            if (!_armed && !_decided) return;
            h.Add(_armed); h.Add(_decided); h.Add(_kicker); h.Add(_crossType); h.Add(_winner); h.Add(_nearestOpp); h.Add(_passTarget); h.Add(_shotCell);
            h.Add(_detailFirst); h.Add((byte)_outcome); h.Add(_winProb); h.Add(_xg); h.Add(_n1); h.Add(_n2); h.Add(_reach); h.Add(_landAbs); h.Add(_contact.X); h.Add(_contact.Y);
        }

        public void Write(StateWriter w)
        {
            w.Write(_armed); w.Write(_decided); w.Write(_kicker); w.Write(_crossType); w.Write(_winner); w.Write(_nearestOpp); w.Write(_passTarget); w.Write(_shotCell);
            w.Write(_detailFirst); w.Write((byte)_outcome); w.Write(_winProb); w.Write(_xg); w.Write(_n1); w.Write(_n2); w.Write(_reach); w.Write(_landAbs); w.Write(_contact);
        }

        public void Read(StateReader r)
        {
            _armed = r.ReadBool(); _decided = r.ReadBool(); _kicker = r.ReadInt(); _crossType = r.ReadByte(); _winner = r.ReadInt(); _nearestOpp = r.ReadInt();
            _passTarget = r.ReadInt(); _shotCell = r.ReadInt(); _detailFirst = r.ReadInt(); _outcome = (HeaderOutcome)r.ReadByte();
            _winProb = r.ReadFloat(); _xg = r.ReadFloat(); _n1 = r.ReadFloat(); _n2 = r.ReadFloat(); _reach = r.ReadFloat(); _landAbs = r.ReadLong(); _contact = r.ReadVec2();
        }
    }
}
