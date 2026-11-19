using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>오프사이드 위치 판정 결과 (specs/012 FR-01·02). d &gt; 0이고 상대 진영이면 오프사이드 위치.</summary>
    public readonly struct OffsidePosition
    {
        /// <summary>마진 d = 공격수 몸 앞쪽 끝 깊이 − max(뒤에서 두 번째 수비수 깊이, 공 깊이) (m, 양수 = 골라인 쪽).</summary>
        public readonly float D;
        /// <summary>공격수 몸 앞쪽 끝이 하프라인을 넘었다 (하프라인 위 정확히 = 자기 진영).</summary>
        public readonly bool InOpponentHalf;
        /// <summary>판정 기준선 (뒤에서 두 번째 수비수 몸 앞쪽 끝 x, 공이 더 깊으면 공 x).</summary>
        public readonly float LineX;

        public OffsidePosition(float d, bool inHalf, float lineX) { D = d; InOpponentHalf = inHalf; LineX = lineX; }

        public bool IsOffside => InOpponentHalf && D > 0f;
    }

    /// <summary>
    /// 오프사이드 위치·면제 판정 (specs/012 FR-01~03, IFAB Law 11). 상태 없음·RNG 없음.
    /// 몸 기준점(플랜 P11): 시뮬은 2D 원점뿐이라 머리·몸통·발 중 가장 앞 = 위치 + 공격 방향 × <c>ref.off.bodyReach</c>. 팔은 넣지 않는다.
    /// </summary>
    public static class OffsideJudge
    {
        /// <summary>몸 앞쪽 끝 x (attTeam 공격 방향 기준). 팔 오프셋은 판정 부위가 아니다 (FR-01) — 받지 않는다.</summary>
        public static float FrontX(float x, int attackDir, float bodyReach) => x + attackDir * bodyReach;

        /// <summary>
        /// 뒤에서 두 번째 수비수(GK 포함)의 몸 앞쪽 끝 x — 수비 팀 골라인 쪽 기준(공격 방향으로 깊은 순). 활성 수비수가 2명 미만이면 골라인.
        /// 골라인 밖에 있는 수비수는 골라인 위로 본다 (플랜 P12). runnerX가 NaN이면 비활성.
        /// </summary>
        public static float SecondLastDefenderX(System.ReadOnlySpan<float> runnerX, int defTeam, int attackDir, float pitchL, float bodyReach)
        {
            var goalLine = attackDir > 0 ? pitchL : 0f;
            var goalDepth = goalLine * attackDir;
            float last = float.MinValue, second = float.MinValue;
            var from = defTeam * RefereeWorld.PerTeam;
            for (int s = from; s < from + RefereeWorld.PerTeam; s++)
            {
                var x = runnerX[s];
                if (x - x != 0f) continue; // NaN = 비활성
                // 수비수의 "골라인 쪽" 몸 끝 = 공격 방향으로 bodyReach 앞 (공격수 기준 같은 부위 비교)
                var depth = FrontX(x, attackDir, bodyReach) * attackDir;
                if (depth > goalDepth) depth = goalDepth;
                if (depth > last) { second = last; last = depth; }
                else if (depth > second) second = depth;
            }
            if (second == float.MinValue) return goalLine;
            return second * attackDir;
        }

        /// <summary>FR-01·02: 공격수 위치 판정. 하프라인은 자기 진영(정확히 위 = 온사이드), d = 0은 온사이드.</summary>
        public static OffsidePosition Position(float attackerX, float ballX, float secondLastX, int attackDir, float pitchL, float bodyReach)
        {
            var front = FrontX(attackerX, attackDir, bodyReach) * attackDir;
            var line = secondLastX * attackDir;
            var ball = ballX * attackDir;
            var refDepth = line > ball ? line : ball;
            var half = pitchL * 0.5f * attackDir;
            var inHalf = front > half;
            return new OffsidePosition(front - refDepth, inHalf, refDepth * attackDir);
        }

        /// <summary>FR-03: 스로인·골킥·코너킥에서 직접 받으면 반칙 아님.</summary>
        public static bool ExemptRestart(Match.Flow.RestartKind kind) =>
            kind == Match.Flow.RestartKind.ThrowIn || kind == Match.Flow.RestartKind.GoalKick || kind == Match.Flow.RestartKind.Corner;
    }
}
