using System;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 이긴 슬라이딩 태클 루트 맞춤 (specs/083 FR-03 ⓑ). 시뮬은 태클러–공 ≤ 슬라이딩 도달 거리(070 act.tackle.reach + slideExtra, 083 FR-03 ⓐ)로 공을 따내는데,
    /// 054 FR-02 루트 맞춤 10 cm로는 클립 미끄러짐 거리(접촉점)와의 차가 남는다 — 이긴 실경기 슬라이딩에 한해 상한을 늘린다(082 헤더 RootCap과 같은 방식).
    /// 값은 Data/tuning/highlight.json `highlight.slideRootMax`와 같다(테스트가 확인).
    /// </summary>
    public static class SlideMotion
    {
        /// <summary>highlight.slideRootMax [제안] 2.2 m (= act.tackle.reach 1.2 + act.tackle.slideExtra 1.0).</summary>
        public const float RootMax = 2.2f;

        /// <summary>루트 맞춤 상한(m) = RootMax + 클립 접촉점 수평 거리 × 키 배율.</summary>
        public static float RootCap(RoadToPro.Core.Math.Vec3 clipContact, float scale) =>
            RootMax + (float)Math.Sqrt(clipContact.X * clipContact.X + clipContact.Z * clipContact.Z) * scale;
    }
}
