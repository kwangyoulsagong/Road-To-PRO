using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Tests.EditMode.Presentation.Players;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>장면 미리 만들기 (specs/041 FR-02·31·53).</summary>
    public class SceneClipBuilderTests
    {
        sealed class Diagnostics : IHighlightDiagnostics
        {
            public readonly List<int> Missing = new List<int>();
            public void OnSceneUnavailable(int sceneId) => Missing.Add(sceneId);
        }

        readonly List<(long from, long to)> _requests = new List<(long, long)>();
        readonly Diagnostics _diag = new Diagnostics();

        ReplayClip Extract(long from, long to)
        {
            _requests.Add((from, to));
            if (from >= 9000) throw new ArgumentException("범위 밖");
            int n = (int)(3 * (to - from));
            var frames = new ReplayFrame[n];
            for (int i = 0; i < n; i++) frames[i] = new ReplayFrame { Index = 3 * from + i };
            return new ReplayClip(from, to, frames, Array.Empty<RoadToPro.MatchEngine.Match.Events.SimEvent>());
        }

        static AnimPlan Plan(HighlightData d) => AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);

        SceneClipBuilder Builder(params HighlightScene[] scenes) =>
            new SceneClipBuilder(scenes, Extract, Plan, PitchSpec.Default, _diag);

        [Test]
        public void Step_TwoScenes_ReportsProgressThenDone()
        {
            var b = Builder(new HighlightScene(1, 10d, 11d), new HighlightScene(2, 20d, 21d));
            Assert.AreEqual(0f, b.Progress);
            Assert.IsTrue(b.Step());
            Assert.AreEqual(0.5f, b.Progress);
            Assert.IsTrue(b.Step());
            Assert.IsTrue(b.IsDone);
            Assert.IsFalse(b.Step());
            Assert.AreEqual(2, b.Built.Count);
            Assert.AreEqual(1, b.Built[0].Scene.Id);
        }

        [Test]
        public void Step_ClipRange_CoversSceneEnd()
        {
            var b = Builder(new HighlightScene(1, 10.04d, 11.04d));
            b.Step();
            Assert.AreEqual((100L, 112L), _requests[0]);
            var d = b.Built[0].Data;
            Assert.LessOrEqual(d.StartTime, 10.04f);
            Assert.GreaterOrEqual(d.EndTime, 11.04f);
        }

        [Test]
        public void Step_ExtractFails_SkipsSceneAndReportsDiagnostic()
        {
            var b = Builder(new HighlightScene(1, 10d, 11d), new HighlightScene(2, 950d, 951d), new HighlightScene(3, 20d, 21d));
            while (b.Step()) { }
            CollectionAssert.AreEqual(new[] { 2 }, _diag.Missing);
            Assert.AreEqual(2, b.Scenes().Length);
            Assert.AreEqual(3, b.Scenes()[1].Id);
            Assert.AreEqual(2, b.Cues().Length);
        }

        [Test]
        public void Progress_NoScenes_IsComplete()
        {
            var b = Builder();
            Assert.IsTrue(b.IsDone);
            Assert.AreEqual(1f, b.Progress);
        }
    }
}
