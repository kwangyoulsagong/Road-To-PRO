using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>국면 판정 입력 (한 팀 기준, 공 x는 팀 관점).</summary>
    public struct PhaseInput
    {
        public long Tick;
        public int Team;
        public float BallX;
        public int PossessionTeam;
        public bool Confirmed;
        public long PossessionChangeTick;
        public long LooseTicks;
        public int LoosePreviousTeam;
        public bool DeadBall;
        public int OpponentsBehindBall;
        public float LineHeight;
        /// <summary>인원 감소로 내리는 블록 단계 수 (FR-92).</summary>
        public int BlockDrop;
    }

    /// <summary>
    /// 팀 국면 FSM (FR-20~24). 점유 변경은 즉시, 3분할·블록 구간은 히스테리시스와 최소 체류를 거친다.
    /// 점유 미확정이면 직전 국면을 유지하고, 루즈볼이 오래가면 양 팀을 전환 국면으로 본다.
    /// </summary>
    public sealed class PhaseMachine
    {
        readonly ShapeParams _p;
        TeamPhase _phase = TeamPhase.DefMid;
        TeamPhase _beforeDead = TeamPhase.DefMid;
        bool _started;
        long _enterTick;
        long _transitionTick = -1;
        int _lastPossession = -1;
        bool _looseApplied;
        TeamPhase _block = TeamPhase.DefMid;
        long _blockTick = -1000;

        public PhaseMachine(ShapeParams p) => _p = p;

        public TeamPhase Current => _phase;
        /// <summary>지시와 공 위치로 정한 현재 수비 블록 (DefHigh/Mid/Low). Regroup 앵커에도 쓴다.</summary>
        public TeamPhase Block => _block;
        /// <summary>전환 국면 경과 틱.</summary>
        public long TransitionTicks(long tick) => _transitionTick < 0 ? 0 : tick - _transitionTick;

        public static bool IsPossessing(TeamPhase p) => p <= TeamPhase.AttTransition;
        public static bool IsDefBlock(TeamPhase p) => p >= TeamPhase.DefHigh && p <= TeamPhase.DefLow;

        /// <summary>한 틱 판정. 바뀌면 true.</summary>
        public bool Step(in PhaseInput i)
        {
            var before = _started ? _phase : (TeamPhase)255;
            UpdateBlock(in i);

            if (i.DeadBall)
            {
                if (_phase != TeamPhase.SetPiece) { _beforeDead = _phase; Set(TeamPhase.SetPiece, i.Tick); }
                _lastPossession = i.PossessionTeam;
                _started = true;
                return before != _phase;
            }
            if (_phase == TeamPhase.SetPiece) Set(_beforeDead, i.Tick);

            bool mine = i.PossessionTeam == i.Team;
            if (i.PossessionTeam != _lastPossession && i.PossessionTeam >= 0)
            {
                // 점유 변경: 첫 점유(직전 없음)는 전환이 아니다
                if (_lastPossession >= 0)
                {
                    _transitionTick = i.PossessionChangeTick;
                    Set(mine ? TeamPhase.AttTransition : TeamPhase.DefTransition, i.Tick);
                }
                else Set(mine ? ZoneFresh(i.BallX) : _block, i.Tick);
                _lastPossession = i.PossessionTeam;
                _looseApplied = false;
            }

            if (i.PossessionTeam < 0)
            {
                _started = true;
                return before != _phase;
            }

            if (!i.Confirmed)
            {
                if (i.LooseTicks > _p.LooseTicks && !_looseApplied)
                {
                    _looseApplied = true;
                    _transitionTick = i.Tick;
                    Set(mine ? TeamPhase.DefTransition : TeamPhase.AttTransition, i.Tick);
                }
                _started = true;
                return before != _phase;
            }
            _looseApplied = false;

            if (mine) StepPossessing(in i);
            else StepDefending(in i);
            _started = true;
            return before != _phase;
        }

        void StepPossessing(in PhaseInput i)
        {
            if (_phase == TeamPhase.AttTransition)
            {
                var t = i.Tick - _transitionTick;
                bool unaligned = i.OpponentsBehindBall < _p.UnalignedBehind;
                if (t < _p.AttMinTicks || (unaligned && t < _p.AttMaxTicks)) return;
                Set(ZoneFresh(i.BallX), i.Tick);
                return;
            }
            if (!IsPossessing(_phase)) { Set(ZoneFresh(i.BallX), i.Tick); return; }
            var z = ZoneHysteresis(i.BallX);
            if (z != _phase && i.Tick - _enterTick >= _p.MinDwellTicks) Set(z, i.Tick);
        }

        void StepDefending(in PhaseInput i)
        {
            if (_phase == TeamPhase.DefTransition)
            {
                if (i.Tick - _transitionTick < _p.DefTicks) return;
                Set(_block, i.Tick);
                return;
            }
            if (_block != _phase) Set(_block, i.Tick);
        }

        TeamPhase ZoneFresh(float x) => x < _p.ThirdLow ? TeamPhase.BuildUp : (x < _p.ThirdHigh ? TeamPhase.Progression : TeamPhase.FinalThird);

        TeamPhase ZoneHysteresis(float x)
        {
            var h = _p.HysteresisX;
            switch (_phase)
            {
                case TeamPhase.BuildUp:
                    return x >= _p.ThirdHigh + h ? TeamPhase.FinalThird : (x >= _p.ThirdLow + h ? TeamPhase.Progression : TeamPhase.BuildUp);
                case TeamPhase.Progression:
                    return x < _p.ThirdLow - h ? TeamPhase.BuildUp : (x >= _p.ThirdHigh + h ? TeamPhase.FinalThird : TeamPhase.Progression);
                default:
                    return x < _p.ThirdLow - h ? TeamPhase.BuildUp : (x < _p.ThirdHigh - h ? TeamPhase.Progression : TeamPhase.FinalThird);
            }
        }

        void UpdateBlock(in PhaseInput i)
        {
            int level = i.LineHeight < _p.BlockLowBound ? 0 : (i.LineHeight < _p.BlockHighBound ? 1 : 2);
            var dropAt = _p.BlockDropBallX + (_block == BlockOf(level) ? 0f : _p.HysteresisX);
            if (i.BallX < dropAt) level--;
            level = Interp.Clamp(level - i.BlockDrop, 0, 2);
            var b = BlockOf(level);
            if (b != _block && (_blockTick < 0 || i.Tick - _blockTick >= _p.MinDwellTicks))
            {
                _block = b;
                _blockTick = i.Tick;
            }
        }

        static TeamPhase BlockOf(int level) => level == 0 ? TeamPhase.DefLow : (level == 1 ? TeamPhase.DefMid : TeamPhase.DefHigh);

        void Set(TeamPhase p, long tick)
        {
            _phase = p;
            _enterTick = tick;
        }

        public void Write(StateWriter w)
        {
            w.Write((byte)_phase); w.Write((byte)_beforeDead); w.Write(_started); w.Write(_enterTick); w.Write(_transitionTick);
            w.Write(_lastPossession); w.Write(_looseApplied); w.Write((byte)_block); w.Write(_blockTick);
        }

        public void Read(StateReader r)
        {
            _phase = (TeamPhase)r.ReadByte(); _beforeDead = (TeamPhase)r.ReadByte(); _started = r.ReadBool(); _enterTick = r.ReadLong(); _transitionTick = r.ReadLong();
            _lastPossession = r.ReadInt(); _looseApplied = r.ReadBool(); _block = (TeamPhase)r.ReadByte(); _blockTick = r.ReadLong();
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add((byte)_phase); h.Add((byte)_beforeDead); h.Add(_started); h.Add(_enterTick); h.Add(_transitionTick);
            h.Add(_lastPossession); h.Add(_looseApplied); h.Add((byte)_block); h.Add(_blockTick);
        }
    }
}
