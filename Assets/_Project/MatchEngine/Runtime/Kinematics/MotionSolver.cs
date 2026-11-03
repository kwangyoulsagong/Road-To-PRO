using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>한 서브스텝 운동 풀이 결과 (진단·기록용).</summary>
    public struct SolveInfo
    {
        /// <summary>적용한 종방향 가속 (m/s², 음수 = 감속).</summary>
        public float LongAccel;
        /// <summary>적용한 횡가속 (m/s², 피벗 중에는 0).</summary>
        public float LatAccel;
        /// <summary>마찰원 사용률 √(a_t²+a_lat²)/a_cap.</summary>
        public float FrictionUse;
        /// <summary>피벗 중 회전각 (rad, 부호 있음).</summary>
        public float PivotTurn;
    }

    /// <summary>
    /// 목표 속도 → 운동 한계 풀이 → semi-implicit Euler 적분 (specs/005 FR-10~13·21).
    /// 비피벗: Δv를 진행 방향 종·횡으로 나눠 횡 ≤ a_lat,max, 종 ≤ F-V a0_eff(1 − v/Vmax_eff), 감속 ≤ 감속 상한,
    /// 마찰원 a_t² + a_lat² ≤ a_cap² (횡 우선, plan [제안] ① a_cap = max(a0_eff, 감속 상한)·접촉 배율).
    /// 횡가속은 방향 회전으로 적용해 곡선에서 속력이 부풀지 않는다.
    /// 피벗(속력 &lt; 진입, 해제는 &gt; 해제 속도): 회전속도 상한으로 방향을 돌리고 종방향만 가감속.
    /// </summary>
    public static class MotionSolver
    {
        const float Eps = 1e-6f;

        public static void Step(ref PlayerMotionState s, in PlayerMotionProfile prof, Vec2 desired,
            float vmaxEff, float a0Eff, float capScale, KinematicsParams p, out SolveInfo info)
        {
            var dt = p.Dt;
            var vx = s.Velocity.X;
            var vy = s.Velocity.Y;
            var speed = DMath.Sqrt(vx * vx + vy * vy);

            // 피벗 히스테리시스 (경계 상황: 2 m/s 근처 떨림 방지)
            if (s.Pivoting) { if (speed > p.PivotExit) s.Pivoting = false; }
            else if (speed < p.PivotEnter) s.Pivoting = true;

            var cap = (a0Eff > prof.MaxDecel ? a0Eff : prof.MaxDecel) * capScale;
            info = default;

            if (!s.Pivoting)
            {
                var fx = vx / speed;
                var fy = vy / speed;
                // 왼쪽 법선 n = (−fy, fx)
                var dvx = desired.X - vx;
                var dvy = desired.Y - vy;
                var invDt = 1f / dt;
                var aT = (dvx * fx + dvy * fy) * invDt;
                var aL = (-dvx * fy + dvy * fx) * invDt;

                var avail = a0Eff * (1f - speed / vmaxEff); // Vmax_eff 초과면 음수 → 자연 감속

                // 횡가속 상한. Vmax_eff 초과(피로로 Vmax_eff가 내려간 경우 등)면 그 감속을 마찰원에서 먼저 확보한다 (T13 수정)
                var lat = prof.MaxLateralAccel;
                if (avail < 0f)
                {
                    var must = -avail < prof.MaxDecel ? -avail : prof.MaxDecel;
                    var room = cap * cap - must * must;
                    var latRoom = room > 0f ? DMath.Sqrt(room) : 0f;
                    if (latRoom < lat) lat = latRoom;
                }
                if (aL > lat) aL = lat; else if (aL < -lat) aL = -lat;

                if (aT > avail) aT = avail;
                if (aT < -prof.MaxDecel) aT = -prof.MaxDecel;

                var tot = aT * aT + aL * aL;
                if (tot > cap * cap)
                {
                    var rest = cap * cap - aL * aL;
                    var m = rest > 0f ? DMath.Sqrt(rest) : 0f;
                    if (aT > m) aT = m; else if (aT < -m) aT = -m;
                    tot = aT * aT + aL * aL;
                }
                if (aT < -speed * invDt) aT = -speed * invDt; // 한 스텝에 방향이 뒤집히지 않음

                var newSpeed = speed + aT * dt;
                var th = aL * dt / speed; // 회전각 근사: 방향 = normalize(f + n·θ)
                var nx = fx - fy * th;
                var ny = fy + fx * th;
                var inv = 1f / DMath.Sqrt(nx * nx + ny * ny);
                nx *= inv; ny *= inv;

                s.Facing = new Vec2(nx, ny);
                s.Velocity = new Vec2(nx * newSpeed, ny * newSpeed);
                info.LongAccel = aT;
                info.LatAccel = aL;
                info.FrictionUse = DMath.Sqrt(tot) / cap;
            }
            else
            {
                var fx = s.Facing.X;
                var fy = s.Facing.Y;
                var dSq = desired.X * desired.X + desired.Y * desired.Y;
                float targetSpeed = 0f;
                if (dSq > Eps)
                {
                    var dLen = DMath.Sqrt(dSq);
                    var dx = desired.X / dLen;
                    var dy = desired.Y / dLen;
                    var cross = fx * dy - fy * dx;
                    var dot = fx * dx + fy * dy;
                    if (dot >= prof.PivotStepCos)
                    {
                        // 회전 상한 안: 원하는 방향으로 바로 (각도는 진단용으로만 근사)
                        info.PivotTurn = cross;
                        fx = dx; fy = dy;
                    }
                    else
                    {
                        // 상한 각만큼 원하는 쪽으로 회전 (cos·sin은 프로필에 미리 계산)
                        var sn = cross >= 0f ? prof.PivotStepSin : -prof.PivotStepSin;
                        var c = prof.PivotStepCos;
                        var rx = fx * c - fy * sn;
                        var ry = fx * sn + fy * c;
                        var inv = 1f / DMath.Sqrt(rx * rx + ry * ry);
                        fx = rx * inv; fy = ry * inv;
                        info.PivotTurn = cross >= 0f ? prof.PivotTurnRate * dt : -prof.PivotTurnRate * dt;
                    }
                    var align = fx * dx + fy * dy;
                    targetSpeed = align > 0f ? dLen * align : 0f;
                }

                // 피벗 중 속도는 항상 몸 방향 (속력 유지한 채 함께 회전, 회전율 상한이 한계)
                var along = speed;
                var invDt = 1f / dt;
                var aT = (targetSpeed - along) * invDt;
                var avail = a0Eff * (1f - along / vmaxEff);
                if (aT > avail) aT = avail;
                if (aT < -prof.MaxDecel) aT = -prof.MaxDecel;
                if (aT > cap) aT = cap;
                if (aT < -along * invDt) aT = -along * invDt;
                var newSpeed = along + aT * dt;

                s.Facing = new Vec2(fx, fy);
                s.Velocity = new Vec2(fx * newSpeed, fy * newSpeed);
                info.LongAccel = aT;
                info.FrictionUse = DMath.Abs(aT) / cap;
            }

            s.Position = new Vec2(s.Position.X + s.Velocity.X * dt, s.Position.Y + s.Velocity.Y * dt);
        }
    }
}
