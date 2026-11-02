using System.Collections.Generic;
using RoadToPro.Core.Time;

namespace RoadToPro.Core.Hashing
{
    /// <summary>처음 갈라진 체크포인트. 구간 = (StartTick, EndTick] 결정 틱.</summary>
    public readonly struct Divergence
    {
        public readonly int Index;
        public readonly long StartTick;
        public readonly long EndTick;

        public Divergence(int index, long startTick, long endTick)
        {
            Index = index;
            StartTick = startTick;
            EndTick = endTick;
        }

        public float StartSeconds => SimClock.SecondsAt(StartTick);
        public float EndSeconds => SimClock.SecondsAt(EndTick);
    }

    /// <summary>
    /// 경기 시각 10초(100 결정 틱)마다 체크포인트를 쌓는다 (specs/001 FR-61, research 07 §2.3).
    /// k번째 체크포인트(0부터)는 틱 (k+1)×100에 기록되고 ((k)×100, (k+1)×100] 구간을 대표한다.
    /// </summary>
    public sealed class CheckpointLog
    {
        readonly List<Checkpoint> _items = new List<Checkpoint>();
        readonly long _interval;

        public CheckpointLog(long intervalTicks = SimClock.CheckpointIntervalTicks)
        {
            if (intervalTicks <= 0) throw new System.ArgumentOutOfRangeException(nameof(intervalTicks), "간격은 1틱 이상");
            _interval = intervalTicks;
        }

        public IReadOnlyList<Checkpoint> Items => _items;
        public long IntervalTicks => _interval;

        /// <summary>이 틱에 체크포인트를 남길 차례인지 (틱 0 제외).</summary>
        public bool IsDue(long tick) => tick > 0 && tick % _interval == 0;

        public void Record(long tick, ulong eventHash, ulong stateHash) =>
            _items.Add(new Checkpoint(_items.Count, tick, eventHash, stateHash));

        public void Clear() => _items.Clear();

        /// <summary>
        /// 두 목록에서 처음 다른 체크포인트. 같으면 null. 길이가 다르면 짧은 쪽이 끝난 다음 위치.
        /// 구간 시작은 앞 체크포인트의 틱(첫 번째면 0).
        /// </summary>
        public static Divergence? FindFirstDivergence(IReadOnlyList<Checkpoint> a, IReadOnlyList<Checkpoint> b)
        {
            var n = a.Count < b.Count ? a.Count : b.Count;
            for (int i = 0; i < n; i++)
                if (!a[i].Equals(b[i]))
                    return new Divergence(i, i == 0 ? 0 : a[i - 1].Tick, a[i].Tick);
            if (a.Count == b.Count) return null;
            var longer = a.Count > b.Count ? a : b;
            return new Divergence(n, n == 0 ? 0 : longer[n - 1].Tick, longer[n].Tick);
        }
    }
}
