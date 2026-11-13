using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 온볼 후보 1개 (specs/072 FR-01·10~14·20). 점수 분해(설명 가능성 NFR): 기대 가치 V × 성공 확률 P → 이득(지시 α·β) × 결합 배수 × 케미·템포 − 상실 위험 × 상실 지점 상대 xT × Risk 가중.
    /// </summary>
    public struct OnBallCandidate
    {
        public OnBallItem Item;
        /// <summary>패스 대상·돌파 상대 슬롯 (없으면 −1).</summary>
        public int Target;
        /// <summary>패스 조준점·드리블 목표·슈팅 목표·걷어내기 낙하점.</summary>
        public Vec2 Point;
        /// <summary>패스류의 008 평가 결과.</summary>
        public PassOption Pass;
        /// <summary>시야 집합에서의 인덱스 (패스류, 없으면 −1) — 진단용.</summary>
        public int VisionIndex;
        /// <summary>돌파 시 제칠 수비수 (없으면 −1).</summary>
        public int Defender;
        /// <summary>컷인 드리블.</summary>
        public bool CutIn;
        public ConsiderationSet Cons;

        public float P, V, LossXt;
        /// <summary>사전 점수 (상한 자르기용, P·V).</summary>
        public float Pre;
        /// <summary>결합 배수 곱하기 전 이득 (케미·템포 포함).</summary>
        public float GainBase;
        public float Mult, MultFull, Chem;
        /// <summary>억제 지시(가중치 &lt; 1)가 걸린 항목 (없으면 Cons 밖 값).</summary>
        public bool Suppressed;
        public Consideration SuppressedKind;
        public float Gain, Loss, Utility;
        public float Noise, Match;
    }
}
