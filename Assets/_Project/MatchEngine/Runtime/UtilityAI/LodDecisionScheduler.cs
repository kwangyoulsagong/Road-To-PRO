using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 결정 LOD (specs/010 FR-40·41 — 006 FR-05는 실행만). 공 소유자·공 반경 20 m 안·시나리오 배역은 매 틱,
    /// 그 밖은 거리에 따라 2~4틱마다(위상은 슬롯 id로 분산). 즉시 재결정 트리거(공 소유 변화·인플레이 아님·상태 전환·단계 요청)가 있으면
    /// 다음 틱에 결정한다. 트리거 없는 틱에는 먼 선수를 상한(12명)까지만 넣고 나머지는 한 틱 미룬다(트리거·배역은 상한 제외).
    /// 인플레이가 아니면(데드볼·골 후) 전원 매 틱 — 058 데드볼 흐름이 매 틱 결정을 전제한다.
    /// </summary>
    public sealed class LodDecisionScheduler : IDecisionScheduler
    {
        readonly DecisionParams _p;
        readonly long[] _next = new long[DecisionWorld.N];
        readonly bool[] _req = new bool[DecisionWorld.N];
        readonly bool[] _trig = new bool[DecisionWorld.N];
        bool _reqAll;
        bool _started;
        int _lastOwner = -1;
        FlowState _lastState;

        public LodDecisionScheduler(DecisionParams p) => _p = p;

        /// <summary>배역 여부 (없으면 배역 없음). 단계가 연결한다.</summary>
        public IIntentSource Cast { get; set; }

        /// <summary>다음 틱 즉시 결정 (의도 도착·해제·게겐프레싱 창 시작, FR-41).</summary>
        public void Request(int slot) => _req[slot] = true;
        public void RequestAll() => _reqAll = true;

        /// <summary>이번 틱 선택에서 트리거로 들어간 슬롯인가 (결정자에 trigger로 넘긴다).</summary>
        public bool WasTriggered(int slot) => _trig[slot];

        /// <summary>거리 → 결정 주기 (반경 안 1, 반경 ~ 먼 반경 사이 2~4 보간·올림).</summary>
        public int PeriodFor(float dist)
        {
            if (dist <= _p.LodRadius) return 1;
            var span = _p.LodFarRadius - _p.LodRadius;
            var u = span > 0f ? (dist - _p.LodRadius) / span : 1f;
            if (u > 1f) u = 1f;
            var per = _p.LodPeriodMin + (_p.LodPeriodMax - _p.LodPeriodMin) * u;
            var k = (int)per;
            if (k < per) k++; // 올림: 반경 바로 밖도 주기 3 이상 — "10틱 동안 2~4회"(AC-08)와 "2~4틱마다"(FR-40)를 함께 만족
            return k < 1 ? 1 : k;
        }

        public int Select(long tick, IMatchView view, int[] outSlots)
        {
            var ball = view.Ball;
            var owner = ball.Phase == BallPhase.Controlled ? ball.OwnerId : -1;
            var state = view.State;
            var all = _reqAll || !_started || owner != _lastOwner || state != FlowState.InPlay || state != _lastState;
            _started = true;
            _reqAll = false;
            _lastOwner = owner;
            _lastState = state;

            int n = 0, far = 0;
            var cap = (int)_p.LodCap;
            var bxy = ball.Position.XY;
            var r2 = _p.LodRadius * _p.LodRadius;
            for (int s = 0; s < DecisionWorld.N; s++)
            {
                _trig[s] = false;
                if (!view.IsSlotActive(s)) { _req[s] = false; continue; }
                var pos = view.SlotState(s).Position;
                var d2 = (pos - bxy).LengthSq();
                if (all || _req[s])
                {
                    _trig[s] = true;
                    _req[s] = false;
                    outSlots[n++] = s;
                    _next[s] = tick + PeriodFor(Core.Math.DMath.Sqrt(d2)) + (s & 1);
                    continue;
                }
                var cast = Cast != null && Cast.IsCast(s);
                if (s == owner || cast || d2 <= r2)
                {
                    if (!cast) far++; // 반경 안 비배역도 동시 인원에 센다
                    outSlots[n++] = s;
                    _next[s] = tick + 1;
                    continue;
                }
                if (tick < _next[s]) continue;
                if (far >= cap) { _next[s] = tick + 1; continue; } // 상한 → 한 틱 미룸
                far++;
                outSlots[n++] = s;
                _next[s] = tick + PeriodFor(Core.Math.DMath.Sqrt(d2));
            }
            return n;
        }

        public void WriteState(StateWriter w)
        {
            w.Write(_started); w.Write(_reqAll); w.Write(_lastOwner); w.Write((byte)_lastState);
            for (int s = 0; s < DecisionWorld.N; s++) { w.Write(_next[s]); w.Write(_req[s]); }
        }

        public void ReadState(StateReader r)
        {
            _started = r.ReadBool(); _reqAll = r.ReadBool(); _lastOwner = r.ReadInt(); _lastState = (FlowState)r.ReadByte();
            for (int s = 0; s < DecisionWorld.N; s++) { _next[s] = r.ReadLong(); _req[s] = r.ReadBool(); }
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_started); h.Add(_reqAll); h.Add(_lastOwner); h.Add((byte)_lastState);
            for (int s = 0; s < DecisionWorld.N; s++) { h.Add(_next[s]); h.Add(_req[s]); }
        }
    }
}
