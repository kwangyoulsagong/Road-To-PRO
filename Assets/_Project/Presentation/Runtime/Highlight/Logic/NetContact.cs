using System;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>
    /// 골 뒤 첫 그물 접촉 시각 (specs/058 FR-22). 시뮬은 그물을 공 물리 안에서만 다뤄 접촉 사건이 없다 — 클립 공 궤적에서 찾는다:
    /// 골라인을 넘은 뒤 한 프레임 사이 속도가 [제안] 40% 넘게 줄거나 진행 방향(x)이 뒤집히는 첫 프레임. 못 찾으면 NaN.
    /// </summary>
    public static class NetContact
    {
        const float SpeedDrop = 0.4f, LineMargin = 0.11f;
        public const int CueKind = 10_211;

        public static double Find(HighlightData d, double goalTime, float halfLength)
        {
            if (double.IsNaN(goalTime)) return double.NaN;
            int f0 = (int)Math.Floor((goalTime - d.StartTime) * HighlightData.FrameRate);
            if (f0 < 1) f0 = 1;
            float dt = 1f / HighlightData.FrameRate;
            for (int f = f0; f + 1 < d.Frames; f++)
            {
                var p0 = d.Ball[f - 1];
                var p1 = d.Ball[f];
                var p2 = d.Ball[f + 1];
                if (Math.Abs(p1.X) < halfLength + LineMargin) continue;
                float vx1 = (p1.X - p0.X) / dt, vy1 = (p1.Y - p0.Y) / dt, vz1 = (p1.Z - p0.Z) / dt;
                float vx2 = (p2.X - p1.X) / dt, vy2 = (p2.Y - p1.Y) / dt, vz2 = (p2.Z - p1.Z) / dt;
                float s1 = (float)Math.Sqrt(vx1 * vx1 + vy1 * vy1 + vz1 * vz1), s2 = (float)Math.Sqrt(vx2 * vx2 + vy2 * vy2 + vz2 * vz2);
                if (s1 < 0.5f) return double.NaN; // 그물 전에 멈춤 (굴러 들어간 골)
                if (s2 < s1 * (1f - SpeedDrop) || vx1 * vx2 < 0f) return d.StartTime + f / (double)HighlightData.FrameRate;
            }
            return double.NaN;
        }
    }
}
