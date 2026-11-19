using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>득점 기회 저지 판정 결과 (specs/012 FR-23·24·26).</summary>
    public enum OpportunityKind : byte
    {
        None = 0,
        /// <summary>유망한 공격 저지 (SPA) → 경고.</summary>
        Spa = 1,
        /// <summary>명백한 득점 기회 저지 (DOGSO).</summary>
        Dogso = 2,
    }

    /// <summary>
    /// DOGSO·SPA 판정 (specs/012 FR-23~26, IFAB Law 12). 상태 없음·RNG 없음.
    /// DOGSO 4요소 (scenarios 03 § 5.2): 골까지 &lt; 30 m ∧ 골 방향 cos &gt; 0.7 ∧ 공 통제 확률 &gt; 0.7 ∧ 공–골 사이 수비수 0명.
    /// 골 방향 cos = 공격자 이동 방향(느리면 몸 방향)과 골 방향의 코사인 — 접촉 상세에 없어 판정 순간 공격자 상태에서 구한다.
    /// </summary>
    public static class DogsoJudge
    {
        /// <summary>공격자 진행 방향과 골 방향의 cos. 속도 0.5 m/s 미만이면 몸 방향.</summary>
        public static float GoalCos(Vec2 attackerPos, Vec2 attackerVel, Vec2 facing, Vec2 goal)
        {
            var g = goal - attackerPos;
            var gl = g.Length();
            if (gl < 1e-3f) return 1f;
            var dir = attackerVel.LengthSq() >= 0.25f ? attackerVel : facing;
            var dl = dir.Length();
            if (dl < 1e-4f) return 0f;
            return (g.X * dir.X + g.Y * dir.Y) / (gl * dl);
        }

        public static OpportunityKind Classify(RefereeParams p, in ContactDetail c, float goalCos)
        {
            if (!c.DogsoValid) return OpportunityKind.None;
            if (c.GoalDist < p.DogsoDist && goalCos > p.DogsoCos && c.ControlLikely > p.DogsoControl && c.DefendersBetween == 0) return OpportunityKind.Dogso;
            if (c.GoalDist < p.SpaDist && goalCos > p.SpaCos && c.ControlLikely > p.SpaControl && c.DefendersBetween <= (int)p.SpaDefenders) return OpportunityKind.Spa;
            return OpportunityKind.None;
        }

        /// <summary>
        /// 반칙 판정 단계에 기회 저지를 반영한다 (FR-24·26). foulDecision ≥ 파울일 때만. 반환 = 최종 단계, outPenalty = 박스 안이라 PK.
        /// DOGSO 박스 밖 → 퇴장. 박스 안 + 공을 향한 도전(<paramref name="genuineChallenge"/>) → PK + 경고(한 단계 완화), 그 밖(잡기·밀기) → PK + 퇴장.
        /// SPA → 최소 경고(박스 안 공을 향한 도전이면 PK만, 경고 없음 — Law 12 완화).
        /// 어드밴티지 후 골(<paramref name="advantageGoal"/>, 규칙 세트 FR-25)이면 DOGSO·SPA 경고 없음.
        /// 이미 더 무거운 단계(과도한 힘 → 퇴장)는 그대로.
        /// </summary>
        public static int Apply(int foulDecision, OpportunityKind kind, bool inBox, bool genuineChallenge, bool advantageGoal, RuleSet rules, out int reason)
        {
            reason = foulDecision == EventCodes.DecisionRed ? EventCodes.ReasonSeriousFoulPlay : foulDecision == EventCodes.DecisionYellow ? EventCodes.ReasonReckless : 0;
            if (foulDecision == EventCodes.DecisionNone || kind == OpportunityKind.None) return foulDecision;
            if (advantageGoal && rules.NoDogsoCautionAfterAdvantageGoal) return foulDecision;
            int d;
            if (kind == OpportunityKind.Dogso)
            {
                d = inBox && genuineChallenge ? EventCodes.DecisionYellow : EventCodes.DecisionRed;
                if (foulDecision > d) return foulDecision;
                reason = EventCodes.ReasonDogso;
                return d;
            }
            if (inBox && genuineChallenge) return foulDecision; // SPA 박스 안 공 도전: PK만
            d = EventCodes.DecisionYellow;
            if (foulDecision >= d) return foulDecision;
            reason = EventCodes.ReasonSpa;
            return d;
        }

        /// <summary>공을 향한 도전인가 (박스 안 완화 조건): 태클·경합(공 다툼)은 도전, 몸싸움 접촉이 공과 무관하면(잡기) 아님.</summary>
        public static bool GenuineChallenge(in ContactDetail c) => c.Kind == ContactKind.Tackle || c.Kind == ContactKind.Aerial || c.Kind == ContactKind.TakeOn;
    }
}
