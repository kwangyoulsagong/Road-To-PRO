using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 AC-08: 드리블·테이크온·태클·몸싸움 실행 10만 건 2회 → 결과 해시 같음, 액션당 RNG 소비 고정(분기 무관).</summary>
    public class DuelDeterminismTests
    {
        static ActionParams P => ActionTestKit.P;

        static ulong Run(out bool fixedCount)
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 5, new Vec2(60f, 34f), new Vec2(5f, 0f));
            ActionTestKit.Put(w, 16, new Vec2(62f, 34.3f), new Vec2(-1f, 0f));
            var rng = new SeedRng(7008UL);
            var shadow = new SeedRng(7008UL);
            var h = StableHasher.Create();
            fixedCount = true;
            for (int i = 0; i < 100_000; i++)
            {
                int expect;
                switch (i % 4)
                {
                    case 0:
                    {
                        var t = DribbleModel.SampleTouch(P, 1f + (i % 7) * 0.5f, 3f + i % 9, new Vec2(1f, 0.1f * (i % 3)), rng);
                        h.Add(t.Distance); h.Add(t.Direction.X); h.Add(t.Direction.Y);
                        expect = DribbleModel.RngPerTouch;
                        break;
                    }
                    case 1:
                    {
                        var p = TakeOnModel.Probability(P, w, 5, 16, (i % 5) * 0.2f, (i % 3) * 0.3f, i % 2);
                        h.Add((int)TakeOnModel.Resolve(P, p, rng));
                        expect = TakeOnModel.RngPerTakeOn;
                        break;
                    }
                    case 2:
                    {
                        var sliding = i % 3 == 0;
                        var p = TackleModel.WinProbability(P, w, 16, 5, (i % 4) * 0.25f, (i % 5) * 0.2f, sliding);
                        h.Add((int)TackleModel.Resolve(P, p, sliding, rng));
                        expect = TackleModel.RngPerTackle;
                        break;
                    }
                    default:
                        h.Add(ShieldModel.Resolve(ShieldModel.KeepProbability(P, w, 5, 16), rng));
                        expect = ShieldModel.RngPerShield;
                        break;
                }
                for (int k = 0; k < expect; k++) shadow.NextFloat();
                var a = rng.SaveState();
                var b = shadow.SaveState();
                if (a.S0 != b.S0 || a.S1 != b.S1 || a.S2 != b.S2 || a.S3 != b.S3) fixedCount = false;
            }
            return h.Value;
        }

        [Test, Category("Determinism")]
        public void Duels100k_TwoRuns_SameHash_FixedRngCount()
        {
            var a = Run(out var fa);
            var b = Run(out var fb);
            Assert.AreEqual(a, b);
            Assert.IsTrue(fa && fb, "액션당 RNG 소비가 분기와 무관하게 고정 (터치 4 · 테이크온 2 · 태클 2 · 몸싸움 1)");
        }
    }
}
