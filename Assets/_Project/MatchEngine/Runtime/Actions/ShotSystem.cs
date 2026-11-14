using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 경기당 슈팅 하위 시스템 (specs/071 FR-10~17, <see cref="ActionSystem.Shots"/>).
    /// ① <see cref="Plan"/>: 결정 단계 — xG·조준 셀(FR-13)·파워로 의도 슛 공 동작(RNG 없음).
    /// ② <see cref="ExecuteVelocity"/>: 예약 킥 접촉 서브스텝 — 페널티면 방향 1(FR-17) + 조준 오차 4(FR-14), 아니면 4.
    /// ③ <see cref="OnSubstep"/>: 슛 비행 중 수비수 블록(FR-15, 맞으면 굴절 2).
    /// 상태: 슛 비행(슈터·시작 서브스텝·직전 공 위치) → 해시·스냅샷(비행 중일 때만 해시에 넣는다 — 안 쓰면 해시 불변).
    /// </summary>
    public sealed class ShotSystem
    {
        public const int RngPenaltySide = 1, RngBlock = 2;

        readonly ActionSystem _a;
        readonly ActionParams _p;
        public readonly ShotAim Aim = new ShotAim();
        /// <summary>조준용 GK 평가 (011 FR-14 자리, 기본 대체 근사).</summary>
        public IShotKeeperModel Keeper;
        readonly BallEventBuffer _scratch = new BallEventBuffer(8);

        bool _flight;
        int _shooter = -1;
        long _start;
        Vec3 _prev;

        /// <summary>테스트·진단 누계 (상태 아님).</summary>
        public long Planned, Executed, Blocks, ArmBlocks;

        public ShotSystem(ActionSystem a)
        {
            _a = a;
            _p = a.P;
            Keeper = new StandInKeeperBeat(_p);
        }

        public bool InFlight => _flight;
        public int FlightShooter => _flight ? _shooter : -1;

        // ── 평가 ──

        /// <summary>FR-10·16: 슬롯 s가 지점 from에서 차는 슛의 xG (액션 월드 기준, RNG 없음).</summary>
        public float Xg(MatchContext ctx, int s, Vec2 from, float pressure, float header = 0f, float assist = 0f, float bouncing = 0f, bool penalty = false)
        {
            _a.Refresh(ctx);
            var w = _a.World;
            var team = ActionWorld.TeamOf(s);
            var c = ShotContextBuilder.Build(_p, w.Pos, w.Active, w.OppGoal(team), w.AttackDir[team], 1 - team, from, pressure, header, assist, bouncing, penalty);
            return ShotModel.Xg(_p, in c);
        }

        /// <summary>GK 좌우 오프셋(골문 중앙 기준). 상대 GK가 없으면 NaN.</summary>
        public float KeeperOffset(int shooterTeam, Vec2 goal)
        {
            var gk = (1 - shooterTeam) * ActionWorld.PerTeam;
            return _a.World.Active[gk] ? _a.World.Pos[gk].Y - goal.Y : float.NaN;
        }

        /// <summary>파워 선택 [대체]: base + k·거리, 0~1.</summary>
        public float PowerFor(float dist)
        {
            var v = _p.ShotPowerBase + _p.ShotPowerDistK * dist;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>
        /// 결정 단계: 의도 슛 공 동작. xg &lt; 0이면 여기서 평가한다(시나리오·페널티). 페널티는 셀을 실행 때 정한다(ShotCell = −1).
        /// RNG 없음. Velocity는 조준 셀 중심을 향한 명목값(예약 킥의 발·준비 시간 계산용).
        /// </summary>
        public BallAction Plan(MatchContext ctx, int s, int team, float xg, bool restart, bool penalty, BodyPart part)
        {
            _a.Refresh(ctx);
            Planned++;
            var w = _a.World;
            var from = ctx.Ball.Position;
            var goal = ctx.OpponentGoal(team);
            var dist = (goal - from.XY).Length();
            var power = penalty ? _p.PenPower : PowerFor(dist);
            var speed = ShotExecution.SpeedFor(_p, power);
            var pressure = penalty ? 0f : _a.PressureOn(ctx, s);
            if (xg < 0f) xg = Xg(ctx, s, from.XY, pressure, penalty: penalty);
            var cell = -1;
            var aimY = goal.Y;
            var aimZ = Aim.Height(GoalGrid.Cell(0, 3));
            if (!penalty)
            {
                var sigma = ShotExecution.SigmaAimDeg(_p, w.Finishing[s], speed, pressure, w.Composure[s], 1f, false);
                cell = Aim.Choose(from.XY, goal, speed, sigma, KeeperOffset(team, goal), Keeper);
                aimY = goal.Y + Aim.OffsetY(cell);
                aimZ = Aim.Height(cell);
            }
            return new BallAction
            {
                Kind = BallActionKind.Shot, KickerSlot = s, TargetSlot = -1,
                Velocity = ShotExecution.Intended(from, new Vec2(goal.X, aimY), aimZ, speed, ctx.BallParams.Gravity),
                Part = part, IsRestart = restart, IsPenalty = penalty,
                HasShot = true, ShotCell = cell, ShotPower = power, ShotXg = xg,
            };
        }

        // ── 실행 ──

        /// <summary>
        /// 접촉 서브스텝: 지금 공 위치에서 셀 중심으로 다시 겨누고(준비 시간 동안 공이 움직임) FR-14 오차를 넣는다.
        /// 페널티면 먼저 방향(자연 측 p = 0.6, FR-17)을 RNG 1로 고르고 그쪽 하단 셀 중 최선. RNG: 페널티 5, 아니면 4.
        /// </summary>
        public Vec3 ExecuteVelocity(MatchContext ctx, ref BallAction a, in KickPlan plan, float extraSigmaMul = 1f)
        {
            Executed++;
            var w = _a.World;
            var s = a.KickerSlot;
            var team = Squad.TeamOfSlot(s);
            var from = ctx.Ball.Position;
            var goal = ctx.OpponentGoal(team);
            var speed = ShotExecution.SpeedFor(_p, a.ShotPower);
            if (a.IsPenalty)
            {
                var natural = _a.Rng.NextFloat() < _p.PenNaturalP;
                a.ShotCell = PenaltyCell(ctx, s, team, goal, from.XY, speed, natural);
            }
            var cell = a.ShotCell >= 0 && a.ShotCell < ShotAim.Cells ? a.ShotCell : GoalGrid.Cell(0, 3);
            var intended = ShotExecution.Intended(from, new Vec2(goal.X, goal.Y + Aim.OffsetY(cell)), Aim.Height(cell), speed, ctx.BallParams.Gravity);
            var pressure = a.IsPenalty ? 0f : _a.PressureOn(ctx, s);
            var curled = plan.Tech == KickTech.Inside;
            var sigma = ShotExecution.SigmaAimDeg(_p, w.Finishing[s], speed, pressure, w.Composure[s], plan.SigmaMul, false,
                ShotExecution.CutInMul(_p, curled, w.CutInLevel[s]), extraSigmaMul);
            return ShotExecution.Apply(intended, sigma, _a.Rng, out _, out _);
        }

        /// <summary>
        /// FR-17 페널티 셀: 자연 측 = 주발 반대쪽(오른발 → 키커 왼쪽). 키커가 골문을 볼 때 왼쪽은 골문 y 오프셋 부호 = 공격 방향.
        /// 그쪽 바깥 두 열 × 하단 두 행 중 P_onTarget × P_beatGK 최대(같으면 낮은 번호).
        /// </summary>
        public int PenaltyCell(MatchContext ctx, int s, int team, Vec2 goal, Vec2 from, float speed, bool natural)
        {
            var dir = ctx.AttackDir(team);
            var leftSign = dir > 0 ? 1 : -1;
            var foot = _a.World.PreferredFoot[s];
            var naturalSign = foot == Foot.Left ? -leftSign : leftSign;
            var sign = natural ? naturalSign : -naturalSign;
            int c0 = sign > 0 ? GoalGrid.Cols - 2 : 0, c1 = c0 + 1;
            var sigma = ShotExecution.SigmaAimDeg(_p, _a.World.Finishing[s], speed, 0f, _a.World.Composure[s], 1f, false);
            var gkY = KeeperOffset(team, goal);
            var best = GoalGrid.Cell(0, sign > 0 ? GoalGrid.Cols - 1 : 0);
            var bestV = -1f;
            for (int r = 0; r < 2; r++)
                for (int c = c0; c <= c1; c++)
                {
                    var i = GoalGrid.Cell(r, c);
                    var tgt = new Vec2(goal.X, goal.Y + Aim.OffsetY(i));
                    var d = tgt - from;
                    var dist = d.Length();
                    var cosIn = dist > 1e-4f ? DMath.Abs(d.X) / dist : 1f;
                    var v = Aim.OnTarget(i, dist, cosIn, sigma) * (Keeper != null ? Keeper.BeatProbability(dist, Aim.OffsetY(i), Aim.Height(i), speed, gkY) : 1f);
                    if (v > bestV) { bestV = v; best = i; }
                }
            return best;
        }

        /// <summary>슛 실행 직후: 블록 판정 대상 비행 시작.</summary>
        public void OnKicked(MatchContext ctx, int shooter, long abs)
        {
            _flight = true;
            _shooter = shooter;
            _start = abs;
            _prev = ctx.Ball.Position;
        }

        public void EndFlight()
        {
            _flight = false;
            _shooter = -1;
        }

        // ── 블록 (FR-15) ──

        /// <summary>
        /// 공 적분 뒤 서브스텝마다: 비행 중이면 직전→지금 공 선분이 상대 필드 선수 몸 원기둥(반지름 005 몸 반지름, 높이 block.height)과 닿는지 본다
        /// (슬롯 오름차순, 첫 교차 1건). 슈터가 아닌 누군가 공을 만졌거나 공이 컨트롤되면 비행 끝. 블록했으면 true.
        /// </summary>
        public bool OnSubstep(MatchContext ctx, long abs, int sub)
        {
            if (!_flight) return false;
            ref var ball = ref ctx.Ball;
            if (ball.Phase == BallPhase.Controlled || ctx.LastTouchSlot != _shooter || ctx.LastKickKind != (byte)BallActionKind.Shot || !ctx.Flow.InPlay)
            {
                EndFlight();
                return false;
            }
            for (int i = 0; i < ctx.BallEvents.Count; i++)
                if (ctx.BallEvents[i].Kind == BallEventKind.GoalFrameHit) { EndFlight(); return false; }
            var a = _prev;
            var b = ball.Position;
            _prev = b;
            if (abs - _start < (long)(_p.BlockShooterGrace / ctx.BallParams.Dt + 0.5f)) return false;
            var r = ctx.KinParams.BodyRadius;
            var opp = 1 - Squad.TeamOfSlot(_shooter);
            var seg = b.XY - a.XY;
            var l2 = seg.LengthSq();
            for (int s = opp * Squad.SlotsPerTeam + 1; s < (opp + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!ctx.IsSlotControllable(s)) continue;
                var c = ctx.SlotState(s).Position;
                var t = l2 > 1e-10f ? Vec2.Dot(c - a.XY, seg) / l2 : 0f;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                var q = a.XY + seg * t;
                var d = (q - c).Length();
                if (d > r) continue;
                var z = a.Z + (b.Z - a.Z) * t;
                if (z > _p.BlockHeight || z < 0f) continue;
                Block(ctx, s, abs, sub, new Vec3(q.X, q.Y, z), c, d);
                return true;
            }
            return false;
        }

        void Block(MatchContext ctx, int s, long abs, int sub, Vec3 at, Vec2 body, float dist)
        {
            Blocks++;
            ref var ball = ref ctx.Ball;
            var v = ball.Velocity;
            var shooter = _shooter;
            EndFlight();
            // 굴절 [대체]: 수평 성분을 몸 중심→접촉점 법선에 반사, 무작위 각 ±deflectAngle, 속도 × deflectMul, 세로는 위로 약하게. RNG 정확히 2
            var u1 = _a.Rng.NextFloat();
            var u2 = _a.Rng.NextFloat();
            var n = at.XY - body;
            var nl = n.Length();
            n = nl > 1e-4f ? n / nl : -v.XY.Normalized();
            var h = v.XY;
            var refl = h - n * (2f * Vec2.Dot(h, n));
            if (Vec2.Dot(refl, n) < 0f) refl = n * h.Length(); // 정면 통과 방지
            refl = ActionMath.Rotate(refl, (u1 * 2f - 1f) * _p.BlockDeflectAngle * ActionMath.DegToRad) * _p.BlockDeflectMul;
            var vz = v.Length() * _p.BlockDeflectMul * 0.3f * u2;
            var arm = at.Z >= _p.HandZoneLo && at.Z <= _p.HandZoneHi && dist >= 0.5f * ctx.KinParams.BodyRadius;
            _scratch.Clear();
            BallCommands.TakeControl(ref ball, s, at, _scratch);
            BallCommands.Kick(ref ball, new Vec3(refl.X, refl.Y, vz), Vec3.Zero, ctx.BallParams, _scratch);
            var team = Squad.TeamOfSlot(s);
            var e = SimEvent.Make(SimEventKind.Block, team, ctx.PlayerAtSlot(s), ctx.PlayerAtSlot(shooter));
            e.Substep = (sbyte)sub;
            e.A = arm ? 1 : 0;
            e.F0 = v.Length();
            e.BallPhase = ball.Phase;
            ctx.SetPosition(ref e, at);
            ctx.Emit(e);
            if (arm)
            {
                ArmBlocks++;
                var cd = ContactBuilder.Make(_p, _a.World, ContactKind.Arm, ctx.Tick, sub, shooter, s, at.XY, 1f, 0f, 0f, false, at.Z, 0f);
                ContactBuilder.AttachHand(_p, ref cd, at.Z, dist, v.Length(), ctx.SlotState(s).Velocity.Length(), false);
                _a.EmitContact(ctx, in cd);
            }
            ctx.LastTouchTeam = team;
            ctx.LastTouchSlot = s;
            ctx.LastKickSlot = s;
            ctx.LastKickSubstep = abs;
            ctx.LastKickKind = (byte)BallActionKind.Clearance;
            ctx.LastTackleVictim = -1;
            ctx.Recorder.MarkAction(s, Match.Replay.MotionHint.Tackle);
            ctx.Keepers.NotifyFlight();
            _a.ResetAttempts();
        }

        // ── 해시 · 스냅샷 ──

        public void AddTo(ref StableHasher h)
        {
            if (!_flight) return;
            h.Add(_flight); h.Add(_shooter); h.Add(_start); h.Add(_prev.X); h.Add(_prev.Y); h.Add(_prev.Z);
        }

        public void Write(StateWriter w)
        {
            w.Write(_flight); w.Write(_shooter); w.Write(_start); w.Write(_prev);
        }

        public void Read(StateReader r)
        {
            _flight = r.ReadBool(); _shooter = r.ReadInt(); _start = r.ReadLong(); _prev = r.ReadVec3();
        }
    }
}
