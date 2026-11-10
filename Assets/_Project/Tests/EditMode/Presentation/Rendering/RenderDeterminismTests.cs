using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Rendering;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.Presentation.Highlight;
using RoadToPro.Tests.EditMode.Presentation.Players;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>프리셋·fps가 시뮬과 재생 시각을 바꾸지 않는다 (specs/048 AC-04).</summary>
    public class RenderDeterminismTests
    {
        sealed class NoDiagnostics : IHighlightDiagnostics
        {
            public void OnSceneUnavailable(int sceneId) => Assert.Fail("재구성 불가 장면: " + sceneId);
        }

        struct Span
        {
            public double First, Last;
        }

        int _savedFps;

        [SetUp]
        public void Save() => _savedFps = Application.targetFrameRate;

        [TearDown]
        public void Restore() => Application.targetFrameRate = _savedFps;

        static AnimPlan Plan(HighlightData d) => AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);

        static HighlightScene[] Scenes(MatchSim sim) =>
            sim.Result.Goals.Take(3).Select((g, i) => new HighlightScene(i + 1, g.Tick * 0.1d - 8d, g.Tick * 0.1d + 3d)).ToArray();

        static SceneClipBuilder Build(MatchSim sim, HighlightScene[] scenes)
        {
            var b = new SceneClipBuilder(scenes, sim.ExtractClip, Plan, PitchSpec.Default, new NoDiagnostics());
            while (b.Step()) { }
            return b;
        }

        static ulong Hash(SceneClipBuilder b)
        {
            ulong h = 17UL;
            foreach (var s in b.Built) h = h * 31UL + s.Data.Hash();
            return h;
        }

        /// <summary>실제 프레임 정책으로 끝까지 재생하고 장면별 재생 시각 구간을 모은다.</summary>
        static List<Span> Play(SceneClipBuilder b, double hz, bool batterySaver, bool toggleMidway)
        {
            var policy = new HighlightFramePolicy(null, batterySaver);
            try
            {
                var seq = new HighlightSequencer(new PlaybackSettings(), 1d, new FakeKeyMoments(), new FakeHud(), new FakeCamera(), policy, new FakeAudio());
                seq.Start(b.Scenes());
                seq.OnLoaded(b.Scenes(), b.Cues());
                var spans = new List<Span>();
                double dt = 1d / hz;
                for (int f = 0; f < 200000 && seq.State != SequencerState.Results; f++)
                {
                    if (toggleMidway && f == 300) policy.SetBatterySaver(!batterySaver);
                    seq.Tick(dt);
                    if (seq.State != SequencerState.Playing) continue;
                    while (spans.Count <= seq.SceneIndex) spans.Add(new Span { First = double.NaN });
                    var s = spans[seq.SceneIndex];
                    if (double.IsNaN(s.First)) s.First = seq.Clock.Position;
                    s.Last = seq.Clock.Position;
                    spans[seq.SceneIndex] = s;
                }
                Assert.AreEqual(SequencerState.Results, seq.State, "결과 화면까지 재생");
                return spans;
            }
            finally
            {
                policy.Dispose();
            }
        }

        [Test, Category("Determinism")]
        public void FramePolicy_30vs60Hz_SameSceneSimSpans()
        {
            var sim = MatchTestKit.Reference();
            var scenes = Scenes(sim);
            Assert.Greater(scenes.Length, 0, "기준 경기에 골이 있어야 한다");
            var b = Build(sim, scenes);

            var at60 = Play(b, 60, false, false);
            var at30 = Play(b, 30, true, false);
            var toggled = Play(b, 60, false, true);
            Assert.AreEqual(at60.Count, at30.Count, "장면 수");
            Assert.AreEqual(at60.Count, toggled.Count, "장면 수(토글)");
            // 재생 시각은 실시간 시계 × 배속: 프레임 간격 차이(1/30 s × 최대 배속 3) 안에서만 다를 수 있다
            const double tol = 3d / 30d;
            var built = b.Scenes();
            for (int i = 0; i < at60.Count; i++)
            {
                Assert.AreEqual(built[i].Start, at60[i].First, tol, $"장면 {i} 시작 60");
                Assert.AreEqual(built[i].Start, at30[i].First, tol, $"장면 {i} 시작 30");
                Assert.AreEqual(at60[i].Last, at30[i].Last, tol, $"장면 {i} 끝 60 vs 30");
                Assert.AreEqual(at60[i].Last, toggled[i].Last, tol, $"장면 {i} 끝 (배터리 토글)");
                Assert.AreEqual(built[i].End, at60[i].Last, tol, $"장면 {i} 끝까지 재생");
            }
        }

        [Test, Category("Determinism")]
        public void ClipBuilder_HashIndependentOfPreset()
        {
            var sim = MatchTestKit.Reference();
            var scenes = Scenes(sim);
            ulong before = sim.StateHash();
            ulong first = Hash(Build(sim, scenes));
            // 렌더 정책을 바꿔 가며 재생한 뒤 다시 만들어도 클립·시뮬 해시가 같다
            var b = Build(sim, scenes);
            Play(b, 30, true, true);
            Play(b, 60, false, false);
            ulong second = Hash(Build(sim, scenes));
            Assert.AreEqual(first, second, "클립 해시");
            Assert.AreEqual(before, sim.StateHash(), "시뮬 해시");
        }
    }
}
