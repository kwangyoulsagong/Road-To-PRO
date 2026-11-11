using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Diagnostics;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Replay
{
    /// <summary>
    /// specs/058 FR-11, AC-16: 하이라이트 클립(30 Hz)의 연속 프레임은 정확히 1/30 s 간격(중복·누락 0), 위치는 원시 100 Hz 궤적을 같은 시각으로
    /// 보간한 값과 1 mm 안, 프레임 차분 가속도가 005 상한 × 1.2를 넘는 경우가 배치·기록 원인으로는 0건.
    /// 구간: 참조 경기의 골 데드볼(골 −8 s ~ +60 s)과 재시작 접근 구간.
    /// </summary>
    public class ClipAlignmentTests
    {
        const float Tolerance = 0.001f, AccelFactor = 1.2f;

        sealed class Resampler : IRawTrajectorySink
        {
            readonly ReplayFrame _tmp = new ReplayFrame();
            public readonly Dictionary<long, float[]> Frames = new Dictionary<long, float[]>();
            readonly List<(long from, long to)> _want;
            public Resampler(List<(long, long)> want) => _want = want;
            public void OnRawSubstep(long s, ReplayFrame pre, ReplayFrame post)
            {
                for (var k = 3 * s / 10 + 1; k <= (3 * s + 3) / 10; k++)
                {
                    bool need = false;
                    foreach (var w in _want) if (k >= w.from && k < w.to) { need = true; break; }
                    if (!need) continue;
                    ReplayRecorder.Lerp(pre, post, (10 * k - 3 * s) / 3f, _tmp);
                    Frames[k] = (float[])_tmp.Values.Clone();
                }
            }
        }

        static List<(long fromTick, long toTick)> Windows(MatchSim sim)
        {
            var w = new List<(long, long)>();
            foreach (var g in sim.Result.Goals) w.Add((Math.Max(0, g.Tick - 80), g.Tick + 600));
            int restarts = 0;
            for (int i = 0; i < sim.Log.Count && restarts < 6; i++)
            {
                var e = sim.Log[i];
                if (e.Kind == SimEventKind.ThrowIn || e.Kind == SimEventKind.Corner || e.Kind == SimEventKind.GoalKick)
                { w.Add((Math.Max(0, e.Tick - 30), e.Tick + 300)); restarts++; }
            }
            return w;
        }

        [Test]
        public void AllScenes_FrameStep_RawMatch_AccelCause()
        {
            var refSim = Reference();
            var windows = Windows(refSim);
            Assert.That(windows.Count, Is.GreaterThan(3));
            var want = new List<(long, long)>();
            foreach (var (a, b) in windows) want.Add((3 * a, 3 * b));
            var raw = new Resampler(want);
            var live = Sim(ReferenceSeed);
            live.Context.RawSink = raw;
            live.RunToEnd();

            float maxAccel = 0f;
            for (int s = 0; s < Squad.Slots; s++) maxAccel = Math.Max(maxAccel, live.Context.Kinematics.Profile(s).MaxAccel);
            var limit = maxAccel * AccelFactor;
            var failures = new List<string>();
            int decisionSpikes = 0;
            foreach (var (from, to) in windows)
            {
                var clip = refSim.ExtractClip(from, to);
                var data = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                for (int f = 0; f < clip.Frames.Length; f++)
                {
                    var fr = clip.Frames[f];
                    if (f > 0 && fr.Index != clip.Frames[f - 1].Index + 1) failures.Add($"[{from},{to}) 프레임 {fr.Index}: 간격 {fr.Index - clip.Frames[f - 1].Index}");
                    if (!raw.Frames.TryGetValue(fr.Index, out var rv)) { failures.Add($"원시 프레임 없음 {fr.Index}"); continue; }
                    for (int e = 0; e < ReplayFrame.Entities; e++)
                    {
                        if (fr.Hints[e] == MotionHint.Absent) continue;
                        var i = e * ReplayFrame.Stride;
                        var d = Math.Max(Math.Abs(fr.Values[i] - rv[i]), Math.Abs(fr.Values[i + 1] - rv[i + 1]));
                        if (d > Tolerance) failures.Add($"프레임 {fr.Index} 엔티티 {e}: 원시와 {d * 1000f:0.0} mm");
                    }
                    if (failures.Count > 20) break;
                }
                // 프레임 차분 가속도 (중심 2차 차분)
                const int E = HighlightData.Entities;
                const float dt = 1f / HighlightData.FrameRate;
                for (int f = 1; f + 1 < data.Frames; f++)
                    for (int e = 0; e < HighlightData.Players; e++)
                    {
                        int i0 = (f - 1) * E + e, i1 = f * E + e, i2 = (f + 1) * E + e;
                        if (!data.Present[i0] || !data.Present[i1] || !data.Present[i2]) continue;
                        float ax = (data.X[i2] - 2 * data.X[i1] + data.X[i0]) / (dt * dt);
                        float az = (data.Z[i2] - 2 * data.Z[i1] + data.Z[i0]) / (dt * dt);
                        var a = (float)Math.Sqrt(ax * ax + az * az);
                        if (a <= limit) continue;
                        var cause = JumpClassifier.Classify(data, f, e);
                        if (cause == JumpCause.Decision) { decisionSpikes++; continue; }
                        // 기간 시작 배치는 예외 (FR-10)
                        bool periodStart = false;
                        foreach (var ev in clip.Events)
                            if (ev.Kind == SimEventKind.PeriodStart && Math.Abs(ev.Tick * 3 - clip.Frames[f].Index) <= 2) periodStart = true;
                        if (!periodStart) failures.Add($"[{from},{to}) 프레임 {clip.Frames[f].Index} 슬롯 {e}: 가속 {a:0} m/s² > {limit:0} (원인 {JumpClassifier.Label(cause)})");
                    }
            }
            TestContext.WriteLine($"판단 원인 가속 초과 {decisionSpikes}건 (008·010 몫, 리포트용)");
            CollectionAssert.IsEmpty(failures, string.Join("\n", failures.GetRange(0, Math.Min(20, failures.Count))));
        }
    }
}
