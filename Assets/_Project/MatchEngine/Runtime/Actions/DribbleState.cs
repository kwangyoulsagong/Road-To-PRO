using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 드리블 터치 사이클 상태 (specs/070 FR-03). 공은 계속 Controlled이고, 이격이 열린 동안 공 위치는 터치 원점에서 004 굴림 표를 따른다:
    /// 굴린 거리(t) = 거리(시작 스텝 + t/dt) − 거리(시작 스텝). 상태라 해시·스냅샷에 들어간다.
    /// </summary>
    public struct DribbleState
    {
        /// <summary>추적 중인 소유자 슬롯 (−1 = 없음).</summary>
        public int Owner;
        public bool Open;
        /// <summary>터치 절대 서브스텝.</summary>
        public long Start;
        public Vec2 Origin, Dir;
        /// <summary>굴림 표 시작 누적 거리·스텝, 초속, 평균·표본 터치 거리.</summary>
        public float X0, S0, V0, Dist;
        /// <summary>FR-03 탈취 판정용 균등 난수 (터치 시작에 1개 소비 — 고정 소비).</summary>
        public float StealU;
        /// <summary>이번 터치에 상대가 이미 공에 닿아 판정했다 (터치당 판정 1회).</summary>
        public bool Contested;
        /// <summary>이격이 발 도달 거리를 한 번 넘었다 (그 뒤 도달 거리로 돌아오면 닫힘).</summary>
        public bool Far;
        /// <summary>이격이 닫히거나 소유자가 바뀐 뒤 공이 발 앞 위치로 돌아가는 중 (서브스텝당 최대 이동 제한).</summary>
        public bool Reglue;

        public static DribbleState None => new DribbleState { Owner = -1 };

        public void AddTo(ref StableHasher h)
        {
            h.Add(Owner); h.Add(Open); h.Add(Reglue);
            if (!Open) return;
            h.Add(Start); h.Add(Origin.X); h.Add(Origin.Y); h.Add(Dir.X); h.Add(Dir.Y); h.Add(X0); h.Add(S0); h.Add(V0); h.Add(Dist); h.Add(StealU); h.Add(Contested); h.Add(Far);
        }

        public void Write(StateWriter w)
        {
            w.Write(Owner); w.Write(Open); w.Write(Reglue);
            w.Write(Start); w.Write(Origin); w.Write(Dir); w.Write(X0); w.Write(S0); w.Write(V0); w.Write(Dist); w.Write(StealU); w.Write(Contested); w.Write(Far);
        }

        public static DribbleState Read(StateReader r)
        {
            var d = new DribbleState { Owner = r.ReadInt(), Open = r.ReadBool(), Reglue = r.ReadBool() };
            d.Start = r.ReadLong(); d.Origin = r.ReadVec2(); d.Dir = r.ReadVec2(); d.X0 = r.ReadFloat(); d.S0 = r.ReadFloat(); d.V0 = r.ReadFloat();
            d.Dist = r.ReadFloat(); d.StealU = r.ReadFloat(); d.Contested = r.ReadBool(); d.Far = r.ReadBool();
            return d;
        }
    }

    /// <summary>선수별 예약 태클 (specs/070 FR-10·11): 결정 틱에 걸고 준비 시간 뒤 접촉 서브스텝에 판정. 상태.</summary>
    public struct PendingTackle
    {
        public bool Active;
        public int Target;
        public bool Sliding;
        public long Due, Expire;

        public void AddTo(ref StableHasher h)
        {
            h.Add(Active);
            if (!Active) return;
            h.Add(Target); h.Add(Sliding); h.Add(Due); h.Add(Expire);
        }

        public void Write(StateWriter w)
        {
            w.Write(Active); w.Write(Target); w.Write(Sliding); w.Write(Due); w.Write(Expire);
        }

        public static PendingTackle Read(StateReader r)
            => new PendingTackle { Active = r.ReadBool(), Target = r.ReadInt(), Sliding = r.ReadBool(), Due = r.ReadLong(), Expire = r.ReadLong() };
    }
}
