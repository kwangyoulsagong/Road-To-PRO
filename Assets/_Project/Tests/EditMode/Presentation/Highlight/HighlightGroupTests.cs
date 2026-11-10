using NUnit.Framework;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>골 시퀀스 묶음 (specs/042 FR-20·53).</summary>
    public class HighlightGroupTests
    {
        static HighlightScene[] GoalGroup() => new[]
        {
            new HighlightScene(1, 92d, 106d, groupId: 1),
            new HighlightScene(2, 97d, 100d, isReplay: true, groupId: 1),
            new HighlightScene(3, 106d, 111d, groupId: 1),
            new HighlightScene(4, 120d, 130d),
        };

        [Test]
        public void Sequencer_GroupedScenes_NoTimeline_SkipEndsGroup()
        {
            var rig = new SequencerRig(1d);
            rig.StartLoaded(GoalGroup());

            rig.Seq.RequestSkipScene();
            rig.Run(1);
            Assert.AreEqual(SequencerState.Timeline, rig.Seq.State, "묶음 다음 장면 앞에는 타임라인");
            Assert.AreEqual(3, rig.Seq.SceneIndex, "묶음 전체를 건너뛴다");
        }

        [Test]
        public void Sequencer_InsideGroup_NoTimelineBetweenScenes()
        {
            var rig = new SequencerRig(1d);
            rig.StartLoaded(GoalGroup());
            rig.RunUntil(() => rig.Seq.SceneIndex == 2 && rig.Seq.State == SequencerState.Playing);
            CollectionAssert.DoesNotContain(rig.Frames.Calls, "timeline");
        }

        [Test]
        public void Sequencer_UngroupedScenes_KeepTimeline()
        {
            var rig = new SequencerRig(1d);
            rig.StartLoaded(new[] { new HighlightScene(1, 10d, 12d), new HighlightScene(2, 20d, 22d) });
            rig.Seq.RequestSkipScene();
            rig.Run(1);
            Assert.AreEqual(SequencerState.Timeline, rig.Seq.State);
            Assert.AreEqual(1, rig.Seq.SceneIndex);
        }

        [Test]
        public void Sequencer_Playing_SendsClockToCamera()
        {
            var rig = new SequencerRig(1d);
            rig.StartLoaded(GoalGroup());
            rig.Run(3);
            Assert.AreEqual(rig.Seq.Clock.Position, rig.Camera.LastPlayback, 1e-9d);
        }

        [Test]
        public void Normalize_GroupTail_NotTrimmedAndGroupKept()
        {
            var r = SceneListNormalizer.Normalize(GoalGroup(), 5400d, new[] { 2700d });
            Assert.AreEqual(4, r.Length);
            Assert.AreEqual(106d, r[0].End, "리플레이가 앞 장면을 자르지 않는다");
            Assert.AreEqual(1, r[2].GroupId);
            Assert.AreEqual(106d, r[2].Start);
        }
    }
}
