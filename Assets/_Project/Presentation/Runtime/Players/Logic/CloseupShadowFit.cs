using System;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 088 T30i: 클로즈업 전용 그림자 맵의 범위. 클로즈업 머리(최대 2명) 위치를 덮는 구 — 중심 = 두 머리의 중점, 반경 = 머리 사이 절반 + HeadPad.
    /// 그림자 맵은 이 구를 태양 방향 정사영으로 그린다. 할당 없음.
    /// </summary>
    public static class CloseupShadowFit
    {
        /// <summary>머리 뼈 위치에서 머리·헤어·어깨 위 목까지 덮는 반경 (m) [제안].</summary>
        public const float HeadPad = 0.25f;
        /// <summary>맵 해상도 [제안] — 머리 하나면 텍셀 ≈ 0.24 mm.</summary>
        public const int Resolution = 2048;

        /// <summary>pos = (x, y, z) × count. 머리가 없으면 false.</summary>
        public static bool Fit(ReadOnlySpan<float> pos, int count, out float cx, out float cy, out float cz, out float radius)
        {
            cx = cy = cz = radius = 0f;
            if (count <= 0 || pos.Length < count * 3) return false;
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                float x = pos[i * 3], y = pos[i * 3 + 1], z = pos[i * 3 + 2];
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            }
            cx = (minX + maxX) * 0.5f;
            cy = (minY + maxY) * 0.5f;
            cz = (minZ + maxZ) * 0.5f;
            float far = 0f;
            for (int i = 0; i < count; i++)
            {
                float dx = pos[i * 3] - cx, dy = pos[i * 3 + 1] - cy, dz = pos[i * 3 + 2] - cz;
                far = Math.Max(far, (float)Math.Sqrt(dx * dx + dy * dy + dz * dz));
            }
            radius = far + HeadPad;
            return true;
        }

        /// <summary>텍셀 한 칸의 월드 크기 (m).</summary>
        public static float TexelSize(float radius, int resolution) => 2f * radius / resolution;
    }
}
