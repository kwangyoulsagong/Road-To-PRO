using RoadToPro.Core.Tuning;
using Ids = RoadToPro.MatchEngine.PitchControl.PitchControlTuningIds;

namespace RoadToPro.MatchEngine.PitchControl
{
    /// <summary>
    /// 피치 컨트롤 파라미터 (specs/007 FR-01~07·11·21, Data/tuning/action.json `pcm.*`). 필드 초기값 = 파일 기본값(<c>PitchControlParamsTests</c>가 일치 확인).
    /// 008 액션 튜닝 묶음에서 읽는다. 묶음에 `pcm.*`가 없으면 그 항목은 코드 기본값. 생성 뒤 바꾸지 않는다.
    /// </summary>
    public sealed class PitchControlParams
    {
        /// <summary>FR-02 도달 확률 σ (s).</summary>
        public readonly float Sigma = 0.45f;
        /// <summary>FR-03 λ_att = λ_def (s⁻¹).</summary>
        public readonly float Lambda = 4.3f;
        /// <summary>FR-03 수비 계수 κ_def (λ_def 배수).</summary>
        public readonly float KappaDef = 1.0f;
        /// <summary>FR-03 λ_gk = GkMul × λ_def.</summary>
        public readonly float GkMul = 3f;
        /// <summary>FR-04 적분 간격 (s). 궤적 샘플 간격도 같다(FR-12).</summary>
        public readonly float Dt = 0.04f;
        /// <summary>FR-04 최대 적분 시간 (s).</summary>
        public readonly float MaxT = 10f;
        /// <summary>FR-04 수렴 허용치 (1 − ΣPC).</summary>
        public readonly float Tol = 0.01f;
        /// <summary>FR-04 먼 선수 건너뛰기: τ 하한 &gt; T + Prune·σ (5σ — 3σ는 차이 ≈ 1e-2, T06).</summary>
        public readonly float Prune = 5f;
        /// <summary>FR-07 뜬공 높이 (m): 필드 선수 도달 확률 0.</summary>
        public readonly float AirZ = 2.5f;
        /// <summary>FR-07 GK 손 사용 높이 (m).</summary>
        public readonly float GkHandZ = 3.0f;
        /// <summary>FR-01 원 논문 모드 반응 (s).</summary>
        public readonly float PaperReaction = 0.7f;
        /// <summary>FR-01 원 논문 모드 v_max (m/s).</summary>
        public readonly float PaperVmax = 5f;
        /// <summary>FR-05 그리드 모드 평균 공 속도 (m/s).</summary>
        public readonly float GridBallSpeed = 15f;
        /// <summary>FR-06 능력치 연결 (1 = 켬).</summary>
        public readonly float Attr = 1f;
        /// <summary>FR-06 λ_def ← 태클 (0~1) 범위.</summary>
        public readonly float LambdaDefMin = 3.0f;
        public readonly float LambdaDefMax = 5.5f;
        /// <summary>FR-06 λ_att ← 퍼스트터치 연결 계수 [미정] (0 = 원 값).</summary>
        public readonly float LambdaAttK = 0f;
        /// <summary>FR-06 λ_gk ← 핸들링 연결 계수 [미정] (0 = 원 값).</summary>
        public readonly float LambdaGkK = 0f;
        /// <summary>FR-12 궤적 상한 시간 (s).</summary>
        public readonly float Horizon = 3f;
        /// <summary>FR-11 그리드 크기.</summary>
        public readonly float GridW = 32f;
        public readonly float GridH = 20f;
        /// <summary>FR-21 궤적 재평가 상위 K.</summary>
        public readonly float RefineK = 3f;
        /// <summary>FR-21 재평가 뒤 최고가 미질의 후보면 추가 질의 상한.</summary>
        public readonly float RefineExtra = 2f;
        /// <summary>FR-21 다른 동료 수신 가치.</summary>
        public readonly float MateValue = 1.0f;
        /// <summary>궤적 질의 정확한 TTI 간격 (샘플, 성능 T16 — 사이는 선형 보간, 1 = 보간 없음).</summary>
        public readonly float TtiStride = 2f;
        /// <summary>궤적 TTI 보간 허용 거리 (m): 공이 선수에서 이보다 가까우면 정확값.</summary>
        public readonly float TtiNear = 6f;
        /// <summary>FR-20 비교 모드: 1이면 008 수신 컨트롤은 기존 대체 로지스틱, 궤적 재평가 끔.</summary>
        public readonly float Legacy = 0f;

        public bool AttrOn => Attr >= 0.5f;
        public bool LegacyOn => Legacy >= 0.5f;
        public int GridCols => (int)GridW;
        public int GridRows => (int)GridH;
        public int RefineCount => RefineK < 0f ? 0 : (int)RefineK;
        public int RefineExtraCount => RefineExtra < 0f ? 0 : (int)RefineExtra;
        public int TtiStrideCount => TtiStride < 1f ? 1 : (int)TtiStride;

        PitchControlParams() { }

        PitchControlParams(TuningBundle t)
        {
            Sigma = G(t, Ids.Sigma, Sigma);
            Lambda = G(t, Ids.Lambda, Lambda);
            KappaDef = G(t, Ids.KappaDef, KappaDef);
            GkMul = G(t, Ids.GkMul, GkMul);
            Dt = G(t, Ids.Dt, Dt);
            MaxT = G(t, Ids.MaxT, MaxT);
            Tol = G(t, Ids.Tol, Tol);
            Prune = G(t, Ids.Prune, Prune);
            AirZ = G(t, Ids.AirZ, AirZ);
            GkHandZ = G(t, Ids.GkHandZ, GkHandZ);
            PaperReaction = G(t, Ids.PaperReaction, PaperReaction);
            PaperVmax = G(t, Ids.PaperVmax, PaperVmax);
            GridBallSpeed = G(t, Ids.GridBallSpeed, GridBallSpeed);
            Attr = G(t, Ids.Attr, Attr);
            LambdaDefMin = G(t, Ids.LambdaDefMin, LambdaDefMin);
            LambdaDefMax = G(t, Ids.LambdaDefMax, LambdaDefMax);
            LambdaAttK = G(t, Ids.LambdaAttK, LambdaAttK);
            LambdaGkK = G(t, Ids.LambdaGkK, LambdaGkK);
            Horizon = G(t, Ids.Horizon, Horizon);
            GridW = G(t, Ids.GridW, GridW);
            GridH = G(t, Ids.GridH, GridH);
            RefineK = G(t, Ids.RefineK, RefineK);
            RefineExtra = G(t, Ids.RefineExtra, RefineExtra);
            MateValue = G(t, Ids.MateValue, MateValue);
            Legacy = G(t, Ids.Legacy, Legacy);
            TtiStride = G(t, Ids.TtiStride, TtiStride);
            TtiNear = G(t, Ids.TtiNear, TtiNear);
        }

        static float G(TuningBundle t, string id, float fallback) => t.Registry.TryGetIndex(id, out var i) ? t[i] : fallback;

        /// <summary>008 액션 튜닝 묶음에서 (null이면 코드 기본값).</summary>
        public static PitchControlParams Create(TuningBundle action) => action == null ? Defaults : new PitchControlParams(action);

        /// <summary>코드 기본값 (파일 기본값과 같다).</summary>
        public static readonly PitchControlParams Defaults = new PitchControlParams();

        /// <summary>테스트용: 원 논문 기본값에서 능력치 연결만 끈 묶음 (AC-01). 값은 기본값과 같고 Attr = 0.</summary>
        public static PitchControlParams PaperDefaults() => new PitchControlParams(0f);

        PitchControlParams(float attr) { Attr = attr; }
    }
}
