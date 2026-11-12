using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 액션 평가 입력 스냅샷 (specs/008). 22 슬롯 위치·속도·TTI·기술/정신 능력치(정규화 a)·주발. <see cref="ActionSystem"/>이 틱마다 채우고,
    /// 테스트·픽스처는 직접 채운다. 007 전 대체 피치 컨트롤·xT도 여기서 계산한다(002·007 연결 때 AC-24로 교체·재확인).
    /// </summary>
    public sealed class ActionWorld
    {
        public const int N = 22, PerTeam = 11;

        public float PitchL = 105f, PitchW = 68f;
        public readonly int[] AttackDir = { 1, -1 };
        public readonly Vec2[] Pos = new Vec2[N], Vel = new Vec2[N];
        public readonly bool[] Active = new bool[N];
        public readonly PlayerMotionProfile[] Profile = new PlayerMotionProfile[N];
        public readonly TimeToIntercept.Context[] Tti = new TimeToIntercept.Context[N];
        /// <summary>정규화 능력치 a = 내부/100 (FR-01).</summary>
        public readonly float[] Passing = new float[N], FirstTouch = new float[N], Technique = new float[N], Vision = new float[N], Composure = new float[N];
        public readonly Foot[] PreferredFoot = new Foot[N];
        public readonly int[] WeakFoot = new int[N];
        /// <summary>원터치 마스터 버릇 레벨 (0 = 없음, FR-71 ②).</summary>
        public readonly byte[] OneTouchLevel = new byte[N];

        /// <summary>땅볼 속도 후보별 구름 표 시작값 캐시 (같은 함수 값 — 평가 핫 경로에서 이진 탐색 2회 절약). <see cref="PrepareRoll"/>.</summary>
        public readonly float[] RollX0 = new float[4], RollStep0 = new float[4], LoftSin2 = new float[2], LoftSin = new float[2];
        public Ball.BallParams RollFor;
        public ActionParams RollParams;

        public void PrepareRoll(ActionParams p, Ball.BallParams bp)
        {
            for (int i = 0; i < 4; i++)
            {
                RollX0[i] = bp.RollTable.DistanceAtSpeed(PassModel.GroundSpeed(p, i));
                RollStep0[i] = bp.RollTable.StepAtDistance(RollX0[i]);
            }
            for (int i = 0; i < 2; i++)
            {
                var e = (i == 0 ? p.LoftLow : p.LoftHigh) * ActionMath.DegToRad;
                LoftSin2[i] = DMath.Sin(2f * e);
                LoftSin[i] = DMath.Sin(e);
            }
            RollFor = bp;
            RollParams = p;
        }

        /// <summary>TTI 하한 가지치기 여유 (s): float 경계(정지 근처 반응 시간만 반환·뉴턴 1 ulp)에서도 결과가 같게 (결정론 리뷰).</summary>
        public const float PruneSlack = 1e-4f;

        public static int TeamOf(int slot) => slot / PerTeam;
        public static bool IsKeeper(int slot) => slot % PerTeam == 0;
        public Vec2 OppGoal(int team) => new Vec2(AttackDir[team] > 0 ? PitchL : 0f, PitchW * 0.5f);

        /// <summary>테스트·픽스처: 능력치로 슬롯을 채운다 (TTI 갱신 포함).</summary>
        public void SetPlayer(int s, in KinematicAttributes kin, KinematicsParams kp, Vec2 pos, Vec2 vel, in TechnicalAttributes tech, float vision = 50f, float composure = 50f)
        {
            Active[s] = true;
            Profile[s] = PlayerMotionProfile.Create(in kin, kp);
            Pos[s] = pos;
            Vel[s] = vel;
            Tti[s] = new TimeToIntercept.Context(in Profile[s], pos, vel, 1f, 0f, kp);
            SetSkills(s, tech.OrAverage(), vision, composure, 0);
        }

        public void SetSkills(int s, in TechnicalAttributes tech, float vision, float composure, byte oneTouchLevel)
        {
            Passing[s] = ActionMath.Norm(tech.Passing);
            FirstTouch[s] = ActionMath.Norm(tech.FirstTouch);
            Technique[s] = ActionMath.Norm(tech.Technique);
            Vision[s] = ActionMath.Norm(vision);
            Composure[s] = ActionMath.Norm(composure);
            PreferredFoot[s] = tech.PreferredFoot;
            WeakFoot[s] = tech.WeakFoot;
            OneTouchLevel[s] = oneTouchLevel;
        }

        public void Clear()
        {
            System.Array.Clear(Active, 0, N);
        }

        /// <summary>
        /// 007 전 대체 피치 컨트롤 (010 <c>StandInSpace.Control</c>과 같은 식): 로지스틱((상대 최단 도달 시간 − 내 도달 시간)/척도).
        /// 상대 0명이면 1.
        /// </summary>
        public float Control(ActionParams p, int s, Vec2 point)
        {
            var mine = TimeToIntercept.Estimate(in Tti[s], point.X, point.Y);
            var opp = 1 - TeamOf(s);
            var best = float.MaxValue;
            for (int o = opp * PerTeam; o < (opp + 1) * PerTeam; o++)
            {
                if (!Active[o]) continue;
                // 가지치기 (결과 동일 — float 오차 여유 PruneSlack): TTI ≥ 반응 + 거리/Vm (005 모노-지수 x(t) ≤ Vm·t, 방향 전환·감속 비용 ≥ 0) — 하한이 현재 최솟값 이상이면 건너뛴다
                ref readonly var k = ref Tti[o];
                var dx = point.X - k.StartX;
                var dy = point.Y - k.StartY;
                if (k.Reaction + DMath.Sqrt(dx * dx + dy * dy) * k.InvVm - PruneSlack >= best) continue;
                var t = TimeToIntercept.Estimate(in k, point.X, point.Y);
                if (t < best) best = t;
            }
            if (best == float.MaxValue) return 1f;
            return ActionMath.Sigmoid((best - mine) / DMath.Max(p.PcScale, 1e-3f));
        }

        /// <summary>007 전 대체 xT: (1 − 상대 골 거리/D)².</summary>
        public float Threat(ActionParams p, int team, Vec2 point)
        {
            var t = 1f - (OppGoal(team) - point).Length() / p.XtDist;
            return t < 0f ? 0f : t * t;
        }
    }
}
