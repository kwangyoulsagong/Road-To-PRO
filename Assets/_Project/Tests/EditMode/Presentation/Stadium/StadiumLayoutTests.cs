using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Serialization;
using RoadToPro.Presentation.Stadium;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>layout.json 해석 (specs/044 FR-24~27, AC-09).</summary>
    public class StadiumLayoutTests
    {
        [Test]
        public void Parse_DefaultLayout_HasPitchAndGoals()
        {
            var l = StadiumTestKit.DefaultLayout;
            Assert.AreEqual(1, l.SchemaVersion);
            Assert.AreEqual(105f, l.Length);
            Assert.AreEqual(68f, l.Width);
            Assert.AreEqual(0.12f, l.LineWidth, 1e-6f);
            Assert.AreEqual(5.25f, l.StripeWidth, 1e-6f);
            Assert.AreEqual(17, l.Segments.Count, "선분 라인 수");
            Assert.AreEqual(7, l.Arcs.Count, "원호 라인 수");
            Assert.AreEqual(3, l.Spots.Count, "마크 수");
            Assert.AreEqual(2, l.Goals.Count);
            Assert.AreEqual("towers", l.LightMode);
            Assert.AreEqual(4, l.LightEmitters.Count);
        }

        [Test]
        public void SeatRows_Expand_CountEqualsCapacity()
        {
            var l = StadiumTestKit.DefaultLayout;
            var seats = new List<Vector3>();
            l.ExpandSeats(seats);
            Assert.AreEqual(40000, l.SeatCount);
            Assert.AreEqual(l.SeatCount, seats.Count);
            foreach (var s in seats)
            {
                Assert.IsTrue(Mathf.Abs(s.x) > 52.5f + 5f || Mathf.Abs(s.z) > 34f + 5f, "좌석이 런오프 안쪽");
                Assert.Greater(s.y, 0f);
            }
        }

        [Test]
        public void SeatRows_Sections_HomeAway()
        {
            var l = StadiumTestKit.DefaultLayout;
            var sections = new HashSet<string>();
            foreach (var r in l.SeatRows)
            {
                sections.Add(r.Section);
                Assert.AreEqual(r.Section != "E", r.Home);
            }
            CollectionAssert.AreEquivalent(new[] { "S", "N", "W", "E" }, sections);
        }

        [Test]
        public void CameraMount_IsOnMainStandSide()
        {
            var l = StadiumTestKit.DefaultLayout;
            Assert.Less(l.MainMountCenter.z, -34f - 5f);
            Assert.Greater(l.MainMountCenter.y, 5f);
            Assert.IsTrue(l.MainMount.Contains(l.MainMountCenter));
            Assert.Greater(l.NoGo.Count, 0);
        }

        [Test]
        public void Parse_WrongSchema_Throws()
        {
            Assert.Throws<JsonException>(() => StadiumLayout.Parse("{\"schemaVersion\": 2}"));
            Assert.Throws<JsonException>(() => StadiumLayout.Parse("[]"));
        }
    }
}
