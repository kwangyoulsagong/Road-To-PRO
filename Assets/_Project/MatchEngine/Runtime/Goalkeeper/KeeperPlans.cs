using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>크로스 처리 모드 (specs/073 FR-11). 번호는 스냅샷에 들어간다.</summary>
    public enum CrossMode : byte
    {
        None = 0,
        Claim = 1,
        Punch = 2,
        /// <summary>후보가 있었지만 위치 선정을 골랐다 — 071 경합에 들어가지 않는다.</summary>
        Abandon = 3,
    }

    /// <summary>1v1 모드 (specs/073 FR-20).</summary>
    public enum OneOnOneMode : byte
    {
        None = 0,
        Smother = 1,
        Set = 2,
    }

    /// <summary>
    /// 팀별 GK 골문 이탈 계획 (specs/073 플랜 `KeeperPlans`). 스위퍼·크로스 결정은 공의 비행 하나(<see cref="Flight"/> = GK 시스템 비행 번호)에
    /// 한 번 정하고 그 비행 동안 유지한다(매 틱 뒤집힘 방지). 1v1은 캐리어 기준으로 결정 틱마다 다시 보되 스마더는 확정 뒤 유지한다.
    /// 값 타입 — 스냅샷·해시에 들어간다.
    /// </summary>
    public struct KeeperPlan
    {
        /// <summary>스위퍼·크로스 결정을 확정한 비행 번호 (−1 = 없음). 후보가 없었으면 확정하지 않고 다음 결정 틱에 다시 본다.</summary>
        public int Flight;
        /// <summary>새 비행에 결정 틱을 한 번 강제한 비행 번호 (결정 주기와 무관하게 킥 직후 1회).</summary>
        public int Seen;

        public CrossMode Cross;
        /// <summary>결단 지연(FR-12)이 끝나 움직이기 시작하는 틱.</summary>
        public long CrossReadyTick;
        public Vec2 Landing;
        /// <summary>결정 때 p_claim (잡기) 또는 p_punch (펀칭) — 071 경합에서 GK가 이기면 이 확률로 성공 판정.</summary>
        public float CrossP;
        /// <summary>잡기 실패(ClaimFail)를 이미 냈다 — 이 비행에서 GK 손 잡기 제외.</summary>
        public bool ClaimFailed;

        public bool Rush;
        public Vec2 RushPoint;
        /// <summary>돌진으로 박스 밖에서 공을 얻었다 — 다음 공 동작은 클리어만 (FR-04).</summary>
        public bool RushOwned;

        public OneOnOneMode OneOnOne;
        /// <summary>1v1 캐리어 슬롯 (−1 = 없음).</summary>
        public int Carrier;
        public Vec2 SetPoint;
        /// <summary>세트 정지 예정 틱 (FR-22 정지 시각 오차 포함).</summary>
        public long StopTick;
        /// <summary>스마더를 확정한 비행 번호 — 캐리어가 공을 다시 차면(새 비행) 취소 불가 구간 밖일 때 위치 선정으로 돌아간다.</summary>
        public int OneOnOneFlight;

        public static KeeperPlan Empty => new KeeperPlan { Flight = -1, Seen = -1, Carrier = -1, OneOnOneFlight = -1 };

        public void ClearFlight()
        {
            Cross = CrossMode.None;
            CrossReadyTick = 0;
            Landing = Vec2.Zero;
            CrossP = 0f;
            ClaimFailed = false;
            Rush = false;
            RushPoint = Vec2.Zero;
        }

        public void ClearOneOnOne()
        {
            OneOnOne = OneOnOneMode.None;
            Carrier = -1;
            SetPoint = Vec2.Zero;
            StopTick = 0;
            OneOnOneFlight = -1;
        }

        public void Write(StateWriter w)
        {
            w.Write(Flight); w.Write(Seen);
            w.Write((byte)Cross); w.Write(CrossReadyTick); w.Write(Landing); w.Write(CrossP); w.Write(ClaimFailed);
            w.Write(Rush); w.Write(RushPoint); w.Write(RushOwned);
            w.Write((byte)OneOnOne); w.Write(Carrier); w.Write(SetPoint); w.Write(StopTick); w.Write(OneOnOneFlight);
        }

        public static KeeperPlan Read(StateReader r)
        {
            var p = new KeeperPlan();
            p.Flight = r.ReadInt(); p.Seen = r.ReadInt();
            p.Cross = (CrossMode)r.ReadByte(); p.CrossReadyTick = r.ReadLong(); p.Landing = r.ReadVec2(); p.CrossP = r.ReadFloat(); p.ClaimFailed = r.ReadBool();
            p.Rush = r.ReadBool(); p.RushPoint = r.ReadVec2(); p.RushOwned = r.ReadBool();
            p.OneOnOne = (OneOnOneMode)r.ReadByte(); p.Carrier = r.ReadInt(); p.SetPoint = r.ReadVec2(); p.StopTick = r.ReadLong(); p.OneOnOneFlight = r.ReadInt();
            return p;
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(Flight); h.Add(Seen);
            h.Add((byte)Cross); h.Add(CrossReadyTick); h.Add(Landing.X); h.Add(Landing.Y); h.Add(CrossP); h.Add(ClaimFailed);
            h.Add(Rush); h.Add(RushPoint.X); h.Add(RushPoint.Y); h.Add(RushOwned);
            h.Add((byte)OneOnOne); h.Add(Carrier); h.Add(SetPoint.X); h.Add(SetPoint.Y); h.Add(StopTick); h.Add(OneOnOneFlight);
        }
    }
}

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>
    /// 팀별 PK GK 전략 (specs/074 FR-01·03). 상대 PK 예약 킥이 잡히면 한 번 뽑고(방향·기울기·조기 시각·라인 이탈),
    /// <see cref="DiveAbs"/> 서브스텝에 조기 다이빙을 시작한다. 판정은 011 슛 감시가 이 계획의 방향·실제 조기 시각으로 한다.
    /// 값 타입 — 스냅샷·해시에 들어간다.
    /// </summary>
    public struct PenaltyPlan
    {
        public bool Active;
        /// <summary>계획을 만든 예약 킥의 실행 가능 서브스텝 (같은 예약에 두 번 뽑지 않는다).</summary>
        public long KickDue;
        /// <summary>−1·0·+1 (골문 y 증가 쪽 +1, 0 = 서서 반응).</summary>
        public int Dir;
        public float Lean;
        /// <summary>뽑은 조기 시각 (s).</summary>
        public float Lead;
        /// <summary>조기 다이빙 시작 절대 서브스텝.</summary>
        public long DiveAbs;
        public bool Started;
        /// <summary>조기 다이빙을 시작한 순간 GK 위치 (판정 기준점).</summary>
        public Vec2 StartPos;
        /// <summary>킥 순간 골라인 이탈 (FR-03) — 012 재차기 판정이 한 번 읽고 지운다.</summary>
        public bool OffLine;
        /// <summary>PK 킥이 실행됐다 (012 재차기 판정 대상).</summary>
        public bool Kicked;
        /// <summary>PK 킥이 실행된 절대 서브스텝 (슛 감시가 이 킥에만 계획을 적용 — 결정론 리뷰 H1).</summary>
        public long KickAbs;
        /// <summary>슛 감시가 이 계획으로 판정을 시작했다 (유효 슛일 때만).</summary>
        public bool Judged;
        /// <summary>012 재차기 판정을 받았다 (골·GK 처리·아웃·골대 중 첫 결과).</summary>
        public bool Settled;

        public void Write(StateWriter w)
        {
            w.Write(Active); w.Write(KickDue); w.Write(Dir); w.Write(Lean); w.Write(Lead); w.Write(DiveAbs); w.Write(Started); w.Write(StartPos); w.Write(OffLine); w.Write(Kicked); w.Write(KickAbs); w.Write(Judged); w.Write(Settled);
        }

        public static PenaltyPlan Read(StateReader r) => new PenaltyPlan
        {
            Active = r.ReadBool(), KickDue = r.ReadLong(), Dir = r.ReadInt(), Lean = r.ReadFloat(), Lead = r.ReadFloat(), DiveAbs = r.ReadLong(),
            Started = r.ReadBool(), StartPos = r.ReadVec2(), OffLine = r.ReadBool(), Kicked = r.ReadBool(), KickAbs = r.ReadLong(), Judged = r.ReadBool(), Settled = r.ReadBool(),
        };

        public void AddTo(ref StableHasher h)
        {
            h.Add(Active); h.Add(KickDue); h.Add(Dir); h.Add(Lean); h.Add(Lead); h.Add(DiveAbs); h.Add(Started); h.Add(StartPos.X); h.Add(StartPos.Y); h.Add(OffLine); h.Add(Kicked); h.Add(KickAbs); h.Add(Judged); h.Add(Settled);
        }
    }

    /// <summary>
    /// 팀별 GK 손 보유 시계 (specs/074 FR-14). 손으로 잡은 절대 서브스텝(100 Hz)부터 놓을 때까지. 마감은 캐치 때 한 번 뽑는다.
    /// 값 타입 — 스냅샷·해시에 들어간다. 012 8초 판정이 <see cref="GoalkeeperSystem.HoldSubsteps"/>로 읽는다.
    /// </summary>
    public struct HoldClock
    {
        public bool Active;
        public long StartAbs;
        /// <summary>배급 마감 절대 서브스텝 (목표 + 지연 표본).</summary>
        public long DeadlineAbs;
        /// <summary>빠른 스로 조건(캐치 순간 상대 미정렬)이었다.</summary>
        public bool Quick;

        public void Write(StateWriter w) { w.Write(Active); w.Write(StartAbs); w.Write(DeadlineAbs); w.Write(Quick); }

        public static HoldClock Read(StateReader r) => new HoldClock { Active = r.ReadBool(), StartAbs = r.ReadLong(), DeadlineAbs = r.ReadLong(), Quick = r.ReadBool() };

        public void AddTo(ref StableHasher h) { h.Add(Active); h.Add(StartAbs); h.Add(DeadlineAbs); h.Add(Quick); }
    }
}
