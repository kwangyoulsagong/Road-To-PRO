using System;
using System.Collections.Generic;

namespace RoadToPro.Presentation.Players.Logic.Motion
{
    /// <summary>한 발의 한 접지: 시뮬 시각 구간 + 월드 디딤 위치(발끝 XZ). 가중치는 T0 전 Ramp 동안 들어오고 T1 뒤 Ramp 동안 빠진다(공중에서 옮긴다).</summary>
    public readonly struct FootPlant
    {
        public readonly float T0, T1, X, Z;

        public FootPlant(float t0, float t1, float x, float z)
        {
            T0 = t0;
            T1 = t1;
            X = x;
            Z = z;
        }
    }

    /// <summary>
    /// 발 디딤 계획 (specs/054 FR-10·23·33). 장면 로드 때 30 Hz로 포즈 지시를 훑어 발마다 접지 구간과 디딤 위치를 정한다.
    /// 재생 중에는 이진 탐색으로 찾기만 한다 — 포즈 = f(t), 할당 0 (AC-13·15).
    /// 디딤 위치 = 접지 시작 순간(공 접촉을 지나는 접지는 접촉 순간)의 화면 루트 + 몸 방향 × 클립의 발끝 위치. 몸이 다리 길이보다 멀어지면 그 자리에서 접지를 끝낸다.
    /// </summary>
    public sealed class FootPlantPlan
    {
        const int E = HighlightData.Entities;
        const float Dt = 1f / HighlightData.FrameRate;
        /// <summary>디딤 위치와 골반 지면 투영의 수평 거리가 이보다 크면 다리가 닿지 않는다 [제안] (기준 키 1.80 m, 다리 ≈ 0.9 m, 골반 높이 ≈ 0.9 m + 발 길이).</summary>
        public const float Reach = 0.65f;
        const float HipHeight = 0.9f;
        /// <summary>접지 구간 밖 가중치 전환 시간(s): 발이 뜬 동안 클립 경로와 디딤 위치 사이를 옮긴다 — 땅에서 끌리지 않게.</summary>
        public const float Ramp = 0.05f;
        const float MinDuration = Dt;
        /// <summary>접지로 볼 가중치 비율.</summary>
        const float StanceWeight = 0.5f;
        /// <summary>프레임 경계 반올림으로 접지 시작이 한 프레임 밀리지 않게 (장면 시작 시각에 따라 갈리면 안 된다).</summary>
        const float FrameEpsilon = 1e-2f;
        const int LandingProbes = 3;

        readonly FootPlant[][] _plants = new FootPlant[E * 2][];

        public FootPlantPlan(AnimPlan plan, HighlightData d, float[] entityScale)
        {
            var open = new List<FootPlant>[2] { new List<FootPlant>(), new List<FootPlant>() };
            var forced = new List<FootPlant>[2] { new List<FootPlant>(), new List<FootPlant>() };
            var s = new PoseSample();
            var c = new PoseSample();
            for (int e = 0; e < E; e++)
            {
                open[0].Clear();
                open[1].Clear();
                float scale = entityScale != null && e < entityScale.Length && entityScale[e] > 0f ? entityScale[e] : 1f;
                forced[0].Clear();
                forced[1].Clear();
                PlanContactPlants(plan, d, e, scale, forced, ref s, ref c);
                var on = new bool[2];
                var t0 = new float[2];
                var ax = new float[2];
                var az = new float[2];
                for (int f = 0; f < d.Frames; f++)
                {
                    float t = d.StartTime + f * Dt;
                    plan.EvaluateBase(e, t, ref s);
                    float sy = (float)Math.Sin(s.Yaw), cy = (float)Math.Cos(s.Yaw);
                    for (int k = 0; k < 2; k++)
                    {
                        // 공 접촉을 지나는 접지는 미리 정했다: 그 구간에서는 새 접지를 열지 않는다
                        if (InForced(forced[k], t))
                        {
                            if (on[k] && t - t0[k] >= MinDuration) open[k].Add(new FootPlant(t0[k], t, ax[k], az[k]));
                            on[k] = false;
                            continue;
                        }
                        bool g = s.Present & LocalFoot(plan.Library, s, k == 0, out var lx, out var lz);
                        float wx = 0f, wz = 0f;
                        if (g)
                        {
                            lx *= scale;
                            lz *= scale;
                            wx = s.Root.X + lx * cy + lz * sy;
                            wz = s.Root.Z - lx * sy + lz * cy;
                        }
                        if (on[k])
                        {
                            // 자세 오버레이가 골반을 옮긴 만큼 함께 본다
                            float hipF = HipHeight * s.Pitch, hipR = HipHeight * s.Roll;
                            float dx = ax[k] - (s.Root.X + hipF * sy + hipR * cy) , dz = az[k] - (s.Root.Z + hipF * cy - hipR * sy);
                            bool far = dx * dx + dz * dz > Reach * Reach * scale * scale;
                            if (!g || far)
                            {
                                if (t - t0[k] >= MinDuration) open[k].Add(new FootPlant(t0[k], t, ax[k], az[k]));
                                on[k] = false;
                                if (far) g = false;     // 다음 프레임에 새 접지로 다시 판정
                            }
                        }
                        if (g && !on[k])
                        {
                            on[k] = true;
                            t0[k] = t;
                            ax[k] = wx;
                            az[k] = wz;
                            // 30 Hz 격자라 실제 착지보다 최대 한 프레임 늦다: 앞쪽을 쪼개 착지 순간에 맞춘다 (그 사이 발이 끌리지 않게)
                            for (int q = 1; q <= LandingProbes && f > 0; q++)
                            {
                                float tp = t - q * Dt / (LandingProbes + 1);
                                plan.EvaluateBase(e, tp, ref c);
                                if (!c.Present || !LocalFoot(plan.Library, c, k == 0, out var px, out var pz)) break;
                                float syp = (float)Math.Sin(c.Yaw), cyp = (float)Math.Cos(c.Yaw);
                                px *= scale;
                                pz *= scale;
                                t0[k] = tp;
                                ax[k] = c.Root.X + px * cyp + pz * syp;
                                az[k] = c.Root.Z - px * syp + pz * cyp;
                            }
                        }
                    }
                }
                for (int k = 0; k < 2; k++)
                {
                    if (on[k] && d.EndTime - t0[k] >= MinDuration) open[k].Add(new FootPlant(t0[k], d.EndTime + 1f, ax[k], az[k]));
                    open[k].AddRange(forced[k]);
                    open[k].Sort((x, y) => x.T0.CompareTo(y.T0));
                    FreeContactFoot(plan, e, k == 0, open[k]);
                    _plants[e * 2 + k] = open[k].ToArray();
                }
            }
        }

        static bool InForced(List<FootPlant> f, float t)
        {
            for (int i = 0; i < f.Count; i++)
                if (t >= f[i].T0 && t < f[i].T1) return true;
            return false;
        }

        /// <summary>
        /// 공 접촉을 지나는 접지 (FR-33): 디딤 위치를 접촉 시각의 클립 위치로 먼저 정하고, 그 자리에 다리가 닿는 프레임부터 접촉 뒤 발이 뜨거나 닿지 않을 때까지 잡는다.
        /// 접지 시작 위치에 잡았다가 접촉 직전에 옮기면 달려오는 선수의 디딤발이 땅 위에서 튄다. 경계는 프레임 번호 — 장면 시작 시각의 반올림과 무관하다.
        /// </summary>
        static void PlanContactPlants(AnimPlan plan, HighlightData d, int e, float scale, List<FootPlant>[] forced, ref PoseSample s, ref PoseSample c)
        {
            var tl = plan.Timeline(e);
            for (int n = 0; n < tl.Count; n++)
            {
                var a = tl.Get(n);
                var info = plan.Library.Get(a.Clip);
                if (!info.HasContact || tl.IsHit(n)) continue;
                float tc = a.ContactAt;
                if (tc < d.StartTime || tc > d.EndTime) continue;
                plan.EvaluateBase(e, tc, ref c);
                int fc = (int)Math.Floor((tc - d.StartTime) * HighlightData.FrameRate);
                for (int k = 0; k < 2; k++)
                {
                    bool left = k == 0;
                    if (!LocalFoot(plan.Library, c, left, out var lx, out var lz)) continue;
                    float sy = (float)Math.Sin(c.Yaw), cy = (float)Math.Cos(c.Yaw);
                    lx *= scale;
                    lz *= scale;
                    float x = c.Root.X + lx * cy + lz * sy, z = c.Root.Z - lx * sy + lz * cy;
                    // 접촉에서 앞뒤로 넓힌다
                    int f0 = fc;
                    while (f0 > 0 && FootHolds(plan, d, e, f0 - 1, left, x, z, scale, ref s)) f0--;
                    int f1 = Math.Min(d.Frames - 1, fc + 2);   // 접촉 시각을 반드시 지난다 (경계 반올림)
                    while (f1 < d.Frames - 1 && FootHolds(plan, d, e, f1, left, x, z, scale, ref s)) f1++;
                    float t0 = Math.Min(tc, d.StartTime + f0 * Dt), t1 = f1 >= d.Frames - 1 ? d.EndTime + 1f : d.StartTime + f1 * Dt;
                    // 연속 접촉 (FR-82): 구간이 겹치면 나중 접촉의 디딤이 이긴다 — Query는 구간이 겹치지 않는다고 본다
                    var list = forced[k];
                    if (list.Count > 0 && list[list.Count - 1].T1 > t0)
                    {
                        var prev = list[list.Count - 1];
                        float cut = Math.Max(prev.T0 + Dt, tc - Dt);
                        list[list.Count - 1] = new FootPlant(prev.T0, cut, prev.X, prev.Z);
                        t0 = cut;
                    }
                    list.Add(new FootPlant(t0, t1, x, z));
                }
            }
        }

        static bool FootHolds(AnimPlan plan, HighlightData d, int e, int f, bool left, float x, float z, float scale, ref PoseSample s)
        {
            plan.EvaluateBase(e, d.StartTime + f * Dt, ref s);
            if (!s.Present || !LocalFoot(plan.Library, s, left, out _, out _)) return false;
            float sy = (float)Math.Sin(s.Yaw), cy = (float)Math.Cos(s.Yaw);
            float hipF = HipHeight * s.Pitch, hipR = HipHeight * s.Roll;
            float dx = x - (s.Root.X + hipF * sy + hipR * cy), dz = z - (s.Root.Z + hipF * cy - hipR * sy);
            return dx * dx + dz * dz <= Reach * Reach * scale * scale;
        }

        /// <summary>공을 건드리는 발은 접촉 전에 완전히 놓는다: 놓는 동안의 당김이 접촉 거리를 망치지 않게 (FR-31).</summary>
        static void FreeContactFoot(AnimPlan plan, int e, bool left, List<FootPlant> a)
        {
            var tl = plan.Timeline(e);
            var part = left ? RoadToPro.MatchEngine.Match.Events.BodyPart.LeftFoot : RoadToPro.MatchEngine.Match.Events.BodyPart.RightFoot;
            for (int k = 0; k < tl.Count; k++)
            {
                var c = tl.Get(k);
                var info = plan.Library.Get(c.Clip);
                if (!info.HasContact || info.ContactPart != part) continue;
                float limit = c.ContactAt - Release;
                for (int i = a.Count - 1; i >= 0; i--)
                {
                    var p = a[i];
                    if (p.T1 <= limit || p.T0 >= c.ContactAt) continue;
                    if (limit - p.T0 < MinDuration) a.RemoveAt(i);
                    else a[i] = new FootPlant(p.T0, limit, p.X, p.Z);
                }
            }
        }

        public FootPlant[] Plants(int e, bool left) => _plants[e * 2 + (left ? 0 : 1)];

        /// <summary>포즈 지시의 클립들에서 이 발이 접지 중이면 가중 평균 발끝 위치.</summary>
        static bool LocalFoot(ClipLibrary lib, in PoseSample s, bool left, out float x, out float z)
        {
            float w = 0f, total = 0f, sx = 0f, sz = 0f;
            for (int i = 0; i < s.Count; i++)
            {
                float wi = s.Weight(i);
                total += wi;
                var c = lib.Get(s.Clip(i));
                float frame = s.Time(i) * ClipInfo.Fps + FrameEpsilon;
                var spans = left ? c.StanceL : c.StanceR;
                for (int n = 0; n < spans.Length; n++)
                {
                    float fr = frame;
                    if (c.Loop && fr < spans[n].F0) fr += c.Frames;
                    if (fr < spans[n].F0 || fr > spans[n].F1) continue;
                    float span = spans[n].F1 - spans[n].F0;
                    float u = span > 1e-4f ? (fr - spans[n].F0) / span : 0f;
                    sx += wi * (spans[n].X0 + (spans[n].X1 - spans[n].X0) * u);
                    sz += wi * (spans[n].Z0 + (spans[n].Z1 - spans[n].Z0) * u);
                    w += wi;
                    break;
                }
            }
            if (total <= 0f || w / total < StanceWeight)
            {
                x = z = 0f;
                return false;
            }
            x = sx / w;
            z = sz / w;
            return true;
        }

        /// <summary>접지가 끝난 뒤 발을 클립 경로로 옮기는 시간(s). 저속 블렌드처럼 발이 안 뜨면 FootLock이 들어 올려 옮긴다 (FR-13 스텝).</summary>
        public const float Release = 0.15f;

        /// <summary>시각 t의 발 고정. 반환 = 가중치 0~1, release = 접지가 끝나고 지난 비율 0~1(그 전이면 −1). 할당 없음.</summary>
        public float Query(int e, bool left, float t, out float x, out float z, out float release)
        {
            release = -1f;
            var a = _plants[e * 2 + (left ? 0 : 1)];
            int lo = 0, hi = a.Length - 1;
            while (lo <= hi)
            {
                int m = (lo + hi) >> 1;
                if (t < a[m].T0) hi = m - 1;
                else if (t >= a[m].T1) lo = m + 1;
                else
                {
                    x = a[m].X;
                    z = a[m].Z;
                    return 1f;
                }
            }
            // 구간 밖: 다음 접지 들어오기가 지난 접지 빠지기보다 우선 (hi = 앞 구간, lo = 뒤 구간)
            if (lo < a.Length && a[lo].T0 - t < Ramp)
            {
                x = a[lo].X;
                z = a[lo].Z;
                float w = 1f - (a[lo].T0 - t) / Ramp;
                return w * w * (3f - 2f * w);
            }
            if (hi >= 0 && t - a[hi].T1 < Release)
            {
                x = a[hi].X;
                z = a[hi].Z;
                release = (t - a[hi].T1) / Release;
                float w = 1f - release;
                return w * w * (3f - 2f * w);
            }
            x = z = 0f;
            return 0f;
        }
    }
}
