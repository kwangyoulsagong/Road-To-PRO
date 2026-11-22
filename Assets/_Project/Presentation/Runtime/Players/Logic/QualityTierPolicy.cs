using System;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 선수 품질 등급 (specs/062 FR-85~87). 061 <c>CinematicModeController.TopTier</c>(카메라 근처 최대 3명, 연출 장면에서만)를 받아
    /// 등급 대상 k명을 정하고, 그중 CloseupSelector(화면 비율 ≥ 25 %, 최대 2명, ±15 %)가 고른 선수 → 클로즈업 머리,
    /// 나머지 대상 → LOD0 고정 + 고해상도 텍스처(FR-86, 3번째 선수에게 클로즈업 머리 없음). TopTier가 비면(일반 중계) CloseupSelector만.
    /// 대상 교체 히스테리시스: 지금 대상인 선수는 화면 비율에 (1 + 15 %)를 곱해 겨룬다 — 061 TopTier는 거리 순이라
    /// 비슷한 두 선수 사이에서 프레임마다 바뀔 수 있다(FR-87). 같은 점수면 인덱스가 작은 쪽. 할당 없음(stackalloc, 선수 ≤ 64).
    /// </summary>
    public static class QualityTierPolicy
    {
        public const int MaxTier = 3;
        public const float Hysteresis = CloseupSelector.Hysteresis;

        /// <summary>
        /// ratio[i] = 머리 높이 / 화면 높이, topTier = 061 대상(선수 인덱스, 길이 = 대상 수, 0이면 일반 중계),
        /// curCloseup·curPinned = 지금 상태, nextCloseup·nextPinned = 결과(클로즈업 머리 / LOD0 고정·고해상도).
        /// </summary>
        public static void Select(ReadOnlySpan<float> ratio, ReadOnlySpan<int> topTier, ReadOnlySpan<bool> curCloseup, ReadOnlySpan<bool> curPinned,
            Span<bool> nextCloseup, Span<bool> nextPinned)
        {
            int n = ratio.Length;
            if (curCloseup.Length < n || curPinned.Length < n || nextCloseup.Length < n || nextPinned.Length < n || n > 64)
                throw new ArgumentException("길이가 맞지 않는다");
            for (int i = 0; i < n; i++) nextPinned[i] = false;
            if (topTier.Length == 0)
            {
                CloseupSelector.Select(ratio, curCloseup, nextCloseup);
                return;
            }
            // 후보 = 새 대상 ∪ 지금 등급 대상
            Span<bool> cand = stackalloc bool[n];
            Span<bool> chosen = stackalloc bool[n];
            foreach (int e in topTier)
                if (e >= 0 && e < n) cand[e] = true;
            for (int i = 0; i < n; i++)
            {
                if (curCloseup[i] || curPinned[i]) cand[i] = true;
                chosen[i] = false;
            }
            int k = Math.Min(MaxTier, topTier.Length);
            for (int pick = 0; pick < k; pick++)
            {
                int best = -1;
                float bestS = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (!cand[i] || chosen[i] || float.IsNaN(ratio[i])) continue;
                    float s = ratio[i] * (curCloseup[i] || curPinned[i] ? 1f + Hysteresis : 1f);
                    if (best < 0 || s > bestS)
                    {
                        best = i;
                        bestS = s;
                    }
                }
                if (best < 0) break;
                chosen[best] = true;
            }
            // 클로즈업 머리는 대상 안에서만 (CloseupSelector 문턱·히스테리시스 그대로)
            Span<float> masked = stackalloc float[n];
            for (int i = 0; i < n; i++) masked[i] = chosen[i] ? ratio[i] : 0f;
            CloseupSelector.Select(masked, curCloseup, nextCloseup);
            for (int i = 0; i < n; i++) nextPinned[i] = chosen[i] && !nextCloseup[i];
        }
    }
}
