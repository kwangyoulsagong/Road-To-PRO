using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>테이크온 결과 분기 (specs/070 FR-06).</summary>
    public enum TakeOnOutcome : byte
    {
        Beat = 0,
        Steal = 1,
        Out = 2,
        FoulWon = 3,
        Loose = 4,
    }

    /// <summary>
    /// 1v1 테이크온 (specs/070 FR-04~06, 리서치 02 §3). 평가는 RNG 없음. <see cref="Resolve"/>는 정확히 균등 난수 2개(성공 1 + 분기 1, 성공이어도 소비).
    /// cover(007 PPCF 전 대체): 수비수 뒤 지점(공격수→골문 방향 2 m)에 두 번째 수비수가 공격수보다 먼저 닿는 정도 = clamp01(0.5 + (TTI_공격수 − TTI_커버)/coverRange).
    /// </summary>
    public static class TakeOnModel
    {
        public const int RngPerTakeOn = 2;
        public const float CoverBehind = 2f;

        /// <summary>FR-04: 수비수가 공격수와 골문 사이(골문 방향 성분 &gt; 0, 측면 ≤ laneHalf)이고 거리 &lt; range (경계 3 m는 발동 안 함).</summary>
        public static bool Triggered(ActionParams p, Vec2 att, Vec2 def, Vec2 goal)
        {
            var rel = def - att;
            if (!(rel.LengthSq() < p.TakeOnRange * p.TakeOnRange)) return false;
            var g = goal - att;
            var gl = g.Length();
            if (gl < 1e-4f) return false;
            var u = g / gl;
            var along = Vec2.Dot(rel, u);
            var lateral = DMath.Abs(u.X * rel.Y - u.Y * rel.X);
            return along > 0f && lateral <= p.TakeOnLaneHalf;
        }

        /// <summary>FR-05 defSetFactor: 수비수 속도 &lt; 1.5 m/s(정면 자세)면 1, 아니면(돌진 중) 0. 정면 여부는 <see cref="Triggered"/>가 보장.</summary>
        public static float DefSetFactor(ActionParams p, Vec2 defVel) => defVel.LengthSq() < p.TakeOnSetSpeed * p.TakeOnSetSpeed ? 1f : 0f;

        /// <summary>각도 우위 [대체]: 공격수 진행 방향과 공격수→수비수 방향 사이 각의 사인(0 = 정면 돌진, 1 = 옆으로 비껴 들어감). 멈춰 있으면 0.</summary>
        public static float AngleAdv(Vec2 att, Vec2 attVel, Vec2 def)
        {
            var sp = attVel.Length();
            var rel = def - att;
            var d = rel.Length();
            if (sp < 0.5f || d < 1e-4f) return 0f;
            var c = (attVel.X * rel.Y - attVel.Y * rel.X) / (sp * d);
            return DMath.Abs(c);
        }

        /// <summary>cover [대체] (0~1). 두 번째 수비수가 없으면 0.</summary>
        public static float Cover(ActionParams p, ActionWorld w, int att, int def, Vec2 goal)
        {
            var g = goal - w.Pos[att];
            var gl = g.Length();
            var u = gl > 1e-4f ? g / gl : new Vec2(w.AttackDir[ActionWorld.TeamOf(att)], 0f);
            var behind = w.Pos[def] + u * CoverBehind;
            var tAtt = TimeToIntercept.Estimate(in w.Tti[att], behind.X, behind.Y);
            var team = ActionWorld.TeamOf(def);
            var best = float.MaxValue;
            // 하한 TTI(반응 + 거리/Vm)가 지금 최솟값(− 여유) 이상이면 정확 계산을 건너뛴다 — 최솟값은 가지치기 없이 구한 값과 같다 (008 Control과 같은 방식)
            for (int o = team * ActionWorld.PerTeam; o < (team + 1) * ActionWorld.PerTeam; o++)
            {
                if (o == def || !w.Active[o] || ActionWorld.IsKeeper(o)) continue;
                ref readonly var k = ref w.Tti[o];
                var dx = behind.X - k.StartX;
                var dy = behind.Y - k.StartY;
                if (best != float.MaxValue && k.Reaction + DMath.Sqrt(dx * dx + dy * dy) * k.InvVm - ActionWorld.PruneSlack >= best) continue;
                var t = TimeToIntercept.Estimate(in k, behind.X, behind.Y);
                if (t < best) best = t;
            }
            if (best == float.MaxValue) return 0f;
            var c = 0.5f + (tAtt - best) / DMath.Max(p.TakeOnCoverRange, 1e-3f);
            return c < 0f ? 0f : (c > 1f ? 1f : c);
        }

        /// <summary>FR-05 logit (a_Agility_or_Pace = max(민첩, 속도) [대체]).</summary>
        public static float Logit(ActionParams p, ActionWorld w, int att, int def, float angleAdv, float cover, float defSet)
        {
            var aAgi = w.Agility[att] > w.Pace[att] ? w.Agility[att] : w.Pace[att];
            return p.TakeOnC0
                + p.TakeOnKSkill * (w.Dribbling[att] - w.Tackling[def] * 0.6f - w.Positioning[def] * 0.4f)
                + p.TakeOnKPace * (aAgi - w.Pace[def])
                + p.TakeOnKAngle * angleAdv
                - p.TakeOnKCover * cover
                - p.TakeOnKSet * defSet;
        }

        public static float Probability(ActionParams p, ActionWorld w, int att, int def, float angleAdv, float cover, float defSet)
            => ActionMath.Prob(Logit(p, w, att, def, angleAdv, cover, defSet), p);

        /// <summary>성공·분기 표본. RNG 정확히 2.</summary>
        public static TakeOnOutcome Resolve(ActionParams p, float prob, SeedRng rng)
        {
            var u1 = rng.NextFloat();
            var u2 = rng.NextFloat();
            if (u1 < prob) return TakeOnOutcome.Beat;
            return Branch(p, u2);
        }

        /// <summary>FR-06 실패 분기 (합으로 정규화): 탈취 60 · 아웃 15 · 파울 획득 10 · 루즈볼 15.</summary>
        public static TakeOnOutcome Branch(ActionParams p, float u)
        {
            var sum = p.TakeOnSteal + p.TakeOnOut + p.TakeOnFoul + p.TakeOnLoose;
            if (!(sum > 0f)) return TakeOnOutcome.Steal;
            var x = u * sum;
            if (x < p.TakeOnSteal) return TakeOnOutcome.Steal;
            x -= p.TakeOnSteal;
            if (x < p.TakeOnOut) return TakeOnOutcome.Out;
            x -= p.TakeOnOut;
            if (x < p.TakeOnFoul) return TakeOnOutcome.FoulWon;
            return TakeOnOutcome.Loose;
        }
    }
}
