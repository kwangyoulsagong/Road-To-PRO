using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Highlight.Selection;
using RoadToPro.Presentation.Stadium.Net;
using RoadToPro.Tests.EditMode.Presentation.Stadium;
using RoadToPro.Verification.Samples.Highlight;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>
    /// specs/059 FR-40 · AC-05: 그물 표현은 읽기만 한다 — 실경기 하이라이트 골 장면에 그물 구동을 켜고 매 프레임 돌려도
    /// 경기 해시·이벤트 해시·SimEvent 목록·클립 공 위치가 끔과 같다.
    /// </summary>
    [Category("Slow")]
    public class GoalNetSimInvarianceTests
    {
        const ulong Seed = 59_003UL;

        [Test]
        public void NetOnOff_SameHashEventsBallPositions()
        {
            var sim = LiveContactAudit.Played90(Seed);
            ulong finalHash = sim.Result.FinalStateHash, eventHash = sim.Result.EventLogHash;
            int events = sim.Result.EventLog.Length;
            var list = LiveSceneSelector.Select(sim.Result.EventLog, sim.Result.EndTick, PitchSpec.Default, LiveMatchFixture.MyPlayerId, new SceneScoreTable());
            var box = GoalBox.Default(PitchSpec.Default);
            var baker = new NetSceneBaker(NetRippleTuningTests.Defaults(), box);
            var drv = new GoalNetDriver(baker.Cloth);
            int goalScenes = 0, contactsSeen = 0;
            for (int i = 0; i < list.Scenes.Length; i++)
            {
                var s = list.Scenes[i];
                var clip = sim.ExtractClip((long)System.Math.Floor(s.Start * 10d), (long)System.Math.Ceiling(s.End * 10d) + 1L);
                bool goal = false;
                foreach (var ev in clip.Events) if (ev.Kind == SimEventKind.Goal) goal = true;
                if (!goal) continue;
                goalScenes++;
                var off = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                var on = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                var contacts = new List<NetContact>();
                ClipConverter.ToCues(clip, on, box, contacts);
                contactsSeen += contacts.Count;
                var bakes = baker.Bake(on, contacts);   // 굽기도 공 궤적을 읽기만 한다
                for (double t = on.StartTime; t <= on.EndTime; t += 1d / 60d) drv.Update(i, t, bakes);
                for (int f = 0; f < on.Frames; f++) Assert.AreEqual(off.Ball[f], on.Ball[f], $"장면 {i} 프레임 {f}: 공 위치가 그물 구동으로 바뀜");
            }
            Assert.Greater(goalScenes, 0, "골 장면이 있는 시드여야 한다");
            Assert.Greater(contactsSeen, 0, "그물 접촉이 있는 골이 하나는 있어야 한다");
            Assert.AreEqual(finalHash, sim.Result.FinalStateHash);
            Assert.AreEqual(eventHash, sim.Result.EventLogHash);
            Assert.AreEqual(events, sim.Result.EventLog.Length);
        }
    }
}
