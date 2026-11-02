using System;
using NUnit.Framework;
using RoadToPro.Verification;

namespace RoadToPro.Tests.EditMode.Verification
{
    // specs/000 T17: 실행 인자 → 프로필 적용 값 (FR-22)
    public class FarmArgsTests
    {
        static readonly string[] Full =
        {
            "/path/Player", "-screen-fullscreen", "0",
            "-farmScenario", "/r/Data/e2e/000-sample-cards.json",
            "-farmOut", "/r/.farm/runs/x/mac-m1-1600p-dark-en",
            "-farmSeed", "42",
            "-farmProfile", "mac-m1-1600p",
            "-farmWidth", "2560", "-farmHeight", "1600",
            "-farmUiScale", "2.0",
            "-farmQuality", "Medium", "-farmUnityQuality", "Mobile",
            "-farmRenderScale", "0.75",
            "-farmTargetFps", "60",
            "-farmTheme", "dark", "-farmLang", "en",
            "-farmBatterySaver",
            "-farmSampleButtonColor", "#ff5000",
            "-farmSampleBlack",
            "-farmSampleSimExtraRngAtTick", "450",
        };

        [Test]
        public void Parse_AllProfileArgs_Applied()
        {
            var a = FarmArgs.Parse(Full);
            Assert.IsTrue(a.IsFarmRun);
            Assert.AreEqual("/r/Data/e2e/000-sample-cards.json", a.ScenarioPath);
            Assert.AreEqual("/r/.farm/runs/x/mac-m1-1600p-dark-en", a.OutDir);
            Assert.AreEqual(42, a.Seed);
            Assert.AreEqual("mac-m1-1600p", a.ProfileId);
            Assert.AreEqual(2560, a.Width);
            Assert.AreEqual(1600, a.Height);
            Assert.AreEqual(2.0f, a.UiScale);
            Assert.AreEqual("Medium", a.Quality);
            Assert.AreEqual("Mobile", a.UnityQualityLevel);
            Assert.AreEqual(0.75f, a.RenderScale);
            Assert.AreEqual(60, a.TargetFps);
            Assert.AreEqual("dark", a.Theme);
            Assert.AreEqual("en", a.Lang);
            Assert.IsTrue(a.BatterySaver);
            Assert.AreEqual("#ff5000", a.SampleButtonColor);
            Assert.IsTrue(a.SampleBlack);
            Assert.AreEqual(450, a.SampleSimExtraRngAtTick);
        }

        [Test]
        public void Parse_NoFarmArgs_NotFarmRun()
        {
            var a = FarmArgs.Parse(new[] { "/path/Player", "-logFile", "x.log" });
            Assert.IsFalse(a.IsFarmRun);
        }

        [Test]
        public void Parse_Defaults_WhenOptionalMissing()
        {
            var a = FarmArgs.Parse(new[] { "p", "-farmScenario", "s.json", "-farmOut", "o", "-farmWidth", "800", "-farmHeight", "600" });
            Assert.AreEqual(1f, a.UiScale);
            Assert.AreEqual(1f, a.RenderScale);
            Assert.AreEqual("light", a.Theme);
            Assert.AreEqual("ko", a.Lang);
            Assert.AreEqual(-1, a.SampleSimExtraRngAtTick);
            Assert.IsFalse(a.BatterySaver);
            Assert.IsNull(a.SampleButtonColor);
        }

        [Test]
        public void Parse_InvariantCulture_DecimalPoint()
        {
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var a = FarmArgs.Parse(new[] { "p", "-farmScenario", "s", "-farmOut", "o", "-farmWidth", "8", "-farmHeight", "6", "-farmRenderScale", "0.67" });
                Assert.AreEqual(0.67f, a.RenderScale, 1e-6f);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = prev; }
        }

        [Test]
        public void Parse_BadValues_Throw()
        {
            Assert.Throws<FormatException>(() => FarmArgs.Parse(new[] { "p", "-farmScenario", "s", "-farmOut", "o", "-farmWidth", "abc", "-farmHeight", "6" }));
            Assert.Throws<FormatException>(() => FarmArgs.Parse(new[] { "p", "-farmScenario", "s", "-farmOut", "o", "-farmWidth", "8", "-farmHeight", "6", "-farmTheme", "sepia" }));
            Assert.Throws<FormatException>(() => FarmArgs.Parse(new[] { "p", "-farmScenario", "s", "-farmOut", "o" })); // 해상도 없음
            Assert.Throws<FormatException>(() => FarmArgs.Parse(new[] { "p", "-farmScenario" })); // 값 없음
        }
    }
}
