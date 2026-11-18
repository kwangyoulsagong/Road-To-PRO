using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.PitchControl
{
    /// <summary>
    /// Spearman 피치 컨트롤 질의 (specs/007 FR-01~13). 상태 = 스크래치 버퍼뿐(질의는 입력을 바꾸지 않고, 같은 입력 → 같은 출력). RNG 없음, 할당 0.
    /// 적분 (FR-03·04): T_k = T_ball + k·dt, dPC_j = (1 − ΣPC)·P_j(T_k)·λ_j·dt (ΣPC는 이전 스텝 값, 슬롯 순 합산), ΣPC ≥ 1 − tol이면 끝 —
    /// 마지막 스텝은 ΣPC가 정확히 1 − tol에 닿도록 비례로 자른다(FR-04 개정 T06).
    /// P_j(T) = 1 / (1 + exp(−π/√3·(T − τ_j)/σ)). λ_j = (공격 팀 ? λ_att : λ_def·κ_def) × (GK ? gkMul : 1).
    /// 먼 선수 건너뛰기 (FR-04 [제안]): τ 하한 &gt; T + prune·σ면 그 스텝에서 계산하지 않는다(P &lt; 0.5 %). 하한이 정확한 τ보다 크지 않으므로 건너뛴 선수의 τ도 그렇다.
    /// 경기당 1개 (008 ActionSystem이 소유).
    /// </summary>
    public sealed class PitchControlQuery
    {
        const int N = ControlPlayers.N;
        /// <summary>π/√3.</summary>
        const float PiOverSqrt3 = 1.81379936423422f;
        const float LogisticClamp = 30f;
        /// <summary>궤적 질의 끝 코드: 수렴 (0~2는 <see cref="TrajectorySamples"/>).</summary>
        public const byte EndConverged = 3;

        readonly PitchControlParams _p;
        readonly float[] _pc = new float[N], _d = new float[N], _tau = new float[N], _lb = new float[N], _lam = new float[N];
        readonly bool[] _known = new bool[N], _in = new bool[N];
        readonly float[] _tpc = new float[N], _e = new float[N], _tauAt = new float[N], _tauNext = new float[N];
        readonly bool[] _eOn = new bool[N];
        readonly int[] _tauIdx = new int[N], _nextIdx = new int[N], _ord = new int[N];

        public PitchControlQuery(PitchControlParams p) => _p = p ?? PitchControlParams.Defaults;

        public PitchControlParams Params => _p;
        /// <summary>질의 횟수 (텔레메트리·AC-12 "경기 중 그리드 0회"). 결과에 영향 없음.</summary>
        public int PointCount, TrajectoryCount, GridCount;
        /// <summary>도착 시간(TTI) 계산 횟수·궤적 적분 샘플 수 (성능 진단, 결과에 영향 없음).</summary>
        public long ArrivalCount, SampleCount;

        /// <summary>마지막 지점 질의의 선수별 컨트롤 (FR-08).</summary>
        public float PlayerControl(int slot) => _pc[slot];

        public void ResetCounts() { PointCount = 0; TrajectoryCount = 0; GridCount = 0; }

        float Sigma => _p.Sigma < 1e-3f ? 1e-3f : _p.Sigma;
        float Dt => _p.Dt < 1e-3f ? 1e-3f : _p.Dt;

        static float Logistic(float x)
        {
            if (x > LogisticClamp) return 1f;
            if (x < -LogisticClamp) return 0f;
            return 1f / (1f + DMath.Exp(-x));
        }

        float Lambda(ControlPlayers pl, int j, int attacking)
        {
            pl.LambdaOf(_p, j, out var la, out var ld);
            var l = pl.Team[j] == attacking ? la : ld * _p.KappaDef;
            return pl.IsKeeper[j] ? l * _p.GkMul : l;
        }

        /// <summary>FR-01 도착 시간: 원 논문 모드 또는 005 TTI.</summary>
        float Arrival(ControlPlayers pl, int j, float x, float y)
        {
            ArrivalCount++;
            if (pl.PaperArrival)
            {
                var tr = _p.PaperReaction;
                var dx = x - (pl.Pos[j].X + pl.Vel[j].X * tr);
                var dy = y - (pl.Pos[j].Y + pl.Vel[j].Y * tr);
                return tr + DMath.Sqrt(dx * dx + dy * dy) / DMath.Max(_p.PaperVmax, 0.1f);
            }
            return TimeToIntercept.Estimate(in pl.Tti[j], x, y);
        }

        /// <summary>도착 시간 하한 (가지치기): 원 논문 모드는 정확한 값, 엔진 모드는 반응 + 직선 거리/Vm (005 x(t) ≤ Vm·t).</summary>
        float LowerBound(ControlPlayers pl, int j, float x, float y)
        {
            if (pl.PaperArrival) return Arrival(pl, j, x, y);
            ref readonly var k = ref pl.Tti[j];
            var dx = x - k.StartX;
            var dy = y - k.StartY;
            return k.Reaction + DMath.Sqrt(dx * dx + dy * dy) * k.InvVm;
        }

        bool Outside(ControlPlayers pl, Vec2 p) => !(p.X >= 0f && p.X <= pl.PitchL && p.Y >= 0f && p.Y <= pl.PitchW);

        /// <summary>
        /// FR-10 지점 질의: 공이 ballTime(s)에 target에 도착할 때 팀별 컨트롤. attacking = 공격(공 소유) 팀 0/1.
        /// 피치 밖 지점은 전부 미통제 [제안]. prune = false면 먼 선수 건너뛰기 없이 전체 적분(AC-12 비교용).
        /// </summary>
        public PointControl Point(ControlPlayers pl, int attacking, Vec2 target, float ballTime, bool prune = true, int exclude = -1)
        {
            PointCount++;
            var r = default(PointControl);
            r.Leader = -1;
            System.Array.Clear(_pc, 0, N);
            if (Outside(pl, target) || !(ballTime >= 0f)) { r.None = 1f; return r; }
            var sigma = Sigma;
            var c = PiOverSqrt3 / sigma;
            var dt = Dt;
            var slack = _p.Prune * sigma;
            var steps = (int)(DMath.Max(_p.MaxT, dt) / dt + 0.5f);
            var minLb = float.MaxValue;
            var any = false;
            for (int j = 0; j < N; j++)
            {
                _in[j] = pl.Active[j] && j != exclude;
                if (!_in[j]) continue;
                any = true;
                _lam[j] = Lambda(pl, j, attacking);
                if (prune)
                {
                    _lb[j] = LowerBound(pl, j, target.X, target.Y);
                    _known[j] = false;
                }
                else
                {
                    _tau[j] = Arrival(pl, j, target.X, target.Y);
                    _lb[j] = _tau[j];
                    _known[j] = true;
                }
                if (_lb[j] < minLb) minLb = _lb[j];
            }
            if (!any) { r.None = 1f; return r; }

            var k = 0;
            if (prune)
            {
                // 모든 선수가 건너뛰어지는 앞 구간은 계산해도 0 — 첫 후보 스텝 바로 앞으로 뛴다(결과 동일)
                var k0 = (int)((minLb - slack - ballTime) / dt);
                if (k0 > 1) k = k0 - 1;
                if (k > steps) k = steps;
            }
            // 들어온 선수 목록 (성능, T16): 하한 오름차순(동률 = 낮은 슬롯)으로 줄 세우고, 하한 ≤ T + prune·σ가 되는 순간 목록에 넣는다(시간이 흐르면 계속 안에 있다).
            // 스텝마다 들어온 선수만 돈다. 합산 순서 = 목록 순서(입력 데이터로만 정해져 결정론적).
            var m = 0;
            for (int j = 0; j < N; j++)
            {
                if (!_in[j]) continue;
                var a = m++;
                while (a > 0 && _lb[_ord[a - 1]] > _lb[j]) { _ord[a] = _ord[a - 1]; a--; }
                _ord[a] = j;
                _eOn[j] = false;
            }
            var entered = prune ? 0 : m;
            var total = 0f;
            var cap = 1f - _p.Tol;
            // 로지스틱 점화식: e_j(T_k) = exp(−c·(T_k − τ_j)) = e_j(T_{k−1})·exp(−c·dt) — 선수마다 exp는 처음 쓸 때 1번 (성능, T16)
            var decay = DMath.Exp(-c * dt);
            while (k < steps)
            {
                k++;
                var t = ballTime + k * dt;
                var lim = t + slack;
                while (entered < m && _lb[_ord[entered]] <= lim) entered++;
                var rem = 1f - total;
                var sum = 0f;
                for (int a = 0; a < entered; a++)
                {
                    var j = _ord[a];
                    _d[j] = 0f;
                    if (prune)
                    {
                        if (!_known[j]) { _tau[j] = Arrival(pl, j, target.X, target.Y); _known[j] = true; }
                        if (_tau[j] > lim) continue;
                    }
                    float e;
                    if (_eOn[j]) e = _e[j] *= decay;
                    else
                    {
                        var x = -c * (t - _tau[j]);
                        if (x > 80f) continue; // P ≈ 0 (exp 오버플로 전) — 점화식을 켜지 않고 다음 스텝에 다시 (비교 모드 prune = false에서만 생김)
                        e = _e[j] = DMath.Exp(x);
                        _eOn[j] = true;
                    }
                    var d = rem * (1f / (1f + e)) * _lam[j] * dt;
                    _d[j] = d;
                    sum += d;
                }
                // 마지막 스텝은 ΣPC = 1 − tol에 닿도록 비례 축소 (FR-04 개정 T06)
                if (sum > 0f && total + sum > cap)
                {
                    var f = (cap - total) / sum;
                    if (f < 0f) f = 0f;
                    for (int a = 0; a < entered; a++) _d[_ord[a]] *= f;
                }
                total = 0f;
                for (int a = 0; a < entered; a++)
                {
                    var j = _ord[a];
                    _pc[j] += _d[j];
                    total += _pc[j];
                }
                if (total >= cap) break;
            }
            r.Elapsed = k * dt;
            Sum(pl, attacking, ref r);
            return r;
        }

        /// <summary>
        /// FR-12 궤적 질의: 샘플마다 공 위치에 대해 dPC_j = (1 − ΣPC)·P_j(t_i)·λ_j·dt (리서치 02 §1.1 절차 2~3). 공격 팀 = 패서 팀, 패서는 수신 대상에서 뺀다.
        /// FR-07: 공 높이 &gt; airZ면 필드 선수, &gt; gkHandZ면 GK도 그 샘플에서 0. 끝: 수렴 → 그대로, 라인 밖 → 남은 확률은 아웃,
        /// 멈춤·상한 → 남은 확률을 끝 지점 지점 질의(패서 제외) 결과로 나누고 그 미통제분은 아웃 [제안].
        /// 가지치기는 지점 질의와 같은 하한을 제곱 비교로(샘플마다 sqrt 없이).
        /// </summary>
        public TrajectoryControl Trajectory(ControlPlayers pl, TrajectorySamples tr, int passer, int receiver, bool prune = true)
            => Trajectory(pl, tr, passer, receiver, prune, _p.TtiStrideCount);

        /// <summary>stride = 정확한 TTI 간격(샘플, 1 = 보간 없음 — 비교용).</summary>
        public TrajectoryControl Trajectory(ControlPlayers pl, TrajectorySamples tr, int passer, int receiver, bool prune, int stride)
        {
            TrajectoryCount++;
            var r = default(TrajectoryControl);
            System.Array.Clear(_pc, 0, N);
            var attacking = passer >= 0 && passer < N ? pl.Team[passer] : 0;
            var sigma = Sigma;
            var c = PiOverSqrt3 / sigma;
            var dt = tr.Dt;
            var slack = _p.TrajPrune * sigma; // 궤적은 `pcm.trajPrune`(T21 — 차이는 성능 테스트가 잰다, ≤ 1e-3)
            var cap = 1f - _p.Tol;
            for (int j = 0; j < N; j++)
            {
                _in[j] = pl.Active[j] && j != passer;
                if (_in[j]) _lam[j] = Lambda(pl, j, attacking);
                _tauIdx[j] = -2;
                _nextIdx[j] = -2;
            }
            var total = 0f;
            var converged = false;
            for (int i = 0; i < tr.Count && !converged; i++)
            {
                SampleCount++;
                var t = tr.T[i];
                var x = tr.X[i];
                var y = tr.Y[i];
                var z = tr.Z[i];
                var rem = 1f - total;
                for (int j = 0; j < N; j++)
                {
                    _d[j] = 0f;
                    if (!_in[j]) continue;
                    if (z > (pl.IsKeeper[j] ? _p.GkHandZ : _p.AirZ)) continue;
                    if (prune && Beyond(pl, j, x, y, t + slack)) continue;
                    var tau = TrajectoryTau(pl, tr, j, i, stride);
                    if (prune && tau > t + slack) continue;
                    _d[j] = rem * Logistic(c * (t - tau)) * _lam[j] * dt;
                }
                Accumulate(ref total);
                if (total >= cap) converged = true;
            }
            for (int j = 0; j < N; j++) _tpc[j] = _pc[j];
            var left = 1f - total;
            if (converged) r.End = EndConverged; // 남은 tol은 아웃 (수렴 문턱에서 tol 크기 불연속이 있다 — 성능 테스트 기준 tol + 1e-3)
            else if (tr.End == TrajectorySamples.EndOut || tr.Count == 0) { r.End = TrajectorySamples.EndOut; r.Out = left; left = 0f; }
            else
            {
                r.End = tr.End;
                var pt = Point(pl, attacking, tr.EndPos, tr.EndTime, prune, passer);
                for (int j = 0; j < N; j++) _tpc[j] += left * _pc[j];
                r.Out = left * pt.None;
                PointCount--; // 궤적 질의 안의 끝 지점 질의는 지점 질의 횟수에 넣지 않는다
            }
            for (int j = 0; j < N; j++)
            {
                if (!_in[j] || _tpc[j] <= 0f) continue;
                if (j == receiver) r.Receiver += _tpc[j];
                else if (pl.Team[j] == attacking) r.Teammate += _tpc[j];
                else r.Opponent += _tpc[j];
            }
            // 합 = 1 (수렴분의 남은 tol은 아웃 — "아무도 못 잡음")
            var sum = r.Receiver + r.Teammate + r.Opponent;
            if (sum > 1f) { var inv = 1f / sum; r.Receiver *= inv; r.Teammate *= inv; r.Opponent *= inv; sum = 1f; }
            r.Out = 1f - sum;
            if (r.Out < 0f) r.Out = 0f;
            return r;
        }

        /// <summary>
        /// FR-11 그리드 질의: 셀 중심마다 지점 질의(공 도착 = 공에서 직선 거리 / gridBallSpeed, FR-05 그리드 모드). 지점 질의 횟수에는 넣지 않는다.
        /// 결정 틱 경로에서 부르지 않는다(AC-12: 진단 끈 경기 중 <see cref="GridCount"/> = 0).
        /// </summary>
        public void Grid(ControlPlayers pl, int attacking, Vec2 ball, long tick, PitchControlGrid g)
        {
            GridCount++;
            var cols = _p.GridCols < 1 ? 1 : _p.GridCols;
            var rows = _p.GridRows < 1 ? 1 : _p.GridRows;
            g.Ensure(cols, rows);
            g.Tick = tick;
            g.Attacking = attacking;
            var cw = pl.PitchL / cols;
            var ch = pl.PitchW / rows;
            var speed = DMath.Max(_p.GridBallSpeed, 0.1f);
            var before = PointCount;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var p = new Vec2((c + 0.5f) * cw, (r + 0.5f) * ch);
                    var res = Point(pl, attacking, p, (p - ball).Length() / speed);
                    g.Att[r * cols + c] = res.Att;
                    g.Def[r * cols + c] = res.Def;
                }
            PointCount = before;
        }

        /// <summary>
        /// 궤적 샘플 i에서 선수 j의 도착 시간 (성능, T16): 정확한 TTI는 기준 샘플에서만 계산하고 사이 샘플은 앞뒤 기준값을 선형 보간한다.
        /// 기준 = 직전 정확값 a(샘플 a) → 다음 기준 b = min(a + stride, 마지막 샘플)을 미리 계산. a~b 사이 샘플은 그 사이에 이 선수가 가지치기·높이로
        /// 빠진 샘플이 있어도 a·b로 보간한다(결정론적, 정확도는 성능 테스트가 잰다). 기준이 없거나 b가 이미 지났으면 그 샘플에서 정확값.
        /// stride = 1이면 보간 없음. 공은 샘플 사이 ≤ 1 m를 움직이고 τ는 공 위치에 연속이다(차이는 성능 테스트가 잰다).
        /// </summary>
        float TrajectoryTau(ControlPlayers pl, TrajectorySamples tr, int j, int i, int stride)
        {
            float tau;
            if (_nextIdx[j] == i) tau = _tauNext[j];
            else if (stride > 1 && _tauIdx[j] >= 0 && _tauIdx[j] < i && Far(pl, j, tr.X[i], tr.Y[i]))
            {
                var a = _tauIdx[j];
                var b = a + stride;
                if (b > tr.Count - 1) b = tr.Count - 1;
                if (b > i)
                {
                    if (_nextIdx[j] != b) { _tauNext[j] = Arrival(pl, j, tr.X[b], tr.Y[b]); _nextIdx[j] = b; }
                    return _tauAt[j] + (_tauNext[j] - _tauAt[j]) * ((float)(i - a) / (b - a));
                }
                tau = Arrival(pl, j, tr.X[i], tr.Y[i]);
            }
            else tau = Arrival(pl, j, tr.X[i], tr.Y[i]);
            _tauAt[j] = tau;
            _tauIdx[j] = i;
            return tau;
        }

        /// <summary>보간 허용 거리(`pcm.ttiNear`): 공이 선수(반응 뒤 위치)에서 이보다 멀면 τ가 매끄럽다(가까우면 최근접 통과의 꺾임 — 정확값).</summary>
        bool Far(ControlPlayers pl, int j, float x, float y)
        {
            float sx, sy;
            if (pl.PaperArrival) { sx = pl.Pos[j].X; sy = pl.Pos[j].Y; }
            else { sx = pl.Tti[j].StartX; sy = pl.Tti[j].StartY; }
            var dx = x - sx;
            var dy = y - sy;
            return dx * dx + dy * dy > _p.TtiNear * _p.TtiNear;
        }

        /// <summary>도착 시간 하한 &gt; limit인가 (가지치기). 엔진 모드는 sqrt 없이 제곱 비교.</summary>
        bool Beyond(ControlPlayers pl, int j, float x, float y, float limit)
        {
            if (pl.PaperArrival) return Arrival(pl, j, x, y) > limit;
            ref readonly var k = ref pl.Tti[j];
            var rhs = limit - k.Reaction;
            if (rhs < 0f) return true;
            var dx = x - k.StartX;
            var dy = y - k.StartY;
            var reach = rhs * k.Vm;
            return dx * dx + dy * dy > reach * reach;
        }

        /// <summary>
        /// 스텝 누적: 이번 스텝 합이 1 − tol을 넘기면 dPC를 비례로 줄여 ΣPC = 1 − tol에서 멈춘다(FR-04 개정, T06 — 조기 종료 불연속 제거,
        /// 선수가 다가가면 팀 컨트롤이 수렴 허용치만큼 줄어드는 경우가 없어진다). 그 뒤 합산·정규화(슬롯 순).
        /// </summary>
        void Accumulate(ref float total)
        {
            var s = 0f;
            for (int j = 0; j < N; j++) s += _d[j];
            var cap = 1f - _p.Tol;
            if (s > 0f && total + s > cap)
            {
                var f = (cap - total) / s;
                if (f < 0f) f = 0f;
                for (int j = 0; j < N; j++) _d[j] *= f;
            }
            total = 0f;
            for (int j = 0; j < N; j++)
            {
                _pc[j] += _d[j];
                total += _pc[j];
            }
            if (total >= cap) total = cap; // 비례 축소의 반올림 — 종료 판정이 흔들리지 않게
        }

        void Sum(ControlPlayers pl, int attacking, ref PointControl r)
        {
            var att = 0f;
            var def = 0f;
            var best = 0f;
            for (int j = 0; j < N; j++)
            {
                if (!_in[j]) continue;
                if (pl.Team[j] == attacking) att += _pc[j];
                else def += _pc[j];
                if (_pc[j] > best) { best = _pc[j]; r.Leader = j; }
            }
            if (att > 1f) att = 1f;
            if (def > 1f - att) def = 1f - att;
            r.Att = att;
            r.Def = def;
            var none = 1f - att - def;
            r.None = none < 0f ? 0f : none;
        }
    }
}
