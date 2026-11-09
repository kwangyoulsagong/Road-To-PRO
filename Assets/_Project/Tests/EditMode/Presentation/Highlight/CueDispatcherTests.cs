using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>큐 발화 (specs/041 FR-55, AC-06).</summary>
    public class CueDispatcherTests
    {
        static PlaybackCue[] Sorted(params PlaybackCue[] cues)
        {
            CueDispatcher.Sort(cues);
            return cues;
        }

        [Test]
        public void FireUpTo_FiveCuesInOneFrame_FireOnceInSimOrder()
        {
            var cues = Sorted(
                new PlaybackCue(1.04d, 5, 0, 0),
                new PlaybackCue(1.01d, 2, 0, 0),
                new PlaybackCue(1.03d, 4, 0, 0),
                new PlaybackCue(1.00d, 1, 0, 0),
                new PlaybackCue(1.02d, 3, 0, 0));
            var d = new CueDispatcher();
            var sink = new FakeAudio();
            d.Reset(cues, 0d);

            d.FireUpTo(1.05d, sink);
            d.FireUpTo(2d, sink);

            Assert.AreEqual(5, sink.Cues.Count);
            for (int i = 0; i < 5; i++) Assert.AreEqual(i + 1, sink.Cues[i].Seq);
        }

        [Test]
        public void Sort_SameTime_UsesSequence()
        {
            var cues = Sorted(new PlaybackCue(1d, 9, 0, 0), new PlaybackCue(1d, 3, 0, 0));
            Assert.AreEqual(3, cues[0].Seq);
        }

        [Test]
        public void Reset_EarlierCues_SkipsAndKeepsCueAtStart()
        {
            var cues = Sorted(new PlaybackCue(1d, 1, 0, 0), new PlaybackCue(2d, 2, 0, 0));
            var d = new CueDispatcher();
            var sink = new FakeAudio();
            d.Reset(cues, 2d);
            d.FireUpTo(2d, sink);

            var seqs = new List<long>();
            foreach (var c in sink.Cues) seqs.Add(c.Seq);
            CollectionAssert.AreEqual(new long[] { 2 }, seqs);
        }
    }
}
