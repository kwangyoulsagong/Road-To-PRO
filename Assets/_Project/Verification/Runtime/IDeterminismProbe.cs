using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 결정론 체크포인트 하나. checkpoints.json 형식 [{name, tick, hash}]의 원소 (specs/000 FR-50·51).
    /// 경기 시뮬(001 FR-61)도 같은 형식으로 낸다.
    /// </summary>
    public readonly struct FarmCheckpoint
    {
        public readonly string Name;
        public readonly long Tick;
        public readonly string Hash;

        public FarmCheckpoint(string name, long tick, string hash)
        {
            Name = name;
            Tick = tick;
            Hash = hash;
        }

        public static string ToJson(IReadOnlyList<FarmCheckpoint> cps, string prefix = "")
        {
            var list = new List<object>(cps.Count);
            foreach (var c in cps)
                list.Add(new Dictionary<string, object> { { "name", prefix + c.Name }, { "tick", c.Tick }, { "hash", c.Hash } });
            return MiniJson.Serialize(list);
        }
    }

    /// <summary>checkpoint 단계가 읽는 결정론 상태 공급자.</summary>
    public interface IDeterminismProbe
    {
        IReadOnlyList<FarmCheckpoint> Checkpoints { get; }
    }

    /// <summary>FNV-1a 64비트. 하네스 자체 검증용 (경기 해시는 001의 안정 해시를 쓴다).</summary>
    public sealed class Fnv1a64
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        ulong _h = Offset;

        public ulong Value => _h;
        public string Hex => _h.ToString("x16", CultureInfo.InvariantCulture);

        public void Add(byte b)
        {
            _h ^= b;
            _h *= Prime;
        }

        public void Add(int v)
        {
            unchecked
            {
                Add((byte)v);
                Add((byte)(v >> 8));
                Add((byte)(v >> 16));
                Add((byte)(v >> 24));
            }
        }

        public void Add(long v)
        {
            Add((int)v);
            Add((int)(v >> 32));
        }

        public void Add(string s)
        {
            foreach (var b in Encoding.UTF8.GetBytes(s)) Add(b);
        }
    }
}
