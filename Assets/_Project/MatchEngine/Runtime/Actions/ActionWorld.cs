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
        /// <summary>070 결투 입력 (정규화 a): 기술 Dribbling·Tackling·Positioning·Strength(002 전 대체), 005 운동 Agility·Pace·Balance.</summary>
        public readonly float[] Dribbling = new float[N], Tackling = new float[N], Positioning = new float[N], Strength = new float[N],
            Agility = new float[N], Pace = new float[N], Balance = new float[N];
        /// <summary>070 태클 스타일 대체 입력 (FR-31): 다혈질(010 정신, 정규화)·슬라이딩 장인 레벨.</summary>
        public readonly float[] Temperament = new float[N];
        public readonly byte[] SlideLevel = new byte[N];

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
        readonly float[] _lb = new float[PerTeam];
        readonly int[] _lbSlot = new int[PerTeam];

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
            SetDuel(s, tech.OrAverage(), in kin, 50f, 0);
        }

        /// <summary>070 결투 입력 (FR-30: 002 FR-14 정규화 능력치만).</summary>
        public void SetDuel(int s, in TechnicalAttributes tech, in KinematicAttributes kin, float temperament, byte slideLevel)
        {
            Dribbling[s] = ActionMath.Norm(tech.Dribbling);
            Tackling[s] = ActionMath.Norm(tech.Tackling);
            Positioning[s] = ActionMath.Norm(tech.Positioning);
            Strength[s] = ActionMath.Norm(tech.Strength);
            Agility[s] = ActionMath.Norm(kin.Agility);
            Pace[s] = ActionMath.Norm(kin.Pace);
            Balance[s] = ActionMath.Norm(kin.Balance);
            Temperament[s] = ActionMath.Norm(temperament);
            SlideLevel[s] = slideLevel;
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
            // 하한 TTI ≥ 반응 + 거리/Vm (005 모노-지수 x(t) ≤ Vm·t, 방향 전환·감속 비용 ≥ 0)을 모두 구해 작은 순서로 정확히 계산하고,
            // 하한(− 여유 PruneSlack)이 현재 최솟값 이상이면 나머지는 건너뛴다 — 최솟값은 가지치기 없이 구한 값과 같다 (결정론 리뷰)
            var n = 0;
            for (int o = opp * PerTeam; o < (opp + 1) * PerTeam; o++)
            {
                if (!Active[o]) continue;
                ref readonly var k = ref Tti[o];
                var dx = point.X - k.StartX;
                var dy = point.Y - k.StartY;
                var lb = k.Reaction + DMath.Sqrt(dx * dx + dy * dy) * k.InvVm;
                // 삽입 정렬 (하한 오름차순, 동률은 낮은 슬롯 먼저)
                var j = n++;
                while (j > 0 && _lb[j - 1] > lb) { _lb[j] = _lb[j - 1]; _lbSlot[j] = _lbSlot[j - 1]; j--; }
                _lb[j] = lb;
                _lbSlot[j] = o;
            }
            var best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_lb[i] - PruneSlack >= best) break;
                var t = TimeToIntercept.Estimate(in Tti[_lbSlot[i]], point.X, point.Y);
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
