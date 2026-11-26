using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Presentation.Stadium.Net
{
    /// <summary>
    /// 골망 그물 시뮬 (specs/059 FR-20~26, 2026-11-26 "ㄱㄱ" — docs/goal-net-research.html 권장안). 순수 C#, 단일 스레드, 고정 순서 → 같은 빌드·같은 입력이면 같은 결과.
    /// <list type="bullet">
    /// <item>노드: <see cref="NetRestShape"/> 면 격자 정점을 위치로 합친 한 장(뒤·옆·위 모서리 공유). 렌더 정점 → 노드 <see cref="VertexToNode"/>.</item>
    /// <item>줄: 가로·세로 이웃만(골망은 대각선 줄이 없다). 밧줄 제약 — 기준 길이(기하 간격 × 처짐)보다 늘어날 때만 당긴다. 처짐이 깊이를 만든다.</item>
    /// <item>고정: 앞 모서리(포스트·크로스바), 바닥 모서리, 뒤 위 두 귀퉁이(지지대 끝). 나머지 모서리는 자유 — 한 장으로 출렁임.</item>
    /// <item>정지 그물 상자 안쪽 금지(FR-02): 뒤 면 노드 깊이 ≥ D, 옆 면 |폭| ≥ 골문 반폭, 위 면 높이 ≥ 골문 높이 — 그물은 바깥쪽으로만 밀린다.</item>
    /// <item>LRA: 가장 가까운 고정점까지 거리 ≤ 정지 거리 × lra (적은 반복으로 늘어남 억제, Kim·Chentanez·Müller 2012).</item>
    /// <item>공: 반지름 ballReach 구가 노드를 면 바깥 법선 방향으로만 민다(공보다 커서 칸 사이로 공이 비치지 않는다, FR-22; 한쪽 방향이라 빠른 공이 노드를 앞질러도 뚫지 않음). 그물은 공을 되밀지 않는다(041 FR-02).</item>
    /// <item>Verlet + 중력 + 바깥 기울기(hangBias, 지지대가 그물을 바깥으로 거는 것) + 속도 감쇠. 정지 모양 = 생성 때 settleSeconds 늘어뜨린 모양.</item>
    /// </list>
    /// 좌표: 골대 로컬(X = 골라인 뒤 깊이, Y = 높이, Z = 폭).
    /// </summary>
    public sealed class NetCloth
    {
        public const float StepHz = 240f;
        const float Gravity = 9.81f;
        const float Eps = 1e-4f;

        readonly NetRippleTuning _t;
        public readonly NetRestShape Shape;
        public readonly int[] VertexToNode;
        public readonly int NodeCount;
        readonly Vec3[] _x, _prev, _rest, _bias, _normal;
        readonly float[] _w;          // 역질량: 자유 1, 고정 0
        readonly byte[] _faces;        // 노드가 속한 면 비트 (1 << NetFace)
        readonly int[] _ca, _cb;
        readonly float[] _len;
        readonly int[] _lraPin;
        readonly float[] _lraLen;
        readonly float _half, _height, _depth;
        bool _settled;

        public NetRippleTuning Tuning => _t;
        public Vec3[] Positions => _x;
        /// <summary>정지 모양(늘어뜨린 뒤) 노드 위치.</summary>
        public Vec3[] Rest => _rest;
        public bool IsPinned(int node) => _w[node] == 0f;
        public byte FacesOf(int node) => _faces[node];

        public NetCloth(NetRippleTuning t)
        {
            _t = t ?? throw new ArgumentNullException(nameof(t));
            var s = t.Shape;
            Shape = NetRestShape.Build(s);
            _half = s.HalfMouth;
            _height = s.Height;
            _depth = s.Depth;

            // 1) 렌더 정점 → 노드 (위치로 합침, 정점 순서대로 번호)
            int n = Shape.Count;
            VertexToNode = new int[n];
            var key = new Dictionary<long, int>(n);
            var pos = new List<Vec3>(n);
            var faces = new List<byte>(n);
            for (int i = 0; i < n; i++)
            {
                var p = Shape.Positions[i];
                long k = Key(p);
                if (!key.TryGetValue(k, out var node))
                {
                    node = pos.Count;
                    key[k] = node;
                    pos.Add(p);
                    faces.Add(0);
                }
                VertexToNode[i] = node;
                faces[node] = (byte)(faces[node] | (1 << Shape.Face[i]));
            }
            NodeCount = pos.Count;
            _x = pos.ToArray();
            _prev = pos.ToArray();
            _rest = new Vec3[NodeCount];
            _faces = faces.ToArray();
            _w = new float[NodeCount];
            _bias = new Vec3[NodeCount];
            _normal = new Vec3[NodeCount];
            float top = s.Top;
            for (int i = 0; i < NodeCount; i++)
            {
                var p = _x[i];
                // 고정: 앞 모서리(포스트·크로스바)·바닥 모서리·뒤 위 두 귀퉁이(지지대 끝). 뒤 그물 옆·위 모서리는 풀어
                // 뒤·옆·위가 한 장으로 끌려가며 출렁이게 한다(T18d — 모서리를 다 묶으면 옆·위가 움직이지 않음)
                bool backTopCorner = (_faces[i] & (1 << (int)NetFace.Back)) != 0 && (_faces[i] & (1 << (int)NetFace.Top)) != 0
                                     && (_faces[i] & ((1 << (int)NetFace.SideNegative) | (1 << (int)NetFace.SidePositive))) != 0;
                bool pin = p.X < Eps || p.Y < Eps || backTopCorner;
                _w[i] = pin ? 0f : 1f;
                // 바깥 기울기: 속한 면 바깥 법선 합 방향
                var b = default(Vec3);
                for (int f = 0; f < NetRestShape.FaceCount; f++)
                    if ((_faces[i] & (1 << f)) != 0) b = b + NetRestShape.FaceNormal((NetFace)f);
                float bl = Length(b);
                _bias[i] = bl > 0f ? b * (t.HangBias / bl) : default;
                _normal[i] = bl > 0f ? b * (1f / bl) : new Vec3(1f, 0f, 0f);
            }

            // 2) 줄: 면 격자 가로·세로 이웃 (중복 제거, 순회 순서 고정)
            var seen = new HashSet<long>();
            var ca = new List<int>();
            var cb = new List<int>();
            var len = new List<float>();
            for (int f = 0; f < NetRestShape.FaceCount; f++)
            {
                float slack = (NetFace)f == NetFace.Back ? t.SlackBack : t.SlackSide;
                int U = Shape.FaceU[f], V = Shape.FaceV[f], st = Shape.FaceStart[f];
                for (int j = 0; j <= V; j++)
                for (int i = 0; i <= U; i++)
                {
                    int v0 = st + j * (U + 1) + i;
                    if (i < U) AddRope(VertexToNode[v0], VertexToNode[v0 + 1], slack, seen, ca, cb, len);
                    if (j < V) AddRope(VertexToNode[v0], VertexToNode[v0 + U + 1], slack, seen, ca, cb, len);
                }
            }
            _ca = ca.ToArray();
            _cb = cb.ToArray();
            _len = len.ToArray();

            // 3) LRA: 정지 전 모양 기준 가장 가까운 고정점
            _lraPin = new int[NodeCount];
            _lraLen = new float[NodeCount];
            for (int i = 0; i < NodeCount; i++)
            {
                _lraPin[i] = -1;
                if (_w[i] == 0f) continue;
                float best = float.MaxValue;
                for (int k = 0; k < NodeCount; k++)
                {
                    if (_w[k] != 0f) continue;
                    float d = Length(_x[i] - _x[k]);
                    if (d < best) { best = d; _lraPin[i] = k; }
                }
                _lraLen[i] = best * t.Lra;
            }

            // 4) 정지 모양: 공 없이 늘어뜨림
            int settle = (int)Math.Round(t.SettleSeconds * StepHz);
            for (int k = 0; k < settle; k++) Step(false, default);
            Array.Copy(_x, _rest, NodeCount);
            _settled = true;
            Reset();
        }

        void AddRope(int a, int b, float slack, HashSet<long> seen, List<int> ca, List<int> cb, List<float> len)
        {
            if (a == b) return;
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            long k = ((long)lo << 32) | (uint)hi;
            if (!seen.Add(k)) return;
            ca.Add(lo);
            cb.Add(hi);
            len.Add(Length(_x[lo] - _x[hi]) * slack);
        }

        static long Key(Vec3 p) =>
            ((long)Math.Round(p.X * 1e4) * 73856093L) ^ ((long)Math.Round(p.Y * 1e4) * 19349663L) ^ ((long)Math.Round(p.Z * 1e4) * 83492791L);

        /// <summary>정지 모양·속도 0으로.</summary>
        public void Reset()
        {
            Array.Copy(_rest, _x, NodeCount);
            Array.Copy(_rest, _prev, NodeCount);
        }

        /// <summary>1/240 s 한 스텝. ball = 그 시각 공 중심(골대 로컬).</summary>
        public void Step(bool hasBall, Vec3 ball)
        {
            float dt = 1f / StepHz, keep = 1f - _t.Damping * dt;
            if (keep < 0f) keep = 0f;
            var g = new Vec3(0f, -Gravity, 0f);
            float pull = _settled ? _t.RestPull : 0f;
            for (int i = 0; i < NodeCount; i++)
            {
                if (_w[i] == 0f) continue;
                var v = (_x[i] - _prev[i]) * keep;
                _prev[i] = _x[i];
                // 정지 모양으로 약하게 당김 (FR-23): 처진 밧줄 그물은 흔들린 뒤 다른 모양으로 멈출 수 있어 정지 모양으로 돌아오게 한다
                var a = g + _bias[i] + (_rest[i] - _x[i]) * pull;
                _x[i] = _x[i] + v + a * (dt * dt);
            }
            int iters = _t.Iterations;
            float reach = _t.BallReach;
            for (int it = 0; it < iters; it++)
            {
                // 밧줄: 늘어날 때만
                for (int c = 0; c < _ca.Length; c++)
                {
                    int a = _ca[c], b = _cb[c];
                    float wa = _w[a], wb = _w[b], ws = wa + wb;
                    if (ws == 0f) continue;
                    var d = _x[b] - _x[a];
                    float l = Length(d);
                    if (l <= _len[c] || l < 1e-9f) continue;
                    var corr = d * ((l - _len[c]) / (l * ws));
                    _x[a] = _x[a] + corr * wa;
                    _x[b] = _x[b] - corr * wb;
                }
                for (int i = 0; i < NodeCount; i++)
                {
                    if (_w[i] == 0f) continue;
                    // LRA
                    int pin = _lraPin[i];
                    if (pin >= 0)
                    {
                        var d = _x[i] - _x[pin];
                        float l = Length(d);
                        if (l > _lraLen[i]) _x[i] = _x[pin] + d * (_lraLen[i] / l);
                    }
                    // 공: 한쪽 방향(면 바깥 법선)으로만 민다 — 공은 늘 그물 안쪽이라, 공이 한 스텝에 노드를 앞질러도
                    // 노드가 공 뒤(골 안쪽)로 밀려 그물을 뚫지 않는다 (T18d)
                    if (hasBall)
                    {
                        var n = _normal[i];
                        var d = _x[i] - ball;
                        float along = d.X * n.X + d.Y * n.Y + d.Z * n.Z;
                        float lat2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z - along * along;
                        float r2 = reach * reach;
                        if (lat2 < r2)
                        {
                            float need = (float)Math.Sqrt(r2 - lat2);
                            if (along < need) _x[i] = _x[i] + n * (need - along);
                        }
                    }
                    // 정지 그물 상자 안쪽 금지 (FR-02) · 바닥
                    var p = _x[i];
                    byte f = _faces[i];
                    float x = p.X, y = p.Y, z = p.Z;
                    if ((f & (1 << (int)NetFace.Back)) != 0 && x < _depth) x = _depth;
                    if ((f & (1 << (int)NetFace.Top)) != 0 && y < _height) y = _height;
                    if ((f & (1 << (int)NetFace.SideNegative)) != 0 && z > -_half) z = -_half;
                    if ((f & (1 << (int)NetFace.SidePositive)) != 0 && z < _half) z = _half;
                    if (y < 0f) y = 0f;
                    _x[i] = new Vec3(x, y, z);
                }
            }
        }

        static float Length(Vec3 a) => (float)Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
    }
}
