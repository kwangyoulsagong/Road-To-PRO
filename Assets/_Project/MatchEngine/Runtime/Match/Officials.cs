using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Match
{
    /// <summary>
    /// 심판 3명 위치 (리플레이 스냅샷 대상, FR-50). 012 전까지 대체: 목표점으로 서브스텝마다 최대 7 m/s 이동.
    /// 0 주심, 1·2 부심(터치라인 밖).
    /// </summary>
    public sealed class Officials
    {
        public const int Count = 3;
        public const float MaxSpeed = 7f;

        readonly Vec2[] _pos = new Vec2[Count];
        readonly Vec2[] _facing = new Vec2[Count];
        readonly Vec2[] _target = new Vec2[Count];

        public Officials(float length, float width)
        {
            _pos[0] = new Vec2(length * 0.5f - 8f, width * 0.5f - 6f);
            _pos[1] = new Vec2(length * 0.75f, -1.5f);
            _pos[2] = new Vec2(length * 0.25f, width + 1.5f);
            for (int i = 0; i < Count; i++) { _target[i] = _pos[i]; _facing[i] = new Vec2(1f, 0f); }
        }

        public Vec2 Position(int i) => _pos[i];
        public Vec2 Facing(int i) => _facing[i];

        public void SetTarget(int i, Vec2 t) => _target[i] = t;

        public void Step(float dt)
        {
            var max = MaxSpeed * dt;
            for (int i = 0; i < Count; i++)
            {
                var d = _target[i] - _pos[i];
                var l = d.Length();
                if (l < 1e-4f) continue;
                var stepLen = l < max ? l : max;
                var dir = d / l;
                _pos[i] = _pos[i] + dir * stepLen;
                _facing[i] = dir;
            }
        }

        public void Write(StateWriter w)
        {
            for (int i = 0; i < Count; i++) { w.Write(_pos[i]); w.Write(_facing[i]); w.Write(_target[i]); }
        }

        public void Read(StateReader r)
        {
            for (int i = 0; i < Count; i++) { _pos[i] = r.ReadVec2(); _facing[i] = r.ReadVec2(); _target[i] = r.ReadVec2(); }
        }

        public void AddTo(ref StableHasher h)
        {
            for (int i = 0; i < Count; i++)
            {
                h.Add(_pos[i].X); h.Add(_pos[i].Y); h.Add(_facing[i].X); h.Add(_facing[i].Y); h.Add(_target[i].X); h.Add(_target[i].Y);
            }
        }
    }
}
