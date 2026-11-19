using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>
    /// 심판 판정기가 읽는 경기 상태 뷰 (specs/012). 판정기는 <see cref="MatchContext"/>를 모르고 이 고정 배열만 본다 — 표 테스트가 상태를 직접 채운다.
    /// 슬롯 0~10 = 팀 0, 11~21 = 팀 1, 각 팀 첫 슬롯 = GK. 할당 없음(생성 때 한 번).
    /// </summary>
    public sealed class RefereeWorld
    {
        public const int N = 22, PerTeam = 11;

        public float PitchL = 105f, PitchW = 68f;
        /// <summary>팀별 공격 방향 (+1 = x 증가 쪽 골을 공격).</summary>
        public readonly int[] AttackDir = { 1, -1 };
        public readonly Vec2[] Pos = new Vec2[N];
        public readonly Vec2[] Vel = new Vec2[N];
        public readonly Vec2[] Facing = new Vec2[N];
        public readonly bool[] Active = new bool[N];
        public readonly int[] PlayerId = new int[N];
        /// <summary>심판 3명 위치 (0 주심, 1·2 부심).</summary>
        public readonly Vec2[] Officials = new Vec2[3];
        public Vec3 Ball;
        public Vec2 BallVel;
        /// <summary>홈 팀 (홈 편향 FR-30·31). 006 기준 팀 0.</summary>
        public int HomeTeam;
        /// <summary>경기 압박 0~1 (빅매치·막판, FR-30 압박 저항 입력).</summary>
        public float Pressure;

        public RefereeWorld()
        {
            for (int s = 0; s < N; s++) { Facing[s] = new Vec2(1f, 0f); PlayerId[s] = s; }
        }

        public static int TeamOf(int slot) => slot / PerTeam;
        public static bool IsKeeper(int slot) => slot % PerTeam == 0;
        public int KeeperSlot(int team) => team * PerTeam;

        public Vec2 OppGoal(int team) => new Vec2(AttackDir[team] > 0 ? PitchL : 0f, PitchW * 0.5f);
        public Vec2 OwnGoal(int team) => new Vec2(AttackDir[team] > 0 ? 0f : PitchL, PitchW * 0.5f);

        /// <summary>attTeam 기준 깊이 (공격 방향으로 클수록 상대 골에 가까움).</summary>
        public float Depth(int attTeam, float x) => x * AttackDir[attTeam];

        public void Fill(MatchContext ctx)
        {
            PitchL = ctx.Pitch.LengthM;
            PitchW = ctx.Pitch.WidthM;
            AttackDir[0] = ctx.AttackDir(0);
            AttackDir[1] = ctx.AttackDir(1);
            for (int s = 0; s < N; s++)
            {
                var a = ctx.IsSlotActive(s);
                Active[s] = a;
                PlayerId[s] = ctx.PlayerAtSlot(s);
                if (!a)
                {
                    // 결정론 리뷰 F1: 빠진 선수의 지난 위치를 남기지 않는다 (이 뷰는 직렬화하지 않아 복원 실행과 갈린다)
                    Pos[s] = Vec2.Zero; Vel[s] = Vec2.Zero; Facing[s] = new Vec2(1f, 0f);
                    continue;
                }
                ref readonly var st = ref ctx.SlotState(s);
                Pos[s] = st.Position;
                Vel[s] = st.Velocity;
                Facing[s] = st.Facing;
            }
            for (int i = 0; i < 3; i++) Officials[i] = ctx.Officials.Position(i);
            Ball = ctx.Ball.Position;
            BallVel = ctx.Ball.Velocity.XY;
        }
    }
}
