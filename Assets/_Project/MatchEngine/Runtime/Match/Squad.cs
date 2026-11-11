using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.Match
{
    public enum PlayerStatus : byte
    {
        Bench = 0,
        OnPitch = 1,
        SubbedOff = 2,
        SentOff = 3,
        /// <summary>부상으로 나갔는데 교체가 없음 (인원 감소).</summary>
        InjuredOff = 4,
    }

    /// <summary>
    /// 경기 선수 ↔ 운동 슬롯(0~21) (specs/006). 슬롯 0~10 홈, 11~21 원정, 팀의 첫 슬롯이 골키퍼.
    /// 운동 시스템은 슬롯 번호를 id로 쓰고, 교체는 같은 슬롯에 새 선수를 넣는다 (처리 순서 불변).
    /// 출전 시간은 들어온 틱·나간 틱으로 센다.
    /// </summary>
    public sealed class Squad
    {
        public const int SlotsPerTeam = 11;
        public const int Slots = 22;
        public const int Empty = -1;

        struct Entry
        {
            public int Id;
            public int Team;
            public KinematicAttributes Attributes;
            public PlayerStatus Status;
            public int Slot;
            public long EnterTick;
            public long ExitTick;
            public MotionStats Stats;
        }

        readonly Entry[] _p;
        readonly int[] _slot = new int[Slots];

        public Squad(MatchSetup setup)
        {
            var n = setup.Home.Players.Length + setup.Away.Players.Length;
            _p = new Entry[n];
            int k = 0;
            for (int t = 0; t < 2; t++)
                foreach (var mp in setup.Team(t).Players)
                    _p[k++] = new Entry { Id = mp.Id, Team = t, Attributes = mp.Attributes, Status = PlayerStatus.Bench, Slot = Empty, EnterTick = -1, ExitTick = -1 };
            Array.Sort(_p, (a, b) => a.Id.CompareTo(b.Id));
            for (int t = 0; t < 2; t++)
            {
                var st = setup.Team(t).Starters;
                for (int i = 0; i < SlotsPerTeam; i++)
                {
                    var idx = IndexOf(st[i]);
                    var slot = t * SlotsPerTeam + i;
                    _p[idx].Status = PlayerStatus.OnPitch;
                    _p[idx].Slot = slot;
                    _p[idx].EnterTick = 0;
                    _slot[slot] = idx;
                }
            }
        }

        public int PlayerCount => _p.Length;
        public static int TeamOfSlot(int slot) => slot / SlotsPerTeam;
        public static bool IsKeeperSlot(int slot) => slot % SlotsPerTeam == 0;

        int IndexOf(int id)
        {
            int lo = 0, hi = _p.Length - 1;
            while (lo <= hi)
            {
                var m = (lo + hi) >> 1;
                if (_p[m].Id == id) return m;
                if (_p[m].Id < id) lo = m + 1; else hi = m - 1;
            }
            return -1;
        }

        public bool Has(int id) => IndexOf(id) >= 0;

        /// <summary>슬롯의 현재 선수 id. 비었으면 −1.</summary>
        public int PlayerAt(int slot) => _slot[slot] < 0 ? Empty : _p[_slot[slot]].Id;
        public bool IsSlotFilled(int slot) => _slot[slot] >= 0;

        public PlayerStatus StatusOf(int id) => _p[Require(id)].Status;
        public int TeamOf(int id) => _p[Require(id)].Team;
        public int SlotOf(int id) => _p[Require(id)].Slot;
        public ref readonly KinematicAttributes AttributesOf(int id) => ref _p[Require(id)].Attributes;
        public int IdAt(int index) => _p[index].Id;

        /// <summary>팀의 경기장 위 인원 (7명 미만 판정 입력, 012 FR-91).</summary>
        public int OnPitchCount(int team)
        {
            int c = 0;
            for (int s = team * SlotsPerTeam; s < (team + 1) * SlotsPerTeam; s++) if (_slot[s] >= 0) c++;
            return c;
        }

        /// <summary>교체: 슬롯 선수를 내보내고 벤치 선수를 넣는다. 검증은 <see cref="Flow.Substitutions"/>가 먼저 한다.</summary>
        public void Substitute(int slot, int inId, long tick, in MotionStats outStats)
        {
            var outIdx = _slot[slot];
            var inIdx = Require(inId);
            if (outIdx < 0) throw new InvalidOperationException($"빈 슬롯 {slot}");
            _p[outIdx].Status = PlayerStatus.SubbedOff;
            _p[outIdx].Slot = Empty;
            _p[outIdx].ExitTick = tick;
            _p[outIdx].Stats = outStats;
            _p[inIdx].Status = PlayerStatus.OnPitch;
            _p[inIdx].Slot = slot;
            _p[inIdx].EnterTick = tick;
            _slot[slot] = inIdx;
        }

        /// <summary>선수를 빈 슬롯으로 옮긴다 (specs/011: 필드 선수가 GK 슬롯을 맡음).</summary>
        public void MoveSlot(int from, int to)
        {
            var idx = _slot[from];
            if (idx < 0) throw new InvalidOperationException($"빈 슬롯 {from}");
            if (_slot[to] >= 0) throw new InvalidOperationException($"슬롯 {to}가 차 있다");
            _slot[to] = idx;
            _p[idx].Slot = to;
            _slot[from] = -1;
        }

        /// <summary>퇴장·교체 없는 부상: 슬롯을 비운다.</summary>
        public void Remove(int slot, PlayerStatus why, long tick, in MotionStats stats)
        {
            var idx = _slot[slot];
            if (idx < 0) throw new InvalidOperationException($"빈 슬롯 {slot}");
            _p[idx].Status = why;
            _p[idx].Slot = Empty;
            _p[idx].ExitTick = tick;
            _p[idx].Stats = stats;
            _slot[slot] = -1;
        }

        /// <summary>출전 시간 (틱). 아직 뛰고 있으면 endTick까지.</summary>
        public long PlayedTicks(int id, long endTick)
        {
            ref readonly var e = ref _p[Require(id)];
            if (e.EnterTick < 0) return 0;
            var exit = e.ExitTick >= 0 ? e.ExitTick : endTick;
            return exit - e.EnterTick;
        }

        /// <summary>나간 선수의 운동 기록 (나갈 때 저장). 뛰는 선수는 운동 시스템에서 읽는다.</summary>
        public ref readonly MotionStats StoredStats(int id) => ref _p[Require(id)].Stats;

        int Require(int id)
        {
            var i = IndexOf(id);
            if (i < 0) throw new ArgumentException($"경기 명단에 없는 선수 {id}", nameof(id));
            return i;
        }

        public void Write(StateWriter w)
        {
            w.Write(_p.Length);
            for (int i = 0; i < _p.Length; i++)
            {
                ref readonly var e = ref _p[i];
                w.Write(e.Id); w.Write((byte)e.Status); w.Write(e.Slot); w.Write(e.EnterTick); w.Write(e.ExitTick);
                e.Stats.Write(w);
            }
            for (int s = 0; s < Slots; s++) w.Write(_slot[s]);
        }

        public void Read(StateReader r)
        {
            var n = r.ReadInt();
            if (n != _p.Length) throw new FormatException($"명단 수 불일치: {n} vs {_p.Length}");
            for (int i = 0; i < n; i++)
            {
                var id = r.ReadInt();
                if (id != _p[i].Id) throw new FormatException($"명단 id 불일치: {id} vs {_p[i].Id}");
                _p[i].Status = (PlayerStatus)r.ReadByte();
                _p[i].Slot = r.ReadInt();
                _p[i].EnterTick = r.ReadLong();
                _p[i].ExitTick = r.ReadLong();
                _p[i].Stats = MotionStats.Read(r);
            }
            for (int s = 0; s < Slots; s++) _slot[s] = r.ReadInt();
        }

        public void AddTo(ref StableHasher h)
        {
            for (int i = 0; i < _p.Length; i++)
            {
                h.Add(_p[i].Id); h.Add((byte)_p[i].Status); h.Add(_p[i].Slot); h.Add(_p[i].EnterTick); h.Add(_p[i].ExitTick);
                _p[i].Stats.AddTo(ref h);
            }
            for (int s = 0; s < Slots; s++) h.Add(_slot[s]);
        }
    }
}
