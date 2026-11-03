using System;
using NUnit.Framework;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-16 임의 구간 재구성 비트 동일, AC-17 골 클립 −8~+3초·잘림, FR-50 링 버퍼.</summary>
    public class ReconstructionTests
    {
        [Test, Category("Determinism")]
        public void Random100Ranges_BitEqualToOriginalFrames()
        {
            var refSim = Reference();
            var frames = ReferenceFrames;
            var end = refSim.Result.EndTick + 1;
            var rng = new SeedRng(16UL);
            for (int k = 0; k < 100; k++)
            {
                var from = (long)rng.Range(0, (int)(end - 10));
                var len = rng.Range(1, 200);
                var to = Math.Min(end, from + len);
                var clip = refSim.ExtractClip(from, to);
                Assert.That(clip.Frames.Length, Is.EqualTo((int)(3 * (to - from))), $"[{from},{to}) 프레임 수");
                foreach (var f in clip.Frames)
                {
                    Assert.That(frames.TryGetValue(f.Index, out var h), $"원본 프레임 {f.Index}");
                    Assert.That(f.Hash(), Is.EqualTo(h), $"구간 [{from},{to}) 프레임 {f.Index}");
                }
            }
        }

        [Test]
        public void GoalClip_330Frames_WithEvents()
        {
            var refSim = Reference();
            var g = refSim.Result.Goals[0];
            var clip = refSim.ExtractClip(g.Tick - 80, g.Tick + 30);
            Assert.That(clip.Frames.Length, Is.InRange(329, 331), "11초 × 30 Hz");
            Assert.That(clip.Frames[0].Index, Is.EqualTo(3 * (g.Tick - 80)));
            for (int i = 1; i < clip.Frames.Length; i++) Assert.That(clip.Frames[i].Index, Is.EqualTo(clip.Frames[i - 1].Index + 1));
            Assert.That(Array.Exists(clip.Events, e => e.Kind == SimEventKind.Goal && e.Seq == g.Seq), "구간 안 골 이벤트");
            foreach (var e in clip.Events) Assert.That(e.Tick, Is.InRange(g.Tick - 80, g.Tick + 29));
        }

        [Test]
        public void EarlyClip_ClampedAtZero()
        {
            var refSim = Reference();
            var clip = refSim.ExtractClip(30 - 80, 30 + 30); // 3초에 골이 났다고 가정한 구간
            Assert.That(clip.FromTick, Is.EqualTo(0L));
            Assert.That(clip.Frames[0].Index, Is.EqualTo(0L), "0초부터");
            Assert.That(clip.Frames.Length, Is.EqualTo(3 * 60));
            Assert.That(clip.Frames[0].Hash(), Is.EqualTo(ReferenceFrames[0]));
        }

        [Test]
        public void RingBuffer_Last60Seconds_MatchesFrames_WithinMemory()
        {
            var refSim = Reference();
            var rec = refSim.Recorder;
            Assert.That(rec.RingCount, Is.EqualTo(ReplayRecorder.RingCapacity));
            Assert.That(rec.NewestIndex - rec.OldestIndex + 1, Is.EqualTo((long)ReplayRecorder.RingCapacity));
            var f = new ReplayFrame();
            for (var i = rec.OldestIndex; i <= rec.NewestIndex; i += 97)
            {
                Assert.That(rec.TryGetFrame(i, f));
                Assert.That(f.Hash(), Is.EqualTo(ReferenceFrames[i]));
            }
            Assert.That(rec.TryGetFrame(rec.OldestIndex - 1, f), Is.False);
            Assert.That(rec.RingBytes, Is.LessThanOrEqualTo(4L * 1024 * 1024), "링 버퍼 ≤ 4 MB");
            // 슬롯 22개 + 공 + 심판 3명, 몸 방향(yaw)·이동 힌트·선수 id
            Assert.That(ReplayFrame.Entities, Is.EqualTo(26));
            Assert.That(f.PlayerIds[0], Is.EqualTo(refSim.Context.Squad.PlayerAt(0)));
        }
    }
}
