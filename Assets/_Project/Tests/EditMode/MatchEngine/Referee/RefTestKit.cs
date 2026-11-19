using System;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Referee;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 판정기 표 테스트 픽스처: 22 슬롯 위치(비활성 NaN), 기본 계수, 접촉 상세 생성.</summary>
    static class RefTestKit
    {
        public static RefereeParams P => RefereeParams.Defaults;

        /// <summary>모두 비활성(NaN) x 22.</summary>
        public static float[] EmptyX()
        {
            var x = new float[22];
            for (int i = 0; i < 22; i++) x[i] = float.NaN;
            return x;
        }

        /// <summary>팀 0이 +x로 공격. 수비(팀 1) GK x=100, 수비수 x=defLine 하나 + 나머지 x=70 뒤쪽 아님(더 얕음).</summary>
        public static float[] Line(float defLine, float gkX = 100f)
        {
            var x = EmptyX();
            x[11] = gkX;
            x[12] = defLine;
            for (int s = 13; s < 22; s++) x[s] = defLine - 5f;
            return x;
        }

        public static float[] Zeros() => new float[22];

        public static ContactDetail Contact(float intensity, float behind = 0f, float studs = 0f, float height = 0f, float late = 0f, float ballFirst = 0f, bool sliding = false, ContactKind kind = ContactKind.Tackle)
        {
            return new ContactDetail
            {
                Kind = kind, Attacker = 5, Defender = 14, Intensity = intensity, FromBehind = behind, StudsUp = studs, Height = height, Lateness = late,
                BallFirst = ballFirst, Sliding = sliding, X = 50f, Y = 34f,
            };
        }

        public static RefereeWorld World()
        {
            var w = new RefereeWorld();
            for (int s = 0; s < 22; s++) { w.Active[s] = true; w.Pos[s] = new Vec2(10f + s * 3f, 5f + (s % 11) * 5f); }
            w.Officials[0] = new Vec2(52f, 30f);
            w.Officials[1] = new Vec2(80f, -1.5f);
            w.Officials[2] = new Vec2(25f, 69.5f);
            return w;
        }

        /// <summary>이항 검정 근사: 두 비율 차의 z (정규 근사).</summary>
        public static double Z(int a, int na, int b, int nb)
        {
            double pa = (double)a / na, pb = (double)b / nb, p = (double)(a + b) / (na + nb);
            var se = Math.Sqrt(p * (1 - p) * (1.0 / na + 1.0 / nb));
            return se <= 0 ? 0 : (pa - pb) / se;
        }
    }
}
