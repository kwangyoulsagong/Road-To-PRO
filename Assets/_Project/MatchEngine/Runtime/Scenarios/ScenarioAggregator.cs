using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>시나리오별 경기 집계 (FR-51·54). 014 태그 갱신과 017 검증이 쓴다.</summary>
    public sealed class ScenarioAggregator
    {
        readonly int _n;
        readonly int[] _starts, _completes, _fallbacks, _counters;
        readonly long[] _ticks;
        readonly int[] _reasons;

        public ScenarioAggregator(int size)
        {
            _n = size;
            _starts = new int[size];
            _completes = new int[size];
            _fallbacks = new int[size];
            _counters = new int[size];
            _ticks = new long[size];
            _reasons = new int[size * ScenarioEndReasons.Count];
        }

        public int Size => _n;
        public int Starts(int o) => _starts[o];
        public int Completes(int o) => _completes[o];
        public int Fallbacks(int o) => _fallbacks[o];
        /// <summary>종료 뒤 창 안에 상대 슈팅을 허용한 횟수.</summary>
        public int CountersAllowed(int o) => _counters[o];
        public int Reason(int o, ScenarioEndReason r) => _reasons[o * ScenarioEndReasons.Count + (int)r];

        public float AverageSeconds(int o)
        {
            var ended = _completes[o] + _fallbacks[o];
            return ended == 0 ? 0f : (float)_ticks[o] / ended / ScenarioParams.TicksPerSecond;
        }

        public void OnStart(int o) => _starts[o]++;

        public void OnEnd(int o, ScenarioEndReason r, long ticks)
        {
            _ticks[o] += ticks;
            _reasons[o * ScenarioEndReasons.Count + (int)r]++;
            if (ScenarioEndReasons.IsFallback(r)) _fallbacks[o]++;
            else _completes[o]++;
        }

        public void OnCounterAllowed(int o) => _counters[o]++;

        public void Write(StateWriter w)
        {
            int n = 0;
            for (int i = 0; i < _n; i++) if (_starts[i] != 0) n++;
            w.Write(n);
            for (int i = 0; i < _n; i++)
            {
                if (_starts[i] == 0) continue;
                w.Write(i);
                w.Write(_starts[i]);
                w.Write(_completes[i]);
                w.Write(_fallbacks[i]);
                w.Write(_counters[i]);
                w.Write(_ticks[i]);
                for (int r = 0; r < ScenarioEndReasons.Count; r++) w.Write(_reasons[i * ScenarioEndReasons.Count + r]);
            }
        }

        public void Read(StateReader r)
        {
            System.Array.Clear(_starts, 0, _n);
            System.Array.Clear(_completes, 0, _n);
            System.Array.Clear(_fallbacks, 0, _n);
            System.Array.Clear(_counters, 0, _n);
            System.Array.Clear(_ticks, 0, _n);
            System.Array.Clear(_reasons, 0, _reasons.Length);
            var n = r.ReadInt();
            for (int k = 0; k < n; k++)
            {
                var i = r.ReadInt();
                _starts[i] = r.ReadInt();
                _completes[i] = r.ReadInt();
                _fallbacks[i] = r.ReadInt();
                _counters[i] = r.ReadInt();
                _ticks[i] = r.ReadLong();
                for (int x = 0; x < ScenarioEndReasons.Count; x++) _reasons[i * ScenarioEndReasons.Count + x] = r.ReadInt();
            }
        }

        public void AddTo(ref StableHasher h)
        {
            for (int i = 0; i < _n; i++)
            {
                if (_starts[i] == 0) continue;
                h.Add(i);
                h.Add(_starts[i]);
                h.Add(_completes[i]);
                h.Add(_fallbacks[i]);
                h.Add(_counters[i]);
                h.Add(_ticks[i]);
                for (int r = 0; r < ScenarioEndReasons.Count; r++) h.Add(_reasons[i * ScenarioEndReasons.Count + r]);
            }
        }
    }
}
