namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 온볼 고려 항목 (specs/072 범위·FR-40, 리서치 03 § A8) 9종 + 폴백 2종. 번호는 텔레메트리·데이터에 들어가므로 바꾸지 않는다.
    /// 결합 배수는 <see cref="Consideration"/>으로 질의한다(<see cref="OnBallItems"/> 대응표).
    /// </summary>
    public enum OnBallItem : byte
    {
        ShortPass = 0,
        ProgPass = 1,
        ThroughBall = 2,
        Switch = 3,
        Cross = 4,
        Carry = 5,
        Dribble = 6,
        Shoot = 7,
        Hold = 8,
        /// <summary>걷어내기 (FR-04 폴백·자기 진영 압박).</summary>
        Clear = 9,
        /// <summary>넘어지기 (FR-20).</summary>
        Dive = 10,
    }

    public static class OnBallItems
    {
        public const int Count = 11;

        public static bool IsPass(OnBallItem k) =>
            k == OnBallItem.ShortPass || k == OnBallItem.ProgPass || k == OnBallItem.ThroughBall || k == OnBallItem.Switch || k == OnBallItem.Cross;

        /// <summary>
        /// 결합 배수 질의 고려 항목 (주 항목). 없으면 false(배수 1).
        /// 패스류 → Pass(스루볼 → ThroughBall, 크로스 → Cross), 드리블 → Dribble, 슈팅 → Shot, 홀드 → HoldBall, 넘어지기 → DiveForFoul. 캐리·걷어내기는 대응 항목 없음 [대체](003 연결 때 재확인).
        /// </summary>
        public static bool Primary(OnBallItem k, out Consideration c)
        {
            switch (k)
            {
                case OnBallItem.ShortPass:
                case OnBallItem.ProgPass:
                case OnBallItem.Switch: c = Consideration.Pass; return true;
                case OnBallItem.ThroughBall: c = Consideration.ThroughBall; return true;
                case OnBallItem.Cross: c = Consideration.Cross; return true;
                case OnBallItem.Dribble: c = Consideration.Dribble; return true;
                case OnBallItem.Shoot: c = Consideration.Shot; return true;
                case OnBallItem.Hold: c = Consideration.HoldBall; return true;
                case OnBallItem.Dive: c = Consideration.DiveForFoul; return true;
                default: c = Consideration.HoldShape; return false;
            }
        }
    }

    /// <summary>후보 하나에 걸리는 결합 배수 질의 항목들 (주 항목 + 보조: 원터치·도움·컷인·중거리·개인기). 최대 3개, 할당 없는 값 타입.</summary>
    public struct ConsiderationSet
    {
        public Consideration A, B, C;
        public byte Count;

        public void Add(Consideration k)
        {
            if (Count == 0) A = k; else if (Count == 1) B = k; else if (Count == 2) C = k; else return;
            Count++;
        }

        public Consideration this[int i] => i == 0 ? A : (i == 1 ? B : C);
    }
}
