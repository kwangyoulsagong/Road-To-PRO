using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>
    /// 072 고정 장면 픽스처: MatchContext 없이 <see cref="OnBallDecider"/>를 직접 부른다(단계와 같은 경로).
    /// 010 <see cref="DecisionFixture"/> 장면 위에 008 평가 월드(<see cref="ActionWorld"/>)를 같은 위치·능력치로 맞춘다. 기본 소유자 = 슬롯 6.
    /// </summary>
    public sealed class OnBallFixture
    {
        public readonly DecisionFixture F;
        public readonly ActionWorld Aw = new ActionWorld();
        public readonly StandInChemistry Chem = new StandInChemistry();
        public readonly StandInOnBallEval Eval;
        public readonly OnBallDecider Decider;
        readonly Vec2[] _op = new Vec2[11], _ov = new Vec2[11];

        public DecisionWorld W => F.W;
        public DecisionParams P => F.P;
        public StandInStyleSource Style => F.Style;
        public static ActionParams Ap => ActionTestKit.P;
        public static BallParams Bp => ActionTestKit.Ball;

        public OnBallFixture(DecisionParams p = null)
        {
            F = new DecisionFixture(p);
            Aw.PrepareRoll(Ap, Bp);
            Eval = new StandInOnBallEval(F.P);
            Decider = new OnBallDecider(F.P, F.Style, Eval, Chem);
            Sync();
        }

        /// <summary>결정 월드 → 008 월드 동기화 (위치·속도·능력치).</summary>
        public void Sync()
        {
            F.Refresh();
            Aw.PitchL = W.PitchL;
            Aw.PitchW = W.PitchW;
            Aw.AttackDir[0] = W.AttackDir[0];
            Aw.AttackDir[1] = W.AttackDir[1];
            for (int s = 0; s < DecisionWorld.N; s++)
            {
                if (!W.Active[s]) { Aw.Active[s] = false; continue; }
                Aw.SetPlayer(s, in W.Kin[s], F.Kp, W.Pos[s], W.Vel[s], in W.Tech[s], W.Mind[s].Vision, W.Mind[s].Composure);
            }
        }

        /// <summary>팀 0의 owner 외 동료를 모두 빼고 owner만 남긴다.</summary>
        public void OnlyOwnerOf(int owner)
        {
            var t = DecisionWorld.TeamOf(owner);
            for (int o = t * 11; o < (t + 1) * 11; o++) if (o != owner) W.Active[o] = false;
        }

        public void Place(int s, Vec2 pos, Vec2 vel = default)
        {
            W.Active[s] = true;
            W.Pos[s] = pos;
            W.Vel[s] = vel;
            W.Anchor[s] = pos;
        }

        public void GiveBall(int s)
        {
            W.Owner = s;
            W.Ball = W.Pos[s];
        }

        /// <summary>008 볼 소유자 압박 (단계와 같은 식).</summary>
        public float Pressure(int s)
        {
            var opp = 1 - DecisionWorld.TeamOf(s);
            var n = 0;
            for (int o = opp * 11; o < (opp + 1) * 11; o++)
            {
                if (!W.Active[o]) continue;
                _op[n] = W.Pos[o];
                _ov[n] = W.Vel[o];
                n++;
            }
            return PressureModel.Carrier(Ap, W.Pos[s], W.Vel[s], W.Ball, W.BallVel, _op, _ov, n, W.PitchW);
        }

        public void Generate(int s = 6, bool oneTouch = false, float held = 1f)
        {
            Sync();
            Decider.Candidates.Generate(W, Aw, Ap, Bp, s, Pressure(s), oneTouch, held);
        }

        public OnBallResult Decide(SeedRng rng, int s = 6, bool oneTouch = false, float held = 1f, bool sync = true)
        {
            if (sync) Sync();
            return Decider.Decide(W, Aw, Ap, Bp, s, ref F.State[s], rng, Pressure(s), oneTouch, held);
        }

        /// <summary>같은 장면 n회 결정 (새 상태) → 항목별 횟수.</summary>
        public int[] Distribution(int s, ulong seed, int n = 1000, Action<OnBallResult> each = null)
        {
            Sync();
            var counts = new int[OnBallItems.Count];
            var rng = new SeedRng(seed);
            for (int i = 0; i < n; i++)
            {
                F.ResetState(s);
                var r = Decide(rng, s, sync: false);
                counts[(int)r.Item]++;
                each?.Invoke(r);
            }
            return counts;
        }

        public static int Passes(int[] c) =>
            c[(int)OnBallItem.ShortPass] + c[(int)OnBallItem.ProgPass] + c[(int)OnBallItem.ThroughBall] + c[(int)OnBallItem.Switch] + c[(int)OnBallItem.Cross];
    }

    /// <summary>고정 결합 배수 (AC-02 검산용): 지정 항목만 m, 나머지 1.</summary>
    public sealed class FixedStyle : IStyleMultiplierSource
    {
        readonly Consideration _k;
        readonly float _m;
        public FixedStyle(Consideration k, float m) { _k = k; _m = m; }
        public float Multiplier(int slot, Consideration k) => k == _k ? _m : 1f;
        public float MultiplierFullCompliance(int slot, Consideration k) => Multiplier(slot, k);
        public float InstructionWeight(int slot, Consideration k) => 1f;
        public void Breakdown(int slot, Consideration k, out MultiplierBreakdown b) { b = default; b.Combined = Multiplier(slot, k); b.Instruction = 1f; }
        public float Freedom(int slot) => 0.5f;
        public int ConflictingHabitLevel(int slot, Consideration intentKind) => 0;
        public float TendencyConflict(int slot, Consideration intentKind) => 0f;
        public int HabitLevel(int slot, Habit h) => 0;
    }
}
