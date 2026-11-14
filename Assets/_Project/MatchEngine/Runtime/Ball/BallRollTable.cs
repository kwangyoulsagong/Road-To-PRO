using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 땅볼 역산용 표 (plan "접근"): 한도 속도에서 공을 한 번 굴리며 스텝별 (속도, 누적 거리)를 기록한다.
    /// 구름 ODE는 자율계라 "초속 v0의 정지거리 = 전체 거리 − x(v0)".
    /// <see cref="BallParams"/> 생성 시 한 번 만들어 읽기 전용으로 쓴다.
    /// </summary>
    public sealed class BallRollTable
    {
        readonly float[] _speeds; // 감소 순
        readonly float[] _dist;   // 증가 순
        readonly int _count;

        public float TotalDistance { get; }

        /// <summary>한도 속도에서 정지까지 스텝 수 (정지 전이가 일어나는 스텝 포함).</summary>
        public int RestStep => _count;

        internal BallRollTable(BallParams p)
        {
            var cap = 4096;
            _speeds = new float[cap];
            _dist = new float[cap];
            var v = p.LimitSpeed;
            var x = 0f;
            var n = 0;
            _speeds[n] = v;
            _dist[n] = 0f;
            n++;
            while (n < cap)
            {
                var vn = BallStepper.RollSpeedStep(v, p);
                if (vn < p.ToRestSpeed)
                {
                    if (vn > 0f) x += vn * p.Dt;
                    break;
                }
                v = vn;
                x += v * p.Dt;
                _speeds[n] = v;
                _dist[n] = x;
                n++;
            }
            if (n >= cap)
                throw new RoadToPro.Core.Tuning.TuningLoadException(BallTuningIds.RollA,
                    "한도 속도에서 정지까지 표 4096칸(40.96 s)을 넘는다 — 구름 감속 a·b가 너무 작다 (리뷰 N-1)");
            _count = n;
            TotalDistance = x;
        }

        /// <summary>한도 속도에서 출발해 속도가 v로 떨어질 때까지의 거리 (선형 보간).</summary>
        public float DistanceAtSpeed(float v)
        {
            if (v >= _speeds[0]) return 0f;
            if (v <= _speeds[_count - 1]) return _dist[_count - 1];
            int lo = 0, hi = _count - 1; // speeds[lo] > v >= speeds[hi]
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (_speeds[mid] > v) lo = mid; else hi = mid;
            }
            var f = (_speeds[lo] - v) / (_speeds[lo] - _speeds[hi]);
            return _dist[lo] + (_dist[hi] - _dist[lo]) * f;
        }

        /// <summary>누적 거리 x에 도달하는 스텝 번호 (실수, 선형 보간). 표 i번째 = i 스텝 뒤.</summary>
        public float StepAtDistance(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= _dist[_count - 1]) return _count - 1;
            int lo = 0, hi = _count - 1;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (_dist[mid] <= x) lo = mid; else hi = mid;
            }
            return lo + (x - _dist[lo]) / (_dist[hi] - _dist[lo]);
        }

        /// <summary>누적 거리 x의 스텝 번호와 속도를 이진 탐색 한 번으로 (specs/008 평가 핫 경로). <see cref="StepAtDistance"/>·<see cref="SpeedAtDistance"/>와 같은 값.</summary>
        public void Sample(float x, out float step, out float speed)
        {
            if (x <= 0f) { step = 0f; speed = _speeds[0]; return; }
            if (x >= _dist[_count - 1]) { step = _count - 1; speed = _speeds[_count - 1]; return; }
            int lo = 0, hi = _count - 1;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (_dist[mid] <= x) lo = mid; else hi = mid;
            }
            var f = (x - _dist[lo]) / (_dist[hi] - _dist[lo]);
            step = lo + f;
            speed = _speeds[lo] + (_speeds[hi] - _speeds[lo]) * f;
        }

        /// <summary>스텝 번호(실수) → 누적 거리 (선형 보간, <see cref="StepAtDistance"/>의 역). specs/070 드리블 이격 공 위치.</summary>
        public float DistanceAtStep(float step)
        {
            if (!(step > 0f)) return 0f; // NaN 포함 (결정론 리뷰 L5)
            if (step >= _count - 1) return _dist[_count - 1];
            var lo = (int)step;
            return _dist[lo] + (_dist[lo + 1] - _dist[lo]) * (step - lo);
        }

        /// <summary>누적 거리 x에서의 속도 (선형 보간).</summary>
        public float SpeedAtDistance(float x)
        {
            if (x <= 0f) return _speeds[0];
            if (x >= _dist[_count - 1]) return _speeds[_count - 1];
            int lo = 0, hi = _count - 1; // dist[lo] <= x < dist[hi]
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (_dist[mid] <= x) lo = mid; else hi = mid;
            }
            var f = (x - _dist[lo]) / (_dist[hi] - _dist[lo]);
            return _speeds[lo] + (_speeds[hi] - _speeds[lo]) * f;
        }
    }
}
