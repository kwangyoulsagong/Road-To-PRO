namespace RoadToPro.MatchEngine.Match.Replay
{
    /// <summary>
    /// 상태 체크포인트 보관 모드 (specs/006 FR-55). 어느 모드든 체크포인트 해시는 10초마다 기록한다 (AC-22).
    /// </summary>
    public enum CheckpointMode : byte
    {
        /// <summary>017 대량 시뮬 기본: 해시만.</summary>
        None = 0,
        /// <summary>관전 경기 기본: 10초마다 상태 스냅샷도 보관 (임의 구간 재구성, FR-52).</summary>
        All = 1,
        /// <summary>지정한 틱(키 모먼트)만 보관.</summary>
        KeyMomentsOnly = 2,
    }

    /// <summary>
    /// 상태 스냅샷 (FR-60): 틱 Tick을 처리하기 직전 상태의 무손실 바이트. 이벤트 로그는 일련번호·해시만 담고
    /// 복원할 때 원본 로그 앞부분을 복사한다. 메모리 안 재구성·재시뮬용 — 세이브 파일에 들어가지 않는다 (경기 중 저장 없음).
    /// </summary>
    public sealed class MatchSnapshot
    {
        /// <summary>2: specs/058 데드볼 진행기·진행 중 정지 시간 추가. 3: specs/011 GK 섹션·운동학 외부 구동.</summary>
        public const byte FormatVersion = 3;

        public readonly long Tick;
        public readonly long Seq;
        public readonly ulong EventHash;
        public readonly ulong StateHash;
        public readonly int CheckpointCount;
        public readonly byte[] Data;

        public MatchSnapshot(long tick, long seq, ulong eventHash, ulong stateHash, int checkpointCount, byte[] data)
        {
            Tick = tick;
            Seq = seq;
            EventHash = eventHash;
            StateHash = stateHash;
            CheckpointCount = checkpointCount;
            Data = data;
        }

        public int SizeBytes => Data.Length;
    }

    /// <summary>클립 (FR-53): 구간 [FromTick, ToTick)의 30 Hz 프레임과 구간 이벤트.</summary>
    public sealed class ReplayClip
    {
        public readonly long FromTick;
        public readonly long ToTick;
        public readonly ReplayFrame[] Frames;
        public readonly Events.SimEvent[] Events;

        public ReplayClip(long from, long to, ReplayFrame[] frames, Events.SimEvent[] events)
        {
            FromTick = from;
            ToTick = to;
            Frames = frames;
            Events = events;
        }
    }
}
