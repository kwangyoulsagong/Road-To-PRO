using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>앵커·블록 값 보간 (FR-25·71). 전환이 겹치면 현재 값에서 새로 시작한다.</summary>
    public sealed class AnchorBlend
    {
        readonly Vec2[] _from = new Vec2[FormationDefinition.SlotCount];
        readonly Vec2[] _current = new Vec2[FormationDefinition.SlotCount];
        SolvedBlock _fromBlock, _lastBlock;
        float _elapsed, _duration;
        bool _active;

        public Vec2[] Current => _current;
        public bool Active => _active;

        /// <summary>현재 형태에서 시작해 duration초 동안 새 목표로 옮긴다.</summary>
        public void Begin(float duration)
        {
            System.Array.Copy(_current, _from, _from.Length);
            _fromBlock = _lastBlock;
            _elapsed = 0f;
            _duration = duration;
            _active = duration > 0f;
        }

        /// <summary>보간 없이 바로 맞춘다 (경기 시작·하프 전환).</summary>
        public void Snap(FormationDefinition f, ShapeProfile profile, in SolvedBlock block)
        {
            for (int i = 0; i < _current.Length; i++) _current[i] = f.Slots[i].Anchors[(int)profile];
            _lastBlock = block;
            _active = false;
        }

        /// <summary>한 틱 진행하고 이번 틱에 쓸 앵커·블록을 낸다.</summary>
        public void Step(FormationDefinition f, ShapeProfile profile, float dt, in SolvedBlock target, out SolvedBlock block)
        {
            var p = (int)profile;
            if (!_active)
            {
                for (int i = 0; i < _current.Length; i++) _current[i] = f.Slots[i].Anchors[p];
                block = target;
            }
            else
            {
                _elapsed += dt;
                var t = Interp.Clamp01(_elapsed / _duration);
                for (int i = 0; i < _current.Length; i++)
                {
                    var a = f.Slots[i].Anchors[p];
                    _current[i] = new Vec2(Interp.Lerp(_from[i].X, a.X, t), Interp.Lerp(_from[i].Y, a.Y, t));
                }
                block = SolvedBlock.Lerp(in _fromBlock, in target, t);
                if (t >= 1f) _active = false;
            }
            _lastBlock = block;
        }

        static void W(StateWriter w, in SolvedBlock b) { w.Write(b.Cx); w.Write(b.Cy); w.Write(b.L); w.Write(b.W); w.Write(b.LMax); w.Write(b.WMax); }
        static SolvedBlock R(StateReader r) => new SolvedBlock { Cx = r.ReadFloat(), Cy = r.ReadFloat(), L = r.ReadFloat(), W = r.ReadFloat(), LMax = r.ReadFloat(), WMax = r.ReadFloat() };
        static void H(ref StableHasher h, in SolvedBlock b) { h.Add(b.Cx); h.Add(b.Cy); h.Add(b.L); h.Add(b.W); h.Add(b.LMax); h.Add(b.WMax); }

        public void Write(StateWriter w)
        {
            for (int i = 0; i < _from.Length; i++) { w.Write(_from[i]); w.Write(_current[i]); }
            W(w, in _fromBlock); W(w, in _lastBlock); w.Write(_elapsed); w.Write(_duration); w.Write(_active);
        }

        public void Read(StateReader r)
        {
            for (int i = 0; i < _from.Length; i++) { _from[i] = r.ReadVec2(); _current[i] = r.ReadVec2(); }
            _fromBlock = R(r); _lastBlock = R(r); _elapsed = r.ReadFloat(); _duration = r.ReadFloat(); _active = r.ReadBool();
        }

        public void AddTo(ref StableHasher h)
        {
            for (int i = 0; i < _from.Length; i++) { h.Add(_from[i].X); h.Add(_from[i].Y); h.Add(_current[i].X); h.Add(_current[i].Y); }
            H(ref h, in _fromBlock); H(ref h, in _lastBlock); h.Add(_elapsed); h.Add(_duration); h.Add(_active);
        }
    }
}
