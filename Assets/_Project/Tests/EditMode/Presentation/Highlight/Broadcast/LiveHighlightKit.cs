using System.Collections.Generic;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Highlight.Selection;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    /// <summary>실경기 하이라이트 카메라 파이프라인 (specs/058): 시뮬 → 장면 선정 → 브리프 → 클립 → 카메라 트랙. HighlightScreen과 같은 순서.</summary>
    static class LiveHighlightKit
    {
        public sealed class Built
        {
            public LiveSceneList List;
            public LiveSceneBriefs Briefs;
            public readonly List<(HighlightScene scene, SceneBrief brief, HighlightData data, CameraTrack track)> Tracks = new List<(HighlightScene, SceneBrief, HighlightData, CameraTrack)>();
        }

        public static int SlotOf(MatchSim sim, long tick, int playerId)
        {
            var f = sim.ExtractClip(tick, tick + 1).Frames[0];
            for (int s = 0; s < Squad.Slots; s++) if (f.PlayerIds[s] == playerId) return s;
            return -1;
        }

        public static Built Build(MatchSim sim, int myPlayerId = -1, CameraSettings settings = null)
        {
            var b = new Built();
            b.List = LiveSceneSelector.Select(sim.Result.EventLog, sim.Result.EndTick, PitchSpec.Default, myPlayerId, new SceneScoreTable());
            b.Briefs = new LiveSceneBriefs(b.List, (t, id) => SlotOf(sim, t, id), myPlayerId, PitchSpec.Default);
            var builder = new CameraTrackBuilder(new CameraRigData(), new CameraTuning(), settings ?? new CameraSettings());
            CameraPose? from = null;
            foreach (var s in b.List.Scenes)
            {
                var clip = sim.ExtractClip((long)System.Math.Floor(s.Start * 10d), (long)System.Math.Ceiling(s.End * 10d) + 1L);
                var data = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                var brief = b.Briefs.Brief(s.Id);
                var track = builder.Build(data, brief, from, false);
                from = track.Poses[track.Frames - 1];
                b.Tracks.Add((s, brief, data, track));
            }
            return b;
        }
    }
}
