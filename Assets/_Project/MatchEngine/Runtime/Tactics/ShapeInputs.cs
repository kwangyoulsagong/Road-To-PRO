using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>002 자리: 정규화(0~1) 정신 능력치·포지션 익숙함. slot은 운동 슬롯(0~21).</summary>
    public interface IPlayerShapeTraits
    {
        float Positioning(int slot);
        float Teamwork(int slot);
        float Concentration(int slot);
        float Familiarity(int slot);
    }

    /// <summary>003 자리: 역할의 위치 오프셋·규율·커버 여부.</summary>
    public interface IRoleShapeSource
    {
        /// <summary>정규화 오프셋 (팀 관점).</summary>
        Vec2 Offset(int slot, ShapeProfile profile);
        /// <summary>재배정 패널티 계수 0~1.</summary>
        float Discipline(int slot);
        /// <summary>역할 부적합 패널티 (m²).</summary>
        float RoleMismatch(int slot, int formationSlot);
        /// <summary>수비 라인 아래로 내려가도 되는 커버 역할 (FR-32 예외).</summary>
        bool IsCover(int slot);
    }

    /// <summary>007 자리: 피치 컨트롤 이득 방향 (정규화, 크기 0~1).</summary>
    public interface IPitchControlQuery
    {
        Vec2 GainDirection(int team, Vec2 perspective);
    }

    /// <summary>013 자리: 시나리오 배역 중 여부와 라인 동기 전진 신호.</summary>
    public interface IScenarioCastSource
    {
        bool IsCast(int slot);
        bool IsLineSyncAdvance(int team);
    }

    /// <summary>압박 트리거 입력 (008 자리, FR-60). 터치라인·수적 우위는 형태 쪽에서 위치로 계산한다.</summary>
    public struct PressSignals
    {
        public bool BackPassReceived;
        /// <summary>수신 터치 품질 0~1 (좋을수록 1).</summary>
        public float TouchQuality;
        public bool ReceiverFacingOwnGoal;
        public bool LoftedOrSlow;
        public bool GkShort;
        /// <summary>패서가 압박받지 않고 전방을 본다 (FR-42 drop).</summary>
        public bool PasserFreeFacingForward;
        /// <summary>패서가 압박 중 (FR-42 step-up).</summary>
        public bool PasserPressured;
        /// <summary>이번 패스의 수신자 팀 내 슬롯(0~10), 없으면 −1 (FR-50).</summary>
        public int PassReceiver;

        public static PressSignals None => new PressSignals { TouchQuality = 1f, PassReceiver = -1 };
    }

    public interface IPressSignalSource
    {
        /// <summary>압박하는 팀(pressingTeam) 관점에서 상대 공 소유자 신호를 읽는다.</summary>
        void Read(Match.MatchContext ctx, int pressingTeam, out PressSignals signals);
    }

    /// <summary>대체 능력치: 전원 같은 값.</summary>
    public sealed class ConstantShapeTraits : IPlayerShapeTraits
    {
        readonly float _v, _familiarity;
        public ConstantShapeTraits(float value = 0.5f, float familiarity = 1f) { _v = value; _familiarity = familiarity; }
        public float Positioning(int slot) => _v;
        public float Teamwork(int slot) => _v;
        public float Concentration(int slot) => _v;
        public float Familiarity(int slot) => _familiarity;
    }

    /// <summary>대체 역할: 오프셋 0, 중간 규율.</summary>
    public sealed class NeutralRoleSource : IRoleShapeSource
    {
        public Vec2 Offset(int slot, ShapeProfile profile) => default;
        public float Discipline(int slot) => 0.5f;
        public float RoleMismatch(int slot, int formationSlot) => 0f;
        public bool IsCover(int slot) => false;
    }

    /// <summary>대체 피치 컨트롤: 이득 없음.</summary>
    public sealed class ZeroPitchControl : IPitchControlQuery
    {
        public Vec2 GainDirection(int team, Vec2 perspective) => default;
    }

    /// <summary>대체 시나리오: 배역 없음.</summary>
    public sealed class NoScenarioCast : IScenarioCastSource
    {
        public bool IsCast(int slot) => false;
        public bool IsLineSyncAdvance(int team) => false;
    }

    /// <summary>대체 압박 신호: 008 전까지 운동·공 상태로 근사한다.</summary>
    public sealed class KinematicPressSignals : IPressSignalSource
    {
        const long RecentTicks = 10;
        const float FreeRadiusM = 5f;

        public void Read(Match.MatchContext ctx, int pressingTeam, out PressSignals s)
        {
            s = PressSignals.None;
            var ball = ctx.Ball;
            s.LoftedOrSlow = ball.Phase == Ball.BallPhase.Air;
            s.GkShort = ctx.Flow.Restart == Match.Flow.RestartKind.GoalKick && !ctx.Flow.InPlay;
            if (ball.OwnerId < 0) return;
            var owner = ball.OwnerId;
            var ownerTeam = Match.Squad.TeamOfSlot(owner);
            if (ownerTeam == pressingTeam) return;
            var dir = ctx.AttackDir(ownerTeam);
            ref readonly var st = ref ctx.SlotState(owner);
            s.ReceiverFacingOwnGoal = st.Facing.X * dir < 0f;
            var recent = ctx.Tick - ctx.PossessionStartTick <= RecentTicks;
            if (recent && ctx.LastTouchTeam == ownerTeam && ctx.LastTouchSlot >= 0 && ctx.LastTouchSlot != owner && ctx.IsSlotActive(ctx.LastTouchSlot))
                s.BackPassReceived = (ctx.SlotState(ctx.LastTouchSlot).Position.X - st.Position.X) * dir > 0f;

            bool pressured = false;
            for (int i = pressingTeam * Match.Squad.SlotsPerTeam; i < (pressingTeam + 1) * Match.Squad.SlotsPerTeam && !pressured; i++)
                if (ctx.IsSlotActive(i) && Vec2.DistanceSq(ctx.SlotState(i).Position, st.Position) < FreeRadiusM * FreeRadiusM) pressured = true;
            s.PasserPressured = pressured;
            s.PasserFreeFacingForward = !pressured && !s.ReceiverFacingOwnGoal;
        }
    }

    /// <summary>주입 입력 묶음. 기본값은 대체 구현이고, 002·003·007·008·013이 생기면 필드를 바꿔 끼운다.</summary>
    public sealed class ShapeInputProviders
    {
        public IPlayerShapeTraits Traits = new ConstantShapeTraits();
        public IRoleShapeSource Roles = new NeutralRoleSource();
        public IPitchControlQuery PitchControl = new ZeroPitchControl();
        public IScenarioCastSource Cast = new NoScenarioCast();
        public IPressSignalSource Signals = new KinematicPressSignals();
    }
}
