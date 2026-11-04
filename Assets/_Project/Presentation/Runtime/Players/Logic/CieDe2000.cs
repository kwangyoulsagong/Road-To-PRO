using System;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>CIELAB 색 (D65).</summary>
    public readonly struct Lab
    {
        public readonly double L, A, B;

        public Lab(double l, double a, double b)
        {
            L = l;
            A = a;
            B = b;
        }
    }

    /// <summary>
    /// CIEDE2000 색차 (Sharma, Wu, Dalal 2005, "The CIEDE2000 Color-Difference Formula: Implementation Notes…").
    /// kL = kC = kH = 1. sRGB → XYZ(D65) → Lab 변환 포함. double로 계산한다(표시 판정용, 시뮬 아님).
    /// </summary>
    public static class CieDe2000
    {
        public static double DeltaE(Rgb a, Rgb b) => DeltaE(ToLab(a), ToLab(b));

        static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        static double F(double t) => t > 216.0 / 24389.0 ? Math.Pow(t, 1.0 / 3.0) : (24389.0 / 27.0 * t + 16.0) / 116.0;

        public static Lab ToLab(Rgb c)
        {
            double r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
            double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
            double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
            double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
            double fx = F(x), fy = F(y), fz = F(z);
            return new Lab(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
        }

        static double Deg(double r) => r * 180.0 / Math.PI;
        static double Rad(double d) => d * Math.PI / 180.0;

        public static double DeltaE(Lab x, Lab y)
        {
            double c1 = Math.Sqrt(x.A * x.A + x.B * x.B), c2 = Math.Sqrt(y.A * y.A + y.B * y.B);
            double cbar = (c1 + c2) / 2.0;
            double c7 = Math.Pow(cbar, 7);
            double g = 0.5 * (1 - Math.Sqrt(c7 / (c7 + Math.Pow(25.0, 7))));
            double a1 = (1 + g) * x.A, a2 = (1 + g) * y.A;
            double cp1 = Math.Sqrt(a1 * a1 + x.B * x.B), cp2 = Math.Sqrt(a2 * a2 + y.B * y.B);
            double h1 = (a1 == 0 && x.B == 0) ? 0 : Deg(Math.Atan2(x.B, a1));
            if (h1 < 0) h1 += 360;
            double h2 = (a2 == 0 && y.B == 0) ? 0 : Deg(Math.Atan2(y.B, a2));
            if (h2 < 0) h2 += 360;

            double dL = y.L - x.L;
            double dC = cp2 - cp1;
            double dh;
            if (cp1 * cp2 == 0) dh = 0;
            else if (Math.Abs(h2 - h1) <= 180) dh = h2 - h1;
            else if (h2 - h1 > 180) dh = h2 - h1 - 360;
            else dh = h2 - h1 + 360;
            double dH = 2 * Math.Sqrt(cp1 * cp2) * Math.Sin(Rad(dh / 2));

            double lbar = (x.L + y.L) / 2;
            double cpbar = (cp1 + cp2) / 2;
            double hbar;
            if (cp1 * cp2 == 0) hbar = h1 + h2;
            else if (Math.Abs(h1 - h2) <= 180) hbar = (h1 + h2) / 2;
            else if (h1 + h2 < 360) hbar = (h1 + h2 + 360) / 2;
            else hbar = (h1 + h2 - 360) / 2;

            double t = 1 - 0.17 * Math.Cos(Rad(hbar - 30)) + 0.24 * Math.Cos(Rad(2 * hbar)) + 0.32 * Math.Cos(Rad(3 * hbar + 6))
                       - 0.20 * Math.Cos(Rad(4 * hbar - 63));
            double dTheta = 30 * Math.Exp(-Math.Pow((hbar - 275) / 25, 2));
            double cp7 = Math.Pow(cpbar, 7);
            double rc = 2 * Math.Sqrt(cp7 / (cp7 + Math.Pow(25.0, 7)));
            double l50 = (lbar - 50) * (lbar - 50);
            double sl = 1 + 0.015 * l50 / Math.Sqrt(20 + l50);
            double sc = 1 + 0.045 * cpbar;
            double sh = 1 + 0.015 * cpbar * t;
            double rt = -Math.Sin(Rad(2 * dTheta)) * rc;
            double tl = dL / sl, tc = dC / sc, th = dH / sh;
            return Math.Sqrt(tl * tl + tc * tc + th * th + rt * tc * th);
        }
    }
}
