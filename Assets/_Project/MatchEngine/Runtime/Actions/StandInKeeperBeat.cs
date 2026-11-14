using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// P_beatGK 대체 근사 (specs/071 FR-13 "011 FR-14 자리에는 거리·각도 기반 근사", 플랜 2):
    /// t_ball = 거리 / 속도, t_gk = 반응 + max(0, |셀 − GK 손 기준점| − 제자리 도달) / 다이빙 속도, P = sigmoid(k·(t_gk − t_ball)).
    /// GK 손 기준점은 GK 좌우 위치·높이 1 m. GK가 없으면 1. RNG 없음.
    /// </summary>
    public sealed class StandInKeeperBeat : IShotKeeperModel
    {
        public const float HandHeight = 1.0f;
        readonly ActionParams _p;

        public StandInKeeperBeat(ActionParams p) { _p = p; }

        public float BeatProbability(float distToGoal, float cellY, float cellZ, float speed, float gkY)
        {
            if (!(gkY == gkY)) return 1f;
            var tBall = distToGoal / (speed > 0.1f ? speed : 0.1f);
            var dy = cellY - gkY;
            var dz = cellZ - HandHeight;
            var gap = DMath.Sqrt(dy * dy + dz * dz) - _p.AimGkReach;
            if (gap < 0f) gap = 0f;
            var tGk = _p.AimGkReact + gap / (_p.AimGkDiveSpeed > 0.1f ? _p.AimGkDiveSpeed : 0.1f);
            return ActionMath.Sigmoid(_p.AimBeatK * (tGk - tBall));
        }
    }
}
