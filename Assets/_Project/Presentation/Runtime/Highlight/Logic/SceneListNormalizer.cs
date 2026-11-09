using System;
using System.Collections.Generic;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>받은 장면 목록을 경기 경계에 맞춘다 (specs/041 FR-51·52).</summary>
    public static class SceneListNormalizer
    {
        /// <param name="halfBoundaries">하프 경계 시뮬 시각 (오름차순).</param>
        public static HighlightScene[] Normalize(HighlightScene[] scenes, double matchEnd, double[] halfBoundaries)
        {
            if (scenes == null) throw new ArgumentNullException(nameof(scenes));
            var result = new List<HighlightScene>(scenes.Length);
            for (int i = 0; i < scenes.Length; i++)
            {
                var s = ClipToMatch(scenes[i], matchEnd);
                if (halfBoundaries != null)
                    for (int b = 0; b < halfBoundaries.Length; b++)
                        s = SplitAtHalf(s, halfBoundaries[b]);
                if (s.End > s.Start) result.Add(s);
            }

            // FR-52: 같은 순간을 두 번 재생하지 않는다. 리플레이는 예외라 비교 대상에서 뺀다.
            int previous = -1;
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].IsReplay) continue;
                if (previous >= 0 && result[previous].End > result[i].Start)
                    result[previous] = result[previous].WithRange(result[previous].Start, result[i].Start);
                previous = i;
            }
            result.RemoveAll(s => s.End <= s.Start);
            return result.ToArray();
        }

        static HighlightScene ClipToMatch(HighlightScene s, double matchEnd) =>
            s.WithRange(Math.Max(0d, s.Start), Math.Min(matchEnd, s.End));

        /// <summary>경계를 걸치면 키 모먼트가 있는 쪽을, 없으면 사건이 들어 있을 가능성이 큰 긴 쪽을 남긴다 (같으면 앞쪽).</summary>
        static HighlightScene SplitAtHalf(HighlightScene s, double boundary)
        {
            if (s.Start >= boundary || s.End <= boundary) return s;
            bool keepLater = s.IsKeyMoment ? s.KeyMomentTime >= boundary : s.End - boundary > boundary - s.Start;
            return keepLater ? s.WithRange(boundary, s.End) : s.WithRange(s.Start, boundary);
        }
    }
}
