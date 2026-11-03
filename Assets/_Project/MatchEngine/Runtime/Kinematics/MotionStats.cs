using RoadToPro.Core.Hashing;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 경기 누적 운동 기록 (specs/005 FR-50 → 015). 거리는 double (120분 정밀도, 경계 상황).
    /// </summary>
    public struct MotionStats
    {
        public double Distance;
        public double HighIntensityDistance;
        public int Sprints;
        public int Accelerations;
        public int Decelerations;
        public float TopSpeed;

        bool _inSprint;
        int _accRun, _decRun;

        /// <summary>서브스텝 끝 속력 v와 그 서브스텝 종방향 가속 aLong으로 갱신.</summary>
        public void Update(float v, float aLong, KinematicsParams p)
        {
            var step = (double)v * p.Dt;
            Distance += step;
            if (v >= p.HirSpeed) HighIntensityDistance += step;
            if (v > TopSpeed) TopSpeed = v;

            // 스프린트: 임계 상향 통과 1회 = 1번, 고강도 아래로 내려와야 다시 셈
            if (!_inSprint && v >= p.SprintSpeed) { _inSprint = true; Sprints++; }
            else if (_inSprint && v < p.HirSpeed) _inSprint = false;

            // 가속·감속: 임계 초과가 0.5 s 지속되면 1회 (B-2)
            if (aLong > p.AccThreshold) { if (++_accRun == p.AccMinSubsteps) Accelerations++; }
            else _accRun = 0;
            if (aLong < -p.AccThreshold) { if (++_decRun == p.AccMinSubsteps) Decelerations++; }
            else _decRun = 0;
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(Distance); h.Add(HighIntensityDistance);
            h.Add(Sprints); h.Add(Accelerations); h.Add(Decelerations);
            h.Add(TopSpeed);
            h.Add(_inSprint); h.Add(_accRun); h.Add(_decRun);
        }
    }
}
