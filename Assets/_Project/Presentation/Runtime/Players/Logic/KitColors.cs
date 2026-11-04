using System;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>sRGB 색 (0~1).</summary>
    public readonly struct Rgb : IEquatable<Rgb>
    {
        public readonly float R, G, B;

        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static Rgb FromHex(uint hex) => new Rgb(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);
        public bool Equals(Rgb o) => R.Equals(o.R) && G.Equals(o.G) && B.Equals(o.B);
        public override bool Equals(object obj) => obj is Rgb o && Equals(o);
        public override int GetHashCode() => (R.GetHashCode() * 397 ^ G.GetHashCode()) * 397 ^ B.GetHashCode();
        public override string ToString() => $"#{(int)Math.Round(R * 255):X2}{(int)Math.Round(G * 255):X2}{(int)Math.Round(B * 255):X2}";
    }

    /// <summary>키트 한 벌의 3색 (FR-20: 주색·보조색·포인트색).</summary>
    public readonly struct KitColors : IEquatable<KitColors>
    {
        public readonly Rgb Primary, Secondary, Accent;

        public KitColors(Rgb primary, Rgb secondary, Rgb accent)
        {
            Primary = primary;
            Secondary = secondary;
            Accent = accent;
        }

        public KitColors WithPrimary(Rgb p) => new KitColors(p, Secondary, Accent);
        public bool Equals(KitColors o) => Primary.Equals(o.Primary) && Secondary.Equals(o.Secondary) && Accent.Equals(o.Accent);
        public override bool Equals(object obj) => obj is KitColors o && Equals(o);
        public override int GetHashCode() => (Primary.GetHashCode() * 397 ^ Secondary.GetHashCode()) * 397 ^ Accent.GetHashCode();
    }

    /// <summary>팀 키트: 홈(1st)·2nd·3rd·GK.</summary>
    public readonly struct TeamKits
    {
        public readonly KitColors Home, Second, Third, Keeper;

        public TeamKits(KitColors home, KitColors second, KitColors third, KitColors keeper)
        {
            Home = home;
            Second = second;
            Third = third;
            Keeper = keeper;
        }

        public KitColors Field(int index) => index == 0 ? Home : index == 1 ? Second : Third;
    }

    /// <summary>경기 키트 배정 결과.</summary>
    public readonly struct KitAssignment : IEquatable<KitAssignment>
    {
        public readonly KitColors Home, Away, HomeKeeper, AwayKeeper, Referee;
        /// <summary>원정팀이 입은 키트: 0 홈 키트, 1 2nd, 2 3rd.</summary>
        public readonly int AwayKitIndex;
        /// <summary>규칙으로 ΔE ≥ 임계를 만족하지 못해 가장 먼 조합을 고른 경우.</summary>
        public readonly bool Fallback;

        public KitAssignment(KitColors home, KitColors away, int awayKitIndex, KitColors homeKeeper, KitColors awayKeeper,
            KitColors referee, bool fallback)
        {
            Home = home;
            Away = away;
            AwayKitIndex = awayKitIndex;
            HomeKeeper = homeKeeper;
            AwayKeeper = awayKeeper;
            Referee = referee;
            Fallback = fallback;
        }

        public bool Equals(KitAssignment o) => Home.Equals(o.Home) && Away.Equals(o.Away) && HomeKeeper.Equals(o.HomeKeeper) &&
                                               AwayKeeper.Equals(o.AwayKeeper) && Referee.Equals(o.Referee) &&
                                               AwayKitIndex == o.AwayKitIndex && Fallback == o.Fallback;
        public override bool Equals(object obj) => obj is KitAssignment o && Equals(o);
        public override int GetHashCode() => Home.GetHashCode() * 397 ^ Away.GetHashCode() ^ AwayKitIndex;
    }

    /// <summary>
    /// 키트 색 충돌 판정 (specs/043 FR-22, 2026-11-04 확정: ΔE2000 &lt; 20이면 원정 2nd → 3rd).
    /// 순서: ① 원정 필드 키트(ΔE ≥ 20) ② 심판(양 팀 필드와 ΔE ≥ 30, 팔레트 순) ③ GK(양 팀 필드·심판·상대 GK와 ΔE ≥ 30, 자기 GK 키트 → 팔레트 순).
    /// GK·심판의 30은 스펙(≥ 20)보다 엄격한 여유 — 조명 받은 화면 색에서도 AC-13(≥ 20)을 지키기 위해.
    /// 팔레트는 가상 색 목록(실존 클럽·리그 색 규정 아님).
    /// </summary>
    public static class KitColorResolver
    {
        public const double MinDeltaE = 20.0;
        /// <summary>GK·심판은 조명을 받은 화면 색에서도 20을 지키도록 여유를 둔다(렌더 측정에서 ΔE가 원색보다 5~8 줄어든다, 043 E2E 실측).</summary>
        public const double OfficialMinDeltaE = 30.0;

        static readonly uint[] RefereePalette = { 0x111111, 0xFFE600, 0xFF3D9A, 0x00E676, 0x2979FF, 0xFF6D00 };
        // 순서 = 우선순위. 파랑~보라는 ΔE2000이 남색과 작게 나와(보라 vs 남색 ≈ 32, 렌더 후 19) 뒤로 둔다
        static readonly uint[] KeeperPalette = { 0x00E5FF, 0xFFD400, 0x00C853, 0xF5F5F5, 0xFF6D00, 0x212121, 0xD500F9, 0x8D6E63 };

        public static KitAssignment Resolve(in TeamKits home, in TeamKits away)
        {
            var h = home.Home;
            int idx = -1;
            for (int i = 0; i < 3; i++)
                if (CieDe2000.DeltaE(h.Primary, away.Field(i).Primary) >= MinDeltaE) { idx = i; break; }
            bool fallback = false;
            if (idx < 0)
            {
                fallback = true;
                double best = -1;
                for (int i = 0; i < 3; i++)
                {
                    double d = CieDe2000.DeltaE(h.Primary, away.Field(i).Primary);
                    if (d > best) { best = d; idx = i; }
                }
            }
            var a = away.Field(idx);
            var referee = Pick(new KitColors(Rgb.FromHex(RefereePalette[0]), Rgb.FromHex(0x111111), Rgb.FromHex(0xFFFFFF)),
                RefereePalette, h.Primary, a.Primary, null, null, ref fallback);
            var hk = Pick(home.Keeper, KeeperPalette, h.Primary, a.Primary, referee.Primary, null, ref fallback);
            var ak = Pick(away.Keeper, KeeperPalette, h.Primary, a.Primary, referee.Primary, hk.Primary, ref fallback);
            return new KitAssignment(h, a, idx, hk, ak, referee, fallback);
        }

        static bool Far(Rgb c, Rgb a, Rgb b, Rgb? x, Rgb? y) =>
            CieDe2000.DeltaE(c, a) >= OfficialMinDeltaE && CieDe2000.DeltaE(c, b) >= OfficialMinDeltaE &&
            (!x.HasValue || CieDe2000.DeltaE(c, x.Value) >= OfficialMinDeltaE) && (!y.HasValue || CieDe2000.DeltaE(c, y.Value) >= OfficialMinDeltaE);

        static KitColors Pick(KitColors own, uint[] palette, Rgb a, Rgb b, Rgb? x, Rgb? y, ref bool fallback)
        {
            if (Far(own.Primary, a, b, x, y)) return own;
            foreach (var hex in palette)
            {
                var c = Rgb.FromHex(hex);
                if (Far(c, a, b, x, y)) return own.WithPrimary(c);
            }
            fallback = true;
            return own;
        }
    }
}
