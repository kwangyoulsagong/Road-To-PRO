using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Verification;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Verification
{
    // specs/000 T21: '잘림 없음'·'겹침 없음' 사각형 판정 (FR-26, AC-06)
    public class LayoutAssertsTests
    {
        static LayoutAsserts.TextBox Box(string name, float measuredW, float measuredH, Rect content, Rect bound, Rect parent) =>
            new LayoutAsserts.TextBox(name, new Vector2(measuredW, measuredH), content, bound, parent);

        [Test]
        public void NoClip_OverflowingLabel_Fails()
        {
            // en 긴 문자열: 글자 폭 260 > 칸 폭 200
            var b = Box("cardTitle", 260, 20, new Rect(0, 0, 200, 24), new Rect(10, 10, 200, 24), new Rect(0, 0, 220, 100));
            var r = LayoutAsserts.FindClips(new[] { b });
            Assert.AreEqual(1, r.Count);
            StringAssert.Contains("cardTitle", r[0]);
            StringAssert.Contains("260", r[0]);
        }

        [Test]
        public void NoClip_FittingLabel_Passes()
        {
            var b = Box("cardTitle", 180, 20, new Rect(0, 0, 200, 24), new Rect(10, 10, 200, 24), new Rect(0, 0, 220, 100));
            Assert.AreEqual(0, LayoutAsserts.FindClips(new[] { b }).Count);
        }

        [Test]
        public void NoClip_ElementOutsideParent_Fails()
        {
            var b = Box("price", 50, 20, new Rect(0, 0, 60, 24), new Rect(200, 10, 60, 24), new Rect(0, 0, 220, 100));
            var r = LayoutAsserts.FindClips(new[] { b });
            Assert.AreEqual(1, r.Count);
            StringAssert.Contains("부모 밖", r[0]);
        }

        [Test]
        public void NoClip_VerticalOverflow_Fails()
        {
            var b = Box("desc", 200, 60, new Rect(0, 0, 200, 40), new Rect(0, 0, 200, 40), new Rect(0, 0, 220, 100));
            Assert.AreEqual(1, LayoutAsserts.FindClips(new[] { b }).Count);
        }

        [Test]
        public void NoOverlap_Siblings_Detected()
        {
            var sib = new List<KeyValuePair<string, Rect>>
            {
                new KeyValuePair<string, Rect>("card1", new Rect(0, 0, 100, 100)),
                new KeyValuePair<string, Rect>("card2", new Rect(90, 0, 100, 100)), // card1과 10px 겹침
                new KeyValuePair<string, Rect>("card3", new Rect(200, 0, 100, 100)),
            };
            var r = LayoutAsserts.FindOverlaps(sib);
            Assert.AreEqual(1, r.Count);
            StringAssert.Contains("card1", r[0]);
            StringAssert.Contains("card2", r[0]);
        }

        [Test]
        public void Tolerance_1px()
        {
            // 0.8px 넘침·1px 겹침은 반올림 오차로 본다
            var b = Box("t", 200.8f, 24.5f, new Rect(0, 0, 200, 24), new Rect(0, 0, 200, 24), new Rect(-0.5f, 0, 201, 24));
            Assert.AreEqual(0, LayoutAsserts.FindClips(new[] { b }).Count);
            var sib = new List<KeyValuePair<string, Rect>>
            {
                new KeyValuePair<string, Rect>("a", new Rect(0, 0, 100, 100)),
                new KeyValuePair<string, Rect>("b", new Rect(99, 0, 100, 100)),
            };
            Assert.AreEqual(0, LayoutAsserts.FindOverlaps(sib).Count);
        }
    }
}
