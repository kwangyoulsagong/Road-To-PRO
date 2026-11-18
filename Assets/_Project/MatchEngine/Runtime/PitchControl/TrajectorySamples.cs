using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.PitchControl
{
    /// <summary>
    /// 궤적 질의 입력 (specs/007 FR-05·12): 공 명목 궤적을 dt 간격 (x, y, z, t) 샘플로. t는 킥 순간부터(첫 샘플 = dt).
    /// 땅볼 = 004 구름 표 조회(스텝 시뮬 없음, 008 평가와 같은 공 물리), 뜬공 = 004 <see cref="BallStepper"/> 상태 복사본을 샘플 간격마다.
    /// 끝: 라인 밖(그 샘플 직전까지만), 멈춤, 상한 시간. 인스턴스 버퍼 재사용, 할당 0.
    /// </summary>
    public sealed class TrajectorySamples
    {
        /// <summary>샘플 상한 (기본 horizon 3 s / dt 0.04 = 75). horizon/dt가 이보다 크면 상한에서 끝(End = Horizon) — 결정론 리뷰 L4, 튜닝 범위로 막는다.</summary>
        public const int Capacity = 256;
        public const byte EndHorizon = 0, EndOut = 1, EndRest = 2;

        public readonly float[] X = new float[Capacity], Y = new float[Capacity], Z = new float[Capacity], T = new float[Capacity];
        public int Count;
        /// <summary>끝 종류 (<see cref="EndHorizon"/>·<see cref="EndOut"/>·<see cref="EndRest"/>).</summary>
        public byte End;
        /// <summary>마지막 샘플 위치·시각 (멈춤·상한이면 남은 확률을 여기서 지점 질의로 나눈다).</summary>
        public Vec2 EndPos;
        public float EndTime;
        /// <summary>샘플 간격 (s) — 궤적 질의의 적분 간격.</summary>
        public float Dt;

        readonly BallEventBuffer _ev = new BallEventBuffer(32);

        static bool Inside(float x, float y, float l, float w) => x >= 0f && x <= l && y >= 0f && y <= w;

        int Steps(float dt, float horizon)
        {
            var n = (int)(horizon / dt + 0.5f);
            return n < 1 ? 1 : (n > Capacity ? Capacity : n);
        }

        void Add(float x, float y, float z, float t)
        {
            X[Count] = x; Y[Count] = y; Z[Count] = z; T[Count] = t;
            Count++;
            EndPos = new Vec2(x, y);
            EndTime = t;
        }

        /// <summary>
        /// 땅볼: from에서 aim 방향으로 초속 v0 (004 구름 표 — 조준점을 지나서도 멈출 때까지 구른다). pitchL·W 밖으로 나가는 샘플에서 끝(아웃).
        /// </summary>
        public void Ground(BallParams bp, Vec2 from, Vec2 aim, float v0, float dt, float horizon, float pitchL, float pitchW)
        {
            Reset(dt);
            var d = aim - from;
            var len = d.Length();
            var u = len > 1e-5f ? d / len : new Vec2(1f, 0f);
            var table = bp.RollTable;
            var x0 = table.DistanceAtSpeed(v0);
            var step0 = table.StepAtDistance(x0);
            var rest = table.RestStep - 1;
            var steps = Steps(Dt, horizon);
            var z = bp.Radius;
            for (int i = 1; i <= steps; i++)
            {
                var t = i * Dt;
                var st = step0 + t / bp.Dt;
                var stopped = st >= rest;
                var dist = table.DistanceAtStep(stopped ? rest : st) - x0;
                var p = from + u * dist;
                if (!Inside(p.X, p.Y, pitchL, pitchW)) { End = EndOut; return; }
                Add(p.X, p.Y, z, t);
                if (stopped) { End = EndRest; return; }
            }
            End = EndHorizon;
        }

        /// <summary>임의 공 상태(뜬공 등)를 004 스테퍼 복사본으로 (BoundaryCrossed = 아웃, Rest = 멈춤).</summary>
        public void Step(BallParams bp, in BallState start, float dt, float horizon)
        {
            Reset(dt);
            var s = start;
            var every = (int)(Dt / bp.Dt + 0.5f);
            if (every < 1) every = 1;
            var samples = Steps(Dt, horizon);
            var total = samples * every;
            for (int i = 1; i <= total; i++)
            {
                _ev.Clear();
                BallStepper.Step(ref s, bp, _ev, out _);
                for (int k = 0; k < _ev.Count; k++)
                    if (_ev[k].Kind == BallEventKind.BoundaryCrossed) { End = EndOut; return; }
                var rest = s.Phase == BallPhase.Rest;
                if (i % every != 0 && !rest) continue;
                Add(s.Position.X, s.Position.Y, s.Position.Z, i * bp.Dt);
                if (rest) { End = EndRest; return; }
            }
            End = EndHorizon;
        }

        /// <summary>테스트용: 높이 z, 일정 속도 직선 (from → to, 도착하면 멈춤).</summary>
        public void Line(Vec2 from, Vec2 to, float speed, float z, float dt, float horizon, float pitchL = 105f, float pitchW = 68f)
        {
            Reset(dt);
            var d = to - from;
            var len = d.Length();
            var u = len > 1e-5f ? d / len : new Vec2(1f, 0f);
            var steps = Steps(Dt, horizon);
            for (int i = 1; i <= steps; i++)
            {
                var t = i * Dt;
                var dist = speed * t;
                var stopped = dist >= len;
                var p = stopped ? to : from + u * dist;
                if (!Inside(p.X, p.Y, pitchL, pitchW)) { End = EndOut; return; }
                Add(p.X, p.Y, z, t);
                if (stopped) { End = EndRest; return; }
            }
            End = EndHorizon;
        }

        void Reset(float dt)
        {
            Count = 0;
            End = EndHorizon;
            Dt = dt < 1e-3f ? 1e-3f : dt;
            EndPos = default;
            EndTime = 0f;
        }
    }
}
