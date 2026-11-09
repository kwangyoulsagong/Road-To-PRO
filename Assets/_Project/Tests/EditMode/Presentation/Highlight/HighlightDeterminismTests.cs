using System.Linq;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.Presentation.Players;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>재생이 시뮬을 바꾸지 않는다 (specs/041 AC-02).</summary>
    public class HighlightDeterminismTests
    {
        sealed class NoDiagnostics : IHighlightDiagnostics
        {
            public void OnSceneUnavailable(int sceneId) => Assert.Fail("재구성 불가 장면: " + sceneId);
        }

        static AnimPlan Plan(HighlightData d) => AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);

        static HighlightScene[] GoalScenes(MatchSim sim) =>
            sim.Result.Goals.Take(3).Select((g, i) => new HighlightScene(i + 1, g.Tick * 0.1d - 8d, g.Tick * 0.1d + 3d)).ToArray();

        static SceneClipBuilder Build(MatchSim sim, HighlightScene[] scenes)
        {
            var b = new SceneClipBuilder(scenes, sim.ExtractClip, Plan, PitchSpec.Default, new NoDiagnostics());
            while (b.Step()) { }
            return b;
        }

        static ulong Play(SceneClipBuilder b)
        {
            var rig = new SequencerRig(gap: 0d);
            rig.StartLoaded(b.Scenes(), b.Cues());
            rig.RunUntil(() => rig.Seq.State == SequencerState.Results);
            ulong h = 17UL;
            foreach (var s in b.Built) h = h * 31UL + s.Data.Hash();
            foreach (var c in rig.Audio.Cues) h = h * 31UL + (ulong)c.Seq;
            return h;
        }

        [Test, Category("Determinism")]
        public void Playback_FullRun_LeavesSimHashUnchanged()
        {
            var sim = MatchTestKit.Reference();
            var scenes = GoalScenes(sim);
            Assert.Greater(scenes.Length, 0, "기준 경기에 골이 있어야 한다");
            ulong before = sim.StateHash();

            ulong first = Play(Build(sim, scenes));
            ulong second = Play(Build(sim, scenes));

            Assert.AreEqual(before, sim.StateHash(), "재생 뒤 시뮬 해시");
            Assert.AreEqual(first, second, "2회 재생 결과");
        }
    }
}
