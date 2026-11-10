using System;
using System.Collections.Generic;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace RoadToPro.Verification.Samples
{
    // specs/041 AC-14·16: 시퀀서 + 장면 교체 + 포즈 적용의 CPU·GC (IL2CPP 판정, 렌더 제외)
    public sealed partial class PlayerScreen
    {
        sealed class SilentPorts : IKeyMomentHandler, IHighlightHud, IHighlightCamera, IFramePolicy, IHighlightAudio
        {
            public void RequestChoice(int sceneId, double simTime, bool textOnly) { }
            public void OnStatus(in PlaybackStatus status) { }
            public void OnLoadingProgress(float progress) { }
            public bool IsReverseAngle => false;
            public void OnSceneBegin(in HighlightScene scene) { }
            public void OnPlayback(double simTime) { }
            public void EnterHighlight() { }
            public void EnterTimeline() { }
            public void ExitTimeline() { }
            public void EnterMenu() { }
            public void OnPlaybackState(float rate, bool paused) { }
            public void OnCue(in PlaybackCue cue) { }
        }

        public Dictionary<string, double> BenchHighlight(int warmup, int frames)
        {
            const int SceneCount = 40;
            const double SceneLength = 3d, Dt = 1d / 60d;
            ShowPerf(1);
            _perf = false;
            var first = _sys.Plan;
            var second = AnimPlan.Build(_lib, _map, first.Data, _log, PerfHighlight.Styles(), RigScales());
            var scenes = new HighlightScene[SceneCount];
            var cues = new PlaybackCue[SceneCount][];
            double start = first.Data.StartTime;
            for (int i = 0; i < SceneCount; i++)
            {
                double s = start + i % 3 * SceneLength;
                scenes[i] = new HighlightScene(i + 1, s, s + SceneLength);
                cues[i] = Array.Empty<PlaybackCue>();
            }
            var ports = new SilentPorts();
            var seq = new HighlightSequencer(new PlaybackSettings(), 0d, ports, ports, ports, ports, ports);
            seq.Start(scenes);
            seq.OnLoaded(cues);

            var ms = new double[frames];
            long gc = 0;
            int applied = 0, swaps = 0;
            for (int i = -warmup; i < frames; i++)
            {
                long g0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                seq.Tick(Dt);
                if (seq.SceneIndex != applied && seq.State == SequencerState.Playing)
                {
                    applied = seq.SceneIndex;
                    _sys.SwapPlan(applied % 2 == 0 ? first : second);
                    swaps++;
                }
                _sys.ApplyAt(seq.Clock.SimTime);
                sw.Stop();
                long g = GC.GetAllocatedBytesForCurrentThread() - g0;
                if (i < 0) continue;
                ms[i] = sw.Elapsed.TotalMilliseconds;
                gc += g;
            }
            Array.Sort(ms);
            double P(double q) => ms[Math.Min(frames - 1, Math.Max(0, (int)Math.Ceiling(q * frames) - 1))];
            return new Dictionary<string, double>
            {
                { "highlight_p50_ms", P(0.5) },
                { "highlight_p95_ms", P(0.95) },
                { "highlight_max_ms", ms[frames - 1] },
                { "highlight_gc_bytes", gc },
                { "highlight_swaps", swaps },
            };
        }
    }
}
