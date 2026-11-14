using System;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.MatchEngine.Match.Pipeline
{
    /// <summary>틱 보드 슬롯. 값마다 쓰는 단계가 하나로 정해져 있다 (<see cref="TickBoard.WriterOf"/>).</summary>
    public enum BoardSlot : byte
    {
        Tactics = 0,
        Phase = 1,
        ShapeTargets = 2,
        RoleAdjust = 3,
        Tendency = 4,
        Decisions = 5,
        Steering = 6,
        PhysicsEvents = 7,
        RefereeCalls = 8,
    }

    /// <summary>단계가 아직 쓰이지 않은(같은 틱의 뒤) 단계 값을 읽거나 남의 슬롯에 쓰려 했다 (FR-02, AC-03).</summary>
    public sealed class StageOrderViolation : Exception
    {
        public readonly StageId Stage;
        public readonly BoardSlot Slot;

        public StageOrderViolation(StageId stage, BoardSlot slot, bool write)
            : base(write ? $"단계 {stage}가 {slot} 슬롯(쓰는 단계 {TickBoard.WriterOf(slot)})에 썼다"
                         : $"단계 {stage}가 같은 틱의 뒤 단계 {TickBoard.WriterOf(slot)} 슬롯 {slot}을 읽었다 (FR-02)")
        {
            Stage = stage;
            Slot = slot;
        }
    }

    /// <summary>선수 하나의 결정 결과 (단계 5 → 6).</summary>
    public struct Decision
    {
        public DecisionKind Kind;
        public Vec2 Point;
        public int TargetSlot;
        public float MaxSpeed;
    }

    public enum DecisionKind : byte
    {
        Keep = 0,
        HoldShape = 1,
        MoveTo = 2,
        Chase = 3,
        Carry = 4,
        Stand = 5,
    }

    /// <summary>
    /// 공에 가하는 동작 (단계 5가 정한다). specs/008: 물리 단계가 예약 킥으로 받아 킥 준비 시간 뒤 공이 도달 거리 안인 서브스텝에 실행한다(FR-65).
    /// <see cref="HasIntent"/>면 "의도"(조준점·속도 후보·발사각)만 담고 실행 오차는 접촉 순간 액션 RNG로 넣는다(FR-13). 아니면(슛·클리어·대체 태클 — 070·071 전)
    /// <see cref="Velocity"/>를 그대로 쓴다.
    /// </summary>
    public struct BallAction
    {
        public BallActionKind Kind;
        public int KickerSlot;
        public int TargetSlot;
        public Vec3 Velocity;
        /// <summary>008 의도 패스: 조준점(미터)·초속·발사각(rad, 0 = 땅볼)·평가 xPass·원터치 여부.</summary>
        public bool HasIntent;
        public Vec2 Aim;
        public float Speed;
        public float Elevation;
        public float XPass;
        public bool IsOneTouch;
        public Vec3 Spin;
        public Events.BodyPart Part;
        public bool IsRestart;
        public bool IsPenalty;
        public bool IsKickoff;
        /// <summary>071 슈팅 의도 (FR-13·14): 조준 셀(011 7×3 격자)·파워(0~1)·슈팅 순간 xG. 실행 오차는 접촉 순간 액션 RNG.</summary>
        public bool HasShot;
        public int ShotCell;
        public float ShotPower;
        public float ShotXg;
        /// <summary>071 크로스 타입 (0 = 크로스 아님, <c>Actions.CrossType</c> + 1).</summary>
        public byte CrossType;
    }

    public enum BallActionKind : byte
    {
        None = 0,
        Pass = 1,
        Shot = 2,
        Clearance = 3,
    }

    /// <summary>물리 서브스텝 사건 (FR-03): 서브스텝 시각을 달고 심판 단계로 넘어간다.</summary>
    public struct PhysicsCall
    {
        public PhysicsCallKind Kind;
        public sbyte Substep;
        public int Slot;
        public Ball.BallBoundary Boundary;
        public bool InGoalMouth;
        public Vec3 Position;
        public int LastTouchTeam;
        public int LastTouchSlot;
    }

    public enum PhysicsCallKind : byte
    {
        BoundaryCrossed = 0,
        WoodworkHit = 1,
    }

    /// <summary>
    /// 틱 단위 단계 간 데이터 (specs/006 FR-02). 슬롯마다 쓰는 단계가 고정이고, 현재 단계가 그 슬롯의 쓰는 단계보다
    /// 앞이면 읽기 거부, 쓰는 단계가 아니면 쓰기 거부 — 정수 비교 하나라 핫패스 비용이 거의 없다.
    /// 매 틱 시작에 비운다.
    /// </summary>
    public sealed class TickBoard
    {
        public const int MaxSlots = 22;
        public const int MaxPhysicsCalls = 64;

        public static StageId WriterOf(BoardSlot s) => s switch
        {
            BoardSlot.Tactics => StageId.TeamTactics,
            BoardSlot.Phase => StageId.Phase,
            BoardSlot.ShapeTargets => StageId.Shape,
            BoardSlot.RoleAdjust => StageId.RoleAdjust,
            BoardSlot.Tendency => StageId.Tendency,
            BoardSlot.Decisions => StageId.ScenarioOrUtility,
            BoardSlot.Steering => StageId.SteeringTargets,
            BoardSlot.PhysicsEvents => StageId.Physics,
            _ => StageId.Referee,
        };

        readonly Vec2[] _shape = new Vec2[MaxSlots];
        readonly bool[] _shapeSkipped = new bool[MaxSlots];
        readonly TeamInstructions[] _tactics = new TeamInstructions[2];
        readonly TeamPhase[] _phase = new TeamPhase[2], _block = new TeamPhase[2];
        readonly long[] _transitionTicks = new long[2];
        readonly int[] _formation = new int[2];
        readonly TeamShapeOutput[] _shapeOutput = new TeamShapeOutput[2];
        readonly float[] _defLineM = new float[2], _offsideLineM = new float[2];
        readonly Decision[] _decisions = new Decision[MaxSlots];
        readonly SteeringCommand[] _steering = new SteeringCommand[MaxSlots];
        readonly bool[] _hasSteering = new bool[MaxSlots];
        readonly PhysicsCall[] _calls = new PhysicsCall[MaxPhysicsCalls];
        BallAction _ballAction;
        int _callCount;
        int _decisionCount;
        readonly int[] _decided = new int[MaxSlots];

        /// <summary>지금 실행 중인 단계 (루프가 설정). 단계 밖(진행 판정 등)은 <see cref="Outside"/>.</summary>
        public StageId Current { get; private set; }
        public bool Outside { get; private set; } = true;

        public void Enter(StageId s)
        {
            Current = s;
            Outside = false;
        }

        public void Leave() => Outside = true;

        public void Clear()
        {
            Array.Clear(_shape, 0, _shape.Length);
            Array.Clear(_shapeSkipped, 0, _shapeSkipped.Length);
            Array.Clear(_decisions, 0, _decisions.Length);
            Array.Clear(_hasSteering, 0, _hasSteering.Length);
            _ballAction = default;
            _callCount = 0;
            _decisionCount = 0;
        }

        public void CheckRead(BoardSlot slot)
        {
            if (Outside) return;
            if (WriterOf(slot) >= Current) throw new StageOrderViolation(Current, slot, false);
        }

        public void CheckWrite(BoardSlot slot)
        {
            if (Outside || WriterOf(slot) != Current) throw new StageOrderViolation(Current, slot, true);
        }

        // 대형 목표 (단계 2)
        public Vec2[] ShapeTargetsForWrite() { CheckWrite(BoardSlot.ShapeTargets); return _shape; }
        public Vec2 ShapeTarget(int slot) { CheckRead(BoardSlot.ShapeTargets); return _shape[slot]; }

        /// <summary>시나리오 배역 중이라 형태 목표가 없는 슬롯 (009 FR-90). 대체 조향은 건너뛴다.</summary>
        public void MarkNoShapeTarget(int slot) { CheckWrite(BoardSlot.ShapeTargets); _shapeSkipped[slot] = true; }
        public bool HasShapeTarget(int slot) { CheckRead(BoardSlot.ShapeTargets); return !_shapeSkipped[slot]; }

        // 팀 지시 (단계 0), 국면 (단계 1), 형태 결과 (단계 2) — 009
        public void SetInstructions(int team, in TeamInstructions i) { CheckWrite(BoardSlot.Tactics); _tactics[team] = i; }
        public ref readonly TeamInstructions Instructions(int team) { CheckRead(BoardSlot.Tactics); return ref _tactics[team]; }
        public void SetFormationIndex(int team, int index) { CheckWrite(BoardSlot.Tactics); _formation[team] = index; }
        public int FormationIndex(int team) { CheckRead(BoardSlot.Tactics); return _formation[team]; }
        public void SetPhase(int team, TeamPhase p, TeamPhase block, long transitionTicks) { CheckWrite(BoardSlot.Phase); _phase[team] = p; _block[team] = block; _transitionTicks[team] = transitionTicks; }
        public TeamPhase Block(int team) { CheckRead(BoardSlot.Phase); return _block[team]; }
        public long TransitionTicks(int team) { CheckRead(BoardSlot.Phase); return _transitionTicks[team]; }
        public TeamPhase Phase(int team) { CheckRead(BoardSlot.Phase); return _phase[team]; }

        public void SetShapeOutput(int team, in TeamShapeOutput o, float defLineM, float offsideLineM)
        {
            CheckWrite(BoardSlot.ShapeTargets);
            _shapeOutput[team] = o;
            _defLineM[team] = defLineM;
            _offsideLineM[team] = offsideLineM;
        }

        public ref readonly TeamShapeOutput ShapeOutput(int team) { CheckRead(BoardSlot.ShapeTargets); return ref _shapeOutput[team]; }
        /// <summary>팀 수비 라인 x (기준 미터 좌표, FR-03).</summary>
        public float DefLineM(int team) { CheckRead(BoardSlot.ShapeTargets); return _defLineM[team]; }
        /// <summary>팀 오프사이드 라인 x (기준 미터 좌표, FR-45).</summary>
        public float OffsideLineM(int team) { CheckRead(BoardSlot.ShapeTargets); return _offsideLineM[team]; }

        // 결정 (단계 5)
        public void SetDecision(int slot, in Decision d)
        {
            CheckWrite(BoardSlot.Decisions);
            _decisions[slot] = d;
        }

        public ref readonly Decision GetDecision(int slot)
        {
            CheckRead(BoardSlot.Decisions);
            return ref _decisions[slot];
        }

        /// <summary>이번 틱에 결정을 실행한 슬롯 (id 오름차순, AC-04).</summary>
        public void MarkDecided(int slot)
        {
            CheckWrite(BoardSlot.Decisions);
            _decided[_decisionCount++] = slot;
        }

        public int DecidedCount { get { CheckRead(BoardSlot.Decisions); return _decisionCount; } }
        public int DecidedAt(int i) { CheckRead(BoardSlot.Decisions); return _decided[i]; }

        public void SetBallAction(in BallAction a)
        {
            CheckWrite(BoardSlot.Decisions);
            _ballAction = a;
        }

        public ref readonly BallAction GetBallAction()
        {
            CheckRead(BoardSlot.Decisions);
            return ref _ballAction;
        }

        // Steering 명령 (단계 6)
        public void SetSteering(int slot, in SteeringCommand c)
        {
            CheckWrite(BoardSlot.Steering);
            _steering[slot] = c;
            _hasSteering[slot] = true;
        }

        public bool TryGetSteering(int slot, out SteeringCommand c)
        {
            CheckRead(BoardSlot.Steering);
            c = _steering[slot];
            return _hasSteering[slot];
        }

        // 물리 사건 (단계 7)
        public void AddPhysicsCall(in PhysicsCall c)
        {
            CheckWrite(BoardSlot.PhysicsEvents);
            if (_callCount == _calls.Length) return; // 한 틱 64건 초과는 버린다 (공 하나라 실제로는 몇 건)
            _calls[_callCount++] = c;
        }

        public int PhysicsCallCount { get { CheckRead(BoardSlot.PhysicsEvents); return _callCount; } }
        public ref readonly PhysicsCall PhysicsCallAt(int i) { CheckRead(BoardSlot.PhysicsEvents); return ref _calls[i]; }
    }
}
