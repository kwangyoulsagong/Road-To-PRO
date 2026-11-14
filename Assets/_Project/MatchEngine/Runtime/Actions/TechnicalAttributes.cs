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
        /// <summary>072 온볼 대체 입력 (002 전, specs/072 플랜 대체 4): 돌파·결정력·크로스·중거리, 그리고 수비 1:1 대체 판정용 태클. 지정 안 하면 50.</summary>
        public readonly float Dribbling, Finishing, Crossing, LongShots, Tackling;
        /// <summary>070 결투 대체 입력 (002 전, specs/070 플랜): 위치선정(테이크온 수비)·근력(태클·몸싸움·접촉 질량). 지정 안 하면 50.</summary>
        public readonly float Positioning, Strength;
        public readonly Foot PreferredFoot;
        /// <summary>약발 등급 1~5 (5 = 양발).</summary>
        public readonly int WeakFoot;

        public TechnicalAttributes(float passing, float firstTouch, float technique, Foot preferredFoot = Foot.Right, int weakFoot = 3,
            float dribbling = 50f, float finishing = 50f, float crossing = 50f, float longShots = 50f, float tackling = 50f,
            float positioning = 50f, float strength = 50f)
        {
            Passing = Check(passing, nameof(passing));
            FirstTouch = Check(firstTouch, nameof(firstTouch));
            Technique = Check(technique, nameof(technique));
            Dribbling = Check(dribbling, nameof(dribbling));
            Finishing = Check(finishing, nameof(finishing));
            Crossing = Check(crossing, nameof(crossing));
            LongShots = Check(longShots, nameof(longShots));
            Tackling = Check(tackling, nameof(tackling));
            Positioning = Check(positioning, nameof(positioning));
            Strength = Check(strength, nameof(strength));
            if (weakFoot < 1 || weakFoot > 5) throw new ArgumentOutOfRangeException(nameof(weakFoot), weakFoot, "약발 등급은 1~5");
            if (preferredFoot != Foot.Right && preferredFoot != Foot.Left) throw new ArgumentOutOfRangeException(nameof(preferredFoot));
            PreferredFoot = preferredFoot;
            WeakFoot = weakFoot;
            IsSet = true;
        }

        /// <summary>모든 값 50, 오른발, 약발 3 (지정 안 된 선수).</summary>
        public static readonly TechnicalAttributes Average = new TechnicalAttributes(50f, 50f, 50f);

        /// <summary>표시값 N(1~20)으로 기술 능력치 전부(072 대체 5종·070 대체 2종 포함)를 채운다.</summary>
        public static TechnicalAttributes Uniform(int display, Foot foot = Foot.Right, int weakFoot = 3)
        {
            var v = Kinematics.KinematicAttributes.InternalFromDisplay(display);
            return new TechnicalAttributes(v, v, v, foot, weakFoot, v, v, v, v, v, v, v);
        }

        /// <summary>072 대체 5종만 바꾼 사본 (null = 그대로).</summary>
        public TechnicalAttributes WithOnBall(float? dribbling = null, float? finishing = null, float? crossing = null, float? longShots = null, float? tackling = null)
        {
            var b = OrAverage();
            return new TechnicalAttributes(b.Passing, b.FirstTouch, b.Technique, b.PreferredFoot, b.WeakFoot,
                dribbling ?? b.Dribbling, finishing ?? b.Finishing, crossing ?? b.Crossing, longShots ?? b.LongShots, tackling ?? b.Tackling,
                b.Positioning, b.Strength);
        }

        /// <summary>070 결투 입력만 바꾼 사본 (null = 그대로).</summary>
        public TechnicalAttributes WithDuel(float? dribbling = null, float? tackling = null, float? positioning = null, float? strength = null)
        {
            var b = OrAverage();
            return new TechnicalAttributes(b.Passing, b.FirstTouch, b.Technique, b.PreferredFoot, b.WeakFoot,
                dribbling ?? b.Dribbling, b.Finishing, b.Crossing, b.LongShots, tackling ?? b.Tackling, positioning ?? b.Positioning, strength ?? b.Strength);
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
