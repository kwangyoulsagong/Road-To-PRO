using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Diagnostics;
using RoadToPro.MatchEngine.Match.Replay;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-24 (FR-83): 100 Hz 원본 출력 켬/끔 해시 동일, 원본을 30 Hz로 뽑으면 기록 프레임과 같다.</summary>
    [Category("Determinism")]
    public class RawTrajectoryTests
    {
        sealed class Resampler : IRawTrajectorySink
        {
            readonly ReplayFrame _tmp = new ReplayFrame();
            public readonly Dictionary<long, float[]> Frames = new Dictionary<long, float[]>();
            public long Substeps;
            public void OnRawSubstep(long s, ReplayFrame pre, ReplayFrame post)
            {
                Substeps++;
                for (var k = 3 * s / 10 + 1; k <= (3 * s + 3) / 10; k++)
                {
                    ReplayRecorder.Lerp(pre, post, (10 * k - 3 * s) / 3f, _tmp);
                    Frames[k] = (float[])_tmp.Values.Clone();
                }
            }
        }

        sealed class Collect : IReplayFrameSink
        {
            public readonly Dictionary<long, float[]> Frames = new Dictionary<long, float[]>();
            public void OnFrame(ReplayFrame f) => Frames[f.Index] = (float[])f.Values.Clone();
        }

        [Test]
        public void OnOff_SameHash_ResampleEqualsRecordedFrames()
        {
            var on = Sim(ReferenceSeed);
            var raw = new Resampler();
            var rec = new Collect();
            on.Context.RawSink = raw;
            on.Recorder.Sink = rec;
            on.RunUntilTick(6000);
            var off = Sim(ReferenceSeed);
            off.RunUntilTick(6000);
            Assert.That(on.Log.Hash, Is.EqualTo(off.Log.Hash));
            Assert.That(on.StateHash(), Is.EqualTo(off.StateHash()));
            Assert.That(raw.Substeps, Is.EqualTo(60_000L));
            Assert.That(raw.Frames.Count, Is.EqualTo(rec.Frames.Count));
            foreach (var kv in rec.Frames)
            {
                var a = kv.Value;
                var b = raw.Frames[kv.Key];
                for (int i = 0; i < a.Length; i++)
                    Assert.That(BitConverter.SingleToInt32Bits(b[i]), Is.EqualTo(BitConverter.SingleToInt32Bits(a[i])), $"프레임 {kv.Key} 값 {i}");
            }
        }
    }
}
