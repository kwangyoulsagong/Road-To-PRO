using RoadToPro.Core.Math;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 테스트 공용: 통계 검정 임계값·표본 표준편차.</summary>
    public static class ShotTestKit
    {
        /// <summary>F(9999, 9999) 단측 p = 0.01 (070 DuelAttributeTests와 같은 값).</summary>
        public const double FCrit10k = 1.048;
        /// <summary>단측 z p = 0.01.</summary>
        public const double ZCrit = 2.326;

        /// <summary>두 비율 차 z (p1 &gt; p2 검정).</summary>
        public static double TwoPropZ(int k1, int n1, int k2, int n2)
        {
            var p1 = k1 / (double)n1;
            var p2 = k2 / (double)n2;
            var p = (k1 + k2) / (double)(n1 + n2);
            var se = System.Math.Sqrt(p * (1 - p) * (1.0 / n1 + 1.0 / n2));
            return se > 0 ? (p1 - p2) / se : 0;
        }

        /// <summary>한 비율 z (p̂ &gt; p0 검정).</summary>
        public static double OnePropZ(int k, int n, double p0)
        {
            var se = System.Math.Sqrt(p0 * (1 - p0) / n);
            return (k / (double)n - p0) / se;
        }

        public sealed class Moments
        {
            double _s, _s2;
            int _n;
            public void Add(double v) { _s += v; _s2 += v * v; _n++; }
            public double Mean => _s / _n;
            public double Var => _s2 / _n - Mean * Mean;
            public double Std => System.Math.Sqrt(Var);
        }
    }
}
