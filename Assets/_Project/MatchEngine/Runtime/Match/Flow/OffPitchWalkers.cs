using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Match.Flow
{
    /// <summary>걸어서 피치를 나가거나 들어오는 이유 (specs/058 FR-61·65).</summary>
    public enum WalkKind : byte
    {
        None = 0,
        /// <summary>퇴장: 경기 인원에서 바로 빠지고 가까운 터치라인으로 걸어 나간다.</summary>
        SentOff = 1,
        /// <summary>부상 퇴장(교체 없음).</summary>
        InjuredOff = 2,
        /// <summary>교체 아웃: 터치라인에 도착하면 그 자리에서 투입 선수와 교대한다.</summary>
        SubOut = 3,
        /// <summary>교체 투입: 터치라인에서 피치 안쪽으로 걸어 들어온 뒤 판단 단계가 넘겨받는다.</summary>
        SubIn = 4,
    }

    /// <summary>
    /// 걸어 나감·들어옴 (specs/058 FR-61·65). 순간이동·즉시 삭제 대신 운동 시스템 안에서 걷게 한다. 조향 명령은 여기서 직접 넣고,
    /// 걷는 선수는 판단·공 잡기·배치 도착 판정에서 빠진다(<see cref="MatchContext.IsSlotControllable"/>).
    /// 교체는 나가는 선수가 터치라인 밖 지점에 닿는 순간 같은 자리에서 교대한다 — 리플레이 프레임 엔티티 수(26)를 늘리지 않기 위한 선택(058 플랜).
    /// 고정 배열, 슬롯당 최대 1건, 삽입 순서로 처리한다. RNG 없음.
    /// </summary>
    public sealed class OffPitchWalkers
    {
        public const int Capacity = Squad.Slots;
        const float ArriveRadius = 0.5f, EnterDepth = 3f;

        readonly MatchContext _ctx;
        readonly WalkKind[] _kind = new WalkKind[Squad.Slots];
        readonly int[] _id = new int[Squad.Slots];
        readonly int[] _inId = new int[Squad.Slots];
        readonly int[] _a = new int[Squad.Slots];
        readonly int[] _b = new int[Squad.Slots];
        readonly Vec2[] _exit = new Vec2[Squad.Slots];

        internal OffPitchWalkers(MatchContext ctx) => _ctx = ctx;

        public WalkKind KindOf(int slot) => _kind[slot];
        public bool IsWalking(int slot) => _kind[slot] != WalkKind.None;
        /// <summary>경기 인원에서 빠졌지만 아직 걸어 나가는 중인 선수 id (없으면 −1). 리플레이 기록이 쓴다.</summary>
        public int LeavingIdAt(int slot) => _kind[slot] == WalkKind.SentOff || _kind[slot] == WalkKind.InjuredOff ? _id[slot] : -1;
        public Vec2 ExitOf(int slot) => _exit[slot];

        /// <summary>교체 아웃이 걸어 나가는 중이라 아직 들어오지 않은 투입 선수인가.</summary>
        public bool IsPendingIn(int playerId)
        {
            for (int s = 0; s < Squad.Slots; s++)
                if (_kind[s] == WalkKind.SubOut && _inId[s] == playerId) return true;
            return false;
        }

        public int Count
        {
            get
            {
                int c = 0;
                for (int s = 0; s < Squad.Slots; s++) if (_kind[s] != WalkKind.None) c++;
                return c;
            }
        }

        /// <summary>가까운 터치라인 밖 지점 (같은 x, 터치라인에서 여유만큼 밖).</summary>
        Vec2 ExitFor(Vec2 pos)
        {
            var L = _ctx.Pitch.LengthM;
            var W = _ctx.Pitch.WidthM;
            var m = _ctx.Params.WalkOffMargin;
            var x = pos.X < 1f ? 1f : (pos.X > L - 1f ? L - 1f : pos.X);
            return new Vec2(x, pos.Y < W * 0.5f ? -m : W + m);
        }

        float WalkSpeed(int slot) => _ctx.Kinematics.Profile(slot).MaxSpeed * _ctx.Params.ReturnSpeedMin;

        void Steer(int slot, Vec2 to) => _ctx.Kinematics.SetCommand(slot, SteeringCommand.Arrive(to, WalkSpeed(slot)));

        /// <summary>퇴장·부상 퇴장: 경기 인원에서 빠진 슬롯(운동은 활성)을 출구로 걷게 한다.</summary>
        internal void BeginLeave(int slot, int playerId, WalkKind kind)
        {
            _kind[slot] = kind;
            _id[slot] = playerId;
            _inId[slot] = -1;
            _exit[slot] = ExitFor(_ctx.SlotState(slot).Position);
            Steer(slot, _exit[slot]);
        }

        /// <summary>교체 아웃 시작: 슬롯은 그대로 두고 출구로 걷게 한다. a·b는 교대 순간 Substitution 이벤트 값.</summary>
        internal void BeginSubstitution(int slot, int outId, int inId, int a, int b)
        {
            _kind[slot] = WalkKind.SubOut;
            _id[slot] = outId;
            _inId[slot] = inId;
            _a[slot] = a;
            _b[slot] = b;
            _exit[slot] = ExitFor(_ctx.SlotState(slot).Position);
            Steer(slot, _exit[slot]);
        }

        /// <summary>틱 끝: 출구에 닿은 선수를 처리한다 (슬롯 오름차순).</summary>
        internal void EndTick()
        {
            for (int s = 0; s < Squad.Slots; s++)
            {
                var kind = _kind[s];
                if (kind == WalkKind.None) continue;
                var pos = _ctx.SlotState(s).Position;
                if ((pos - _exit[s]).LengthSq() > ArriveRadius * ArriveRadius)
                {
                    // 판단 단계가 명령을 덮지 않지만, 접촉 등으로 큐가 비면 다시 넣는다
                    ref readonly var act = ref _ctx.Kinematics.State(s).Active;
                    if (_ctx.Kinematics.PendingCommands(s) == 0 && (act.Kind != SteeringKind.Arrive || !act.Point.Equals(_exit[s]))) Steer(s, _exit[s]); // 리뷰 L1
                    continue;
                }
                switch (kind)
                {
                    case WalkKind.SentOff:
                    case WalkKind.InjuredOff:
                        Left(s, _id[s], kind == WalkKind.SentOff ? EventCodes.LeaveSentOff : EventCodes.LeaveInjured, pos);
                        _ctx.Kinematics.SetActive(s, false);
                        _kind[s] = WalkKind.None;
                        break;
                    case WalkKind.SubOut:
                        Swap(s, pos);
                        break;
                    case WalkKind.SubIn:
                        _kind[s] = WalkKind.None; // 판단 단계가 넘겨받는다
                        break;
                }
            }
        }

        void Left(int slot, int id, int reason, Vec2 pos)
        {
            var e = SimEvent.Make(SimEventKind.PlayerLeftPitch, Squad.TeamOfSlot(slot), id);
            e.Substep = SimEvent.TickEnd;
            e.A = reason;
            _ctx.SetPosition(ref e, pos);
            _ctx.Emit(e);
        }

        /// <summary>교대: 나간 선수 기록 → 같은 자리에서 투입 선수로 바꾸고 피치 안쪽으로 걷게 한다.</summary>
        void Swap(int slot, Vec2 pos)
        {
            var outId = _id[slot];
            var inId = _inId[slot];
            var team = Squad.TeamOfSlot(slot);
            Left(slot, outId, EventCodes.LeaveSubstituted, pos);
            var k = _ctx.Kinematics;
            _ctx.Squad.Substitute(slot, inId, _ctx.Tick, k.Stats(slot));
            var W = _ctx.Pitch.WidthM;
            var inward = pos.Y < W * 0.5f ? 1f : -1f;
            k.ResetSlot(slot, _ctx.Squad.AttributesOf(inId), pos, new Vec2(0f, inward));
            var ev = SimEvent.Make(SimEventKind.Substitution, team, inId, outId);
            ev.Substep = SimEvent.TickEnd;
            ev.A = _a[slot];
            ev.B = _b[slot];
            _ctx.SetPosition(ref ev, pos);
            _ctx.Emit(ev);
            var en = SimEvent.Make(SimEventKind.PlayerEntered, team, inId, outId);
            en.Substep = SimEvent.TickEnd;
            _ctx.SetPosition(ref en, pos);
            _ctx.Emit(en);
            _kind[slot] = WalkKind.SubIn;
            _id[slot] = inId;
            _inId[slot] = -1;
            var y = inward > 0f ? EnterDepth : W - EnterDepth;
            _exit[slot] = new Vec2(pos.X, y);
            Steer(slot, _exit[slot]);
        }

        /// <summary>
        /// 기간 사이(하프타임 등): 걷던 선수를 끝낸다 — 퇴장은 비활성, 교체는 바로 교대. 이어지는 킥오프 배치(FR-10)가 자리를 잡는다.
        /// </summary>
        internal void FlushAtBreak()
        {
            for (int s = 0; s < Squad.Slots; s++)
            {
                switch (_kind[s])
                {
                    case WalkKind.SentOff:
                    case WalkKind.InjuredOff:
                        Left(s, _id[s], _kind[s] == WalkKind.SentOff ? EventCodes.LeaveSentOff : EventCodes.LeaveInjured, _ctx.SlotState(s).Position);
                        _ctx.Kinematics.SetActive(s, false);
                        break;
                    case WalkKind.SubOut:
                        Swap(s, _ctx.SlotState(s).Position);
                        break;
                }
                _kind[s] = WalkKind.None;
            }
        }

        // ── 직렬화 ──

        internal void Write(StateWriter w)
        {
            for (int s = 0; s < Squad.Slots; s++)
            {
                w.Write((byte)_kind[s]);
                if (_kind[s] == WalkKind.None) continue;
                w.Write(_id[s]); w.Write(_inId[s]); w.Write(_a[s]); w.Write(_b[s]); w.Write(_exit[s]);
            }
        }

        internal void Read(StateReader r)
        {
            for (int s = 0; s < Squad.Slots; s++)
            {
                _kind[s] = (WalkKind)r.ReadByte();
                if (_kind[s] == WalkKind.None) { _id[s] = 0; _inId[s] = 0; _a[s] = 0; _b[s] = 0; _exit[s] = default; continue; }
                _id[s] = r.ReadInt(); _inId[s] = r.ReadInt(); _a[s] = r.ReadInt(); _b[s] = r.ReadInt(); _exit[s] = r.ReadVec2();
            }
        }

        internal void AddTo(ref StableHasher h)
        {
            for (int s = 0; s < Squad.Slots; s++)
            {
                h.Add((byte)_kind[s]);
                if (_kind[s] == WalkKind.None) continue;
                h.Add(_id[s]); h.Add(_inId[s]); h.Add(_a[s]); h.Add(_b[s]); h.Add(_exit[s].X); h.Add(_exit[s].Y);
            }
        }
    }
}
