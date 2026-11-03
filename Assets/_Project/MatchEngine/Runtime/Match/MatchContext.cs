using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Match.Diagnostics;

namespace RoadToPro.MatchEngine.Match
{
    /// <summary>
    /// 경기 하나의 시뮬 상태와 단계가 쓰는 공용 도구 (specs/006). <see cref="MatchSim"/>이 소유하고 단계에 넘긴다.
    /// 정적 가변 상태 없음 — 여러 경기를 다른 스레드에서 동시에 돌려도 서로 영향이 없다 (FR-71).
    /// 운동 시스템 id = 슬롯 번호(0~21).
    /// </summary>
    public sealed class MatchContext
    {
        public readonly MatchSetup Setup;
        public readonly MatchParams Params;
        public readonly BallParams BallParams;
        public readonly KinematicsParams KinParams;
        public readonly PitchSpec Pitch;
        public readonly KinematicsSystem Kinematics;
        public readonly BallEventBuffer BallEvents = new BallEventBuffer(32);
        public readonly Squad Squad;
        public readonly TickBoard Board = new TickBoard();
        public readonly EventPublisher Publisher = new EventPublisher();
        public readonly MatchFlow Flow;
        public readonly Officials Officials;
        public readonly SeedRng DecisionRng;
        public readonly SeedRng RefereeRng;
        public readonly SeedRng FlowRng;
        public readonly InputLog Inputs;
        public readonly RefereeScript RefereeScript;
        public readonly ReplayRecorder Recorder;
        public IRawTrajectorySink RawSink;
        /// <summary>이 경기 이벤트 로그 (발행 단계가 쓴다).</summary>
        public SimEventLog Log { get; internal set; }
        /// <summary>읽기 전용 조회 (구독자용).</summary>
        public IMatchView View { get; internal set; }

        /// <summary>이번 틱 결정 대상 (스케줄러 결과, 중복 제거·오름차순). 단계 5가 읽는다.</summary>
        public readonly int[] DecisionList = new int[Squad.Slots];
        public int DecisionCount;

        BallState _ball;
        public ref BallState Ball => ref _ball;

        /// <summary>마지막으로 공을 만진 팀·슬롯 (아웃 판정용).</summary>
        public int LastTouchTeam = -1;
        public int LastTouchSlot = -1;
        /// <summary>마지막 킥 (킥한 선수가 같은 순간 다시 잡지 않게, 패스 받음 판정).</summary>
        public int LastKickSlot = -1;
        public long LastKickSubstep = -1;
        public int LastTackleVictim = -1;
        public byte LastKickKind;
        /// <summary>현재 공 소유 시작 틱 (대체 단계 보유 시간).</summary>
        public long PossessionStartTick;

        /// <summary>지금 처리 중인 틱.</summary>
        public long Tick { get; internal set; }

        internal MatchContext(MatchSetup setup, MatchTuning tuning, InputLog inputs, RefereeScript referee)
        {
            Setup = setup;
            Pitch = setup.Pitch;
            Params = MatchParams.Create(tuning.Match);
            BallParams = BallParams.Create(tuning.Ball, setup.Surface, setup.Pitch);
            KinParams = KinematicsParams.Create(tuning.Kinematics, setup.Pitch);
            Squad = new Squad(setup);
            var players = new PlayerSetup[Squad.Slots];
            for (int s = 0; s < Squad.Slots; s++)
            {
                var id = Squad.PlayerAt(s);
                players[s] = new PlayerSetup(s, Squad.AttributesOf(id), new Vec2(Pitch.LengthM * 0.5f, Pitch.WidthM * 0.5f), default, new Vec2(1f, 0f));
            }
            Kinematics = new KinematicsSystem(KinParams, players);
            DecisionRng = RngStreams.Create(setup.Seed, RngStreamId.PlayerDecision);
            RefereeRng = RngStreams.Create(setup.Seed, RngStreamId.Referee);
            FlowRng = RngStreams.Create(setup.Seed, RngStreamId.MatchFlow);
            Officials = new Officials(Pitch.LengthM, Pitch.WidthM);
            Inputs = inputs;
            RefereeScript = referee;
            Recorder = new ReplayRecorder(this);
            _ball = BallState.AtRest(new Vec2(Pitch.LengthM * 0.5f, Pitch.WidthM * 0.5f), BallParams.Radius);
            Flow = new MatchFlow(this);
        }

        // ── 이벤트 ──

        /// <summary>지금 단계 이름으로 이벤트를 낸다 (단계 밖이면 진행 판정 가상 단계 10).</summary>
        public void Emit(SimEvent e) => EmitAs(e, Board.Outside ? SimEvent.FlowStage : (byte)Board.Current);

        public void EmitAs(SimEvent e, byte stage)
        {
            e.Period = (byte)Flow.Period;
            // 표기 시각: 틱 시작 시각. 틱 끝 사건(진행 판정)은 그 틱이 끝난 시각 — 45:00 발표, 45+2:00 휘슬 (AC-11)
            var clock = Tick - Flow.PeriodStartTick + (e.Substep == SimEvent.TickEnd ? 1 : 0);
            e.Clock = clock < 0 ? 0 : (int)clock;
            Publisher.Emit(Tick, stage, e);
        }

        /// <summary>미터 좌표 → 이벤트 정규화 좌표.</summary>
        public void SetPosition(ref SimEvent e, Vec3 meters)
        {
            e.X = meters.X / Pitch.LengthM;
            e.Y = meters.Y / Pitch.WidthM;
            e.Z = meters.Z;
        }

        public void SetPosition(ref SimEvent e, Vec2 meters) => SetPosition(ref e, new Vec3(meters.X, meters.Y, 0f));

        // ── 좌표 · 진영 ──

        /// <summary>팀의 공격 방향 (+1 = +x). 기간 0·2는 홈이 +x (001 FR-21 관점 규칙).</summary>
        public int AttackDir(int team) => ((team == 0) ^ ((Flow.Period & 1) == 1)) ? 1 : -1;

        public Vec2 OpponentGoal(int team) => new Vec2(AttackDir(team) > 0 ? Pitch.LengthM : 0f, Pitch.WidthM * 0.5f);
        public Vec2 OwnGoal(int team) => new Vec2(AttackDir(team) > 0 ? 0f : Pitch.LengthM, Pitch.WidthM * 0.5f);

        /// <summary>팀 관점 정규화 좌표 → 기준 미터 좌표.</summary>
        public Vec2 FromTeamPerspective(int team, Vec2 n)
        {
            var w = PitchCoords.FromPerspective(n, team == 0 ? TeamSide.Home : TeamSide.Away, Flow.Period);
            return PitchCoords.ToMeters(w, Pitch);
        }

        public int PlayerAtSlot(int slot) => Squad.PlayerAt(slot);
        public bool IsSlotActive(int slot) => Squad.IsSlotFilled(slot) && Kinematics.IsActive(slot);
        public ref readonly PlayerMotionState SlotState(int slot) => ref Kinematics.State(slot);

        // ── 상태 해시 · 직렬화 ──

        internal void AddStateTo(ref StableHasher h)
        {
            h.Add(Tick);
            Flow.AddTo(ref h);
            Squad.AddTo(ref h);
            Officials.AddTo(ref h);
            var d = DecisionRng.SaveState(); h.Add(d.S0); h.Add(d.S1); h.Add(d.S2); h.Add(d.S3);
            var r = RefereeRng.SaveState(); h.Add(r.S0); h.Add(r.S1); h.Add(r.S2); h.Add(r.S3);
            var f = FlowRng.SaveState(); h.Add(f.S0); h.Add(f.S1); h.Add(f.S2); h.Add(f.S3);
            _ball.AddTo(ref h);
            Kinematics.AddTo(ref h);
            h.Add(LastTouchTeam); h.Add(LastTouchSlot); h.Add(LastKickSlot); h.Add(LastKickSubstep); h.Add(LastTackleVictim); h.Add(LastKickKind); h.Add(PossessionStartTick);
        }

        internal void WriteCore(StateWriter w)
        {
            w.Write(Tick);
            w.Write(DecisionRng.SaveState());
            w.Write(RefereeRng.SaveState());
            w.Write(FlowRng.SaveState());
            w.Write((byte)_ball.Phase); w.Write(_ball.Position); w.Write(_ball.Velocity); w.Write(_ball.Spin); w.Write(_ball.OwnerId);
            w.Write(LastTouchTeam); w.Write(LastTouchSlot); w.Write(LastKickSlot); w.Write(LastKickSubstep); w.Write(LastTackleVictim); w.Write(LastKickKind); w.Write(PossessionStartTick);
        }

        internal void ReadCore(StateReader r)
        {
            Tick = r.ReadLong();
            DecisionRng.RestoreState(r.ReadRngState());
            RefereeRng.RestoreState(r.ReadRngState());
            FlowRng.RestoreState(r.ReadRngState());
            _ball.Phase = (BallPhase)r.ReadByte(); _ball.Position = r.ReadVec3(); _ball.Velocity = r.ReadVec3(); _ball.Spin = r.ReadVec3(); _ball.OwnerId = r.ReadInt();
            LastTouchTeam = r.ReadInt(); LastTouchSlot = r.ReadInt(); LastKickSlot = r.ReadInt(); LastKickSubstep = r.ReadLong(); LastTackleVictim = r.ReadInt(); LastKickKind = r.ReadByte(); PossessionStartTick = r.ReadLong();
        }
    }
}
