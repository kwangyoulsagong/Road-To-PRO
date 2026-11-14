using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 압박 트리거 입력 플래그 (specs/070 FR-14). 점수 결합·압박 인원은 009 FR-60~61, 개인 실행 여부는 010이 정한다 — 여기서는 플래그만.
    /// 수신 이벤트 B의 bit3~7(<see cref="EventShift"/>)과 <see cref="ActionSystem.CarrierTriggers"/>(상태)로 제공한다.
    /// </summary>
    public static class PressTriggers
    {
        public const byte BackToGoal = 1, HeavyTouch = 2, BackPass = 4, NearTouchline = 8, OppHalf = 16;
        public const int EventShift = 3;
        /// <summary>등진 수신: 몸 방향과 공격 방향의 내적이 이 값 미만 [대체].</summary>
        public const float BackToGoalDot = -0.3f;
        public const float TouchlineBand = 3f;

        /// <summary>수신 순간 플래그. heavy = 첫 터치 이격 &gt; 1.5 m(008 FR-67), backPass = 직전 패스가 자기 골 쪽으로 간 동료 패스.</summary>
        public static byte OnReception(Vec2 pos, Vec2 facing, int attackDir, float pitchL, float pitchW, bool heavy, bool backPass)
        {
            byte f = Positional(pos, attackDir, pitchL, pitchW);
            if (facing.X * attackDir < BackToGoalDot) f |= BackToGoal;
            if (heavy) f |= HeavyTouch;
            if (backPass) f |= BackPass;
            return f;
        }

        /// <summary>위치로만 정해지는 플래그 (매 틱 다시 계산 가능): 터치라인 3 m 안, 상대 진영.</summary>
        public static byte Positional(Vec2 pos, int attackDir, float pitchL, float pitchW)
        {
            byte f = 0;
            if (pos.Y < TouchlineBand || pos.Y > pitchW - TouchlineBand) f |= NearTouchline;
            if ((pos.X - pitchL * 0.5f) * attackDir > 0f) f |= OppHalf;
            return f;
        }

        /// <summary>백패스: 패스 진행 방향이 패서 팀 공격 방향과 반대(x 성분 &lt; 0).</summary>
        public static bool IsBackPass(Vec2 from, Vec2 to, int attackDir) => (to.X - from.X) * attackDir < 0f;
    }
}
