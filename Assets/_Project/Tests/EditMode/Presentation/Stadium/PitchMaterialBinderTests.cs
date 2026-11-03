using NUnit.Framework;
using RoadToPro.Presentation.Stadium;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>라인 정의 → 셰이더 배열 (specs/044 FR-30~33).</summary>
    public class PitchMaterialBinderTests
    {
        [Test]
        public void Build_PacksAllLines()
        {
            var d = PitchShaderData.From(StadiumTestKit.DefaultLayout);
            Assert.IsTrue(d.Folded, "Law 1 피치는 두 축 대칭 → 1사분면 접기");
            Assert.AreEqual(7, d.SegmentCount, "1사분면에 걸친 선분: 터치N·골R·하프·PA앞·PA옆N·GA앞·GA옆N");
            Assert.AreEqual(3, d.ArcCount, "센터서클·페널티 아크 R·코너 NE");
            Assert.AreEqual(2, d.SpotCount, "센터마크·페널티 마크 R");
            Assert.LessOrEqual(d.SegmentCount, PitchShaderData.MaxSegments);
            Assert.AreEqual(0.06f, d.HalfLineWidth, 1e-6f);
            Assert.AreEqual(5.25f, d.StripeWidth, 1e-6f);
            // 하프라인: x = 0, z −34 → 34 (가운데를 지나 접어도 남는다)
            var found = false;
            for (int i = 0; i < d.SegmentCount; i++)
            {
                var s = d.Segments[i];
                if (Mathf.Abs(s.x) < 1e-4f && Mathf.Abs(s.z) < 1e-4f && Mathf.Abs(Mathf.Abs(s.y - s.w) - 68f) < 1e-3f) found = true;
            }
            Assert.IsTrue(found, "하프라인 (ax, az, bx, bz) 배열에 없음");
        }

        [Test]
        public void Arcs_DotTest()
        {
            var d = PitchShaderData.From(StadiumTestKit.DefaultLayout);
            for (int i = 0; i < d.ArcCount; i++)
            {
                var a = d.Arcs[i];
                var mid = new Vector2(d.ArcEnds[i].x, d.ArcEnds[i].y);
                Assert.AreEqual(1f, mid.magnitude, 1e-5f);
                Assert.Greater(a.z, 0f);
                Assert.IsTrue(a.w == -2f || (a.w >= -1f && a.w <= 1f));
            }
            // 코너 NE (52.5, 34) 사분원의 가운데 방향은 피치 안쪽 (−x, −z)
            var found = false;
            for (int i = 0; i < d.ArcCount; i++)
                if (Mathf.Abs(d.Arcs[i].x - 52.5f) < 1e-3f && Mathf.Abs(d.Arcs[i].y - 34f) < 1e-3f)
                {
                    found = true;
                    Assert.Less(d.ArcEnds[i].x, 0f);
                    Assert.Less(d.ArcEnds[i].y, 0f);
                    Assert.AreEqual(Mathf.Cos(Mathf.PI / 4f), d.Arcs[i].w, 1e-5f);
                }
            Assert.IsTrue(found);
        }

        [Test]
        public void Asymmetric_NotFolded()
        {
            var json = System.IO.File.ReadAllText(StadiumTestKit.DefaultLayoutPath)
                .Replace("\"name\": \"PenaltyMark_R\"", "\"name\": \"PenaltyMark_R\", \"x\": 0");
            var l = StadiumLayout.Parse(json);
            l.Spots.RemoveAt(l.Spots.Count - 1);
            var d = PitchShaderData.From(l);
            Assert.IsFalse(d.Folded);
            Assert.AreEqual(17, d.SegmentCount);
            Assert.AreEqual(7, d.ArcCount);
        }

        [Test]
        public void HighPresetSlots_AreZero()
        {
            var d = PitchShaderData.From(StadiumTestKit.DefaultLayout);
            Assert.AreEqual(0f, d.ShellAmount);
            Assert.AreEqual(0f, d.WearAmount);
            Assert.AreEqual(0f, d.Wetness);
        }
    }
}

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    using UnityEditor;

    public class PitchShaderCompileTests
    {
        [Test]
        public void PitchShader_CompilesWithoutErrors()
        {
            var s = Shader.Find("RoadToPro/Pitch");
            Assert.IsNotNull(s, "RoadToPro/Pitch 셰이더 없음");
            Assert.IsFalse(ShaderUtil.ShaderHasError(s), "셰이더 에러");
            var msgs = ShaderUtil.GetShaderMessages(s);
            Assert.AreEqual(0, msgs.Length, msgs.Length > 0 ? msgs[0].message : "");
        }
    }
}
