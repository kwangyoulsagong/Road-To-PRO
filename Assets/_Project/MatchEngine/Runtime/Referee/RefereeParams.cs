using RoadToPro.Core.Tuning;
using Ids = RoadToPro.MatchEngine.Referee.RefereeTuningIds;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>
    /// 심판 파라미터 (specs/012, Data/tuning/match.json `ref.*`). 필드 초기값 = 파일 기본값(<c>RefereeParamsTests</c>가 일치 확인).
    /// 경기 튜닝 묶음(Match)에서 읽는다 — 묶음에 `ref.*`가 없으면 그 항목은 코드 기본값. 생성 뒤 바꾸지 않는다.
    /// </summary>
    public sealed class RefereeParams
    {
        /// <summary>012 P1: FR-21 심각도 강도 가중 (리서치 07 § 1.4 예시 0.45 → 늦음 항 확보)</summary>
        public readonly float FoulWIntensity = 0.4f;
        /// <summary>012 P1: FR-21 뒤에서 가중 (리서치 07 § 1.4)</summary>
        public readonly float FoulWBehind = 0.2f;
        /// <summary>012 P1: FR-21 발바닥 가중 (리서치 07 § 1.4) | 012: T29 보정 0.25 → 0.08 (실경기 슬라이딩 파울 90 % → 33 %, AC-23)</summary>
        public readonly float FoulWStuds = 0.08f;
        /// <summary>012 P1: FR-21 높이 가중 (0.10 → 0.05)</summary>
        public readonly float FoulWHeight = 0.05f;
        /// <summary>012 P1: FR-21 늦음 가중 (신규)</summary>
        public readonly float FoulWLate = 0.1f;
        /// <summary>012 P1: 높이 항 = 접촉 높이 / 이 값 (1 상한)</summary>
        public readonly float FoulHeightFull = 1.2f;
        /// <summary>012 FR-21: 파울 점수 = 심각도 × (1 − k·공 먼저)</summary>
        public readonly float FoulBallFirstK = 0.7f;
        /// <summary>012 FR-21: 슬라이딩 장인 파울 ×1.4 (003 FR-21)</summary>
        public readonly float SlideMasterMul = 1.4f;
        /// <summary>012 P1: 파울 임계 (리서치 07 § 1.4) | 012: T29 보정 0.30 → 0.40 (스탠딩 태클 파울률 25 % → 12.5 %, AC-23)</summary>
        public readonly float FoulThreshold = 0.4f;
        /// <summary>012 P1: 경고 임계 (리서치 07 § 1.4) | 012: T29 보정 0.60 → 0.55 (파울 임계 상향에 맞춤, 반칙 경고 ≈ 2.4/경기)</summary>
        public readonly float YellowThreshold = 0.55f;
        /// <summary>012 P1: 퇴장 임계 (리서치 07 § 1.4) | 012: T29 보정 0.85 → 0.80 (직접 퇴장 ≈ 0.03/경기)</summary>
        public readonly float RedThreshold = 0.8f;
        /// <summary>012 FR-26: 같은 선수 이 번째 반칙부터 경고 임계를 낮춤</summary>
        public readonly float RepeatFoulCount = 3f;
        /// <summary>012 FR-26: 반복 반칙 경고 임계 감소량</summary>
        public readonly float RepeatFoulDrop = 0.15f;
        /// <summary>012 FR-23: 골까지 거리 < 30 m (scenarios 03 § 5.2)</summary>
        public readonly float DogsoDist = 30f;
        /// <summary>012 FR-23: 골 방향 cos > 0.7 (scenarios 03 § 5.2)</summary>
        public readonly float DogsoCos = 0.7f;
        /// <summary>012 FR-23: 공 통제 확률 > 0.7 (scenarios 03 § 5.2)</summary>
        public readonly float DogsoControl = 0.7f;
        /// <summary>012 FR-26: SPA(유망한 공격 저지) = DOGSO 아님 ∧ 골까지 < 35 m</summary>
        public readonly float SpaDist = 35f;
        /// <summary>012 FR-26: SPA 골 방향 cos > 0.5</summary>
        public readonly float SpaCos = 0.5f;
        /// <summary>012 FR-26: SPA 공 통제 확률 > 0.5</summary>
        public readonly float SpaControl = 0.5f;
        /// <summary>012 FR-26: SPA 공–골 사이 수비수 ≤ 2</summary>
        public readonly float SpaDefenders = 2f;
        /// <summary>012 FR-30: 엄격함 0~1 → 경고·퇴장 임계 ± (엄격 − 0.5) × 이 값</summary>
        public readonly float StrictShift = 0.2f;
        /// <summary>012 FR-30: 파울 임계 변수 0~1 → 파울 임계 ± (값 − 0.5) × 이 값</summary>
        public readonly float FoulShift = 0.1f;
        /// <summary>012 FR-30: 홈 편향 상한 0~0.05 (리서치 07 § 1.3)</summary>
        public readonly float HomeBiasMax = 0.05f;
        /// <summary>012 FR-30: 압박 저항 낮으면 빅매치·막판 노이즈 × (1 + k·(1 − 저항)·압박)</summary>
        public readonly float PressureK = 0.5f;
        /// <summary>012 P2: 인지 노이즈 σ = σ0·(1.5 − 일관성)·(1.5 − 시야)</summary>
        public readonly float Sigma0 = 0.12f;
        /// <summary>012 FR-32: 시야 최적 거리 15~25 m (리서치 07 § 1.4)</summary>
        public readonly float SightBestMin = 15f;
        /// <summary>012 FR-32: 시야 최적 거리 상한</summary>
        public readonly float SightBestMax = 25f;
        /// <summary>012 P2: 이 거리에서 거리 항 = 최저</summary>
        public readonly float SightFar = 55f;
        /// <summary>012 P2: 거리 항 최저값</summary>
        public readonly float SightDistMin = 0.4f;
        /// <summary>012 P2: 0 m 거리 항 (너무 가까우면 시야 좁음)</summary>
        public readonly float SightNearMin = 0.7f;
        /// <summary>012 P2: 주심–사건 선분에서 이 거리 안 선수 = 가림</summary>
        public readonly float OcclusionWidth = 1f;
        /// <summary>012 P2: 가림 1명당 시야 감소</summary>
        public readonly float OcclusionPenalty = 0.2f;
        /// <summary>012 P2: 가림 인원 상한</summary>
        public readonly float OcclusionMax = 3f;
        /// <summary>012 FR-32: 가까운 부심(20 m 안)이 사건을 보면 시야 + 이 값 × (1 − 시야)</summary>
        public readonly float AssistantAssist = 0.2f;
        /// <summary>012 FR-32: 부심 도움 거리</summary>
        public readonly float AssistantRange = 20f;
        /// <summary>012 P8: 속임 = base·(1 − 시야)·(1 − 0.5·일관성) + 약한 접촉 가산</summary>
        public readonly float DiveFoolBase = 0.35f;
        /// <summary>012 P8: 약한 접촉 다이빙 속임 가산</summary>
        public readonly float DiveFoolWeak = 0.15f;
        /// <summary>012 P8: 들킨 다이빙 경고 확률</summary>
        public readonly float DiveCardProb = 0.6f;
        /// <summary>012 FR-41: 보류 창 (리서치 07 § 1.1 약 2~3 s, 설계 예시 3 s)</summary>
        public readonly float AdvantageWindow = 3f;
        /// <summary>012 P5: 이득 점수 = 점유 유지 × clamp(base + 위협도)</summary>
        public readonly float AdvantageBase = 0.3f;
        /// <summary>012 P5: 위협도 = clamp(1 − 상대 골까지 거리 / 이 값) (xT 표 전 대체)</summary>
        public readonly float AdvantageThreatRange = 40f;
        /// <summary>012 FR-50: 반응 시간이 이 값 이상이면 반응 항 1 (선형)</summary>
        public readonly float HandReactFull = 0.4f;
        /// <summary>012 FR-50: 이 거리 안 + 반응 부족 = 근거리 굴절 면제</summary>
        public readonly float HandNear = 0.5f;
        /// <summary>012 FR-50: 실루엣 × 반응 항이 이 값 초과면 진짜 핸드볼</summary>
        public readonly float HandThreshold = 0.5f;
        /// <summary>012 P11: 몸 앞쪽 끝 = 위치 + 공격 방향 × 0.25 m (팔 제외)</summary>
        public readonly float OffsideBodyReach = 0.25f;
        /// <summary>012 FR-07: σ_AR = 0.25 + 0.04 × |부심 정렬 차| (scenarios 03 § 4.2)</summary>
        public readonly float OffsideSigma0 = 0.25f;
        /// <summary>012 FR-07: σ_AR 거리 계수</summary>
        public readonly float OffsideSigmaK = 0.04f;
        /// <summary>012 FR-07: flash-lag 하한 (공격수 쪽)</summary>
        public readonly float FlashLagMin = 0.3f;
        /// <summary>012 FR-07: flash-lag 상한</summary>
        public readonly float FlashLagMax = 0.5f;
        /// <summary>012 FR-07: flash-lag × clamp(공격수 속도 / 이 값) — 움직임이 없으면 0</summary>
        public readonly float FlashLagSpeed = 8f;
        /// <summary>012 P4: |d| ≤ 0.5 m면 늦은 깃발 보류</summary>
        public readonly float LateFlagMargin = 0.5f;
        /// <summary>012 FR-10: |d| < 0.10 m 근소 표시</summary>
        public readonly float TightMargin = 0.1f;
        /// <summary>012: 오프사이드 라인 뒤 이 거리 안 공격수도 후보(부심 오판 가능)</summary>
        public readonly float CandidateBack = 1.5f;
        /// <summary>012 FR-04 ②: 상대가 공을 플레이할 때 이 거리 안이면 상대 관여</summary>
        public readonly float InterfereRadius = 1f;
        /// <summary>012 FR-04 ② OFF-09: 슛 선 이 폭 안 + GK 근처 = 시야 방해</summary>
        public readonly float SightCorridor = 1f;
        /// <summary>012 OFF-09: GK와 이 거리 안</summary>
        public readonly float SightGkDist = 4f;
        /// <summary>012 P6: 명백한 오류 = |인지 − 진짜| > 0.25 + 판정 단계 다름</summary>
        public readonly float VarClearError = 0.25f;
        /// <summary>012 P7: OFR 뒤 번복 확률</summary>
        public readonly float VarOfrOverturn = 0.85f;
        /// <summary>012 P9: 근소 오프사이드 체크 시간 하한</summary>
        public readonly float VarTightMin = 60f;
        /// <summary>012 P9: 근소 오프사이드 체크 시간 상한</summary>
        public readonly float VarTightMax = 120f;
        /// <summary>012 P9: 사실 판정 체크 하한</summary>
        public readonly float VarFactMin = 30f;
        /// <summary>012 P9: 사실 판정 체크 상한</summary>
        public readonly float VarFactMax = 60f;
        /// <summary>012 P9: 주관(OFR) 체크 하한</summary>
        public readonly float VarOfrMin = 60f;
        /// <summary>012 P9: 주관(OFR) 체크 상한</summary>
        public readonly float VarOfrMax = 120f;
        /// <summary>012 FR-90: 카드 지연 정지 하한</summary>
        public readonly float CardStopMin = 15f;
        /// <summary>012 FR-90: 카드 지연 정지 상한</summary>
        public readonly float CardStopMax = 30f;
        /// <summary>012 FR-90: 항의 정지</summary>
        public readonly float DissentStop = 20f;
        /// <summary>012 FR-30: 경기 압박 = (중요도 − 1) × 이 값 + 막판 가산</summary>
        public readonly float PressureImportanceK = 0.5f;
        /// <summary>012 FR-30: 후반 막판 구간 (명목 종료 전 분)</summary>
        public readonly float PressureLateMinutes = 10f;
        /// <summary>012 FR-30: 막판 압박 가산</summary>
        public readonly float PressureLateAdd = 0.5f;
        /// <summary>012 FR-32: 각도 항 = base + (1 − base) × |sin(시선, 진행 방향)|</summary>
        public readonly float SightAngleBase = 0.75f;
        /// <summary>012 FR-32: 시야 품질 하한</summary>
        public readonly float SightFloor = 0.05f;
        /// <summary>012 FR-27: 속은 다이빙의 반칙자 = 이 거리 안 가장 가까운 상대</summary>
        public readonly float DiveOffenderRadius = 3f;
        /// <summary>012 FR-63: GK 손 보유 > 8.0 s → 상대 코너 (2025/26)</summary>
        public readonly float KeeperHoldMax = 8f;
        /// <summary>012 FR-80: 목표 = lerp(공, 대각선 점, 0.6) (리서치 07 § 1.5)</summary>
        public readonly float DiagLerp = 0.6f;
        /// <summary>012 FR-80: 공에서 15~20 m</summary>
        public readonly float RefDistMin = 15f;
        /// <summary>012 FR-80: 공 거리 상한</summary>
        public readonly float RefDistMax = 20f;
        /// <summary>012 FR-80: 체력 0 심판 최대 속도</summary>
        public readonly float RefSpeedMin = 6f;
        /// <summary>012 FR-80: 체력 1 심판 최대 속도 (대체 7 m/s 포함 구간)</summary>
        public readonly float RefSpeedMax = 7.5f;
        /// <summary>012 FR-80: 부심 터치라인 밖 거리 (대체 값 그대로)</summary>
        public readonly float AssistantOffset = 1.5f;

        public int RepeatFoulCountInt => RepeatFoulCount < 1f ? 1 : (int)RepeatFoulCount;
        public int OcclusionMaxInt => OcclusionMax < 0f ? 0 : (int)OcclusionMax;

        RefereeParams() { }

        RefereeParams(TuningBundle t)
        {
            FoulWIntensity = G(t, Ids.FoulWIntensity, FoulWIntensity);
            FoulWBehind = G(t, Ids.FoulWBehind, FoulWBehind);
            FoulWStuds = G(t, Ids.FoulWStuds, FoulWStuds);
            FoulWHeight = G(t, Ids.FoulWHeight, FoulWHeight);
            FoulWLate = G(t, Ids.FoulWLate, FoulWLate);
            FoulHeightFull = G(t, Ids.FoulHeightFull, FoulHeightFull);
            FoulBallFirstK = G(t, Ids.FoulBallFirstK, FoulBallFirstK);
            SlideMasterMul = G(t, Ids.SlideMasterMul, SlideMasterMul);
            FoulThreshold = G(t, Ids.FoulThreshold, FoulThreshold);
            YellowThreshold = G(t, Ids.YellowThreshold, YellowThreshold);
            RedThreshold = G(t, Ids.RedThreshold, RedThreshold);
            RepeatFoulCount = G(t, Ids.RepeatFoulCount, RepeatFoulCount);
            RepeatFoulDrop = G(t, Ids.RepeatFoulDrop, RepeatFoulDrop);
            DogsoDist = G(t, Ids.DogsoDist, DogsoDist);
            DogsoCos = G(t, Ids.DogsoCos, DogsoCos);
            DogsoControl = G(t, Ids.DogsoControl, DogsoControl);
            SpaDist = G(t, Ids.SpaDist, SpaDist);
            SpaCos = G(t, Ids.SpaCos, SpaCos);
            SpaControl = G(t, Ids.SpaControl, SpaControl);
            SpaDefenders = G(t, Ids.SpaDefenders, SpaDefenders);
            StrictShift = G(t, Ids.StrictShift, StrictShift);
            FoulShift = G(t, Ids.FoulShift, FoulShift);
            HomeBiasMax = G(t, Ids.HomeBiasMax, HomeBiasMax);
            PressureK = G(t, Ids.PressureK, PressureK);
            Sigma0 = G(t, Ids.Sigma0, Sigma0);
            SightBestMin = G(t, Ids.SightBestMin, SightBestMin);
            SightBestMax = G(t, Ids.SightBestMax, SightBestMax);
            SightFar = G(t, Ids.SightFar, SightFar);
            SightDistMin = G(t, Ids.SightDistMin, SightDistMin);
            SightNearMin = G(t, Ids.SightNearMin, SightNearMin);
            OcclusionWidth = G(t, Ids.OcclusionWidth, OcclusionWidth);
            OcclusionPenalty = G(t, Ids.OcclusionPenalty, OcclusionPenalty);
            OcclusionMax = G(t, Ids.OcclusionMax, OcclusionMax);
            AssistantAssist = G(t, Ids.AssistantAssist, AssistantAssist);
            AssistantRange = G(t, Ids.AssistantRange, AssistantRange);
            DiveFoolBase = G(t, Ids.DiveFoolBase, DiveFoolBase);
            DiveFoolWeak = G(t, Ids.DiveFoolWeak, DiveFoolWeak);
            DiveCardProb = G(t, Ids.DiveCardProb, DiveCardProb);
            AdvantageWindow = G(t, Ids.AdvantageWindow, AdvantageWindow);
            AdvantageBase = G(t, Ids.AdvantageBase, AdvantageBase);
            AdvantageThreatRange = G(t, Ids.AdvantageThreatRange, AdvantageThreatRange);
            HandReactFull = G(t, Ids.HandReactFull, HandReactFull);
            HandNear = G(t, Ids.HandNear, HandNear);
            HandThreshold = G(t, Ids.HandThreshold, HandThreshold);
            OffsideBodyReach = G(t, Ids.OffsideBodyReach, OffsideBodyReach);
            OffsideSigma0 = G(t, Ids.OffsideSigma0, OffsideSigma0);
            OffsideSigmaK = G(t, Ids.OffsideSigmaK, OffsideSigmaK);
            FlashLagMin = G(t, Ids.FlashLagMin, FlashLagMin);
            FlashLagMax = G(t, Ids.FlashLagMax, FlashLagMax);
            FlashLagSpeed = G(t, Ids.FlashLagSpeed, FlashLagSpeed);
            LateFlagMargin = G(t, Ids.LateFlagMargin, LateFlagMargin);
            TightMargin = G(t, Ids.TightMargin, TightMargin);
            CandidateBack = G(t, Ids.CandidateBack, CandidateBack);
            InterfereRadius = G(t, Ids.InterfereRadius, InterfereRadius);
            SightCorridor = G(t, Ids.SightCorridor, SightCorridor);
            SightGkDist = G(t, Ids.SightGkDist, SightGkDist);
            VarClearError = G(t, Ids.VarClearError, VarClearError);
            VarOfrOverturn = G(t, Ids.VarOfrOverturn, VarOfrOverturn);
            VarTightMin = G(t, Ids.VarTightMin, VarTightMin);
            VarTightMax = G(t, Ids.VarTightMax, VarTightMax);
            VarFactMin = G(t, Ids.VarFactMin, VarFactMin);
            VarFactMax = G(t, Ids.VarFactMax, VarFactMax);
            VarOfrMin = G(t, Ids.VarOfrMin, VarOfrMin);
            VarOfrMax = G(t, Ids.VarOfrMax, VarOfrMax);
            CardStopMin = G(t, Ids.CardStopMin, CardStopMin);
            CardStopMax = G(t, Ids.CardStopMax, CardStopMax);
            DissentStop = G(t, Ids.DissentStop, DissentStop);
            PressureImportanceK = G(t, Ids.PressureImportanceK, PressureImportanceK);
            PressureLateMinutes = G(t, Ids.PressureLateMinutes, PressureLateMinutes);
            PressureLateAdd = G(t, Ids.PressureLateAdd, PressureLateAdd);
            SightAngleBase = G(t, Ids.SightAngleBase, SightAngleBase);
            SightFloor = G(t, Ids.SightFloor, SightFloor);
            DiveOffenderRadius = G(t, Ids.DiveOffenderRadius, DiveOffenderRadius);
            KeeperHoldMax = G(t, Ids.KeeperHoldMax, KeeperHoldMax);
            DiagLerp = G(t, Ids.DiagLerp, DiagLerp);
            RefDistMin = G(t, Ids.RefDistMin, RefDistMin);
            RefDistMax = G(t, Ids.RefDistMax, RefDistMax);
            RefSpeedMin = G(t, Ids.RefSpeedMin, RefSpeedMin);
            RefSpeedMax = G(t, Ids.RefSpeedMax, RefSpeedMax);
            AssistantOffset = G(t, Ids.AssistantOffset, AssistantOffset);
        }

        static float G(TuningBundle t, string id, float fallback) => t.Registry.TryGetIndex(id, out var i) ? t[i] : fallback;

        /// <summary>경기 튜닝 묶음에서 (null이면 코드 기본값).</summary>
        public static RefereeParams Create(TuningBundle match) => match == null ? Defaults : new RefereeParams(match);

        /// <summary>코드 기본값 (파일 기본값과 같다).</summary>
        public static readonly RefereeParams Defaults = new RefereeParams();
    }
}
