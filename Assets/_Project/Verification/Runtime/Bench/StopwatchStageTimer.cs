using System.Diagnostics;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.Verification.Bench
{
    /// <summary>단계별 누적 시간 (specs/006 AC-21 단계별 프로파일). 시계 API는 코어 밖(Verification)에만 둔다.</summary>
    public sealed class StopwatchStageTimer : IStageTimer
    {
        readonly long[] _ticks = new long[Stages.Count];
        readonly long[] _calls = new long[Stages.Count];
        long _start;

        public void Begin(StageId stage) => _start = Stopwatch.GetTimestamp();

        public void End(StageId stage)
        {
            _ticks[(int)stage] += Stopwatch.GetTimestamp() - _start;
            _calls[(int)stage]++;
        }

        public void Reset()
        {
            System.Array.Clear(_ticks, 0, _ticks.Length);
            System.Array.Clear(_calls, 0, _calls.Length);
        }

        /// <summary>단계 1회 평균 (µs).</summary>
        public double AvgMicros(StageId stage) =>
            _calls[(int)stage] == 0 ? 0 : _ticks[(int)stage] * 1e6 / Stopwatch.Frequency / _calls[(int)stage];

        public long Calls(StageId stage) => _calls[(int)stage];
    }
}
