using NUnit.Framework;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>구간 정규화 (specs/041 FR-51·52, AC-08).</summary>
    public class SceneListNormalizerTests
    {
        static readonly double[] Half = { 2700d };

        [Test]
        public void Normalize_BeyondMatchAndHalf_ClipsToBoundaries()
        {
            var r = SceneListNormalizer.Normalize(new[]
            {
                new HighlightScene(1, -5d, 6d),
                new HighlightScene(2, 2695d, 2698d),
                new HighlightScene(3, 2692d, 2703d),
                new HighlightScene(4, 2698d, 2709d),
                new HighlightScene(5, 5395d, 5410d),
            }, 5400d, Half);

            Assert.AreEqual(0d, r[0].Start);
            Assert.AreEqual(6d, r[0].End);
            var cutEarly = System.Array.Find(r, s => s.Id == 3);
            Assert.AreEqual(2700d, cutEarly.End, "앞쪽이 긴 구간은 하프 경계에서 끝난다");
            var cutLate = System.Array.Find(r, s => s.Id == 4);
            Assert.AreEqual(2700d, cutLate.Start, "뒤쪽이 긴 구간은 하프 경계에서 시작한다");
            Assert.AreEqual(5400d, r[r.Length - 1].End);
        }

        [Test]
        public void Normalize_KeyMomentAcrossHalf_KeepsItsSide()
        {
            var r = SceneListNormalizer.Normalize(new[]
            {
                new HighlightScene(1, 2690d, 2702d, isKeyMoment: true, keyMomentTime: 2701d),
            }, 5400d, Half);

            Assert.AreEqual(2700d, r[0].Start);
            Assert.AreEqual(2702d, r[0].End);
        }

        [Test]
        public void Normalize_Overlap_PullsEarlierEndExceptReplay()
        {
            var r = SceneListNormalizer.Normalize(new[]
            {
                new HighlightScene(1, 100d, 111d),
                new HighlightScene(2, 105d, 116d),
                new HighlightScene(3, 105d, 116d, isReplay: true),
                new HighlightScene(4, 300d, 311d),
            }, 5400d, Half);

            Assert.AreEqual(105d, r[0].End, "뒤 장면 시작으로 당긴다");
            Assert.AreEqual(116d, r[1].End);
            Assert.AreEqual(105d, r[2].Start, "리플레이는 같은 순간을 다시 보여도 된다");
            Assert.AreEqual(4, r.Length);
        }

        [Test]
        public void Normalize_EmptyAfterClip_RemovesScene()
        {
            var r = SceneListNormalizer.Normalize(new[]
            {
                new HighlightScene(1, -20d, -3d),
                new HighlightScene(2, 5500d, 5510d),
            }, 5400d, Half);
            Assert.AreEqual(0, r.Length);
        }
    }
}
