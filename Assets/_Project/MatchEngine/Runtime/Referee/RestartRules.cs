using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Flow;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>재시작 결정 (종류·팀·위치) — specs/012 FR-61~67.</summary>
    public readonly struct RestartDecision
    {
        public readonly RestartKind Kind;
        public readonly int Team;
        public readonly Vec2 Spot;
        /// <summary>프리킥이면 직접(true)·간접(false).</summary>
        public readonly bool Direct;

        public RestartDecision(RestartKind kind, int team, Vec2 spot, bool direct = true) { Kind = kind; Team = team; Spot = spot; Direct = direct; }
    }

    /// <summary>
    /// 재시작 규칙 표 (specs/012 FR-60~66, IFAB Law 10·13~17). 상태 없음·RNG 없음.
    /// 좌표: 피치 (0,0)~(L,W). attackDir = 팀별 공격 방향.
    /// </summary>
    public static class RestartRules
    {
        public const float GoalAreaDepth = 5.5f, GoalAreaHalfWidth = 9.16f, PenaltySpotDist = 11f;
        public const float BoxDepth = 16.5f, BoxHalfWidth = 20.16f;

        /// <summary>
        /// FR-61: 라인 아웃 (득점 아님). 골라인 — 마지막 터치가 수비 팀이면 코너(가까운 코너 아크 0.5 m 안쪽), 공격 팀이면 골킥(골 에어리어 앞 5.5 m 중앙).
        /// 터치라인 — 마지막 터치 반대 팀 스로인(라인 위, 끝 0.5 m 안쪽으로 자름). lastTouchTeam &lt; 0이면 팀 0 터치로 본다(대체 단계와 같음).
        /// </summary>
        public static RestartDecision Out(BallBoundary boundary, Vec2 pos, int lastTouchTeam, int attackDir0, float pitchL, float pitchW)
        {
            var last = lastTouchTeam < 0 ? 0 : lastTouchTeam;
            if (boundary == BallBoundary.GoalLineLow || boundary == BallBoundary.GoalLineHigh)
            {
                var lineX = boundary == BallBoundary.GoalLineLow ? 0f : pitchL;
                var defending = (attackDir0 > 0) == (lineX == 0f) ? 0 : 1;
                var attacking = 1 - defending;
                var inward = lineX == 0f ? 1f : -1f;
                if (last == defending)
                    return new RestartDecision(RestartKind.Corner, attacking, new Vec2(lineX + inward * 0.5f, pos.Y < pitchW * 0.5f ? 0.5f : pitchW - 0.5f));
                return new RestartDecision(RestartKind.GoalKick, defending, new Vec2(lineX + inward * GoalAreaDepth, pitchW * 0.5f));
            }
            var y = boundary == BallBoundary.TouchlineLow ? 0f : pitchW;
            var x = pos.X < 0.5f ? 0.5f : (pos.X > pitchL - 0.5f ? pitchL - 0.5f : pos.X);
            return new RestartDecision(RestartKind.ThrowIn, 1 - last, new Vec2(x, y));
        }

        /// <summary>FR-60: 공 전체가 골라인을 넘었다 — 공 중심이 라인 밖으로 반지름 이상 (004 경계 판정과 같은 기준). 일부만 넘음 = 아님.</summary>
        public static bool WhollyOver(float ballX, float radius, float lineX, int outwardDir) => (ballX - lineX) * outwardDir >= radius;

        /// <summary>팀 team이 공격하는 골 기준 페널티 박스 안인지 (그 골을 지키는 팀 박스).</summary>
        public static bool InBoxOfGoal(Vec2 p, Vec2 goal, int attackDirTowardGoal)
        {
            var depth = (goal.X - p.X) * attackDirTowardGoal;
            return depth >= 0f && depth <= BoxDepth && DMath.Abs(p.Y - goal.Y) <= BoxHalfWidth;
        }

        /// <summary>
        /// FR-62: 반칙 재시작. awardedTeam = 반칙당한 팀, oppGoal = awardedTeam이 공격하는 골. 직접 FK 반칙이 상대 박스 안이면 PK.
        /// 간접 FK가 상대 골 에어리어 안이면 골 에어리어 선(골라인과 평행) 위 가장 가까운 점으로 옮긴다. 자기 골 에어리어 안 FK는 그대로(어디서든 차도 됨).
        /// 위치는 피치 안으로 자른다.
        /// </summary>
        public static RestartDecision Foul(Vec2 spot, int awardedTeam, Vec2 oppGoal, int attackDir, bool direct, float pitchL, float pitchW)
        {
            spot = Clamp(spot, pitchL, pitchW);
            if (direct && InBoxOfGoal(spot, oppGoal, attackDir))
                return new RestartDecision(RestartKind.Penalty, awardedTeam, PenaltySpot(oppGoal, attackDir), true);
            if (!direct)
            {
                var depth = (oppGoal.X - spot.X) * attackDir;
                if (depth >= 0f && depth < GoalAreaDepth && DMath.Abs(spot.Y - oppGoal.Y) <= GoalAreaHalfWidth)
                    spot = new Vec2(oppGoal.X - attackDir * GoalAreaDepth, spot.Y);
            }
            return new RestartDecision(RestartKind.FreeKick, awardedTeam, spot, direct);
        }

        public static Vec2 PenaltySpot(Vec2 oppGoal, int attackDir) => new Vec2(oppGoal.X - attackDir * PenaltySpotDist, oppGoal.Y);

        /// <summary>
        /// FR-63: GK 손 보유 초과 (&gt; 기준, 정확히 기준 = 위반 아님). 규칙 세트가 코너면 상대 코너(GK 쪽 가까운 코너), 아니면 상대 간접 FK(GK 위치).
        /// </summary>
        public static bool KeeperHoldViolation(float seconds, float max) => seconds > max;
        /// <summary>074 FR-14: 서브스텝 정수 판정 한계 = round(max / dt) (8 s · 0.01 s → 800).</summary>
        public static long KeeperHoldLimit(float max, float dt) => (long)DMath.Floor(max / dt + 0.5f);
        /// <summary>074 FR-14: 보유 서브스텝이 한계를 넘었나 (정확히 800 = 8.00 s는 위반 아님).</summary>
        public static bool KeeperHoldViolation(long heldSubsteps, long limit) => heldSubsteps > limit;

        public static RestartDecision KeeperHold(RuleSet rules, int keeperTeam, Vec2 keeperPos, Vec2 ownGoal, int keeperAttackDir, float pitchL, float pitchW)
        {
            var opp = 1 - keeperTeam;
            if (rules.KeeperHoldCorner)
            {
                var inward = ownGoal.X == 0f ? 1f : -1f;
                return new RestartDecision(RestartKind.Corner, opp, new Vec2(ownGoal.X + inward * 0.5f, keeperPos.Y < pitchW * 0.5f ? 0.5f : pitchW - 0.5f));
            }
            return Foul(keeperPos, opp, ownGoal, -keeperAttackDir, false, pitchL, pitchW);
        }

        /// <summary>FR-64: 백패스(동료의 의도적 발 패스·스로인을 GK가 자기 박스 안에서 손으로) → 상대 간접 FK (GK 위치, 골 에어리어 보정).</summary>
        public static RestartDecision BackPass(int keeperTeam, Vec2 keeperPos, Vec2 ownGoal, int keeperAttackDir, float pitchL, float pitchW) =>
            Foul(keeperPos, 1 - keeperTeam, ownGoal, -keeperAttackDir, false, pitchL, pitchW);

        /// <summary>FR-65: 드롭볼 — 페널티 박스 안 중단이면 그 박스 GK에게, 아니면 마지막 터치 팀에게 그 자리에서.</summary>
        public static RestartDecision DropBall(Vec2 at, int lastTouchTeam, int attackDir0, float pitchL, float pitchW)
        {
            var goalLow = new Vec2(0f, pitchW * 0.5f);
            var goalHigh = new Vec2(pitchL, pitchW * 0.5f);
            // 골 낮은 쪽을 지키는 팀: attackDir0 > 0이면 팀 0
            var lowDef = attackDir0 > 0 ? 0 : 1;
            if (InBoxOfGoal(at, goalLow, -1)) return new RestartDecision(RestartKind.DropBall, lowDef, at);
            if (InBoxOfGoal(at, goalHigh, 1)) return new RestartDecision(RestartKind.DropBall, 1 - lowDef, at);
            return new RestartDecision(RestartKind.DropBall, lastTouchTeam < 0 ? 0 : lastTouchTeam, Clamp(at, pitchL, pitchW));
        }

        /// <summary>FR-66: PK 재차기 — 킥 순간 GK 라인 이탈 또는 침범이 있었고 득점 실패(결과에 영향)면 재차기. 득점이면 그대로.</summary>
        public static bool PenaltyRetake(bool keeperOffLine, bool encroachment, bool scored) => (keeperOffLine || encroachment) && !scored;

        static Vec2 Clamp(Vec2 p, float L, float W) => new Vec2(p.X < 0f ? 0f : (p.X > L ? L : p.X), p.Y < 0f ? 0f : (p.Y > W ? W : p.Y));
    }
}
