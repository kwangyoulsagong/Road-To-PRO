using System;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 클로즈업 머리 대상 고르기 (specs/056 FR-61). 얼굴(머리 높이)이 화면 높이의 Enter(25 %) 이상인 선수 중 큰 순서 최대 Max(2)명.
    /// 히스테리시스 ±15 %(043 LOD와 같은 값): 켜진 선수는 Enter × 0.85 아래로 내려가야 꺼지고, 꺼진 선수는 Enter × 1.15를 넘어야 켜진다.
    /// 같은 크기면 인덱스가 작은 쪽(결정론). 할당 없음.
    /// </summary>
    public static class CloseupSelector
    {
        public const float Enter = 0.25f;
        public const float Hysteresis = 0.15f;
        public const int Max = 2;

        /// <summary>ratio[i] = 머리 높이 / 화면 높이. current = 지금 켜진 상태(입력), next = 결과(출력). 켜진 수를 돌려준다.</summary>
        public static int Select(ReadOnlySpan<float> ratio, ReadOnlySpan<bool> current, Span<bool> next)
        {
            if (current.Length < ratio.Length || next.Length < ratio.Length) throw new ArgumentException("길이가 맞지 않는다");
            for (int i = 0; i < ratio.Length; i++) next[i] = false;
            int on = 0;
            for (int k = 0; k < Max; k++)
            {
                int best = -1;
                float bestR = 0f;
                for (int i = 0; i < ratio.Length; i++)
                {
                    if (next[i]) continue;
                    float th = current[i] ? Enter * (1f - Hysteresis) : Enter * (1f + Hysteresis);
                    float r = ratio[i];
                    if (r < th || float.IsNaN(r)) continue;
                    if (best < 0 || r > bestR) { best = i; bestR = r; }
                }
                if (best < 0) break;
                next[best] = true;
                on++;
            }
            return on;
        }
    }
}
