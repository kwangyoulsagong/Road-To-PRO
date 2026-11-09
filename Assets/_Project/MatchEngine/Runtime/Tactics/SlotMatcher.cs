namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>
    /// 헝가리안 최소 비용 배정 (FR-51). 행 수 ≤ 열 수. 비용은 평면 배열 cost[r * MaxSize + c].
    /// 비교는 strict(&lt;)이고 행·열을 오름차순으로 훑는다 — 전부 동점이면 행 i가 열 i를 받는다(슬롯 id 순).
    /// 사전 할당, 틱 경로 할당 0.
    /// </summary>
    public sealed class SlotMatcher
    {
        public const int MaxSize = 11;

        readonly float[] _u = new float[MaxSize + 2], _v = new float[MaxSize + 2], _minv = new float[MaxSize + 2];
        readonly int[] _p = new int[MaxSize + 2], _way = new int[MaxSize + 2];
        readonly bool[] _used = new bool[MaxSize + 2];

        public void Solve(int rows, int cols, float[] cost, int[] rowToCol)
        {
            System.Array.Clear(_u, 0, _u.Length);
            System.Array.Clear(_v, 0, _v.Length);
            System.Array.Clear(_p, 0, _p.Length);
            for (int r = 1; r <= rows; r++)
            {
                _p[0] = r;
                int j0 = 0;
                for (int j = 0; j <= cols; j++) { _minv[j] = float.PositiveInfinity; _used[j] = false; }
                do
                {
                    _used[j0] = true;
                    int i0 = _p[j0], j1 = 0;
                    var delta = float.PositiveInfinity;
                    for (int j = 1; j <= cols; j++)
                    {
                        if (_used[j]) continue;
                        var cur = cost[(i0 - 1) * MaxSize + (j - 1)] - _u[i0] - _v[j];
                        if (cur < _minv[j]) { _minv[j] = cur; _way[j] = j0; }
                        if (_minv[j] < delta) { delta = _minv[j]; j1 = j; }
                    }
                    if (j1 == 0) throw new System.ArithmeticException("슬롯 매칭 비용이 유한하지 않다"); // NaN이면 무한 루프 대신 그 경기만 실패 (006 FR-72)
                    for (int j = 0; j <= cols; j++)
                    {
                        if (_used[j]) { _u[_p[j]] += delta; _v[j] -= delta; }
                        else _minv[j] -= delta;
                    }
                    j0 = j1;
                } while (_p[j0] != 0);
                do
                {
                    var j1 = _way[j0];
                    _p[j0] = _p[j1];
                    j0 = j1;
                } while (j0 != 0);
            }
            for (int j = 1; j <= cols; j++)
                if (_p[j] != 0) rowToCol[_p[j] - 1] = j - 1;
        }
    }
}
