using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>
    /// 골문 7×3 격자 (specs/011 FR-02·14, 071 FR-13과 같은 격자). 셀 인덱스 = 행 × 7 + 열, 행 0 = 낮음, 열 0 = 골문 y가 작은 쪽.
    /// 셀 중심은 골문 중앙 기준 좌우 오프셋(m)과 높이(m). 사전분포 w_t는 행 가중 × 열 가중을 합 1로 정규화.
    /// </summary>
    public sealed class GoalGrid
    {
        public const int Cols = 7, Rows = 3, Cells = Cols * Rows;

        readonly float[] _y = new float[Cells];
        readonly float[] _z = new float[Cells];
        readonly float[] _w = new float[Cells];

        public GoalGrid(GoalkeeperParams p)
        {
            var row = new[] { p.GridRowLow, p.GridRowMid, p.GridRowHigh };
            var col = new[] { p.GridColOuter, p.GridColSecond, p.GridColThird, p.GridColCenter, p.GridColThird, p.GridColSecond, p.GridColOuter };
            float sum = 0f;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    var i = r * Cols + c;
                    _y[i] = -BallParams.GoalWidth * 0.5f + (c + 0.5f) * BallParams.GoalWidth / Cols;
                    _z[i] = (r + 0.5f) * BallParams.GoalHeight / Rows;
                    _w[i] = row[r] * col[c];
                    sum += _w[i];
                }
            for (int i = 0; i < Cells; i++) _w[i] = sum > 0f ? _w[i] / sum : 1f / Cells;
        }

        /// <summary>골문 중앙 기준 좌우 오프셋 (m).</summary>
        public float OffsetY(int cell) => _y[cell];
        public float Height(int cell) => _z[cell];
        public float Prior(int cell) => _w[cell];

        public static int Cell(int row, int col) => row * Cols + col;
    }
}
