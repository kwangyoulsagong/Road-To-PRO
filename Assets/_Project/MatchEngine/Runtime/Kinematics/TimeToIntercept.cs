using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 선수별 도달 시간 (specs/005 FR-40·41). 007 피치 컨트롤·008 액션·011 GK·010 AI가 같은 함수를 쓴다.
    /// 반응 지연 동안 현재 속도로 진행 → 목표 방향 속도 성분 투영(반대 방향이면 정지 시간 가산) →
    /// 모노-지수 F-V 도달 시간 x(t) = Vm·t − (Vm − u)·τ·(1 − e^{−t/τ}) = d 를 뉴턴 3회로 푼다.
    /// 정지·먼 거리에서 t ≈ t_r + d/Vmax + τ 와 일치한다.
    /// </summary>
    public static class TimeToIntercept
    {
        const int NewtonIterations = 3;

        /// <summary>
        /// 한 선수의 TTI 불변값 (선수·시각마다 한 번 계산). 여러 목표를 잴 때(인터셉트 탐색, 피치 컨트롤) 다시 쓰면 빠르다.
        /// </summary>
        public readonly struct Context
        {
            public readonly float Vm, InvVm, Tau, InvTau, Reaction;
            public readonly float StartX, StartY, VelX, VelY;
            public readonly float MaxDecel, InvTwoDecel, TurnCostPerSpeed;

            public Context(in PlayerMotionProfile prof, Vec2 pos, Vec2 vel, float w, float c, KinematicsParams p)
            {
                var fw = 1f - w;
                Vm = prof.MaxSpeed * (1f - p.VmaxAcute * fw - p.VmaxChronic * c);
                InvVm = 1f / Vm;
                Tau = Vm / (prof.MaxAccel * (1f - p.AccAcute * fw - p.AccChronic * c));
                InvTau = 1f / Tau;
                Reaction = prof.ReactionDelay + p.ReactionAdd * fw;
                VelX = vel.X;
                VelY = vel.Y;
                StartX = pos.X + vel.X * Reaction;
                StartY = pos.Y + vel.Y * Reaction;
                MaxDecel = prof.MaxDecel;
                InvTwoDecel = 0.5f / prof.MaxDecel;
                TurnCostPerSpeed = p.TtiTurnCost / prof.MaxLateralAccel;
            }
        }

        public static float Estimate(in PlayerMotionProfile prof, Vec2 pos, Vec2 vel, float w, float c, KinematicsParams p, Vec2 target)
        {
            // 단건 경로: 컨텍스트 구조체를 만들지 않고 같은 식을 바로 계산 (Context 경로와 결과 동일)
            var fw = 1f - w;
            var vm = prof.MaxSpeed * (1f - p.VmaxAcute * fw - p.VmaxChronic * c);
            var tau = vm / (prof.MaxAccel * (1f - p.AccAcute * fw - p.AccChronic * c));
            var tr = prof.ReactionDelay + p.ReactionAdd * fw;
            var rx = target.X - (pos.X + vel.X * tr);
            var ry = target.Y - (pos.Y + vel.Y * tr);
            var dSq = rx * rx + ry * ry;
            if (dSq < 1e-8f) return tr;
            var d = DMath.Sqrt(dSq);
            var invD = 1f / d;
            var u = (vel.X * rx + vel.Y * ry) * invD;
            var side = (vel.X * ry - vel.Y * rx) * invD;
            if (side < 0f) side = -side;
            var extra = side * (p.TtiTurnCost / prof.MaxLateralAccel);
            if (u < 0f)
            {
                extra -= u / prof.MaxDecel;
                d += u * u * (0.5f / prof.MaxDecel);
                u = 0f;
            }
            if (u > vm) u = vm;
            return tr + extra + RunTime(d, u, vm, 1f / vm, tau, 1f / tau);
        }

        public static float Estimate(in Context k, float targetX, float targetY)
        {
            var rx = targetX - k.StartX;
            var ry = targetY - k.StartY;
            var dSq = rx * rx + ry * ry;
            if (dSq < 1e-8f) return k.Reaction;
            var d = DMath.Sqrt(dSq);
            var invD = 1f / d;
            var u = (k.VelX * rx + k.VelY * ry) * invD;               // 목표 방향 성분
            var side = (k.VelX * ry - k.VelY * rx) * invD;            // 옆 성분
            if (side < 0f) side = -side;
            var extra = side * k.TurnCostPerSpeed;                     // 옆 성분은 횡가속 한계로 지워야 한다 (방향 전환 비용)
            if (u < 0f)
            {
                // 반대 방향: 감속 상한으로 멈추는 시간 + 그동안 더 멀어진 거리 (투영 보정)
                extra -= u / k.MaxDecel;
                d += u * u * k.InvTwoDecel;
                u = 0f;
            }
            if (u > k.Vm) u = k.Vm;
            return k.Reaction + extra + RunTime(d, u, k.Vm, k.InvVm, k.Tau, k.InvTau);
        }

        /// <summary>속력 u에서 출발해 거리 d를 가는 시간 (모노-지수).</summary>
        public static float RunTime(float d, float u, float vm, float tau) => RunTime(d, u, vm, 1f / vm, tau, 1f / tau);

        static float RunTime(float d, float u, float vm, float invVm, float tau, float invTau)
        {
            // 시작점: 점근선 해와 등가속(a0 = (vm − u)/τ) 해 중 큰 쪽 — 둘 다 해보다 작아 볼록 함수 뉴턴이 한 번에 오른쪽으로 넘어가 단조 수렴
            var gap = vm - u;
            var t = (d + gap * tau) * invVm;
            var a0 = gap * invTau;
            if (a0 > 1e-4f)
            {
                var disc = u * u + 2f * a0 * d;
                var tq = (DMath.Sqrt(disc) - u) / a0;
                if (tq > t) t = tq;
            }
            for (int i = 0; i < NewtonIterations; i++)
            {
                var e = DMath.Exp(-t * invTau);
                var x = vm * t - gap * tau * (1f - e);
                var v = vm - gap * e;
                if (v < 1e-3f) v = 1e-3f;
                t -= (x - d) / v;
                if (t < 0f) t = 0f;
            }
            return t;
        }

        /// <summary>
        /// 공 인터셉트 지점 (FR-22): 예측 샘플을 시각 순으로 훑어 처음으로 TTI ≤ t_ball 인 지점. 예측 시간 상한 2.0 s.
        /// maxBallHeight보다 높은 공 샘플은 건너뛴다 (높이 규칙은 008). 못 찾으면 false.
        /// </summary>
        public static bool FindBallIntercept(in PlayerMotionProfile prof, Vec2 pos, Vec2 vel, float w, float c, KinematicsParams p,
            BallPrediction prediction, float maxBallHeight, out Vec2 point, out float ballTime)
        {
            var ctx = new Context(in prof, pos, vel, w, c, p);
            var n = prediction.Count;
            var times = prediction.Times;
            var positions = prediction.Positions;
            for (int i = 0; i < n; i++)
            {
                var t = times[i];
                if (t > p.InterceptMaxT) break;
                var bp = positions[i];
                if (bp.Z > maxBallHeight) continue;
                if (Estimate(in ctx, bp.X, bp.Y) <= t)
                {
                    point = new Vec2(bp.X, bp.Y);
                    ballTime = t;
                    return true;
                }
            }
            point = default;
            ballTime = 0f;
            return false;
        }
    }
}
