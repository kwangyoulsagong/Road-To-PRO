using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>실행 결과 종류 (specs/008 FR-70). 058 FR-50~52 3D 동작 선택이 SimEvent 종류로 읽는다.</summary>
    public enum ActionOutcomeKind : byte
    {
        None = 0,
        Pass, Cross, Shot, Header, DribbleTouch, FirstTouch,
        StandingTackle, SlidingTackle, Interception, Block, Clearance, Duel, Recovery,
    }

    /// <summary>터치 종류 (FR-60): 012 오프사이드 리셋 판정(012 FR-05)이 읽는다.</summary>
    public enum TouchKind : byte
    {
        Deliberate = 0,
        Deflection = 1,
        Block = 2,
    }

    /// <summary>FR-70 분류 규칙. 순수 함수.</summary>
    public static class ActionOutcome
    {
        /// <summary>
        /// 수신 분류: 같은 팀 동료의 패스를 받으면 패스 받음, 상대의 패스·슛(속도 > 3 m/s)을 끊으면 인터셉트, 나머지(루즈볼·느린 공)는 공 회수.
        /// </summary>
        public static SimEventKind Reception(int team, int prevTeam, BallActionKind lastKick, int prevKicker, int slot, float ballSpeed)
        {
            if (team == prevTeam && lastKick == BallActionKind.Pass && prevKicker != slot) return SimEventKind.PassReceived;
            if (team != prevTeam && (lastKick == BallActionKind.Pass || lastKick == BallActionKind.Shot) && ballSpeed > 3f) return SimEventKind.Interception;
            return SimEventKind.BallRecovered;
        }

        /// <summary>
        /// 태클 기록 조건 (FR-70): 상대 볼 캐리어가 태클 도달 거리(070 FR-10·11 — 대체: 경기 파라미터 TackleRange) 안이고 그 선수가 아직 공을 가졌을 때만.
        /// </summary>
        public static bool IsTackle(float distanceToCarrierSq, float reach, bool carrierStillOwns)
            => carrierStillOwns && distanceToCarrierSq <= reach * reach;

        public static ActionOutcomeKind OfEvent(SimEventKind k) => k switch
        {
            SimEventKind.Pass => ActionOutcomeKind.Pass,
            SimEventKind.Cross => ActionOutcomeKind.Cross,
            SimEventKind.Shot => ActionOutcomeKind.Shot,
            SimEventKind.Tackle => ActionOutcomeKind.StandingTackle,
            SimEventKind.Interception => ActionOutcomeKind.Interception,
            SimEventKind.Block => ActionOutcomeKind.Block,
            SimEventKind.Clearance => ActionOutcomeKind.Clearance,
            SimEventKind.Duel => ActionOutcomeKind.Duel,
            SimEventKind.PassReceived => ActionOutcomeKind.FirstTouch,
            SimEventKind.BallRecovered => ActionOutcomeKind.Recovery,
            SimEventKind.Dribble => ActionOutcomeKind.DribbleTouch,
            _ => ActionOutcomeKind.None,
        };

        /// <summary>수신 이벤트 B 비트: bit0 불안한 터치, bit1~2 터치 종류.</summary>
        public const int HeavyTouchBit = 1;
        public static int ReceptionFlags(bool heavy, TouchKind touch) => (heavy ? HeavyTouchBit : 0) | ((int)touch << 1);

        /// <summary>패스 이벤트 A 비트: bit0~1 킥 기술, bit2 원터치, bit3 약발.</summary>
        public const int OneTouchBit = 4, WeakFootBit = 8;
        public static int PassFlags(KickTech tech, bool oneTouch, bool weak) => (int)tech | (oneTouch ? OneTouchBit : 0) | (weak ? WeakFootBit : 0);
    }
}
