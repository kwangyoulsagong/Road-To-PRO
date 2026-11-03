using System;
using RoadToPro.Core.Hashing;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Match.Replay
{
    /// <summary>이동 상태 코드 (FR-50 애니 힌트). 043과 합의 전 기본 목록 (스펙 [미정]).</summary>
    public enum MotionHint : byte
    {
        Idle = 0,
        Walk = 1,
        Jog = 2,
        Run = 3,
        Sprint = 4,
        Kick = 5,
        Tackle = 6,
        Fall = 7,
        Celebrate = 8,
        /// <summary>경기장에 없음 (퇴장·빈 슬롯).</summary>
        Absent = 255,
    }

    /// <summary>
    /// 리플레이 프레임 1개 (specs/006 FR-50): 엔티티 26개(슬롯 22 + 공 + 심판 3) × {x, y, z, yaw}, 이동 힌트, 슬롯 선수 id, 공 상태.
    /// 기록은 float 무손실 (압축은 재생 쪽 041). 재사용 버퍼로 쓴다.
    /// </summary>
    public sealed class ReplayFrame
    {
        public const int Entities = 26;
        public const int BallEntity = 22;
        public const int FirstOfficial = 23;
        public const int Stride = 4;

        /// <summary>절대 프레임 번호 (시각 = 번호 / 30 s). 100 Hz 원본이면 서브스텝 번호.</summary>
        public long Index;
        public readonly float[] Values = new float[Entities * Stride];
        public readonly MotionHint[] Hints = new MotionHint[Entities];
        public readonly int[] PlayerIds = new int[Squad.Slots];
        public BallPhase BallPhase;
        public int BallOwnerSlot = -1;

        public float X(int e) => Values[e * Stride];
        public float Y(int e) => Values[e * Stride + 1];
        public float Z(int e) => Values[e * Stride + 2];
        public float Yaw(int e) => Values[e * Stride + 3];

        public void CopyFrom(ReplayFrame o)
        {
            Index = o.Index;
            Array.Copy(o.Values, Values, Values.Length);
            Array.Copy(o.Hints, Hints, Hints.Length);
            Array.Copy(o.PlayerIds, PlayerIds, PlayerIds.Length);
            BallPhase = o.BallPhase;
            BallOwnerSlot = o.BallOwnerSlot;
        }

        public ReplayFrame Clone()
        {
            var c = new ReplayFrame();
            c.CopyFrom(this);
            return c;
        }

        public ulong Hash()
        {
            var h = StableHasher.Create();
            h.Add(Index);
            for (int i = 0; i < Values.Length; i++) h.Add(Values[i]);
            for (int i = 0; i < Hints.Length; i++) h.Add((byte)Hints[i]);
            for (int i = 0; i < PlayerIds.Length; i++) h.Add(PlayerIds[i]);
            h.Add((byte)BallPhase);
            h.Add(BallOwnerSlot);
            return h.Value;
        }

        /// <summary>비트 단위 비교 (AC-16).</summary>
        public bool BitEquals(ReplayFrame o)
        {
            if (Index != o.Index || BallPhase != o.BallPhase || BallOwnerSlot != o.BallOwnerSlot) return false;
            for (int i = 0; i < Values.Length; i++)
                if (BitConverter.SingleToInt32Bits(Values[i]) != BitConverter.SingleToInt32Bits(o.Values[i])) return false;
            for (int i = 0; i < Hints.Length; i++) if (Hints[i] != o.Hints[i]) return false;
            for (int i = 0; i < PlayerIds.Length; i++) if (PlayerIds[i] != o.PlayerIds[i]) return false;
            return true;
        }
    }

    /// <summary>프레임 수신 (클립·검증). 받은 프레임은 재사용 버퍼다.</summary>
    public interface IReplayFrameSink
    {
        void OnFrame(ReplayFrame frame);
    }
}
