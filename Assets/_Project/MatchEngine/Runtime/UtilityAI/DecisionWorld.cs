using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 판단 AI 입력 스냅샷 (specs/010). 결정 단계(실경기)가 매 틱 <c>MatchContext</c>에서 채우고, 테스트 픽스처는 직접 채운다 — 같은 결정 경로를 쓴다.
    /// 좌표는 기준 미터 좌표, 슬롯 0~21 (0~10 홈, 11~21 원정, 팀 첫 슬롯 = GK). 생성 뒤 배열 할당 없음.
    /// </summary>
    public sealed class DecisionWorld
    {
        public const int N = 22, PerTeam = 11;

        public long Tick;
        public float Dt = 0.1f;
        public float PitchL = 105f, PitchW = 68f;

        public readonly Vec2[] Pos = new Vec2[N], Vel = new Vec2[N];
        public readonly bool[] Active = new bool[N];
        public readonly float[] W = new float[N], C = new float[N];
        public readonly PlayerMotionProfile[] Profile = new PlayerMotionProfile[N];
        public readonly TimeToIntercept.Context[] Tti = new TimeToIntercept.Context[N];
        public readonly KinematicAttributes[] Kin = new KinematicAttributes[N];
        public readonly DecisionAttributes[] Mind = new DecisionAttributes[N];

        public Vec2 Ball, BallVel;
        public float BallZ;
        /// <summary>공 소유 슬롯 (없으면 −1).</summary>
        public int Owner = -1;
        /// <summary>소유 시작 틱 (FR-19a 침투 출발 기준).</summary>
        public long OwnerSinceTick;
        /// <summary>날아가는 패스의 받는 슬롯 (없으면 −1) — 수신 우선권 (FR-41 "공이 자기에게 패스됨").</summary>
        public int PassTarget = -1;

        public readonly int[] AttackDir = { 1, -1 };
        public readonly TeamPhase[] Phase = new TeamPhase[2];
        /// <summary>팀의 오프사이드 라인 x (그 팀이 수비할 때 상대 공격수가 지켜야 하는 선).</summary>
        public readonly float[] OffsideLineX = new float[2];
        public readonly float[] DefLineX = new float[2];
        /// <summary>형태 앵커 (009 형태 목표, 배역 중이면 마지막 앵커).</summary>
        public readonly Vec2[] Anchor = new Vec2[N];
        /// <summary>이번 틱 009 형태 목표가 있는가 (없으면 HoldShape 대신 앵커로 MoveTo).</summary>
        public readonly bool[] HasShape = new bool[N];
        /// <summary>압박 지정 1·2·3순위 (슬롯, 없으면 −1) — 009 FR-61.</summary>
        public readonly int[] Presser = new int[6];
        public readonly float[] PressIntensity = new float[2];
        public readonly bool[] GegenActive = new bool[2];
        /// <summary>마크 대상 (슬롯, 없으면 −1) — FR-19b.</summary>
        public readonly int[] MarkTarget = new int[N];
        public readonly IntentPacket[] Intent = new IntentPacket[N];
        /// <summary>이번 틱 공 반경 안 팀별 선수 수 (FR-15, 실제 위치).</summary>
        public readonly int[] NearBall = new int[2];

        public DecisionWorld()
        {
            for (int i = 0; i < Presser.Length; i++) Presser[i] = -1;
            for (int i = 0; i < N; i++) { MarkTarget[i] = -1; W[i] = 1f; Mind[i] = DecisionAttributes.Average; }
        }

        public static int TeamOf(int slot) => slot / PerTeam;
        public static bool IsKeeper(int slot) => slot % PerTeam == 0;

        public int PossTeam => Owner >= 0 ? TeamOf(Owner) : -1;

        public Vec2 OwnGoal(int team) => new Vec2(AttackDir[team] > 0 ? 0f : PitchL, PitchW * 0.5f);
        public Vec2 OppGoal(int team) => new Vec2(AttackDir[team] > 0 ? PitchL : 0f, PitchW * 0.5f);

        public int PresserOrder(int slot)
        {
            var t = TeamOf(slot);
            for (int o = 0; o < 3; o++) if (Presser[t * 3 + o] == slot) return o;
            return -1;
        }

        public Vec2 ClampToPitch(Vec2 p, float margin = 1f) =>
            new Vec2(DMath.Min(DMath.Max(p.X, margin), PitchL - margin), DMath.Min(DMath.Max(p.Y, margin), PitchW - margin));

        /// <summary>선수 s가 지각하는 o의 위치 (FR-24: 005 인지 지연만큼 과거 — 속도 역외삽 근사).</summary>
        public Vec2 Perceived(int o, float delay) => Pos[o] - Vel[o] * delay;

        /// <summary>운동 프로필·TTI 컨텍스트 갱신 (위치·속도·피로를 바꾼 뒤 부른다).</summary>
        public void RefreshTti(KinematicsParams kp)
        {
            for (int s = 0; s < N; s++)
                if (Active[s]) Tti[s] = new TimeToIntercept.Context(in Profile[s], Pos[s], Vel[s], W[s], C[s], kp);
        }

        /// <summary>테스트·픽스처용: 능력치로 프로필을 만든다.</summary>
        public void SetPlayer(int s, in KinematicAttributes a, KinematicsParams kp, Vec2 pos, Vec2 vel = default)
        {
            Active[s] = true;
            Kin[s] = a;
            Profile[s] = PlayerMotionProfile.Create(in a, kp);
            Pos[s] = pos;
            Vel[s] = vel;
            Anchor[s] = pos;
            HasShape[s] = true;
        }

        public void CountNearBall(float radius)
        {
            NearBall[0] = 0;
            NearBall[1] = 0;
            var r2 = radius * radius;
            for (int s = 0; s < N; s++)
                if (Active[s] && !IsKeeper(s) && (Pos[s] - Ball).LengthSq() <= r2) NearBall[TeamOf(s)]++;
        }
    }
}
