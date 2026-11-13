using System;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>주발 (002 FR 주발·약발 전 대체).</summary>
    public enum Foot : byte
    {
        Right = 0,
        Left = 1,
    }

    /// <summary>
    /// 기술 능력치 입력 (내부 0~100, specs/008 FR-01·71). 002 완료 전 대체 입력 — <c>KinematicAttributes</c>·<c>DecisionAttributes</c>와 같은 방식으로
    /// 호출자가 채우고, 002가 나중에 선수 도메인에서 채운다. Vision·Composure는 <c>DecisionAttributes</c> 값을 쓴다(이중 정의 금지).
    /// 범위 밖·NaN은 생성 시 거부. <see cref="IsSet"/>이 false(기본값)이면 <see cref="Average"/>로 읽는다.
    /// </summary>
    public readonly struct TechnicalAttributes
    {
        public const float InternalMax = 100f;

        public readonly bool IsSet;
        public readonly float Passing, FirstTouch, Technique;
        public readonly Foot PreferredFoot;
        /// <summary>약발 등급 1~5 (5 = 양발).</summary>
        public readonly int WeakFoot;

        public TechnicalAttributes(float passing, float firstTouch, float technique, Foot preferredFoot = Foot.Right, int weakFoot = 3)
        {
            Passing = Check(passing, nameof(passing));
            FirstTouch = Check(firstTouch, nameof(firstTouch));
            Technique = Check(technique, nameof(technique));
            if (weakFoot < 1 || weakFoot > 5) throw new ArgumentOutOfRangeException(nameof(weakFoot), weakFoot, "약발 등급은 1~5");
            if (preferredFoot != Foot.Right && preferredFoot != Foot.Left) throw new ArgumentOutOfRangeException(nameof(preferredFoot));
            PreferredFoot = preferredFoot;
            WeakFoot = weakFoot;
            IsSet = true;
        }

        /// <summary>모든 값 50, 오른발, 약발 3 (지정 안 된 선수).</summary>
        public static readonly TechnicalAttributes Average = new TechnicalAttributes(50f, 50f, 50f);

        /// <summary>표시값 N(1~20)으로 기술 3종을 채운다.</summary>
        public static TechnicalAttributes Uniform(int display, Foot foot = Foot.Right, int weakFoot = 3)
        {
            var v = Kinematics.KinematicAttributes.InternalFromDisplay(display);
            return new TechnicalAttributes(v, v, v, foot, weakFoot);
        }

        /// <summary>지정 안 됐으면 평균값.</summary>
        public TechnicalAttributes OrAverage() => IsSet ? this : Average;

        static float Check(float v, string name)
        {
            if (!(v >= 0f && v <= InternalMax)) throw new ArgumentOutOfRangeException(name, v, "기술 능력치 내부값은 0~100");
            return v;
        }
    }
}
