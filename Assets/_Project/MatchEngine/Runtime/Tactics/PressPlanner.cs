using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.Core.Time;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>압박 판단 입력. 위치는 팀 관점 정규화, 인덱스는 팀 내 슬롯(0~10).</summary>
    public struct PressContext
    {
        public long Tick;
        public bool Defending;
        public bool CounterPressWindow;
        public Vec2 Ball;
        public Vec2[] Own, Opp;
        public bool[] OwnCandidate, OwnActive, OppActive;
    }

    /// <summary>팀 압박 트리거와 1st/2nd/3rd 지정 (FR-60·61, A7). 시나리오 배역·GK는 후보에서 빠진다.</summary>
    public sealed class PressPlanner
    {
        readonly ShapeParams _p;
        readonly int[] _who = { -1, -1, -1 };
        long _until = -1;

        public PressPlanner(ShapeParams p) => _p = p;

        public bool Active { get; private set; }
        public int Count { get; private set; }
        /// <summary>order: 0 = 1st, 1 = 2nd, 2 = 3rd. 없으면 −1.</summary>
        public int Presser(int order) => _who[order];

        /// <summary>트리거 점수 T (FR-60).</summary>
        public float Score(in PressSignals s, in PressContext c)
        {
            var t = 0f;
            if (s.BackPassReceived) t += _p.WBackPass;
            if (s.TouchQuality < _p.BadTouchBelow) t += _p.WBadTouch;
            if (s.ReceiverFacingOwnGoal) t += _p.WFacingOwnGoal;
            if (c.Ball.Y < _p.TouchlineY || c.Ball.Y > 1f - _p.TouchlineY) t += _p.WTouchline;
            if (s.LoftedOrSlow) t += _p.WLoftedSlow;
            if (NumericalSuperiority(in c)) t += _p.WNumericalSup;
            if (s.GkShort) t += _p.WGkShort;
            return t;
        }

        public float Theta(PressLineLevel line, float ballX)
        {
            int l = (int)line;
            var over = ballX - _p.Xb[l];
            return _p.Theta[l] + (over > 0f ? over * _p.ThetaSlope : 0f);
        }

        public void Step(in PressSignals s, in PressContext c, in TeamInstructions ins)
        {
            Count = 0;
            _who[0] = _who[1] = _who[2] = -1;
            if (!c.Defending || ins.PressIntensity <= 0f)
            {
                Active = false;
                _until = -1;
                return;
            }
            if (Score(in s, in c) * ins.PressIntensity > Theta(ins.PressLine, c.Ball.X))
                _until = c.Tick + (long)((_p.HoldBase + _p.HoldIntensity * ins.PressIntensity) * SimClock.TicksPerSecond);
            Active = c.Tick < _until;
            if (!Active) return;

            int want = ins.PressIntensity < _p.Count2At ? 1 : (ins.PressIntensity < _p.Count3At ? 2 : 3);
            if (c.CounterPressWindow && want < 3) want++;

            _who[0] = Nearest(in c, c.Ball, -1, -1);
            if (_who[0] >= 0) Count = 1;
            if (want >= 2 && Count == 1)
            {
                var carrier = NearestOpp(in c, c.Ball, -1);
                var target = NearestOpp(in c, c.Ball, carrier);
                var aim = target >= 0 ? (c.Ball + c.Opp[target]) * 0.5f : c.Ball;
                _who[1] = Nearest(in c, aim, _who[0], -1);
                if (_who[1] >= 0) Count = 2;
            }
            if (want >= 3 && Count == 2)
            {
                _who[2] = Nearest(in c, new Vec2(c.Ball.X - _p.BalanceBackX, c.Ball.Y), _who[0], _who[1]);
                if (_who[2] >= 0) Count = 3;
            }
        }

        bool NumericalSuperiority(in PressContext c)
        {
            var rx = _p.SupRadius / _p.PitchL;
            var ry = _p.SupRadius / _p.PitchW;
            int own = 0, opp = 0;
            for (int i = 0; i < c.Own.Length; i++)
            {
                if (c.OwnActive[i] && InRadius(c.Own[i], c.Ball, rx, ry)) own++;
                if (c.OppActive[i] && InRadius(c.Opp[i], c.Ball, rx, ry)) opp++;
            }
            return own > opp;
        }

        static bool InRadius(Vec2 a, Vec2 c, float rx, float ry)
        {
            var dx = (a.X - c.X) / rx;
            var dy = (a.Y - c.Y) / ry;
            return dx * dx + dy * dy <= 1f;
        }

        int Nearest(in PressContext c, Vec2 point, int skipA, int skipB)
        {
            int best = -1;
            var bd = float.MaxValue;
            for (int i = 1; i < c.Own.Length; i++)
            {
                if (!c.OwnCandidate[i] || i == skipA || i == skipB) continue;
                var d = DistSq(c.Own[i], point);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        int NearestOpp(in PressContext c, Vec2 point, int skip)
        {
            int best = -1;
            var bd = float.MaxValue;
            for (int i = 1; i < c.Opp.Length; i++)
            {
                if (!c.OppActive[i] || i == skip) continue;
                var d = DistSq(c.Opp[i], point);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        float DistSq(Vec2 a, Vec2 b)
        {
            var dx = (a.X - b.X) * _p.PitchL;
            var dy = (a.Y - b.Y) * _p.PitchW;
            return dx * dx + dy * dy;
        }

        public void Write(StateWriter w) { w.Write(_until); w.Write(Active); w.Write(Count); w.Write(_who[0]); w.Write(_who[1]); w.Write(_who[2]); }
        public void Read(StateReader r) { _until = r.ReadLong(); Active = r.ReadBool(); Count = r.ReadInt(); _who[0] = r.ReadInt(); _who[1] = r.ReadInt(); _who[2] = r.ReadInt(); }
        public void AddTo(ref StableHasher h) { h.Add(_until); h.Add(Active); h.Add(Count); h.Add(_who[0]); h.Add(_who[1]); h.Add(_who[2]); }
    }
}
