using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// Separation (specs/005 FR-24): 반경 1.75 m 이내 쌍의 밀어내기 목표 속도 + 적분 후 위치 투영으로 중심 거리 ≥ 2·몸 반지름.
    /// 쌍 순서는 인덱스(= id 오름차순) i &lt; j 사전순 고정. 같은 위치면 id 순서로 정한 결정론적 방향 (작은 id → −x).
    /// 위치 투영은 <see cref="Push"/>가 모은 후보 쌍(서브스텝 시작 거리 &lt; <see cref="CandidateRadius"/>)만 본다 —
    /// 한 서브스텝 이동(≤ 0.1 m) + 투영 총량(≤ 0.35 m)으로 그보다 먼 쌍은 겹칠 수 없다. 후보도 사전순이라 전체 검사와 결과가 같다.
    /// </summary>
    public sealed class Separation
    {
        const float SamePosSq = 1e-8f;
        /// <summary>서브스텝당 위치 투영 상한 (발산 방지, plan 버그 방지).</summary>
        public const float MaxProjection = 0.2f;
        public const int ProjectionIterations = 4;
        /// <summary>투영 목표 간격 여유 (m).</summary>
        public const float ProjectionSlop = 0.04f;
        /// <summary>선수 1명의 서브스텝당 위치 투영 총량 상한 (m).</summary>
        public const float MaxTotalProjection = 0.35f;

        /// <summary>
        /// 투영 후보 쌍 거리 (튜닝에서 계산, 리뷰 R2): 목표 간격(몸 지름 + 여유) + 두 선수 최대 이동(Vmax 상한 × dt) + 두 선수 투영 총량.
        /// 서브스텝 시작에 이보다 먼 쌍은 그 서브스텝 안에 겹칠 수 없다.
        /// </summary>
        public readonly float CandidateRadius;

        readonly int[] _pi, _pj;
        readonly float[] _x, _y, _ox, _oy;
        int _pairs;

        public Separation(int maxPlayers, KinematicsParams p)
        {
            var maxSpeed = p.VmaxBase + p.VmaxRange; // 매핑 상한 (s = 1)
            CandidateRadius = 2f * p.BodyRadius + ProjectionSlop + 2f * maxSpeed * p.Dt + 2f * MaxTotalProjection + 1e-3f;
            var cap = maxPlayers * (maxPlayers - 1) / 2;
            _pi = new int[cap];
            _pj = new int[cap];
            _x = new float[maxPlayers];
            _y = new float[maxPlayers];
            _ox = new float[maxPlayers];
            _oy = new float[maxPlayers];
        }

        public int CandidatePairs => _pairs;

        /// <summary>밀어내기 목표 속도를 outPush[i]에 쓰고 투영 후보 쌍을 모은다 (n개). pos는 서브스텝 시작 스냅숏.</summary>
        public void Push(Vec2[] pos, int n, Vec2[] outPush, KinematicsParams p)
        {
            // 핫패스: 구조체 배열 대신 float 배열로 (Mono 에디터에서 필드 접근 비용이 크다)
            var xs = _x; var ys = _y; var ox = _ox; var oy = _oy;
            for (int i = 0; i < n; i++) { xs[i] = pos[i].X; ys[i] = pos[i].Y; ox[i] = 0f; oy[i] = 0f; }
            var r = p.SepRadius;
            var rSq = r * r;
            var candSq = CandidateRadius * CandidateRadius;
            var reach = r > CandidateRadius ? r : CandidateRadius; // 빠른 배제 거리
            var contact = 2f * p.BodyRadius;
            var invSpan = 1f / (r - contact);
            var speed = p.SepPushSpeed;
            var pairs = 0;
            for (int i = 0; i < n; i++)
            {
                var xi = xs[i];
                var yi = ys[i];
                for (int j = i + 1; j < n; j++)
                {
                    var dx = xi - xs[j];
                    if (dx >= reach || dx <= -reach) continue;
                    var dy = yi - ys[j];
                    if (dy >= reach || dy <= -reach) continue;
                    var dSq = dx * dx + dy * dy;
                    if (dSq < candSq) { _pi[pairs] = i; _pj[pairs] = j; pairs++; }
                    if (dSq >= rSq) continue;
                    float ux, uy, d;
                    if (dSq < SamePosSq) { ux = -1f; uy = 0f; d = 0f; } // i(작은 id)는 −x, j는 +x
                    else { d = DMath.Sqrt(dSq); ux = dx / d; uy = dy / d; }
                    var k = (r - d) * invSpan;
                    if (k > 1f) k = 1f;
                    var w = k * k * speed;
                    ox[i] += ux * w; oy[i] += uy * w;
                    ox[j] -= ux * w; oy[j] -= uy * w;
                }
            }
            _pairs = pairs;
            var sSq = speed * speed;
            for (int i = 0; i < n; i++)
            {
                var lsq = ox[i] * ox[i] + oy[i] * oy[i];
                if (lsq > sSq) { var k = speed / DMath.Sqrt(lsq); outPush[i] = new Vec2(ox[i] * k, oy[i] * k); }
                else outPush[i] = new Vec2(ox[i], oy[i]);
            }
        }

        /// <summary>
        /// 겹침 위치 투영 (후보 쌍만). 상태 배열의 위치만 고친다 (속도 불변 → 운동 한계 검사 제외, plan [제안] ⑦).
        /// corrections[i]에 선수별 투영량을 더한다.
        /// </summary>
        public void Project(PlayerMotionState[] s, float[] corrections, KinematicsParams p)
        {
            if (_pairs == 0) return;
            // 목표 간격 = 몸 지름 + 여유: 반복 수가 유한해 남는 압축이 0.8 m 아래로 내려가지 않게 (22명 한 점 Arrive 혼잡, T08)
            var contact = 2f * p.BodyRadius + ProjectionSlop;
            var cSq = contact * contact;
            for (int it = 0; it < ProjectionIterations; it++)
            {
                var any = false;
                for (int k = 0; k < _pairs; k++)
                {
                    var i = _pi[k];
                    var j = _pj[k];
                    var a = s[i].Position;
                    var b = s[j].Position;
                    var dx = a.X - b.X;
                    var dy = a.Y - b.Y;
                    var dSq = dx * dx + dy * dy;
                    if (dSq >= cSq) continue;
                    float ux, uy, d;
                    if (dSq < SamePosSq) { ux = -1f; uy = 0f; d = 0f; }
                    else { d = DMath.Sqrt(dSq); ux = dx / d; uy = dy / d; }
                    var half = 0.5f * (contact - d);
                    if (half > MaxProjection) half = MaxProjection;
                    var ri = MaxTotalProjection - corrections[i];
                    var rj = MaxTotalProjection - corrections[j];
                    if (half > ri) half = ri;
                    if (half > rj) half = rj;
                    if (half <= 0f) continue;
                    s[i].Position = new Vec2(a.X + ux * half, a.Y + uy * half);
                    s[j].Position = new Vec2(b.X - ux * half, b.Y - uy * half);
                    corrections[i] += half;
                    corrections[j] += half;
                    any = true;
                }
                if (!any) break;
            }
        }
    }
}
