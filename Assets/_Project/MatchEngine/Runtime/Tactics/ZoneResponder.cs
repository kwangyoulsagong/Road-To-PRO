using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>존 책임 (FR-50, A5 가중 영역): 점이 속한 슬롯 = (거리 / 가중치)가 가장 작은 슬롯, 동점은 낮은 번호.</summary>
    public static class ZoneResponder
    {
        /// <summary>point를 담당하는 슬롯 번호, 후보가 없으면 −1. skip이 true인 슬롯은 제외한다. 미터 기준 거리.</summary>
        public static int Find(Vec2 point, Vec2[] slots, float[] weights, bool[] candidate, bool[] skip, float pitchL, float pitchW)
        {
            int best = -1;
            var bestScore = float.MaxValue;
            for (int i = 1; i < slots.Length; i++)
            {
                if (!candidate[i] || skip[i]) continue;
                var dx = (slots[i].X - point.X) * pitchL;
                var dy = (slots[i].Y - point.Y) * pitchW;
                var score = (dx * dx + dy * dy) / (weights[i] * weights[i]);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
    }
}
