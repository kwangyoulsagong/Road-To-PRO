using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Match.Stages.StandIn
{
    /// <summary>
    /// 단계 2 대체 (009 자리, FR-04): 대형 슬롯을 공 쪽으로 일정 비율 옮긴 목표 좌표. 골키퍼는 골 앞에 머문다.
    /// </summary>
    public sealed class HoldShapeStage : StatelessStage
    {
        public override StageId Id => StageId.Shape;

        public override void Execute(MatchContext ctx)
        {
            var targets = ctx.Board.ShapeTargetsForWrite();
            var p = ctx.Params;
            var ballN = PitchCoords.ToNormalized(ctx.Ball.Position.XY, ctx.Pitch);
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (!ctx.IsSlotActive(s)) continue;
                var team = Squad.TeamOfSlot(s);
                var side = team == 0 ? TeamSide.Home : TeamSide.Away;
                var b = PitchCoords.ToPerspective(ballN, side, ctx.Flow.Period);
                var f = ctx.Setup.Team(team).Formation[s % Squad.SlotsPerTeam];
                float x, y;
                if (Squad.IsKeeperSlot(s))
                {
                    x = Clamp(f.X + 0.1f * (b.X - 0.5f), 0.01f, 0.12f);
                    y = Clamp(0.5f + 0.25f * (b.Y - 0.5f), 0.42f, 0.58f);
                }
                else
                {
                    x = Clamp(f.X + p.ShapeShiftX * (b.X - 0.5f), 0.03f, 0.97f);
                    y = Clamp(f.Y + p.ShapeShiftY * (b.Y - 0.5f), 0.03f, 0.97f);
                }
                targets[s] = ctx.FromTeamPerspective(team, new Vec2(x, y));
            }
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
