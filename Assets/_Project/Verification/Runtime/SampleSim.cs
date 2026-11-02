using System.Collections.Generic;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 하네스 검증용 작은 결정론 시뮬 (specs/000 plan '결정론 · 성능').
    /// 정수 상태 22개 점이 시드 RNG로 움직이고, 100틱마다 누적 상태 해시를 체크포인트로 남긴다.
    /// 실제 경기 시뮬이 아니다 — 체크포인트 수집·비교 경로를 검증하는 데만 쓴다.
    /// </summary>
    public sealed class SampleSim : IDeterminismProbe
    {
        public const int Entities = 22;
        public const int Field = 10_000;

        readonly int[] _x = new int[Entities];
        readonly int[] _y = new int[Entities];
        readonly List<FarmCheckpoint> _checkpoints = new List<FarmCheckpoint>();
        readonly Fnv1a64 _log = new Fnv1a64();
        readonly int _extraRngAtTick;
        ulong _rng;
        long _tick;

        public IReadOnlyList<FarmCheckpoint> Checkpoints => _checkpoints;
        public long Tick => _tick;

        /// <param name="extraRngAtTick">AC-11 셀프테스트: 이 틱에서 RNG를 한 번 더 호출해 결과를 일부러 바꾼다 (-1이면 없음).</param>
        public SampleSim(int seed, int extraRngAtTick = -1)
        {
            _extraRngAtTick = extraRngAtTick;
            ulong s = unchecked((ulong)(long)seed);
            _rng = SplitMix(ref s);
            if (_rng == 0) _rng = 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < Entities; i++)
            {
                _x[i] = (int)(Next() % Field);
                _y[i] = (int)(Next() % Field);
            }
        }

        static ulong SplitMix(ref ulong s)
        {
            unchecked
            {
                ulong z = s += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        // xorshift64*
        ulong Next()
        {
            unchecked
            {
                _rng ^= _rng >> 12;
                _rng ^= _rng << 25;
                _rng ^= _rng >> 27;
                return _rng * 2685821657736338717UL;
            }
        }

        public void Run(int ticks = 1000, int checkpointEvery = 100)
        {
            for (int t = 0; t < ticks; t++) Step(checkpointEvery);
        }

        void Step(int checkpointEvery)
        {
            _tick++;
            if (_tick == _extraRngAtTick) Next();
            for (int i = 0; i < Entities; i++)
            {
                int dx = (int)(Next() % 61) - 30;
                int dy = (int)(Next() % 61) - 30;
                _x[i] = Clamp(_x[i] + dx);
                _y[i] = Clamp(_y[i] + dy);
            }
            _log.Add(_tick);
            for (int i = 0; i < Entities; i++)
            {
                _log.Add(_x[i]);
                _log.Add(_y[i]);
            }
            if (_tick % checkpointEvery == 0)
                _checkpoints.Add(new FarmCheckpoint("t" + _tick, _tick, _log.Hex));
        }

        static int Clamp(int v) => v < 0 ? 0 : v >= Field ? Field - 1 : v;
    }
}
