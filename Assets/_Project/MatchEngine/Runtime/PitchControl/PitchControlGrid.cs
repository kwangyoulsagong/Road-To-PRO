namespace RoadToPro.MatchEngine.PitchControl
{
    /// <summary>
    /// FR-11 그리드 질의 결과 (32×20 기본, 행 우선: index = row·Cols + col, 셀 중심 = ((col + 0.5)·L/Cols, (row + 0.5)·W/Rows)).
    /// 시뮬 루프(결정 틱) 경로에서는 쓰지 않는다 — 디버그(040)·키 모먼트(019)·경기 후 분석. Tick = 계산한 틱(유효성 판단용).
    /// 버퍼는 크기가 바뀔 때만 다시 만든다.
    /// </summary>
    public sealed class PitchControlGrid
    {
        public int Cols, Rows;
        public float[] Att = System.Array.Empty<float>(), Def = System.Array.Empty<float>();
        public long Tick = -1;
        public int Attacking;

        public void Ensure(int cols, int rows)
        {
            if (cols == Cols && rows == Rows && Att.Length == cols * rows) return;
            Cols = cols;
            Rows = rows;
            Att = new float[cols * rows];
            Def = new float[cols * rows];
        }
    }
}
