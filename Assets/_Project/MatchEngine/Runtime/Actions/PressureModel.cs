using System;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 압박 강도 (specs/008 FR-02·03, Bekkers — 리서치 02 §4.1 [V]). 순수 함수, RNG 없음, 할당 없음.
    /// T_ij = τ_r + ‖d_j − d_i‖/v_max + τ_β, d_j = r_j + v_j(1초 뒤), d_i = r_i + v_i·τ_r, τ_β = ‖v_i‖·β/π,
    /// p_ij = 1/(1 + exp(−(π/(√3σ))·(T − T_ij))), 팀 P_j = 1 − Π(1 − p_ij). 2 m/s 미만 수비수 0, 터치라인 띠 안 +보정.
    /// 070(드리블)·071(슈팅)도 같은 값을 pressure 입력으로 쓴다.
    /// </summary>
    public static class PressureModel
    {
        const float Sqrt3 = 1.7320508075688772f;

        /// <summary>수비수 i(위치 ri, 속도 vi)가 목표 j(rj, vj)를 압박할 확률 p_ij.</summary>
        public static float Pair(ActionParams p, Vec2 ri, Vec2 vi, Vec2 rj, Vec2 vj)
        {
            var speedSq = vi.X * vi.X + vi.Y * vi.Y;
            if (speedSq < p.PressMinSpeed * p.PressMinSpeed) return 0f;
            var speed = DMath.Sqrt(speedSq);
            var djx = rj.X + vj.X;
            var djy = rj.Y + vj.Y;
            var dix = ri.X + vi.X * p.PressReaction;
            var diy = ri.Y + vi.Y * p.PressReaction;
            // β = 수비수 진행 방향과 (현재 위치 → 목표 1초 뒤) 방향 사이 각
            var tox = djx - ri.X;
            var toy = djy - ri.Y;
            var dot = vi.X * tox + vi.Y * toy;
            var cross = vi.X * toy - vi.Y * tox;
            var beta = (tox == 0f && toy == 0f) ? 0f : DMath.Atan2(DMath.Abs(cross), dot);
            var tauBeta = speed * beta / DMath.Pi;
            var ex = djx - dix;
            var ey = djy - diy;
            var tij = p.PressReaction + DMath.Sqrt(ex * ex + ey * ey) / p.PressVmax + tauBeta;
            var k = DMath.Pi / (Sqrt3 * p.PressSigma);
            return ActionMath.Sigmoid(k * (p.PressT - tij));
        }

        /// <summary>
        /// 목표 j에 대한 팀 압박 P_j (수비수 count명, 위치·속도 배열 앞 count칸). 수비수 0명 → 0(+터치라인 보정).
        /// 결과는 0~1로 자른다. 곱 순서는 배열 순서(슬롯 순) 고정.
        /// </summary>
        public static float Team(ActionParams p, Vec2 rj, Vec2 vj, ReadOnlySpan<Vec2> defPos, ReadOnlySpan<Vec2> defVel, int count, float pitchWidth)
        {
            var prod = 1f;
            for (int i = 0; i < count; i++) prod *= 1f - Pair(p, defPos[i], defVel[i], rj, vj);
            var pj = 1f - prod;
            if (rj.Y < p.PressTouchBand || rj.Y > pitchWidth - p.PressTouchBand) pj += p.PressTouchline;
            return pj < 0f ? 0f : (pj > 1f ? 1f : pj);
        }

        /// <summary>볼 소유자: 선수 목표와 공 목표 중 큰 값 (FR-02).</summary>
        public static float Carrier(ActionParams p, Vec2 carrier, Vec2 carrierVel, Vec2 ball, Vec2 ballVel,
            ReadOnlySpan<Vec2> defPos, ReadOnlySpan<Vec2> defVel, int count, float pitchWidth)
        {
            var a = Team(p, carrier, carrierVel, defPos, defVel, count, pitchWidth);
            var b = Team(p, ball, ballVel, defPos, defVel, count, pitchWidth);
            return a > b ? a : b;
        }
    }
}
