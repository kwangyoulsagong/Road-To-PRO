using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>카드 집행 결과 (specs/012 FR-22·29).</summary>
    public enum CardOutcome : byte
    {
        /// <summary>이미 경기에서 빠진 선수 등 — 집행 안 함.</summary>
        Ignored = 0,
        Yellow = 1,
        /// <summary>두 번째 경고 → 퇴장.</summary>
        SecondYellow = 2,
        Red = 3,
    }

    /// <summary>선수 행동 신고 (FR-26·28: 항의·지연·셔츠 세리머니·허가 없는 출입·감독 항의). 행동을 만드는 쪽(050·060·010)이 부른다.</summary>
    public enum ConductKind : byte
    {
        Dissent = 1,
        Delay = 2,
        ShirtOff = 3,
        EnterWithoutPermission = 4,
        BenchDissent = 5,
    }

    /// <summary>
    /// 경기 중 징계 기록 (specs/012 FR-22·26·28·29). 선수 id 기준 경고 수·파울 수(고정 배열, 경기 선수 수 상한 48), 감독 항의 누적,
    /// 다음 정지 때 집행할 보류 카드 큐(어드밴티지, FR-42). RNG 없음. 경기 간 누적은 050.
    /// </summary>
    public sealed class Discipline
    {
        public const int MaxPlayers = 48;
        public const int MaxDeferred = 8;
        /// <summary>감독 항의 이 횟수면 벤치 퇴장 (FR-28 [제안]: 경고 1 → 2회째 퇴장).</summary>
        public const int BenchDissentLimit = 2;

        readonly int[] _ids = new int[MaxPlayers];
        readonly byte[] _yellows = new byte[MaxPlayers];
        readonly byte[] _fouls = new byte[MaxPlayers];
        readonly bool[] _sentOff = new bool[MaxPlayers];
        int _n;
        readonly int[] _benchDissent = new int[2];
        readonly bool[] _benchSentOff = new bool[2];

        readonly int[] _defId = new int[MaxDeferred];
        readonly byte[] _defColor = new byte[MaxDeferred];
        readonly byte[] _defReason = new byte[MaxDeferred];
        int _defN;

        int Index(int id, bool create)
        {
            for (int i = 0; i < _n; i++) if (_ids[i] == id) return i;
            if (!create || _n == MaxPlayers) return -1;
            _ids[_n] = id; _yellows[_n] = 0; _fouls[_n] = 0; _sentOff[_n] = false;
            return _n++;
        }

        public int Yellows(int id) { var i = Index(id, false); return i < 0 ? 0 : _yellows[i]; }
        public int Fouls(int id) { var i = Index(id, false); return i < 0 ? 0 : _fouls[i]; }
        public bool IsSentOff(int id) { var i = Index(id, false); return i >= 0 && _sentOff[i]; }
        public bool BenchSentOff(int team) => _benchSentOff[team];
        public int DeferredCount => _defN;

        /// <summary>FR-26 반복 반칙 집계: 휘슬 분 파울 1건.</summary>
        public void AddFoul(int id)
        {
            var i = Index(id, true);
            if (i >= 0 && _fouls[i] < byte.MaxValue) _fouls[i]++;
        }

        /// <summary>
        /// FR-22·29: 카드 집행. 경고 2장째 → <see cref="CardOutcome.SecondYellow"/>(퇴장). 이미 퇴장한 선수 → 무시.
        /// </summary>
        public CardOutcome Book(int id, int color)
        {
            var i = Index(id, true);
            if (i < 0 || _sentOff[i]) return CardOutcome.Ignored;
            if (color == EventCodes.CardRed) { _sentOff[i] = true; return CardOutcome.Red; }
            _yellows[i]++;
            if (_yellows[i] >= 2) { _sentOff[i] = true; return CardOutcome.SecondYellow; }
            return CardOutcome.Yellow;
        }

        /// <summary>FR-42: 어드밴티지 뒤 다음 정지 때 집행할 카드. 가득 차면 false.</summary>
        public bool Defer(int id, int color, int reason)
        {
            if (_defN == MaxDeferred) return false;
            _defId[_defN] = id; _defColor[_defN] = (byte)color; _defReason[_defN] = (byte)reason;
            _defN++;
            return true;
        }

        public void DeferredAt(int i, out int id, out int color, out int reason) { id = _defId[i]; color = _defColor[i]; reason = _defReason[i]; }
        public void ClearDeferred() => _defN = 0;

        /// <summary>
        /// FR-26·28: 행동 신고 → 카드 색 (0 = 카드 없음). 항의·지연·셔츠·출입 = 경고. 감독 항의는 1회째 경고(벤치), 누적 <see cref="BenchDissentLimit"/>회 = 벤치 퇴장.
        /// </summary>
        public int Conduct(ConductKind kind, int team, out int reason)
        {
            switch (kind)
            {
                case ConductKind.Dissent: reason = EventCodes.ReasonDissent; return EventCodes.CardYellow;
                case ConductKind.Delay: reason = EventCodes.ReasonDelay; return EventCodes.CardYellow;
                case ConductKind.ShirtOff: reason = EventCodes.ReasonShirtOff; return EventCodes.CardYellow;
                case ConductKind.EnterWithoutPermission: reason = EventCodes.ReasonEnterWithoutPermission; return EventCodes.CardYellow;
                case ConductKind.BenchDissent:
                    reason = EventCodes.ReasonBench;
                    if (_benchSentOff[team]) return 0;
                    _benchDissent[team]++;
                    if (_benchDissent[team] >= BenchDissentLimit) { _benchSentOff[team] = true; return EventCodes.CardRed; }
                    return EventCodes.CardYellow;
            }
            reason = 0;
            return 0;
        }

        public void Write(StateWriter w)
        {
            w.Write(_n);
            for (int i = 0; i < _n; i++) { w.Write(_ids[i]); w.Write(_yellows[i]); w.Write(_fouls[i]); w.Write(_sentOff[i]); }
            w.Write(_benchDissent[0]); w.Write(_benchDissent[1]); w.Write(_benchSentOff[0]); w.Write(_benchSentOff[1]);
            w.Write(_defN);
            for (int i = 0; i < _defN; i++) { w.Write(_defId[i]); w.Write(_defColor[i]); w.Write(_defReason[i]); }
        }

        public void Read(StateReader r)
        {
            _n = r.ReadInt();
            for (int i = 0; i < _n; i++) { _ids[i] = r.ReadInt(); _yellows[i] = r.ReadByte(); _fouls[i] = r.ReadByte(); _sentOff[i] = r.ReadBool(); }
            _benchDissent[0] = r.ReadInt(); _benchDissent[1] = r.ReadInt(); _benchSentOff[0] = r.ReadBool(); _benchSentOff[1] = r.ReadBool();
            _defN = r.ReadInt();
            for (int i = 0; i < _defN; i++) { _defId[i] = r.ReadInt(); _defColor[i] = r.ReadByte(); _defReason[i] = r.ReadByte(); }
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_n);
            for (int i = 0; i < _n; i++) { h.Add(_ids[i]); h.Add(_yellows[i]); h.Add(_fouls[i]); h.Add(_sentOff[i]); }
            h.Add(_benchDissent[0]); h.Add(_benchDissent[1]); h.Add(_benchSentOff[0]); h.Add(_benchSentOff[1]);
            h.Add(_defN);
            for (int i = 0; i < _defN; i++) { h.Add(_defId[i]); h.Add(_defColor[i]); h.Add(_defReason[i]); }
        }
    }
}
