using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>수비 라인 이동·일체성·오프사이드 라인 (FR-40~45). 값은 팀 관점 정규화 x.</summary>
    public sealed class DefensiveLine
    {
        readonly ShapeParams _p;
        float _x;
        bool _init, _stepUpCond, _stepUp;

        public DefensiveLine(ShapeParams p) => _p = p;

        public float X => _x;

        public void Reset() => _init = false;

        /// <summary>
        /// 한 틱 진행. baseX는 형태가 낸 라인 위치, threatX는 위협 감지 값(없으면 큰 값).
        /// 수비가 아니면 형태를 그대로 따라가고, 수비면 FR-41(더 보수적인 값) + FR-42 이동식으로 움직인다.
        /// </summary>
        public void Step(float baseX, float threatX, bool defending, in PressSignals s, float offsideTrap, SeedRng rng, float dt)
        {
            if (!_init || !defending)
            {
                _x = baseX;
                _init = true;
                _stepUpCond = _stepUp = false;
                return;
            }

            var cond = s.BackPassReceived || s.PasserPressured;
            if (cond && !_stepUpCond) _stepUp = rng.Bernoulli(Interp.Lerp(_p.TrapStepProbMin, 1f, Interp.Clamp01(offsideTrap)));
            if (!cond) _stepUp = false;
            _stepUpCond = cond;

            var target = baseX;
            if (_stepUp) target += _p.StepUpM / _p.PitchL;
            else if (s.PasserFreeFacingForward) target -= _p.DropM / _p.PitchL;
            if (threatX < target) target = threatX;

            var maxStep = _p.LineVMax * dt / _p.PitchL;
            _x += Interp.Clamp(_p.LineK * (target - _x), -maxStep, maxStep);
        }

        /// <summary>라인 일체성 오차 σ (m, FR-43·44). OffsideTrap 0.5에서 기본식과 같다.</summary>
        public static float CohesionSigma(ShapeParams p, float teamwork, float fatigue, float offsideTrap)
        {
            var baseSigma = p.SigmaTeamwork * (1f - Interp.Clamp01(teamwork)) + p.SigmaFatigue * Interp.Clamp01(fatigue);
            return baseSigma * (1f + p.TrapTolSpread * (1f - 2f * Interp.Clamp01(offsideTrap)));
        }

        /// <summary>오프사이드 라인 (FR-45): 뒤에서 두 번째 선수(GK 포함)와 공 중 자기 골라인에 가까운 쪽, 하프라인 상한.</summary>
        public static float OffsideLine(Vec2[] positions, bool[] active, float ballX)
        {
            float first = float.MaxValue, second = float.MaxValue;
            for (int i = 0; i < positions.Length; i++)
            {
                if (!active[i]) continue;
                var x = positions[i].X;
                if (x < first) { second = first; first = x; }
                else if (x < second) second = x;
            }
            var line = second == float.MaxValue ? (first == float.MaxValue ? 0.5f : first) : second;
            if (ballX < line) line = ballX;
            return line > 0.5f ? 0.5f : line;
        }

        public void Write(StateWriter w) { w.Write(_x); w.Write(_init); w.Write(_stepUpCond); w.Write(_stepUp); }
        public void Read(StateReader r) { _x = r.ReadFloat(); _init = r.ReadBool(); _stepUpCond = r.ReadBool(); _stepUp = r.ReadBool(); }
        public void AddTo(ref StableHasher h) { h.Add(_x); h.Add(_init); h.Add(_stepUpCond); h.Add(_stepUp); }
    }
}
