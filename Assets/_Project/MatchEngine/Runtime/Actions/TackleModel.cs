using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>태클 판정 결과 (specs/070 FR-10·11).</summary>
    public enum TackleResult : byte
    {
        /// <summary>실패: 캐리어가 공을 지킨다 (슬라이딩이면 다운).</summary>
        Miss = 0,
        /// <summary>성공: 태클러가 바로 공을 잡는다 (스탠딩만).</summary>
        WinKeep = 1,
        /// <summary>성공: 공을 찔러낸다 (루즈볼).</summary>
        WinPoke = 2,
    }

    /// <summary>
    /// 태클 (specs/070 FR-07·10·11·31, 리서치 02 §4). 평가는 RNG 없음. <see cref="Resolve"/>는 정확히 균등 난수 2개(성공 1 + 결과 1), 스타일 선택은 1개.
    /// ballExposure [대체] = clamp01(0.5·이격/exposeTouch + 0.5·(1 − |공 − 태클러| / (도달 + exposeDist))).
    /// </summary>
    public static class TackleModel
    {
        public const int RngPerTackle = 2, RngPerStyle = 1;

        /// <summary>FR-10·11 도달 거리: 스탠딩 reach, 슬라이딩 +1.0 m.</summary>
        public static float Reach(ActionParams p, bool sliding) => p.TackleReach + (sliding ? p.TackleSlideExtra : 0f);

        public static float BallExposure(ActionParams p, float gap, float ballToTackler, float reach)
        {
            var a = gap / DMath.Max(p.TackleExposeTouch, 1e-3f);
            var b = 1f - ballToTackler / DMath.Max(reach + p.TackleExposeDist, 1e-3f);
            a = a < 0f ? 0f : (a > 1f ? 1f : a);
            b = b < 0f ? 0f : (b > 1f ? 1f : b);
            return 0.5f * a + 0.5f * b;
        }

        /// <summary>뒤에서 들어간 정도 (0 = 정면, 0.5 = 옆, 1 = 바로 뒤): 캐리어 진행(또는 몸) 방향과 캐리어→태클러 방향 사이 각 기준.</summary>
        public static float AngleFromBehind(Vec2 carrierPos, Vec2 carrierHeading, Vec2 tacklerPos)
        {
            var h = carrierHeading.Normalized();
            var r = (tacklerPos - carrierPos).Normalized();
            if (h == Vec2.Zero || r == Vec2.Zero) return 0f;
            var c = Vec2.Dot(h, r);
            var v = 0.5f * (1f - c);
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>FR-10 logit: −0.1 + 2.0·(a_Tackling − a_Dribbling·0.7 − a_Strength_att·0.3) + 1.2·exposure − 0.8·behind (+ 슬라이딩 −0.3).</summary>
        public static float Logit(ActionParams p, ActionWorld w, int tackler, int carrier, float exposure, float behind, bool sliding)
            => p.TackleB0
               + p.TackleKSkill * (w.Tackling[tackler] - w.Dribbling[carrier] * 0.7f - w.Strength[carrier] * 0.3f)
               + p.TackleKExpose * exposure
               - p.TackleKBehind * behind
               + (sliding ? p.TackleSlideB : 0f);

        public static float WinProbability(ActionParams p, ActionWorld w, int tackler, int carrier, float exposure, float behind, bool sliding)
            => ActionMath.Prob(Logit(p, w, tackler, carrier, exposure, behind, sliding), p);

        /// <summary>FR-07: 저킹 중이면 터치 거리 &gt; 1.2 m이고 공 TTI 우위일 때만 시도 가능. 저킹이 아니면 항상 가능.</summary>
        public static bool CanAttempt(ActionParams p, bool jockeying, float touchDist, bool ttiAdvantage)
            => !jockeying || (touchDist > p.TackleJockeyTouch && ttiAdvantage);

        /// <summary>FR-31 [대체] 슬라이딩 비율: (base + k·(다혈질 − 0.5)) × (1 + habitK·슬라이딩 장인 레벨), 0~1.</summary>
        public static float SlideProbability(ActionParams p, float temperament, byte slideLevel)
        {
            var v = (p.TackleSlideBase + p.TackleSlideTemperK * (temperament - 0.5f)) * (1f + p.TackleSlideHabitK * slideLevel);
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>스타일 선택 표본. RNG 정확히 1.</summary>
        public static bool ChooseSliding(ActionParams p, float temperament, byte slideLevel, SeedRng rng)
            => rng.NextFloat() < SlideProbability(p, temperament, slideLevel);

        /// <summary>늦은 정도: 공이 떠난 뒤 경과 시간 / 창 (0~1). 공이 아직 있으면 0.</summary>
        public static float Lateness(ActionParams p, float secondsSinceBallLeft)
        {
            if (!(secondsSinceBallLeft > 0f)) return 0f;
            var v = secondsSinceBallLeft / DMath.Max(p.TackleLateWindow, 1e-3f);
            return v > 1f ? 1f : v;
        }

        /// <summary>성공·결과 표본. RNG 정확히 2. 슬라이딩 성공은 항상 찔러내기.</summary>
        public static TackleResult Resolve(ActionParams p, float prob, bool sliding, SeedRng rng)
        {
            var u1 = rng.NextFloat();
            var u2 = rng.NextFloat();
            if (!(u1 < prob)) return TackleResult.Miss;
            return !sliding && u2 < p.TackleWinKeep ? TackleResult.WinKeep : TackleResult.WinPoke;
        }
    }
}
