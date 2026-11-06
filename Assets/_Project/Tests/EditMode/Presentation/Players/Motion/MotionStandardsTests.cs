using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic.Motion;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>기준값 데이터 (specs/054 FR-72).</summary>
    public class MotionStandardsTests
    {
        internal const string Path = "Assets/_Project/Presentation/Players/Data/054-motion-standards.json";

        internal static MotionStandards Load() => MotionStandards.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(Path).text);

        [Test]
        public void Parse_RealFile_SpecValues()
        {
            var s = Load();
            Assert.AreEqual(0.15f, s.Max("rootErrorP95"), 1e-6f);
            Assert.AreEqual(0.05f, s.Max("slideMax"), 1e-6f);
            Assert.AreEqual(20f, s.Max("yawFrameDelta"), 1e-6f);
            Assert.AreEqual(0.05f, s.Min("plantSide"), 1e-6f);
            Assert.AreEqual(0.28f, s.Max("plantSide"), 1e-6f);
            Assert.AreEqual(4, s.Cadence.Count);
            Assert.AreEqual("sprint", s.Cadence[3].Gait);
        }

        [Test]
        public void Passes_OneSided_InfiniteOtherBound()
        {
            var s = Load();
            Assert.IsTrue(s["postureUsage"].Passes(1f));
            Assert.IsFalse(s["postureUsage"].Passes(0.5f));
            Assert.IsTrue(s["slideMax"].Passes(-1f));
        }

        [Test]
        public void Defaults_MatchFile()
        {
            var s = Load();
            Assert.AreEqual(s.Min("accelPosture"), MotionDefaults.AccelPosture, 1e-5f);
            Assert.AreEqual(s.Min("decelPosture"), MotionDefaults.DecelPosture, 1e-5f);
            Assert.AreEqual(s.Min("cutHeading"), MotionDefaults.CutHeadingDeg, 1e-5f);
            Assert.AreEqual(s.Max("cutWindow"), MotionDefaults.CutWindow, 1e-5f);
            Assert.AreEqual(s.Min("turnHeading"), MotionDefaults.TurnHeadingDeg, 1e-5f);
            Assert.AreEqual(s.Max("yawFrameDelta"), MotionDefaults.YawFrameDeltaDeg, 1e-5f);
            Assert.AreEqual(s.Max("rootErrorContact"), MotionDefaults.RootContactMax, 1e-5f);
            Assert.AreEqual(s.Max("torsoOverlap"), MotionDefaults.TorsoOverlap, 1e-5f);
            Assert.AreEqual(s.Max("postPenetration"), MotionDefaults.PostPenetration, 1e-5f);
        }

        [Test]
        public void Indexer_UnknownId_Throws() => Assert.Throws<KeyNotFoundException>(() => { var _ = Load()["없음"]; });

        [Test]
        public void Parse_MissingBounds_Throws() =>
            Assert.Throws<FormatException>(() => MotionStandards.Parse("{\"schemaVersion\":1,\"thresholds\":{\"a\":{\"unit\":\"m\"}}}"));

        [Test]
        public void Parse_PreviousValues_Readable()
        {
            var s = MotionStandards.Parse("{\"schemaVersion\":1,\"thresholds\":{\"a\":{\"max\":1}},\"previous\":{\"a\":{\"max\":2}}}");
            Assert.IsTrue(s.TryGetPrevious("a", out var p));
            Assert.AreEqual(2f, p.Max);
        }
    }
}
