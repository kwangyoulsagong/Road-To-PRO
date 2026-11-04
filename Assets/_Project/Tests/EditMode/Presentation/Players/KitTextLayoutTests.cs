using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>번호·이름 배치 (specs/043 FR-21·23·65·66, AC-05). 실제 구운 글리프 메트릭(kit-glyphs.json)을 쓴다.</summary>
    public class KitTextLayoutTests
    {
        // uv.TEXT_AREAS × 1024 px (player.layout.json과 같은 값)
        const float NameW = 0.30f * 1024, NameH = 0.06f * 1024;
        const float NumW = 0.24f * 1024, NumH = 0.24f * 1024;
        const float FrontW = 0.08f * 1024, FrontH = 0.08f * 1024;

        internal static GlyphAtlasData Atlas()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(KitCompositorTests.FontsDir + "/kit-glyphs.json");
            Assert.IsNotNull(json);
            return GlyphAtlasData.Parse(json.text);
        }

        static void AssertInside(List<GlyphQuad> q, float w, float h, string what)
        {
            foreach (var g in q)
            {
                Assert.That(g.X, Is.GreaterThanOrEqualTo(-1e-3f), what);
                Assert.That(g.Y, Is.GreaterThanOrEqualTo(-1e-3f), what);
                Assert.That(g.X + g.W, Is.LessThanOrEqualTo(w + 1e-3f), what);
                Assert.That(g.Y + g.H, Is.LessThanOrEqualTo(h + 1e-3f), what);
            }
        }

        static (float min, float max) Span(List<GlyphQuad> q)
        {
            float a = float.MaxValue, b = float.MinValue;
            foreach (var g in q) { a = Mathf.Min(a, g.X); b = Mathf.Max(b, g.X + g.W); }
            return (a, b);
        }

        [Test]
        public void Numbers_1_7_10_99_InsideAndCentered()
        {
            var at = Atlas();
            var q = new List<GlyphQuad>();
            foreach (var n in new[] { 1, 7, 10, 99 })
            {
                foreach (var (w, h) in new[] { (NumW, NumH), (FrontW, FrontH) })
                {
                    q.Clear();
                    int c = KitTextLayout.LayoutNumber(n, at.Numbers, w, h, q, NullLogger.Instance);
                    Assert.AreEqual(n.ToString().Length, c, "글자 수 " + n);
                    AssertInside(q, w, h, "번호 " + n);
                    var (a, b) = Span(q);
                    Assert.That((a + b) * 0.5f, Is.EqualTo(w * 0.5f).Within(w * 0.03f), "가운데 정렬 " + n);
                    float hgt = 0; foreach (var g in q) hgt = Mathf.Max(hgt, g.H);
                    Assert.That(hgt, Is.GreaterThan(h * 0.55f), "번호가 영역을 충분히 채운다(두 자리는 폭에 맞춰 줄어듦) " + n);
                }
            }
        }

        [Test]
        public void Numbers_OutOfRange_NotShown_AndLogged()
        {
            var at = Atlas();
            var log = new MemoryLogger(LogLevel.Warning);
            var q = new List<GlyphQuad>();
            Assert.AreEqual(0, KitTextLayout.LayoutNumber(0, at.Numbers, NumW, NumH, q, log));
            Assert.AreEqual(0, KitTextLayout.LayoutNumber(100, at.Numbers, NumW, NumH, q, log));
            Assert.AreEqual(0, q.Count);
            Assert.AreEqual(2, log.Entries.Count, "진단 로그 2건");
        }

        [TestCase("", 0)]
        [TestCase("Kim", 3)]
        [TestCase("Alexandersen", 12)]
        [TestCase("Maximilian Oberhausenberg", 25)]
        [TestCase("김민준", 3)]
        [TestCase("Ünal-Østergård Lindqvist", 24)]
        public void Names_InsideArea(string name, int len)
        {
            Assert.AreEqual(len, name.Length);
            var at = Atlas();
            var q = new List<GlyphQuad>();
            var r = KitTextLayout.LayoutName(name, at.Names, NameW, NameH, q, NullLogger.Instance);
            AssertInside(q, NameW, NameH, name);
            if (len == 0) Assert.AreEqual(0, q.Count);
            else Assert.Greater(q.Count, 0);
            Assert.That(r.Tracking, Is.GreaterThanOrEqualTo(KitTextLayout.MinTracking - 1e-6f));
        }

        [Test]
        public void LongName_FallsBackToSurname()
        {
            var at = Atlas();
            var q = new List<GlyphQuad>();
            var r = KitTextLayout.LayoutName("Maximilian Oberhausenberg", at.Names, NameW, NameH, q, NullLogger.Instance);
            Assert.AreEqual("Oberhausenberg", r.Shown);
            Assert.IsTrue(r.SurnameOnly);
        }

        [Test]
        public void VeryLongSurname_TruncatedWithoutEllipsis()
        {
            var at = Atlas();
            var q = new List<GlyphQuad>();
            var r = KitTextLayout.LayoutName("Wolfeschlegelsteinhausenbergerdorff", at.Names, NameW, NameH, q, NullLogger.Instance);
            Assert.IsTrue(r.Truncated);
            Assert.IsFalse(r.Shown.Contains("…") || r.Shown.Contains("..."));
            Assert.AreEqual(r.Shown.Length, q.Count);
            AssertInside(q, NameW, NameH, "자르기");
        }

        [Test]
        public void HangulName_Surname_IsFirstSyllable()
        {
            Assert.AreEqual("김", KitTextLayout.Surname("김민준"));
            Assert.AreEqual("Smith", KitTextLayout.Surname("John Smith"));
            Assert.AreEqual("Pelé", KitTextLayout.Surname("Pelé"));
        }

        [Test]
        public void MissingGlyph_Skipped_AndLogged()
        {
            var at = Atlas();
            var log = new MemoryLogger(LogLevel.Warning);
            var q = new List<GlyphQuad>();
            // '똠'은 KS X 1001 2,350자 밖
            KitTextLayout.LayoutName("김똠", at.Names, NameW, NameH, q, log);
            Assert.AreEqual(1, q.Count);
            Assert.AreEqual(1, log.Entries.Count);
        }
    }
}
