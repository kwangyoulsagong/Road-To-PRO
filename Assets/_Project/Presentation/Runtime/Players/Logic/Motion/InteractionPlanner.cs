using System;

namespace RoadToPro.Presentation.Players.Logic.Motion
{
    /// <summary>
    /// 선수 간·골대 겹침을 화면 루트 오프셋으로 푼다 (specs/054 FR-40·45·85·86). 시뮬은 읽기만 한다.
    /// 프레임별로 필요한 밀어내기를 구하고 앞뒤로 펴서(부드럽게) 저장한다 — 재생 중에는 조회만, 포즈 = f(t).
    /// </summary>
    public sealed class InteractionPlanner
    {
        const int E = HighlightData.Entities;
        /// <summary>몸통 반지름 합 − 허용 겹침 3 cm (FR-40) − 여유 1 cm (골반-목 캡슐이 기울어도 넘지 않게).</summary>
        public const float MinRootGap = 0.15f + 0.15f - MotionDefaults.TorsoOverlap + 0.01f;
        /// <summary>둘 다 움직일 때 간격 [제안]: 어깨 폭 + 팔 스윙 반경 (측정 실측).</summary>
        public const float MinMovingGap = 0.50f;
        /// <summary>골대 기둥 반지름 + 몸통 반지름 − 허용 5 cm (FR-86).</summary>
        public const float PostRadius = 0.06f, MinPostGap = PostRadius + 0.15f - MotionDefaults.PostPenetration + 0.01f;
        /// <summary>회피 동작이 보이는 거리 범위 (m) [제안]: 005 접촉 반경 0.4 m 근처.</summary>
        public const float EvadeFar = 0.9f, EvadeNear = 0.4f;
        public const float MaxTwist = 25f * (float)(Math.PI / 180.0);
        /// <summary>펴는 창 (프레임, 앞뒤). 0.3 s — 보정 속도 변화 상한(FR-03) 안.</summary>
        const int Spread = 9;

        readonly HighlightData _d;
        readonly float[] _ox, _oz, _twist;

        /// <param name="posts">골대 기둥 위치(월드 X, Z) 쌍 나열 [x0, z0, x1, z1, ...]. null이면 없음.</param>
        public InteractionPlanner(HighlightData d, float[] posts)
        {
            _d = d;
            int n = d.Frames * E;
            var rx = new float[n];
            var rz = new float[n];
            var tw = new float[n];
            for (int f = 0; f < d.Frames; f++)
            {
                int b = f * E;
                for (int i = 0; i < E; i++)
                {
                    if (!d.Present[b + i]) continue;
                    for (int j = i + 1; j < E; j++)
                    {
                        if (!d.Present[b + j]) continue;
                        float dx = d.X[b + j] - d.X[b + i], dz = d.Z[b + j] - d.Z[b + i];
                        float dd = dx * dx + dz * dz;
                        if (dd > EvadeFar * EvadeFar) continue;
                        float m = (float)Math.Sqrt(dd);
                        float ux = m > 1e-4f ? dx / m : 1f, uz = m > 1e-4f ? dz / m : 0f;
                        // 둘 다 달리면 팔 스윙이 있어 더 띄운다 (FR-40 팔다리 관통 5 cm)
                        bool moving = Moving(d, f, i) && Moving(d, f, j);
                        float minGap = moving ? MinMovingGap : MinRootGap;
                        if (m < minGap)
                        {
                            float push = (minGap - m) * 0.5f;
                            rx[b + i] -= ux * push;
                            rz[b + i] -= uz * push;
                            rx[b + j] += ux * push;
                            rz[b + j] += uz * push;
                        }
                        float k = m <= EvadeNear ? 1f : (EvadeFar - m) / (EvadeFar - EvadeNear);
                        SetTwist(tw, b + i, d.Yaw[b + i], ux, uz, k);
                        SetTwist(tw, b + j, d.Yaw[b + j], -ux, -uz, k);
                    }
                    if (posts == null) continue;
                    for (int p = 0; p + 1 < posts.Length; p += 2)
                    {
                        float dx = d.X[b + i] - posts[p], dz = d.Z[b + i] - posts[p + 1];
                        float dd = dx * dx + dz * dz;
                        if (dd >= MinPostGap * MinPostGap) continue;
                        // 폭 방향으로만 비켜 간다: 기둥을 지나며 방향이 뒤집히지 않게. 정면이면 골 안쪽으로
                        float side = Math.Abs(dz) > 1e-3f ? Math.Sign(dz) : -Math.Sign(posts[p + 1]);
                        rz[b + i] += side * ((float)Math.Sqrt(MinPostGap * MinPostGap - dx * dx) - Math.Abs(dz));
                    }
                }
            }
            _ox = Spreaded(rx, d.Frames);
            _oz = Spreaded(rz, d.Frames);
            _twist = Spreaded(tw, d.Frames);
        }

        static bool Moving(HighlightData d, int f, int e)
        {
            if (f + 1 >= d.Frames) return false;
            float dx = d.X[(f + 1) * E + e] - d.X[f * E + e], dz = d.Z[(f + 1) * E + e] - d.Z[f * E + e];
            return (dx * dx + dz * dz) * HighlightData.FrameRate * HighlightData.FrameRate >= 1f;
        }

        /// <summary>상대 쪽 어깨를 뒤로: 몸 정면 기준 상대가 오른쪽이면 왼쪽으로 돈다(− = 왼쪽).</summary>
        static void SetTwist(float[] tw, int i, float yaw, float ux, float uz, float k)
        {
            float side = ux * (float)Math.Cos(yaw) - uz * (float)Math.Sin(yaw);
            float v = -Math.Sign(side) * k * MaxTwist;
            if (Math.Abs(v) > Math.Abs(tw[i])) tw[i] = v;
        }

        /// <summary>최대값 유지(큰 쪽) 후 평균 — 필요한 밀어내기를 프레임 전에 시작하고 부호를 유지한 채 편다.</summary>
        static float[] Spreaded(float[] a, int frames)
        {
            var o = new float[a.Length];
            var hold = new float[frames];
            for (int e = 0; e < E; e++)
            {
                for (int f = 0; f < frames; f++)
                {
                    float best = 0f;
                    for (int g = Math.Max(0, f - Spread); g <= Math.Min(frames - 1, f + Spread); g++)
                    {
                        float v = a[g * E + e];
                        if (Math.Abs(v) > Math.Abs(best)) best = v;
                    }
                    hold[f] = best;
                }
                for (int f = 0; f < frames; f++)
                {
                    float s = 0f;
                    int c = 0;
                    for (int g = Math.Max(0, f - Spread); g <= Math.Min(frames - 1, f + Spread); g++)
                    {
                        s += hold[g];
                        c++;
                    }
                    o[f * E + e] = s / c;
                }
            }
            return o;
        }

        public void At(int e, float t, out float ox, out float oz, out float twist)
        {
            _d.FrameAt(t, out var f, out var a);
            int i = f * E + e;
            ox = _ox[i] + (_ox[i + E] - _ox[i]) * a;
            oz = _oz[i] + (_oz[i + E] - _oz[i]) * a;
            twist = _twist[i] + (_twist[i + E] - _twist[i]) * a;
        }
    }

    /// <summary>시뮬 한계를 넘는 궤적의 화면 추종 (FR-80): 한계 안으로 따라간다. 결과 오차는 054 실패가 아니라 시뮬 실패로 분류.</summary>
    public sealed class FaultFollower
    {
        const int E = HighlightData.Entities;
        readonly HighlightData _d;
        readonly float[][] _x = new float[E][], _z = new float[E][];

        public FaultFollower(HighlightData d, MotionTrack tr)
        {
            _d = d;
            float max = MotionDefaults.MaxSimSpeed / HighlightData.FrameRate;
            for (int e = 0; e < E; e++)
            {
                if (!tr.HasFault(e)) continue;
                var ox = new float[d.Frames];
                var oz = new float[d.Frames];
                float cx = d.X[e], cz = d.Z[e];
                for (int f = 0; f < d.Frames; f++)
                {
                    float tx = d.X[f * E + e], tz = d.Z[f * E + e];
                    float dx = tx - cx, dz = tz - cz;
                    float m = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (m > max)
                    {
                        dx *= max / m;
                        dz *= max / m;
                    }
                    cx += dx;
                    cz += dz;
                    ox[f] = cx - tx;
                    oz[f] = cz - tz;
                }
                _x[e] = ox;
                _z[e] = oz;
            }
        }

        public bool Active(int e) => _x[e] != null;

        public void At(int e, float t, out float ox, out float oz)
        {
            _d.FrameAt(t, out var f, out var a);
            ox = _x[e][f] + (_x[e][f + 1] - _x[e][f]) * a;
            oz = _z[e][f] + (_z[e][f + 1] - _z[e][f]) * a;
        }
    }

    /// <summary>
    /// 공 보간 접촉 키 (specs/054 FR-52): 30 Hz 기록 사이에 있는 접촉 시각을 보간 키로 고정한다.
    /// 들어오는 공과 나가는 공을 접촉 지점에서 꺾어, 보간이 접촉을 지나치거나 앞당기지 않게 한다.
    /// </summary>
    public sealed class ContactKeys
    {
        readonly float[] _t;
        readonly RoadToPro.Core.Math.Vec3[] _p;
        readonly HighlightData _d;

        public ContactKeys(HighlightData d)
        {
            _d = d;
            var evs = new System.Collections.Generic.List<AnimEvent>();
            foreach (var ev in d.Events)
                if (IsBallTouch(ev.Kind) && !AnimPlan.MissesBall(ev)) evs.Add(ev);
            evs.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Seq.CompareTo(b.Seq));
            _t = new float[evs.Count];
            _p = new RoadToPro.Core.Math.Vec3[evs.Count];
            for (int i = 0; i < evs.Count; i++)
            {
                _t[i] = evs[i].Time;
                _p[i] = evs[i].BallPos;
            }
        }

        internal static bool IsBallTouch(RoadToPro.MatchEngine.Match.Events.SimEventKind k)
        {
            switch (k)
            {
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Pass:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.PassReceived:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Cross:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Dribble:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Carry:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Shot:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Clearance:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Interception:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Tackle:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Save:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Catch:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Punch:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.Block:
                case RoadToPro.MatchEngine.Match.Events.SimEventKind.BallRecovered:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>접촉 키를 넣은 공 위치. 결과는 시뮬 기록 + 접촉 키 — 시뮬 값은 바꾸지 않는다.</summary>
        public RoadToPro.Core.Math.Vec3 Ball(float t)
        {
            _d.FrameAt(t, out var f, out var a);
            float t0 = _d.StartTime + f / HighlightData.FrameRate, t1 = _d.StartTime + (f + 1) / HighlightData.FrameRate;
            t = t < t0 ? t0 : t > t1 ? t1 : t;    // 장면 밖에서 공만 외삽되지 않게 (선수처럼 범위로 자른다)
            var p0 = _d.Ball[f];
            var p1 = _d.Ball[f + 1];
            float from = t0;
            var pf = p0;
            for (int i = 0; i < _t.Length; i++)
            {
                if (_t[i] < t0) continue;
                if (_t[i] > t1) break;
                if (_t[i] >= t)
                {
                    float span0 = _t[i] - from;
                    return span0 > 1e-6f ? pf + (_p[i] - pf) * ((t - from) / span0) : _p[i];
                }
                from = _t[i];
                pf = _p[i];
            }
            float span = t1 - from;
            return span > 1e-6f ? pf + (p1 - pf) * ((t - from) / span) : pf;
        }
    }
}
