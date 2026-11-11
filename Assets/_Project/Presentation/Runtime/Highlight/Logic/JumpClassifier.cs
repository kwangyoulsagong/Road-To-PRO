using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>속도 점프 경고의 원인 분류 (specs/058 FR-12). 클립 변환이 표시한 불연속(<see cref="HighlightData.Break"/>)을 읽는다.</summary>
    public static class JumpClassifier
    {
        /// <summary>프레임 f의 엔티티 e에서 앞 프레임과 뒤 프레임 사이 점프의 원인. 둘 다 표시가 없으면 판단(시뮬 행동) 원인.</summary>
        public static JumpCause Classify(HighlightData d, int f, int e)
        {
            const int E = HighlightData.Entities;
            for (int k = f - 1; k <= f + 1; k++)
            {
                if (k <= 0 || k >= d.Frames) continue;
                var b = (JumpCause)d.Break[k * E + e];
                if (b != JumpCause.Decision) return b;
            }
            return JumpCause.Decision;
        }

        public static string Label(JumpCause c) => c == JumpCause.Placement ? "배치" : c == JumpCause.Record ? "기록" : "판단";
    }
}
