namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 슈팅 xG 평가 입력 (specs/071 FR-10). 거리(m)·각도(rad)는 <see cref="ShotModel.Angle"/> 정의, 나머지는 0~1 정규화(블로커는 명 수).
    /// <see cref="Behind"/>면(골라인 밖) xG 0, <see cref="Penalty"/>면 FR-11 고정값.
    /// </summary>
    public struct ShotContext
    {
        public float Dist;
        public float Angle;
        /// <summary>헤더면 1.</summary>
        public float Header;
        public float Pressure;
        /// <summary>슈터–두 골포스트 삼각형 안 수비 필드 선수 수.</summary>
        public int Blockers;
        public float GkOut;
        public float Assist;
        /// <summary>원터치로 바운드된 공을 차면 1.</summary>
        public float Bouncing;
        public bool Penalty;
        public bool Behind;
    }
}
