using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>
    /// 희소성 게이트 (FR-12~13): 경기당 상한·쿨다운. 기회를 막을 뿐 결과를 보장하지 않는다.
    /// 경기마다 새 인스턴스라 경기 간 누수가 없다. 스냅샷은 시작된 시나리오만 쓴다 (라이브러리가 커도 작게).
    /// </summary>
    public sealed class RarityGate
    {
        readonly int[] _count;
        readonly long[] _last;

        public RarityGate(int size)
        {
            _count = new int[size];
            _last = new long[size];
        }

        public int Count(int ordinal) => _count[ordinal];

        public bool Allows(ScenarioDefinition d, long tick)
        {
            if (!d.HasGate) return true;
            var n = _count[d.Ordinal];
            if (n >= d.MaxPerMatch) return false;
            if (n > 0 && tick - _last[d.Ordinal] < ScenarioParams.Ticks(d.CooldownS)) return false;
            return true;
        }

        public void OnStart(ScenarioDefinition d, long tick)
        {
            _count[d.Ordinal]++;
            _last[d.Ordinal] = tick;
        }

        public void Write(StateWriter w)
        {
            int n = 0;
            for (int i = 0; i < _count.Length; i++) if (_count[i] != 0) n++;
            w.Write(n);
            for (int i = 0; i < _count.Length; i++)
            {
                if (_count[i] == 0) continue;
                w.Write(i);
                w.Write(_count[i]);
                w.Write(_last[i]);
            }
        }

        public void Read(StateReader r)
        {
            System.Array.Clear(_count, 0, _count.Length);
            System.Array.Clear(_last, 0, _last.Length);
            var n = r.ReadInt();
            for (int k = 0; k < n; k++)
            {
                var i = r.ReadInt();
                _count[i] = r.ReadInt();
                _last[i] = r.ReadLong();
            }
        }

        public void AddTo(ref StableHasher h)
        {
            for (int i = 0; i < _count.Length; i++)
            {
                if (_count[i] == 0) continue;
                h.Add(i);
                h.Add(_count[i]);
                h.Add(_last[i]);
            }
        }
    }
}
