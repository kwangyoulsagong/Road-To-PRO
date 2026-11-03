using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 조향 행동 → 목표 속도 (specs/005 FR-20~26). 모두 순수 함수, 할당 없음.
    /// 대상 선수 위치·속도는 서브스텝 시작 스냅숏을 받는다 (처리 순서 무관).
    /// </summary>
    public static class Steering
    {
        const float Eps = 1e-4f;

        public static Vec2 Seek(Vec2 pos, Vec2 target, float maxSpeed)
        {
            var e = target - pos;
            var d = e.Length();
            if (d < Eps) return Vec2.Zero; // d = 0 → 정지 (0으로 나누기 금지)
            return e * (maxSpeed / d);
        }

        /// <summary>Arrive: v_des = min(Vmax_eff, √(2·a_dec·d)), a_dec = 0.8 × 감속 상한. 허용 반경 안·저속이면 정지 (FR-20).</summary>
        public static Vec2 Arrive(Vec2 pos, Vec2 vel, Vec2 target, float maxSpeed, float maxDecel, KinematicsParams p)
        {
            var e = target - pos;
            var d = e.Length();
            if (d < Eps) return Vec2.Zero;
            if (d <= p.ArriveTolerance && vel.LengthSq() <= p.ArriveStopSpeed * p.ArriveStopSpeed) return Vec2.Zero;
            var aDec = p.ArriveDecelFactor * maxDecel;
            var v = DMath.Sqrt(2f * aDec * d);
            if (v > maxSpeed) v = maxSpeed;
            return e * (v / d);
        }

        /// <summary>사람 추적 예측 시간 T = d/(v_self + v_target·cosθ), 상한 1.5 s (FR-23).</summary>
        public static float PursuitLead(Vec2 pos, Vec2 vel, Vec2 tPos, Vec2 tVel, KinematicsParams p)
        {
            var rel = pos - tPos; // 대상 → 추적자
            var d = rel.Length();
            if (d < Eps) return 0f;
            var tSpeed = tVel.Length();
            var cos = tSpeed > Eps ? Vec2.Dot(tVel, rel) / (tSpeed * d) : 0f;
            var denom = vel.Length() + tSpeed * cos;
            if (denom <= 0.1f) return p.PursueMaxT;
            var t = d / denom;
            return t > p.PursueMaxT ? p.PursueMaxT : t;
        }

        public static Vec2 Pursue(Vec2 pos, Vec2 vel, Vec2 tPos, Vec2 tVel, float maxSpeed, KinematicsParams p)
        {
            var t = PursuitLead(pos, vel, tPos, tVel, p);
            return Seek(pos, Clamp(tPos + tVel * t, p), maxSpeed);
        }

        public static Vec2 Evade(Vec2 pos, Vec2 vel, Vec2 tPos, Vec2 tVel, float maxSpeed, KinematicsParams p)
        {
            var t = PursuitLead(pos, vel, tPos, tVel, p);
            var away = pos - (tPos + tVel * t);
            var d = away.Length();
            if (d < Eps) away = pos - tPos;
            return away.Normalized() * maxSpeed;
        }

        /// <summary>마킹 지점: 상대와 우리 골대를 잇는 선 위, 상대에서 offset (FR-25).</summary>
        public static Vec2 MarkPoint(Vec2 oppPos, Vec2 ownGoal, KinematicsParams p) =>
            oppPos + (ownGoal - oppPos).Normalized() * p.MarkOffset;

        /// <summary>마킹: 상대 속도 앞먹임 + 마킹 지점 위치 오차 보정.</summary>
        public static Vec2 Mark(Vec2 pos, Vec2 oppPos, Vec2 oppVel, Vec2 ownGoal, float maxSpeed, KinematicsParams p)
        {
            var point = Clamp(MarkPoint(oppPos, ownGoal, p), p);
            return ClampLength(oppVel + (point - pos) * p.FollowGain, maxSpeed);
        }

        /// <summary>저킹 지점: 공격수 → 자기 골문 선분 위 jockeyDistance, 약한 발 쪽 측면 오프셋 (FR-26).</summary>
        public static Vec2 JockeyPoint(Vec2 attPos, Vec2 ownGoal, float side, KinematicsParams p)
        {
            var u = (ownGoal - attPos).Normalized();
            var left = new Vec2(-u.Y, u.X);
            return attPos + u * p.JockeyDistance + left * (side * p.JockeySideOffset);
        }

        /// <summary>
        /// 저킹: 공격수 속도 앞먹임 + 지점 보정. 골 방향(후퇴·백페달) 성분 ≤ 4 m/s,
        /// 공격수 쪽(접근) 성분 ≤ max(공격수 속력 × 0.9, 하한) — 리서치 02 §3.4 "접근 시 감속" 해석 (T07 결과).
        /// </summary>
        public static Vec2 Jockey(Vec2 pos, Vec2 attPos, Vec2 attVel, Vec2 ownGoal, float side, float maxSpeed, KinematicsParams p)
        {
            var point = Clamp(JockeyPoint(attPos, ownGoal, side, p), p);
            var v = attVel + (point - pos) * p.FollowGain;
            var u = (ownGoal - attPos).Normalized();
            if (u == Vec2.Zero) return ClampLength(v, maxSpeed);
            var along = Vec2.Dot(v, u); // + 후퇴, − 접근
            var approachCap = DMath.Max(attVel.Length() * p.JockeySpeedRatio, p.JockeyMinApproach);
            var clamped = along > p.JockeyBackpedalMax ? p.JockeyBackpedalMax : (along < -approachCap ? -approachCap : along);
            v += u * (clamped - along);
            return ClampLength(v, maxSpeed);
        }

        /// <summary>저킹 후퇴(자기 골 방향) 성분을 백페달 상한으로 자른다 (합성 후 최종 적용).</summary>
        public static Vec2 LimitBackpedal(Vec2 v, Vec2 attPos, Vec2 ownGoal, KinematicsParams p)
        {
            var u = (ownGoal - attPos).Normalized();
            var along = Vec2.Dot(v, u);
            return along > p.JockeyBackpedalMax ? v + u * (p.JockeyBackpedalMax - along) : v;
        }

        public static Vec2 ClampLength(Vec2 v, float max)
        {
            var sq = v.LengthSq();
            if (sq <= max * max) return v;
            return v * (max / DMath.Sqrt(sq));
        }

        /// <summary>목표점을 피치 경계(+여유) 안으로 (경계 상황: 피치 밖 목표).</summary>
        public static Vec2 Clamp(Vec2 t, KinematicsParams p)
        {
            var x = t.X < p.MinX ? p.MinX : (t.X > p.MaxX ? p.MaxX : t.X);
            var y = t.Y < p.MinY ? p.MinY : (t.Y > p.MaxY ? p.MaxY : t.Y);
            return new Vec2(x, y);
        }
    }
}
