using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 성능 구간 측정 (specs/000 FR-40). 프레임마다 값을 모으고, 워밍업 프레임 수를 함께 남긴다
    /// (버리는 건 Python farm/perf.py). 렌더 카운터·GPU 시간은 개발 빌드에서만 유효하다 — 없으면 null.
    /// </summary>
    public sealed class PerfSampler
    {
        public const int DefaultWarmup = 30;
        public const int DefaultMinFrames = 120;

        readonly List<object> _sections = new List<object>();
        string _name;
        int _warmup;
        int _minFrames;
        List<object> _frame, _main, _gc, _dc, _tri, _gpu;
        readonly FrameTiming[] _timing = new FrameTiming[1];
        ProfilerRecorder _rMain, _rGc, _rDc, _rTri, _rTex, _rTotal;

        public bool Active => _name != null;
        public int Frames => _frame?.Count ?? 0;
        public bool Enough => Active && Frames >= _warmup + _minFrames;
        public IReadOnlyList<object> Sections => _sections;

        public void Begin(string name, int warmup = DefaultWarmup, int minFrames = DefaultMinFrames)
        {
            _name = name;
            _warmup = warmup;
            _minFrames = minFrames;
            _frame = new List<object>(warmup + minFrames);
            _main = new List<object>();
            _gc = new List<object>();
            _dc = new List<object>();
            _tri = new List<object>();
            _gpu = new List<object>();
            _rMain = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            _rGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            _rDc = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
            _rTri = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            _rTex = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory", 1);
            _rTotal = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);
        }

        /// <summary>매 프레임 (Update). 직전 프레임 값을 읽는다.</summary>
        public void Sample()
        {
            if (!Active) return;
            _frame.Add((double)(Time.unscaledDeltaTime * 1000f));
            _main.Add(_rMain.Valid ? (object)(_rMain.LastValue / 1e6) : null);
            _gc.Add(_rGc.Valid ? (object)_rGc.LastValue : null);
            _dc.Add(_rDc.Valid ? (object)_rDc.LastValue : null);
            _tri.Add(_rTri.Valid ? (object)_rTri.LastValue : null);
            // GPU 프레임 시간 (specs/044). 개발 빌드에서 FrameTimingManager가 켜진다 — 아니면 null
            FrameTimingManager.CaptureFrameTimings();
            _gpu.Add(FrameTimingManager.GetLatestTimings(1, _timing) > 0 && _timing[0].gpuFrameTime > 0 ? (object)_timing[0].gpuFrameTime : null);
        }

        public void End()
        {
            if (!Active) return;
            var d = new Dictionary<string, object>
            {
                { "name", _name },
                { "warmupFrames", _warmup },
                { "frameMs", _frame },
                { "mainThreadMs", AllNull(_main) ? null : _main },
                { "gpuMs", AllNull(_gpu) ? null : _gpu },
                { "gcAllocBytes", AllNull(_gc) ? null : _gc },
                { "drawCalls", AllNull(_dc) ? null : _dc },
                { "triangles", AllNull(_tri) ? null : _tri },
                { "textureMemoryBytes", _rTex.Valid ? (object)_rTex.LastValue : null },
                { "totalMemoryBytes", _rTotal.Valid ? (object)_rTotal.LastValue : null },
                { "power", SystemInfo.batteryStatus.ToString() },
            };
            _sections.Add(d);
            _rMain.Dispose();
            _rGc.Dispose();
            _rDc.Dispose();
            _rTri.Dispose();
            _rTex.Dispose();
            _rTotal.Dispose();
            _name = null;
        }

        static bool AllNull(List<object> l)
        {
            foreach (var x in l)
                if (x != null) return false;
            return true;
        }
    }
}
