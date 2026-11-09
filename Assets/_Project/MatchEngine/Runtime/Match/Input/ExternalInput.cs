using System;
using System.Collections.Generic;

namespace RoadToPro.MatchEngine.Match.Input
{
    public enum InputKind : byte
    {
        None = 0,
        /// <summary>침투 요구 (키 모먼트 예시, 아키텍처 4).</summary>
        RunInBehind = 1,
        /// <summary>지금 슈팅.</summary>
        ShootNow = 2,
        /// <summary>제자리 유지.</summary>
        HoldPosition = 3,
        /// <summary>교체 요청 (PlayerId = 나가는 선수, Value = 들어오는 선수 id). 그 틱 시작에 교체 대기열로 (리뷰 B2).</summary>
        Substitute = 10,
        /// <summary>뇌진탕 교체 요청.</summary>
        SubstituteConcussion = 11,
        /// <summary>팀 지시 변경 (009 FR-71). PlayerId = 팀 × 100 + 지시 번호, Value = 새 값.</summary>
        TeamInstruction = 20,
        /// <summary>포메이션 변경 (009 FR-71). PlayerId = 팀, Value = 카탈로그 번호.</summary>
        TeamFormation = 21,
    }

    /// <summary>외부 입력 (specs/006 FR-62): (틱, 대상 선수, 내용). 결정 단계에서 그 틱에 적용된다.</summary>
    public readonly struct ExternalInput : IEquatable<ExternalInput>
    {
        public readonly long Tick;
        public readonly int PlayerId;
        public readonly InputKind Kind;
        public readonly float Value;

        public ExternalInput(long tick, int playerId, InputKind kind, float value = 0f)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            Tick = tick;
            PlayerId = playerId;
            Kind = kind;
            Value = value;
        }

        public bool Equals(ExternalInput o) => Tick == o.Tick && PlayerId == o.PlayerId && Kind == o.Kind && Value.Equals(o.Value);
        public override bool Equals(object obj) => obj is ExternalInput e && Equals(e);
        public override int GetHashCode() => unchecked((int)Tick * 31 + PlayerId);
    }

    /// <summary>입력 로그 (FR-51·62): (틱, 선수 id) 순으로 정렬해 보관. 결과에 들어가고 재구성·재시뮬에 쓴다.</summary>
    public sealed class InputLog
    {
        readonly List<ExternalInput> _items = new List<ExternalInput>();

        public int Count => _items.Count;
        public ExternalInput this[int i] => _items[i];

        public void Add(in ExternalInput e)
        {
            int i = _items.Count;
            while (i > 0 && Compare(_items[i - 1], e) > 0) i--;
            _items.Insert(i, e);
        }

        /// <summary>tick의 첫 입력 인덱스 (없으면 그 뒤 첫 인덱스).</summary>
        public int FirstAtOrAfter(long tick)
        {
            int lo = 0, hi = _items.Count;
            while (lo < hi)
            {
                var m = (lo + hi) >> 1;
                if (_items[m].Tick < tick) lo = m + 1; else hi = m;
            }
            return lo;
        }

        public InputLog Clone()
        {
            var c = new InputLog();
            c._items.AddRange(_items);
            return c;
        }

        public ExternalInput[] ToArray() => _items.ToArray();

        static int Compare(in ExternalInput a, in ExternalInput b)
        {
            if (a.Tick != b.Tick) return a.Tick < b.Tick ? -1 : 1;
            if (a.PlayerId != b.PlayerId) return a.PlayerId < b.PlayerId ? -1 : 1;
            return ((byte)a.Kind).CompareTo((byte)b.Kind);
        }
    }

    public enum RefereeActionKind : byte
    {
        SendOff = 1,
        Injury = 2,
        AwardPenalty = 3,
        Stoppage = 4,
        DisallowLastGoal = 5,
        Booking = 6,
    }

    /// <summary>예정된 심판 사건 (012·027이 생기기 전 대체 심판 단계가 실행 — 테스트·검증용 주입 훅).</summary>
    public readonly struct RefereeAction
    {
        public readonly long Tick;
        public readonly RefereeActionKind Kind;
        /// <summary>대상 선수 id (퇴장·부상·경고) 또는 팀 (페널티: 차는 팀).</summary>
        public readonly int Target;
        /// <summary>정지 틱 (부상 처치·VAR 등).</summary>
        public readonly long Ticks;
        /// <summary>정지 요인 (<see cref="Events.EventCodes"/>.Stop*) 또는 뇌진탕 여부(부상: 1).</summary>
        public readonly int Code;

        public RefereeAction(long tick, RefereeActionKind kind, int target, long ticks = 0, int code = 0)
        {
            Tick = tick;
            Kind = kind;
            Target = target;
            Ticks = ticks;
            Code = code;
        }
    }

    /// <summary>예정 심판 사건 목록 (틱 순, 불변 — 상태가 아니라 입력이라 스냅샷에 넣지 않는다).</summary>
    public sealed class RefereeScript
    {
        readonly List<RefereeAction> _items = new List<RefereeAction>();

        public int Count => _items.Count;
        public RefereeAction this[int i] => _items[i];

        public void Add(in RefereeAction a)
        {
            int i = _items.Count;
            while (i > 0 && _items[i - 1].Tick > a.Tick) i--;
            _items.Insert(i, a);
        }

        public int FirstAt(long tick)
        {
            int lo = 0, hi = _items.Count;
            while (lo < hi)
            {
                var m = (lo + hi) >> 1;
                if (_items[m].Tick < tick) lo = m + 1; else hi = m;
            }
            return lo;
        }

        public RefereeScript Clone()
        {
            var c = new RefereeScript();
            c._items.AddRange(_items);
            return c;
        }
    }
}
