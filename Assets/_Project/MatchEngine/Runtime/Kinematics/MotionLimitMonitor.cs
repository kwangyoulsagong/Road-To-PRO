using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    public enum MotionLimitKind : byte
    {
        LongAccel = 0,
        Decel = 1,
        LatAccel = 2,
        FrictionCircle = 3,
        PivotTurnRate = 4,
        Speed = 5,
    }

    /// <summary>"운동 한계 초과" 진단 이벤트 (FR-52).</summary>
    public readonly struct MotionLimitViolation
    {
        public readonly long Substep;
        public readonly int PlayerId;
        public readonly MotionLimitKind Kind;
        public readonly float Value;
        public readonly float Limit;

        public MotionLimitViolation(long substep, int playerId, MotionLimitKind kind, float value, float limit)
        {
            Substep = substep; PlayerId = playerId; Kind = kind; Value = value; Limit = limit;
        }
    }

    /// <summary>
    /// 운동 한계 자가 검사 (specs/005 FR-52, 054 요청). 서브스텝 전·후 속도로 실제 종·횡 가속·감속·회전율을 재고
    /// 프로필 한계(접촉 허용 포함, 수치 여유)를 넘으면 기록한다. 겹침 위치 투영은 속도를 바꾸지 않으므로 대상 아님 (plan [제안] ⑦).
    /// 위반이 하나라도 있으면 검증 실행은 시뮬 쪽 실패다. 버퍼는 생성 시 1회 할당.
    /// </summary>
    public sealed class MotionLimitMonitor
    {
        const float RelTol = 1e-3f;
        /// <summary>m/s² 절대 여유: 속력 9 m/s의 float 해상도(≈1e-6 m/s)를 dt 0.01로 나누면 1e-4 m/s² — 그 수백 배 여유, 실제 위반(수 m/s²)보다 훨씬 작다.</summary>
        const float AbsTol = 0.05f;

        readonly MotionLimitViolation[] _items;
        public int Count { get; private set; }
        /// <summary>버퍼를 넘친 위반 수 (Count에는 포함, 내용은 버림).</summary>
        public long TotalViolations { get; private set; }
        public bool Failed => TotalViolations > 0;

        public MotionLimitMonitor(int capacity = 256) { _items = new MotionLimitViolation[capacity]; }

        public MotionLimitViolation this[int i] => _items[i];

        public void Clear() { Count = 0; TotalViolations = 0; }

        internal void Check(long substep, int playerId, bool pivoting, Vec2 vBefore, Vec2 vAfter, Vec2 facingBefore, Vec2 facingAfter,
            in PlayerMotionProfile prof, float vmaxEff, float a0Eff, float capScale, float dt)
        {
            var sb = vBefore.Length();
            var sa = vAfter.Length();
            var aLong = (sa - sb) / dt;
            var avail = a0Eff * (1f - sb / vmaxEff);
            if (aLong > Tol(avail)) Add(substep, playerId, MotionLimitKind.LongAccel, aLong, avail);
            if (-aLong > Tol(prof.MaxDecel)) Add(substep, playerId, MotionLimitKind.Decel, -aLong, prof.MaxDecel);
            if (sa > Tol(vmaxEff) && sa > sb + AbsTol * dt) Add(substep, playerId, MotionLimitKind.Speed, sa, vmaxEff);

            if (pivoting)
            {
                var turn = DMath.Abs(DMath.Atan2(Vec2.Cross(facingBefore, facingAfter), Vec2.Dot(facingBefore, facingAfter))) / dt;
                if (turn > Tol(prof.PivotTurnRate)) Add(substep, playerId, MotionLimitKind.PivotTurnRate, turn, prof.PivotTurnRate);
            }
            else if (sb > 1e-4f && sa > 1e-4f)
            {
                var ang = DMath.Atan2(Vec2.Cross(vBefore, vAfter), Vec2.Dot(vBefore, vAfter));
                var aLat = DMath.Abs(ang) * sb / dt;
                if (aLat > Tol(prof.MaxLateralAccel)) Add(substep, playerId, MotionLimitKind.LatAccel, aLat, prof.MaxLateralAccel);
                var cap = (a0Eff > prof.MaxDecel ? a0Eff : prof.MaxDecel) * capScale;
                var tot = DMath.Sqrt(aLong * aLong + aLat * aLat);
                if (tot > Tol(cap)) Add(substep, playerId, MotionLimitKind.FrictionCircle, tot, cap);
            }
        }

        static float Tol(float limit) => limit + DMath.Abs(limit) * RelTol + AbsTol;

        void Add(long substep, int id, MotionLimitKind kind, float value, float limit)
        {
            TotalViolations++;
            if (Count < _items.Length) _items[Count++] = new MotionLimitViolation(substep, id, kind, value, limit);
        }
    }
}
