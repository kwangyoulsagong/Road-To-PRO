using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Stages;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>007 자리: 공간·수 조건 값을 <see cref="SpaceField"/> 순서로 채운다.</summary>
    public interface IScenarioSpaceQuery
    {
        void Fill(MatchContext ctx, int team, float[] values);
    }

    /// <summary>007 xT 자리: 팀 관점 위치의 기대 가치 이득.</summary>
    public interface IScenarioValueQuery
    {
        float Gain(int team, Vec2 perspective);
    }

    /// <summary>003 자리: 공 소유자·배역의 성향·버릇이 이 시나리오를 얼마나 선호하는지 (배수, 기본 1).</summary>
    public interface IScenarioTraitSource
    {
        float Bonus(int slot, ScenarioDefinition def);
    }

    /// <summary>003 자리: 슬롯이 채울 수 있는 역할 비트.</summary>
    public interface IScenarioRoleSource
    {
        PositionMask RoleOf(MatchContext ctx, int slot);
    }

    /// <summary>002 자리: 판단력 정규화 값 0~1 (softmax 온도).</summary>
    public interface IScenarioJudgement
    {
        float Decisions(int slot);
    }

    /// <summary>008 자리: 대체 입력으로 감지할 수 없는 트리거 이벤트를 주입한다 (나쁜 터치 등).</summary>
    public interface IScenarioEventSource
    {
        TriggerEventMask Extra(long tick, int team);
    }

    /// <summary>
    /// 주입 입력 묶음. 구현은 순수 함수여야 한다: 같은 상태에서 호출 이력과 무관하게 같은 값을 돌려준다.
    /// 재시뮬·클립 복원이 같은 인스턴스를 공유하므로 내부 상태를 두면 결과가 갈린다.
    /// 기본값은 대체 구현이고 해당 스펙이 생기면 필드를 바꿔 끼운다.</summary>
    public sealed class ScenarioInputProviders
    {
        public IScenarioSpaceQuery Space = new GeometricSpaceQuery();
        public IScenarioValueQuery Value = new SimpleValueQuery();
        public IScenarioTraitSource Traits = new NeutralTraitSource();
        /// <summary>null이면 009 포메이션 슬롯에서 유도한다.</summary>
        public IScenarioRoleSource Roles;
        public IScenarioJudgement Judgement = new ConstantJudgement();
        public IScenarioEventSource Events;
    }

    /// <summary>대체 성향: 전원 중립.</summary>
    public sealed class NeutralTraitSource : IScenarioTraitSource
    {
        public float Bonus(int slot, ScenarioDefinition def) => 1f;
    }

    /// <summary>대체 판단력: 전원 같은 값.</summary>
    public sealed class ConstantJudgement : IScenarioJudgement
    {
        readonly float _v;
        public ConstantJudgement(float value = 0.5f) => _v = value;
        public float Decisions(int slot) => _v;
    }

    /// <summary>대체 xT: 상대 골대에 가까울수록 남은 이득이 작다.</summary>
    public sealed class SimpleValueQuery : IScenarioValueQuery
    {
        public float Gain(int team, Vec2 perspective) => 0.1f * (1f - perspective.X);
    }

    /// <summary>대체 역할: 009 포메이션 슬롯의 기본 포지션. 배역 중이라 슬롯이 비면 None이고, 마지막 값은 런타임이 스냅샷과 함께 보관한다.</summary>
    public sealed class FormationRoleSource : IScenarioRoleSource
    {
        readonly ShapeStage _shape;

        public FormationRoleSource(ShapeStage shape) => _shape = shape;

        public PositionMask RoleOf(MatchContext ctx, int slot)
        {
            var team = Squad.TeamOfSlot(slot);
            var engine = _shape.Engine(team);
            var idx = slot % Squad.SlotsPerTeam;
            var fs = engine.FormationSlotOf(idx);
            if (Squad.IsKeeperSlot(slot)) return PositionMask.GK;
            if (fs >= 0 && engine.Formation != null && fs < engine.Formation.Slots.Length)
                return Positions.FromFormationPos(engine.Formation.Slots[fs].Position);
            return PositionMask.None;
        }
    }

    /// <summary>대체 공간: 007 전까지 기하 계산으로 근사한다.</summary>
    public sealed class GeometricSpaceQuery : IScenarioSpaceQuery
    {
        const float CorridorM = 6f, UnmarkedM = 5f;

        public void Fill(MatchContext ctx, int team, float[] v)
        {
            System.Array.Clear(v, 0, v.Length);
            ref readonly var ball = ref ctx.Ball;
            var owner = ball.Phase == BallPhase.Controlled ? ball.OwnerId : -1;
            var attackTeam = owner >= 0 ? Squad.TeamOfSlot(owner) : team;
            var defendTeam = 1 - attackTeam;
            var dir = ctx.AttackDir(attackTeam);
            var from = owner >= 0 ? ctx.SlotState(owner).Position : ball.Position.XY;
            var goalX = dir > 0 ? ctx.Pitch.LengthM : 0f;
            var ahead = (goalX - from.X) * dir;
            float nearestAhead = ahead < 0f ? 0f : ahead, nearest = float.MaxValue;
            int defenders = 0, attackers = 0;
            var unmarked = false;

            for (int s = defendTeam * Squad.SlotsPerTeam; s < (defendTeam + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!ctx.IsSlotActive(s) || Squad.IsKeeperSlot(s)) continue;
                var p = ctx.SlotState(s).Position;
                var dx = (p.X - from.X) * dir;
                var dy = p.Y - from.Y;
                var abs = dy < 0f ? -dy : dy;
                if (dx > 0f)
                {
                    defenders++;
                    if (abs <= CorridorM && dx < nearestAhead) nearestAhead = dx;
                }
                var d = (p - from).Length();
                if (d < nearest) nearest = d;
            }
            for (int s = attackTeam * Squad.SlotsPerTeam; s < (attackTeam + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!ctx.IsSlotActive(s) || s == owner) continue;
                var p = ctx.SlotState(s).Position;
                if ((p.X - from.X) * dir <= 0f) continue;
                attackers++;
                var marked = false;
                for (int o = defendTeam * Squad.SlotsPerTeam; o < (defendTeam + 1) * Squad.SlotsPerTeam && !marked; o++)
                    if (ctx.IsSlotActive(o) && Vec2.DistanceSq(ctx.SlotState(o).Position, p) < UnmarkedM * UnmarkedM) marked = true;
                if (!marked) unmarked = true;
            }

            var line = ctx.Board.OffsideLineM(defendTeam);
            var behind = (goalX - line) * dir;
            v[(int)SpaceField.SpaceAheadM] = nearestAhead;
            v[(int)SpaceField.FbFrontSpaceM] = nearestAhead;
            v[(int)SpaceField.BehindSpaceM] = behind < 0f ? 0f : behind;
            v[(int)SpaceField.NearestPresserDistM] = nearest == float.MaxValue ? 100f : nearest;
            v[(int)SpaceField.LineHeight] = ctx.Board.ShapeOutput(defendTeam).DefLineX;
            v[(int)SpaceField.DefendersAhead] = defenders;
            v[(int)SpaceField.AttackersAhead] = attackers;
            v[(int)SpaceField.P3UnmarkedInFront] = unmarked ? 1f : 0f;
        }
    }
}
