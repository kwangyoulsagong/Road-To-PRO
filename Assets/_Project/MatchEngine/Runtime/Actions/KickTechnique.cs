using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>킥 기술 (FR-66 [대체] 목록). 번호는 이벤트 A에 들어가므로 바꾸지 않는다.</summary>
    public enum KickTech : byte
    {
        Inside = 0,
        Instep = 1,
        Outside = 2,
        Chip = 3,
    }

    /// <summary>킥 계획: 사용 발·기술·몸–킥 방향 차이·준비 시간·σ 배수.</summary>
    public struct KickPlan
    {
        public BodyPart Foot;
        public bool WeakFoot;
        public KickTech Tech;
        /// <summary>접촉 순간 몸 방향과 킥 방향 차이 (rad, 0~π).</summary>
        public float BodyAngle;
        public float Windup;
        /// <summary>실행 오차 σ 공통 배수 (각도 임계·약발).</summary>
        public float SigmaMul;
    }

    /// <summary>
    /// 킥 기술·사용 발 결정 (specs/008 FR-66·71). 성향 → 기술 선택 매핑은 [미정]이라 규칙 대체:
    /// 뜬공 → 짧으면 Chip, 길면 Instep / 땅볼 ≥ instepSpeed → Instep / 몸–킥 차이 > 임계 → Outside / 나머지 Inside.
    /// 발: 목표가 몸 기준 주발 쪽이거나 약발 등급 ≤ 2면 주발, 아니면 반대 발(약발). 순수 함수.
    /// </summary>
    public static class KickTechnique
    {
        public static KickPlan Plan(ActionParams p, Vec2 facing, Vec2 kickDir, bool lofted, float distance, float speed, Foot preferred, int weakFootGrade)
        {
            var plan = default(KickPlan);
            var dot = facing.X * kickDir.X + facing.Y * kickDir.Y;
            var cross = facing.X * kickDir.Y - facing.Y * kickDir.X;
            plan.BodyAngle = DMath.Atan2(DMath.Abs(cross), dot);
            if (plan.BodyAngle < 0f) plan.BodyAngle = 0f;
            var natural = cross > 0f ? Foot.Left : Foot.Right; // 목표가 몸 왼쪽이면 왼발이 자연스럽다
            var foot = natural == preferred || weakFootGrade <= 2 ? preferred : natural;
            plan.WeakFoot = foot != preferred;
            plan.Foot = foot == Foot.Left ? BodyPart.LeftFoot : BodyPart.RightFoot;
            var thresh = p.KickAngleThresh * ActionMath.DegToRad;
            if (lofted) plan.Tech = distance <= p.ChipMaxDist ? KickTech.Chip : KickTech.Instep;
            else if (plan.BodyAngle > thresh) plan.Tech = KickTech.Outside;
            else if (speed >= p.InstepSpeed) plan.Tech = KickTech.Instep;
            else plan.Tech = KickTech.Inside;
            plan.Windup = BaseWindup(p, plan.Tech) + p.WindupTurnK * plan.BodyAngle;
            var mul = plan.BodyAngle > thresh ? p.KickSigmaMul : 1f;
            if (plan.WeakFoot) mul *= 1f + p.WeakFootK * (5 - weakFootGrade) / 4f;
            plan.SigmaMul = mul;
            return plan;
        }

        public static float BaseWindup(ActionParams p, KickTech t) => t switch
        {
            KickTech.Instep => p.WindupInstep,
            KickTech.Outside => p.WindupOutside,
            KickTech.Chip => p.WindupChip,
            _ => p.WindupInside,
        };
    }
}
