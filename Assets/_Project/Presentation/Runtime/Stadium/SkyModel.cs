using System;
using UnityEngine;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>시간대 (specs/044 FR-40 낮·나이트, 061 FR-17 — 해질녘은 후속).</summary>
    public enum TimeOfDay { Day, Night }

    /// <summary>
    /// specs/061 하늘·환경광 계산 (FR-10~13·16·17). 상태 없음 — 같은 입력 → 같은 값.
    /// </summary>
    public static class SkyModel
    {
        /// <summary>구름이 흐르는 하늘 평면 크기 m (오프셋 0~1 = 이 거리).</summary>
        const double CloudTile = 400d;

        public static TimeOfDay ParseTimeOfDay(string name, Action<string> warn)
        {
            if (string.Equals(name?.Trim(), "night", StringComparison.OrdinalIgnoreCase)) return TimeOfDay.Night;
            if (string.Equals(name?.Trim(), "day", StringComparison.OrdinalIgnoreCase) || name == null) return TimeOfDay.Day;
            warn?.Invoke($"알 수 없는 시간대 '{name}': 낮을 쓴다");
            return TimeOfDay.Day;
        }

        /// <summary>
        /// 구름 오프셋 (0~1로 감쌈). 시뮬 시각(double)만의 함수라 재생 속도·fps·리플레이와 무관하고 키 모먼트 정지 중엔 멈춘다 (FR-16).
        /// 바람 방향은 고정 (+x, +z 약간).
        /// </summary>
        public static Vector2 CloudOffset(double simTime, float speedMetersPerSecond)
        {
            if (speedMetersPerSecond <= 0f) return Vector2.zero;
            double d = simTime * speedMetersPerSecond / CloudTile;
            double x = d - Math.Floor(d);
            double z = d * 0.37d - Math.Floor(d * 0.37d);
            return new Vector2((float)x, (float)z);
        }

        /// <summary>하늘 색 → 3색 환경광 (FR-12). 위 = 하늘 위·지평선 섞음, 가운데 = 지평선, 아래 = 지면 반사.</summary>
        public static void Ambient(Color top, Color horizon, Color ground, float intensity, out Color sky, out Color equator, out Color groundOut)
        {
            // 하늘색을 절반만 — 그림자 속 잔디가 청록으로 기울지 않게(색상 75~110°, FR-05), 그래도 햇빛보다 차갑게(FR-13)
            var s = Color.Lerp(top, horizon, 0.35f);
            sky = Color.Lerp(new Color(s.grayscale, s.grayscale, s.grayscale), s, 0.5f) * intensity;
            equator = horizon * (0.8f * intensity);
            groundOut = ground * intensity;
            sky.a = equator.a = groundOut.a = 1f;
        }

        /// <summary>
        /// 반사 큐브맵 한 면의 방향 → 하늘 색 (구름 없는 그라데이션 + 태양 번짐). CPU로 굽는다 — 하늘 셰이더와 같은 식의 단순판.
        /// </summary>
        public static Color SkyColor(Vector3 dir, Color top, Color horizon, Color ground, Vector3 toSun, Color sun)
        {
            dir.Normalize();
            Color c;
            if (dir.y >= 0f) c = Color.Lerp(horizon, top, Mathf.Pow(dir.y, 0.45f));
            else c = Color.Lerp(horizon, ground, Mathf.Clamp01(-dir.y * 6f));
            float s = Mathf.Max(0f, Vector3.Dot(dir, toSun.normalized));
            c += sun * (Mathf.Pow(s, 12f) * 0.18f) * (dir.y >= 0f ? 1f : 0f);
            c.a = 1f;
            return c;
        }

        /// <summary>하늘 평균색 (Low 단순 반사, FR-34).</summary>
        public static Color AverageSky(Color top, Color horizon, Color ground)
        {
            var c = top * 0.3f + horizon * 0.5f + ground * 0.2f;
            c.a = 1f;
            return c;
        }
    }
}
