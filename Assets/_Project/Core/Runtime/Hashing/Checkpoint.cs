using System;

namespace RoadToPro.Core.Hashing
{
    /// <summary>
    /// 결정론 체크포인트 하나 (specs/001 FR-61): 그 틱까지의 이벤트 로그 누적 해시 + 상태 스냅샷 해시.
    /// 000 하네스의 checkpoints.json({name, tick, hash})으로 내보낼 때는 <see cref="CombinedHash"/>를 쓴다.
    /// </summary>
    public readonly struct Checkpoint : IEquatable<Checkpoint>
    {
        public readonly int Index;
        public readonly long Tick;
        public readonly ulong EventHash;
        public readonly ulong StateHash;

        public Checkpoint(int index, long tick, ulong eventHash, ulong stateHash)
        {
            Index = index;
            Tick = tick;
            EventHash = eventHash;
            StateHash = stateHash;
        }

        public ulong CombinedHash
        {
            get
            {
                var h = StableHasher.Create();
                h.Add(EventHash);
                h.Add(StateHash);
                return h.Value;
            }
        }

        /// <summary>해시가 같은지 (Index·Tick 포함).</summary>
        public bool Equals(Checkpoint o) => Index == o.Index && Tick == o.Tick && EventHash == o.EventHash && StateHash == o.StateHash;
        public override bool Equals(object obj) => obj is Checkpoint c && Equals(c);
        public override int GetHashCode() => unchecked((int)(EventHash ^ StateHash) ^ Index);
    }
}
