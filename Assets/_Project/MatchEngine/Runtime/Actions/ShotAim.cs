using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 조준 셀 선택 (specs/071 FR-13): 골문 7×3 격자(011 <see cref="GoalGrid"/>와 같은 셀 번호·중심)에서 P_onTarget × P_beatGK가 최대인 셀.
    /// P_onTarget = 셀 중심을 겨눈 슛이 σ_aim 각 오차로 골문 안(가로 ±3.66 m, 높이 0~2.44 m — 낮은 쪽은 바운드라 하한 없음)에 들어갈 확률(정규 CDF 곱).
    /// 같은 값이면 낮은 셀 번호. 전부 0이면 기본 선호(GK에서 먼 쪽 하단 코너). RNG 없음, 할당 0.
    /// </summary>
    public sealed class ShotAim
    {
        public const int Cells = GoalGrid.Cells;
        readonly float[] _y = new float[Cells], _z = new float[Cells];
        /// <summary>마지막 선택의 셀별 점수 (테스트·진단, 상태 아님).</summary>
        public readonly float[] Score = new float[Cells];

        public ShotAim()
        {
            for (int r = 0; r < GoalGrid.Rows; r++)
                for (int c = 0; c < GoalGrid.Cols; c++)
                {
                    var i = GoalGrid.Cell(r, c);
                    _y[i] = -BallParams.GoalWidth * 0.5f + (c + 0.5f) * BallParams.GoalWidth / GoalGrid.Cols;
                    _z[i] = (r + 0.5f) * BallParams.GoalHeight / GoalGrid.Rows;
                }
        }

        public float OffsetY(int cell) => _y[cell];
        public float Height(int cell) => _z[cell];

        /// <summary>
        /// P_onTarget: 출발점–셀 중심 거리 dist, 진입각 보정 cosIn(골라인 법선과 슛 방향 사이 코사인), σ(°).
        /// 가로 σ_y = dist·tan σ / cosIn, 세로 σ_z = dist·tan σ.
        /// </summary>
        public float OnTarget(int cell, float dist, float cosIn, float sigmaDeg)
        {
            var half = BallParams.GoalWidth * 0.5f;
            var tan = sigmaDeg * ActionMath.DegToRad; // 작은 각: tan ≈ 각 (σ ≤ 수십 도에서 충분)
            var sz = dist * tan;
            if (sz < 1e-4f) return 1f;
            var ci = cosIn < 0.2f ? 0.2f : cosIn;
            var sy = sz / ci;
            var y = _y[cell];
            var z = _z[cell];
            var py = ActionMath.NormalCdf((half - y) / sy) - ActionMath.NormalCdf((-half - y) / sy);
            var pz = ActionMath.NormalCdf((BallParams.GoalHeight - z) / sz);
            var v = py * pz;
            return v < 0f ? 0f : v;
        }

        /// <summary>
        /// 최대 셀 선택. from = 공(경기장 좌표 xy), goal = 골라인 중앙, dirIn = 슛이 골문으로 들어가는 x 방향 부호(+1: x 증가),
        /// gkY = GK 좌우 오프셋(골문 중앙 기준, NaN = GK 없음).
        /// </summary>
        public int Choose(Vec2 from, Vec2 goal, float speed, float sigmaDeg, float gkY, IShotKeeperModel keeper)
        {
            var best = -1;
            var bestV = 0f;
            for (int i = 0; i < Cells; i++)
            {
                var tgt = new Vec2(goal.X, goal.Y + _y[i]);
                var d = tgt - from;
                var dist = d.Length();
                var cosIn = dist > 1e-4f ? DMath.Abs(d.X) / dist : 1f;
                var on = OnTarget(i, dist, cosIn, sigmaDeg);
                var beat = keeper != null ? keeper.BeatProbability(dist, _y[i], _z[i], speed, gkY) : 1f;
                var v = on * beat;
                Score[i] = v;
                if (v > bestV) { bestV = v; best = i; }
            }
            return best >= 0 ? best : DefaultCell(gkY);
        }

        /// <summary>기본 선호: GK에서 먼 하단 코너 (GK 없거나 정중앙이면 y가 큰 쪽 — 규칙 고정).</summary>
        public static int DefaultCell(float gkY) => GoalGrid.Cell(0, gkY == gkY && gkY > 0f ? 0 : GoalGrid.Cols - 1);
    }
}
