using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Match.Flow
{
    /// <summary>
    /// 킥오프 배치 계산 (specs/006 FR-11, 058 FR-02·10). 경기·하프 시작은 이 지점에 바로 놓고(FR-10), 골 뒤에는 걸어서 가는 목표로 쓴다.
    /// 각 팀 대형을 자기 진영으로 압축, 상대는 센터서클 밖, 키커는 중앙. 골키퍼는 골문 앞 그대로.
    /// </summary>
    public static class KickoffLayout
    {
        /// <summary>센터서클 밖 배치 반경 (9.15 m + 여유).</summary>
        public const float CircleClear = 9.6f;
        /// <summary>킥오프 규칙 판정 반경 (9.15 m).</summary>
        public const float CircleRadius = 9.15f;

        public static Vec2 Center(MatchContext ctx) => new Vec2(ctx.Pitch.LengthM * 0.5f, ctx.Pitch.WidthM * 0.5f);

        /// <summary>키커 슬롯: 킥오프 팀에서 대형 x가 가장 앞선 필드 선수, 없으면 첫 활성 슬롯. 없으면 −1.</summary>
        public static int Kicker(MatchContext ctx, int kickTeam)
        {
            int kicker = -1;
            float bestX = -1f;
            var form = ctx.Setup.Team(kickTeam).Formation;
            for (int i = 1; i < Squad.SlotsPerTeam; i++)
            {
                var s = kickTeam * Squad.SlotsPerTeam + i;
                if (!ctx.IsSlotControllable(s)) continue;
                if (form[i].X > bestX) { bestX = form[i].X; kicker = s; }
            }
            if (kicker < 0)
                for (int s = kickTeam * Squad.SlotsPerTeam; s < (kickTeam + 1) * Squad.SlotsPerTeam; s++)
                    if (ctx.IsSlotControllable(s)) { kicker = s; break; }
            return kicker;
        }

        /// <summary>슬롯 s의 킥오프 지점.</summary>
        public static Vec2 Target(MatchContext ctx, int kickTeam, int kicker, int s)
        {
            var center = Center(ctx);
            var team = Squad.TeamOfSlot(s);
            var f = ctx.Setup.Team(team).Formation[s % Squad.SlotsPerTeam];
            var dir = ctx.AttackDir(team);
            if (s == kicker) return new Vec2(center.X - dir * 0.3f, center.Y);
            var pos = ctx.FromTeamPerspective(team, new Vec2(Squad.IsKeeperSlot(s) ? f.X : f.X * 0.47f, f.Y));
            if (team != kickTeam)
            {
                var d = pos - center;
                var l = d.Length();
                if (l < CircleClear)
                {
                    var away = l < 1e-3f ? new Vec2(-dir, 0f) : d / l;
                    pos = center + away * CircleClear;
                    if ((pos.X - center.X) * dir > -0.5f) pos = new Vec2(center.X - dir * CircleClear, pos.Y);
                }
            }
            return pos;
        }

        /// <summary>
        /// 킥오프 규칙 위반 (058 FR-03): 자기 진영 밖, 또는 킥오프 팀이 아닌데 센터서클 안. 키커는 중앙 점에 서므로 검사하지 않는다.
        /// </summary>
        public static bool Violates(MatchContext ctx, int kickTeam, int kicker, int s, Vec2 pos)
        {
            if (s == kicker) return false;
            var center = Center(ctx);
            var team = Squad.TeamOfSlot(s);
            var dir = ctx.AttackDir(team);
            if ((pos.X - center.X) * dir > 0f) return true;
            return team != kickTeam && (pos - center).LengthSq() < CircleRadius * CircleRadius;
        }
    }
}
