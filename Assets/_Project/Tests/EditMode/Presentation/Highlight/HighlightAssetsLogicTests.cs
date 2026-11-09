using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>로딩 핸들 해제 (specs/041 AC-20).</summary>
    public class HighlightAssetsLogicTests
    {
        [Test]
        public void Cancel_WhileLoading_ReleasesAllHandles()
        {
            var t = new LoadHandleTracker();
            var released = new List<int>();
            t.Acquire(() => released.Add(1));
            t.Acquire(() => released.Add(2));
            Assert.AreEqual(2, t.Outstanding);

            t.ReleaseAll();

            Assert.AreEqual(0, t.Outstanding);
            CollectionAssert.AreEqual(new[] { 2, 1 }, released, "잡은 역순으로 해제");
        }

        [Test]
        public void ReleaseAll_Twice_ReleasesOnce()
        {
            var t = new LoadHandleTracker();
            int count = 0;
            t.Acquire(() => count++);
            t.ReleaseAll();
            t.ReleaseAll();
            Assert.AreEqual(1, count);
        }

        [Test]
        public void OnLoaded_AllScenesUnavailable_GoesToResults()
        {
            var r = new SequencerRig();
            r.Seq.Start(new[] { new HighlightScene(1, 10d, 11d) });
            r.Seq.OnLoaded(new HighlightScene[0], new PlaybackCue[0][]);
            Assert.AreEqual(SequencerState.Results, r.Seq.State);
            Assert.IsFalse(r.Seq.Wants3D);
        }

        [Test]
        public void OnLoaded_FilteredList_PlaysFilteredList()
        {
            var r = new SequencerRig();
            r.Seq.Start(new[] { new HighlightScene(1, 10d, 11d), new HighlightScene(2, 20d, 21d) });
            r.Seq.OnLoaded(new[] { new HighlightScene(2, 20d, 21d) }, new[] { new PlaybackCue[0] });
            Assert.AreEqual(1, r.Seq.SceneCount);
            CollectionAssert.AreEqual(new[] { 2 }, r.Camera.Begun);
        }
    }
}
