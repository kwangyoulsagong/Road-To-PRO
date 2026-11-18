using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.PitchControl
{
    /// <summary>
    /// 피치 컨트롤 질의 입력 (specs/007 FR-01·03·06). 슬롯 22개, 질의는 이 뷰만 읽는다(008 ActionWorld를 모른다).
    /// 008 ActionWorld는 자기 배열(Active·Pos·Vel·TTI·퍼스트터치·태클)을 그대로 공유하는 뷰를 만든다 — 복사 없음, 틱마다 채울 필요 없음.
    /// 도착 시간 모드: 엔진 = 005 TTI 컨텍스트(<see cref="Tti"/>), 원 논문 = 위치·속도·반응 0.7 s·v_max 5 m/s(FR-01).
    /// λ: 능력치 배열이 있으면 질의 때 FR-06 식으로 계산(<see cref="Lambdas"/>), 없으면 <see cref="LambdaAtt"/>·<see cref="LambdaDef"/>(테스트·고정 배치). 수비 κ·GK 배수는 질의가 곱한다.
    /// </summary>
    public sealed class ControlPlayers
    {
        public const int N = 22, PerTeam = 11;

        public readonly bool[] Active;
        public readonly Vec2[] Pos, Vel;
        public readonly TimeToIntercept.Context[] Tti;
        public readonly byte[] Team = new byte[N];
        public readonly bool[] IsKeeper = new bool[N];
        public readonly float[] LambdaAtt = new float[N], LambdaDef = new float[N];
        /// <summary>FR-06 능력치 (정규화 0~1). null이면 λ 배열을 쓴다.</summary>
        public readonly float[] FirstTouch, Tackling;

        /// <summary>true = 원 논문 도착 시간(FR-01), false = 005 TTI(엔진 모드, 연결 경로).</summary>
        public bool PaperArrival;
        public float PitchL = 105f, PitchW = 68f;

        /// <summary>독립 뷰 (테스트·검증): 자기 배열을 가진다. 팀 = 슬롯/11, GK = 슬롯 %11 == 0이 기본이고 Set*가 덮어쓴다.</summary>
        public ControlPlayers() : this(new bool[N], new Vec2[N], new Vec2[N], new TimeToIntercept.Context[N], null, null) { }

        /// <summary>공유 뷰 (008 ActionWorld): 배열을 참조로 받는다. 팀 = 슬롯/11, GK = 슬롯 %11 == 0.</summary>
        public ControlPlayers(bool[] active, Vec2[] pos, Vec2[] vel, TimeToIntercept.Context[] tti, float[] firstTouch, float[] tackling)
        {
            Active = active;
            Pos = pos;
            Vel = vel;
            Tti = tti;
            FirstTouch = firstTouch;
            Tackling = tackling;
            for (int i = 0; i < N; i++)
            {
                Team[i] = (byte)(i / PerTeam);
                IsKeeper[i] = i % PerTeam == 0;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < N; i++) Active[i] = false;
        }

        /// <summary>원 논문 모드 선수 (테스트·검증용). λ는 기본값.</summary>
        public void SetPaper(int slot, int team, bool keeper, Vec2 pos, Vec2 vel, PitchControlParams p)
        {
            Active[slot] = true;
            Team[slot] = (byte)team;
            IsKeeper[slot] = keeper;
            Pos[slot] = pos;
            Vel[slot] = vel;
            LambdaAtt[slot] = p.Lambda;
            LambdaDef[slot] = p.Lambda;
        }

        /// <summary>엔진 모드 선수: TTI 컨텍스트 + λ (FR-06 연결 결과).</summary>
        public void SetEngine(int slot, int team, bool keeper, Vec2 pos, Vec2 vel, in TimeToIntercept.Context tti, float lambdaAtt, float lambdaDef)
        {
            Active[slot] = true;
            Team[slot] = (byte)team;
            IsKeeper[slot] = keeper;
            Pos[slot] = pos;
            Vel[slot] = vel;
            Tti[slot] = tti;
            LambdaAtt[slot] = lambdaAtt;
            LambdaDef[slot] = lambdaDef;
        }

        /// <summary>질의용 λ (공격·수비 기본값, κ·GK 배수 전).</summary>
        public void LambdaOf(PitchControlParams p, int j, out float att, out float def)
        {
            if (FirstTouch == null || Tackling == null)
            {
                att = LambdaAtt[j];
                def = LambdaDef[j];
                return;
            }
            Lambdas(p, IsKeeper[j], FirstTouch[j], Tackling[j], 0.5f, out att, out def);
        }

        /// <summary>
        /// FR-06 λ 연결: λ_def ← 태클(0~1) lerp(min, max), λ_att ← 퍼스트터치(0~1) λ·(1 + k·(a − 0.5)), GK 핸들링은 λ_def에 같은 방식(k_gk).
        /// 연결을 끄면 둘 다 λ. (핸들링은 002 전 입력이 없어 0.5 — k_gk 기본 0이라 영향 없음)
        /// </summary>
        public static void Lambdas(PitchControlParams p, bool keeper, float firstTouch, float tackling, float handling, out float att, out float def)
        {
            if (!p.AttrOn)
            {
                att = p.Lambda;
                def = p.Lambda;
                return;
            }
            att = p.Lambda * (1f + p.LambdaAttK * (firstTouch - 0.5f));
            def = p.LambdaDefMin + (p.LambdaDefMax - p.LambdaDefMin) * (tackling < 0f ? 0f : (tackling > 1f ? 1f : tackling));
            if (keeper) def *= 1f + p.LambdaGkK * (handling - 0.5f);
            if (att < 0.01f) att = 0.01f;
            if (def < 0.01f) def = 0.01f;
        }
    }
}
