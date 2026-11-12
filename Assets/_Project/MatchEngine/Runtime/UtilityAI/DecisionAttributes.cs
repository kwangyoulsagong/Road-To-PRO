using System;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 판단 AI 정신·성격 입력 (내부 0~100, specs/010 FR-21·23·83~86·93). 002 완료 전 대체 입력 — <c>KinematicAttributes</c>·<c>KeeperAttributes</c>와 같은 방식으로
    /// 호출자가 채우고, 002가 나중에 선수 도메인에서 채운다. Anticipation·Concentration은 005 운동 능력치 값을 쓴다(이중 정의 금지).
    /// 범위 밖·NaN은 생성 시 거부. <see cref="IsSet"/>이 false(기본값)이면 <see cref="Average"/>로 읽는다.
    /// </summary>
    public readonly struct DecisionAttributes
    {
        public const float InternalMax = 100f;

        public readonly bool IsSet;
        public readonly float Decisions, Vision, Composure, OffBall, Teamwork, WorkRate;
        public readonly float Temperament, Professionalism, Ambition, Leadership;
        /// <summary>포지션 친숙도 (100 = 원래 포지션, FR-23, 002 제공).</summary>
        public readonly float Familiarity;

        public DecisionAttributes(float decisions, float vision, float composure, float offBall, float teamwork, float workRate,
            float temperament, float professionalism, float ambition, float leadership, float familiarity = 100f)
        {
            Decisions = Check(decisions, nameof(decisions));
            Vision = Check(vision, nameof(vision));
            Composure = Check(composure, nameof(composure));
            OffBall = Check(offBall, nameof(offBall));
            Teamwork = Check(teamwork, nameof(teamwork));
            WorkRate = Check(workRate, nameof(workRate));
            Temperament = Check(temperament, nameof(temperament));
            Professionalism = Check(professionalism, nameof(professionalism));
            Ambition = Check(ambition, nameof(ambition));
            Leadership = Check(leadership, nameof(leadership));
            Familiarity = Check(familiarity, nameof(familiarity));
            IsSet = true;
        }

        /// <summary>모든 값 50, 친숙도 100 (지정 안 된 선수).</summary>
        public static readonly DecisionAttributes Average = new DecisionAttributes(50f, 50f, 50f, 50f, 50f, 50f, 50f, 50f, 50f, 50f, 100f);

        /// <summary>표시값 N(1~20)으로 정신 6종을 채운다. 성격은 50, 친숙도 100.</summary>
        public static DecisionAttributes UniformMental(int display)
        {
            var v = Kinematics.KinematicAttributes.InternalFromDisplay(display);
            return new DecisionAttributes(v, v, v, v, v, v, 50f, 50f, 50f, 50f, 100f);
        }

        /// <summary>지정 안 됐으면 평균값.</summary>
        public DecisionAttributes OrAverage() => IsSet ? this : Average;

        public DecisionAttributes With(float? decisions = null, float? vision = null, float? composure = null, float? offBall = null, float? teamwork = null,
            float? workRate = null, float? temperament = null, float? professionalism = null, float? ambition = null, float? leadership = null, float? familiarity = null)
        {
            var b = OrAverage();
            return new DecisionAttributes(decisions ?? b.Decisions, vision ?? b.Vision, composure ?? b.Composure, offBall ?? b.OffBall, teamwork ?? b.Teamwork,
                workRate ?? b.WorkRate, temperament ?? b.Temperament, professionalism ?? b.Professionalism, ambition ?? b.Ambition, leadership ?? b.Leadership,
                familiarity ?? b.Familiarity);
        }

        /// <summary>정규화 s = 내부/100 (002 FR-14), 0~1로 자름.</summary>
        public static float N(float internalValue)
        {
            var s = internalValue / InternalMax;
            return s < 0f ? 0f : (s > 1f ? 1f : s);
        }

        static float Check(float v, string name)
        {
            if (!(v >= 0f && v <= InternalMax)) throw new ArgumentOutOfRangeException(name, v, "판단 능력치 내부값은 0~100");
            return v;
        }
    }
}
