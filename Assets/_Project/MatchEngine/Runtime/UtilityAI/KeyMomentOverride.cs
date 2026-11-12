using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 키 모먼트 선택 입구 (specs/010 FR-52). 019가 플레이어 선택을 넣으면 그 선수의 다음 결정 1회를 덮어쓴다(의도 보너스·softmax보다 우선).
    /// 019 전에는 테스트·외부 입력만 쓴다. 상태는 직렬화·해시 대상.
    /// </summary>
    public sealed class KeyMomentOverride
    {
        readonly bool[] _has = new bool[DecisionWorld.N];
        readonly Consideration[] _kind = new Consideration[DecisionWorld.N];
        readonly Vec2[] _point = new Vec2[DecisionWorld.N];

        public void Set(int slot, Consideration kind, Vec2 point)
        {
            _has[slot] = true;
            _kind[slot] = kind;
            _point[slot] = point;
        }

        public bool Has(int slot) => _has[slot];

        /// <summary>대기 중인 선택을 꺼낸다 (한 번만).</summary>
        public bool TryTake(int slot, out Consideration kind, out Vec2 point)
        {
            kind = _kind[slot];
            point = _point[slot];
            if (!_has[slot]) return false;
            _has[slot] = false;
            return true;
        }

        public void Clear(int slot) => _has[slot] = false;

        public void Write(StateWriter w)
        {
            for (int s = 0; s < DecisionWorld.N; s++) { w.Write(_has[s]); w.Write((byte)_kind[s]); w.Write(_point[s]); }
        }

        public void Read(StateReader r)
        {
            for (int s = 0; s < DecisionWorld.N; s++) { _has[s] = r.ReadBool(); _kind[s] = (Consideration)r.ReadByte(); _point[s] = r.ReadVec2(); }
        }

        public void AddTo(ref StableHasher h)
        {
            for (int s = 0; s < DecisionWorld.N; s++) { h.Add(_has[s]); h.Add((byte)_kind[s]); h.Add(_point[s].X); h.Add(_point[s].Y); }
        }
    }
}
