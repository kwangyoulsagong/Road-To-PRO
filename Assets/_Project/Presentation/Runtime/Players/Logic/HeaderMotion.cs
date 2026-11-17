using System;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 헤더 동작 (specs/082 FR-01·02·03 ②·③). 실경기 헤더(부위 Head인 슈팅·패스·걷어내기·크로스)는
    /// ① 필요한 점프(접촉 공 높이 − 서서 헤딩 클립 이마 높이 × 키 배율) ≥ <see cref="JumpMin"/>이면 점프 헤딩, 아니면 서서 헤딩을 고르고
    /// ② 남는 높이 차는 수직 리프트(점프 높이 보정, [<see cref="LiftMin"/>, <see cref="LiftMax"/>])로,
    /// ③ 수평 차는 루트 맞춤 상한 <see cref="RootMax"/>(054 FR-02 10 cm의 헤더 예외)까지 메운다. 나머지는 화면 공 보정(054 FR-35 ≤ 15 cm).
    /// 값은 Data/tuning/highlight.json `highlight.header*`와 같다(테스트가 확인).
    /// </summary>
    public static class HeaderMotion
    {
        public const string JumpClip = "header_jump", StandClip = "header_stand";
        /// <summary>highlight.headerJumpMin [제안] 0.10 m (2026-11-17 사용자 결정).</summary>
        public const float JumpMin = 0.10f;
        /// <summary>highlight.headerLiftMin·Max [제안] −0.20 / +0.35 m.</summary>
        public const float LiftMin = -0.20f, LiftMax = 0.35f;
        /// <summary>highlight.headerRootMax [제안] 1.0 m (시뮬 act.aerial.headRadius와 같은 값, T10 사용자 결정).</summary>
        public const float RootMax = 1.0f;

        /// <summary>
        /// 루트 맞춤 상한(m) = RootMax + 클립 접촉점의 수평 거리 × 배율. 시뮬은 승자 루트–공 수평 ≤ headRadius로 헤더를 실행하는데,
        /// 클립 이마는 루트 앞 0.4 m라 공이 옆·뒤면 루트 보정이 RootMax를 넘는다(T10 진단 9.4 cm).
        /// </summary>
        public static float RootCap(RoadToPro.Core.Math.Vec3 clipContact, float scale) =>
            RootMax + (float)Math.Sqrt(clipContact.X * clipContact.X + clipContact.Z * clipContact.Z) * scale;

        public static bool IsHeader(in AnimEvent ev) =>
            ev.Part == BodyPart.Head &&
            (ev.Kind == SimEventKind.Shot || ev.Kind == SimEventKind.Pass || ev.Kind == SimEventKind.Clearance || ev.Kind == SimEventKind.Cross);

        /// <summary>필요한 점프(m): 접촉 공 높이 − 서서 헤딩 클립 접촉점 높이 × 키 배율.</summary>
        public static float RequiredJump(float ballY, float standContactY, float scale) => ballY - standContactY * scale;

        public static bool UseJump(float requiredJump) => requiredJump >= JumpMin;

        /// <summary>점프/서서 헤딩 클립 인덱스. 라이브러리에 없으면 −1.</summary>
        public static int Choose(ClipLibrary lib, float ballY, float scale)
        {
            int stand = lib.IndexOf(StandClip), jump = lib.IndexOf(JumpClip);
            if (stand < 0 || jump < 0) return stand >= 0 ? stand : jump;
            return UseJump(RequiredJump(ballY, lib.Get(stand).ContactPoint.Y, scale)) ? jump : stand;
        }

        /// <summary>수직 리프트(m) = 공 높이 − 고른 클립 접촉점 높이 × 키 배율, [LiftMin, LiftMax]로 자름 (서서 헤딩: 무릎 굽힘 ~ 살짝 뜀).</summary>
        public static float Lift(float ballY, float clipContactY, float scale) =>
            Math.Max(LiftMin, Math.Min(LiftMax, ballY - clipContactY * scale));

        /// <summary>
        /// 점프 헤딩 리프트: 하한은 "점프 높이가 JumpMin까지 낮아짐" = (서서 접촉점 − 점프 접촉점) × 배율 + JumpMin
        /// (클립 점프 0.5 m 고정 — 필요한 점프 0.10~0.30 m 헤더가 LiftMin에 걸리지 않게, T07 진단). 상한은 LiftMax.
        /// </summary>
        public static float JumpLift(float ballY, float jumpContactY, float standContactY, float scale) =>
            Math.Max((standContactY - jumpContactY) * scale + JumpMin, Math.Min(LiftMax, ballY - jumpContactY * scale));

        /// <summary>
        /// 리프트 가중치: 클립 시작 → 접촉에서 0 → 1(smoothstep), 접촉 → 접촉 + (접촉 − 시작)에서 1 → 0. 접촉 시각에 정점 1, 밖은 0.
        /// </summary>
        public static float LiftWeight(float start, float contactAt, float t)
        {
            float rise = Math.Max(1e-4f, contactAt - start);
            if (t <= start || t >= contactAt + rise) return 0f;
            float x = t <= contactAt ? (t - start) / rise : 1f - (t - contactAt) / rise;
            return x * x * (3f - 2f * x);
        }
    }
}
