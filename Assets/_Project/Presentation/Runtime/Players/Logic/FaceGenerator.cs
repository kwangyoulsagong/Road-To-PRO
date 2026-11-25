using System;
using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// player.face.json (specs/056 FR-22, 생성기 face.py). 얼굴 축(셰이프키 pos/neg 쌍)·수염 종류·랜드마크(기준 위치 + 키별 델타).
    /// </summary>
    public sealed class FaceData
    {
        public sealed class Axis
        {
            public string Id;          // 축 이름 (예: nose_width)
            public string PosKey, NegKey;
            public float Range;        // 축 값 ∈ [−Range, Range]
            /// <summary>088 T30m: 개성 얼굴이 이 축을 고를 때의 최대 |값| (face.json distinctMax, 원본 타깃 세기 ≤ 1). 0 = 개성 확장 축 아님.</summary>
            public float DistinctMax;
        }

        public sealed class Landmark
        {
            public string Name;
            public Vec3 Base;
            public readonly Dictionary<string, Vec3> Deltas = new Dictionary<string, Vec3>();
        }

        public readonly List<Axis> Axes = new List<Axis>();
        public readonly List<string> Beards = new List<string>();
        /// <summary>062 FR-60·62: 바탕 머리 셰이프 키 이름(Base_n)과 지역 태그 — face.json baseHeads 순서 = AppearanceGenerator 바탕 인덱스.</summary>
        public readonly List<string> BaseHeads = new List<string>();
        public readonly List<string> BaseRegions = new List<string>();
        /// <summary>062 FR-70: 표정 셰이프 키 이름(Expr_*, 무표정 = 키 0) — ExpressionLogic.Keys 순서.</summary>
        public readonly List<string> Expressions = new List<string>();
        public readonly List<Landmark> Landmarks = new List<Landmark>();

        /// <summary>088 T30m: 개성 얼굴 특징 묶음 — 축 인덱스(Axes 순서)와 비율(부호 = 방향, 크기 × 축 최대치). face.json distinctBundles.</summary>
        public sealed class Bundle
        {
            public string Id;
            public int[] Axis;
            public float[] Frac;
        }
        public readonly List<Bundle> Bundles = new List<Bundle>();

        static float F(object o) => (float)(double)o;

        static Vec3 V(object o)
        {
            var l = (List<object>)o;
            return new Vec3(F(l[0]), F(l[1]), F(l[2]));
        }

        public static FaceData Parse(string json)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            if (!root.TryGet("schemaVersion", out var sv) || (double)sv != 1) throw new FormatException("face.json schemaVersion 1 아님");
            var d = new FaceData();
            root.TryGet("targets", out var tg);
            var byAxis = new Dictionary<string, Axis>();
            foreach (var x in (List<object>)tg)
            {
                var o = (JsonObject)x;
                o.TryGet("id", out var id);
                o.TryGet("axis", out var ax);
                o.TryGet("sign", out var sg);
                o.TryGet("max", out var mx);
                float dmx = o.TryGet("distinctMax", out var dm) ? F(dm) : 0f;
                if (!byAxis.TryGetValue((string)ax, out var a))
                {
                    a = new Axis { Id = (string)ax, Range = F(mx) };
                    byAxis.Add(a.Id, a);
                    d.Axes.Add(a);
                }
                if ((double)sg > 0) a.PosKey = (string)id; else a.NegKey = (string)id;
                a.Range = Math.Min(a.Range, F(mx));
                a.DistinctMax = a.DistinctMax == 0f ? dmx : Math.Min(a.DistinctMax, dmx);
            }
            foreach (var a in d.Axes)
                if (a.PosKey == null || a.NegKey == null) throw new FormatException("face.json 축 쌍이 모자람: " + a.Id);
            if (root.TryGet("distinctBundles", out var bs))   // 088 T30m (필드 추가만 — 없으면 묶음 없이 축 단위 개성)
                foreach (var x in (List<object>)bs)
                {
                    var o = (JsonObject)x;
                    o.TryGet("id", out var bid);
                    o.TryGet("axes", out var bax);
                    var list = (List<object>)bax;
                    var b = new Bundle { Id = (string)bid, Axis = new int[list.Count], Frac = new float[list.Count] };
                    for (int j = 0; j < list.Count; j++)
                    {
                        var e = (JsonObject)list[j];
                        e.TryGet("axis", out var an);
                        e.TryGet("frac", out var fr);
                        b.Axis[j] = d.Axes.FindIndex(a => a.Id == (string)an);
                        if (b.Axis[j] < 0) throw new FormatException("face.json 묶음 축이 없다: " + an);
                        b.Frac[j] = F(fr);
                    }
                    d.Bundles.Add(b);
                }
            root.TryGet("beards", out var bd);
            foreach (var x in (List<object>)bd)
            {
                ((JsonObject)x).TryGet("id", out var id);
                d.Beards.Add((string)id);
            }
            // 062: 없는 필드는 무시 (schemaVersion 1 유지, 필드 추가만)
            if (root.TryGet("baseHeads", out var bh) && bh is List<object> bhl)
                foreach (var x in bhl)
                {
                    var o = (JsonObject)x;
                    o.TryGet("id", out var id);
                    o.TryGet("region", out var rg);
                    d.BaseHeads.Add((string)id);
                    d.BaseRegions.Add(rg as string);
                }
            if (root.TryGet("expressions", out var ex) && ex is List<object> exl)
                foreach (var x in exl)
                {
                    ((JsonObject)x).TryGet("id", out var id);
                    d.Expressions.Add((string)id);
                }
            root.TryGet("landmarks", out var lm);
            foreach (var x in (List<object>)lm)
            {
                var o = (JsonObject)x;
                o.TryGet("name", out var nm);
                o.TryGet("base", out var b);
                o.TryGet("deltas", out var ds);
                var l = new Landmark { Name = (string)nm, Base = V(b) };
                foreach (var kv in ((JsonObject)ds).Members) l.Deltas.Add(kv.Key, V(kv.Value));
                d.Landmarks.Add(l);
            }
            return d;
        }
    }

    /// <summary>선수 한 명의 얼굴: 축 값(부호 있음, FaceData.Axes 순서). 062: 수염은 PlayerAppearance.Beard로 옮겼다(plan '데이터').</summary>
    public sealed class FaceShape : IEquatable<FaceShape>
    {
        public readonly float[] Axis;

        public FaceShape(float[] axis)
        {
            Axis = axis;
        }

        /// <summary>축 i → (셰이프키 이름, 가중치 0~1). 양수면 pos 키, 음수면 neg 키.</summary>
        public void Key(FaceData d, int i, out string key, out float weight)
        {
            float v = Axis[i];
            key = v >= 0 ? d.Axes[i].PosKey : d.Axes[i].NegKey;
            weight = Math.Abs(v);
        }

        public bool Equals(FaceShape o)
        {
            if (o == null || o.Axis.Length != Axis.Length) return false;
            for (int i = 0; i < Axis.Length; i++)
                if (!Axis[i].Equals(o.Axis[i])) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is FaceShape o && Equals(o);

        public override int GetHashCode()
        {
            int h = 17;
            foreach (var a in Axis) h = h * 31 + a.GetHashCode();
            return h;
        }
    }

    /// <summary>
    /// 선수 id → 얼굴 (specs/056 FR-22·23·24). 시드 = StableHasher(태그, id, 시도 번호) → SplitMix64. 실존 인물 입력 경로 없음.
    /// 축 값: 두 균등의 합(삼각 분포, 평균 얼굴 쪽이 많다) × 범위. 062: 수염·바탕 머리는 AppearanceGenerator가 뽑는다.
    /// GenerateGroup: 한 장면(팀 22명 등)에서 앞 선수와 랜드마크 거리가 기준(FaceLandmarks.MinDistance)보다 가까우면 시도 번호를 올려 다시 뽑는다(AC-04).
    /// </summary>
    /// <summary>088 FR-60 인상 유형 — id로만 정한다(지역·피부·바탕 머리와 독립). 겉모습 전용, 게임 규칙과 연결 없음.</summary>
    public enum FaceImpression { Handsome, Plain, Distinctive }

    public static class FaceGenerator
    {
        const uint Tag = 0x0056FACEu;
        const uint ImpressionTag = 0x0088FACEu;
        public const int MaxTries = 32;
        /// <summary>088 FR-60 [제안] 천분율: 잘생김 200 · 평범 600 · 개성 200.</summary>
        public const int HandsomePermille = 200, PlainPermille = 600;
        /// <summary>잘생김: 평균성(축 크기 × 0.3) + 성적 이형성 쪽 치우침(범위 비율) [제안] — Langlois & Roggman 1990, Rhodes 2006 방향.
        /// 088 T30m: 0.45 → 0.3, 치우침 키움 + 코 매부리·코 폭 줄임 (유형 차이가 안 보였다).</summary>
        public const float HandsomeSpread = 0.3f;
        static readonly (string axis, float bias)[] HandsomeBias =
        {
            ("chin_prominent", 0.6f), ("chin_width", 0.45f), ("brow_ridge", 0.4f), ("cheekbones", 0.5f), ("cheek_volume", -0.5f),
            ("nose_hump", -0.4f), ("nose_width", -0.3f),
        };
        /// <summary>088 T30m 개성: 특징 묶음(face.json distinctBundles — 큰 코, 약한 턱, 각진 턱, 넓은 얼굴, 두꺼운 입술 등, 눈 없음)
        /// 1~2개(축이 겹치지 않게)를 한 방향으로 함께 — 축 값 = 비율 × 축 최대치 × [0.85, 1], 나머지 축 × 0.75 [제안].
        /// 1차(눈에 띄는 축 2~3개 단독)는 축이 원본 최대치 근처라 더 키울 수 없었다(사용자 "베스트케이스" → 묶음).
        /// 묶음이 없는 face.json이면 1차 방식(확장 축 2~3개 × 범위 [1.3, 1.6]).</summary>
        public const int DistinctMin = 2, DistinctMax = 3;
        public const float DistinctLo = 1.3f, DistinctHi = 1.6f, DistinctOthers = 0.75f;
        public const int BundleMin = 1, BundleMax = 2;
        public const float BundleLo = 0.85f;

        static float U01(ref ulong s) => (float)(SplitMix64.Next(ref s) >> 40) / (1 << 24);

        public static FaceImpression ImpressionOf(int id)
        {
            var h = StableHasher.Create();
            h.Add(ImpressionTag);
            h.Add((uint)id);
            ulong s = h.Value;
            int pm = (int)(SplitMix64.Next(ref s) % 1000UL);
            return pm < HandsomePermille ? FaceImpression.Handsome : pm < HandsomePermille + PlainPermille ? FaceImpression.Plain : FaceImpression.Distinctive;
        }

        public static FaceShape Generate(FaceData d, int id, int attempt = 0)
        {
            var h = StableHasher.Create();
            h.Add(Tag);
            h.Add((uint)id);
            h.Add((uint)attempt);
            ulong s = h.Value;
            var ax = new float[d.Axes.Count];
            for (int i = 0; i < ax.Length; i++)
                ax[i] = d.Axes[i].Range * (U01(ref s) + U01(ref s) - 1f);
            // 088 FR-60: 유형은 시도 번호와 무관(GenerateGroup 재추첨에도 유지). 평범 = 위 기존 분포 그대로
            switch (ImpressionOf(id))
            {
                case FaceImpression.Handsome:
                    for (int i = 0; i < ax.Length; i++) ax[i] *= HandsomeSpread;
                    foreach (var (axis, bias) in HandsomeBias)
                    {
                        int k = IndexOf(d, axis);
                        if (k >= 0) ax[k] += d.Axes[k].Range * bias;
                    }
                    break;
                case FaceImpression.Distinctive:
                {
                    for (int i = 0; i < ax.Length; i++) ax[i] *= DistinctOthers;
                    if (d.Bundles.Count > 0)
                    {
                        int nb = BundleMin + (int)(SplitMix64.Next(ref s) % (ulong)(BundleMax - BundleMin + 1));
                        Span<int> order = stackalloc int[d.Bundles.Count];
                        for (int i = 0; i < order.Length; i++) order[i] = i;
                        Span<bool> used = stackalloc bool[ax.Length];
                        int took = 0;
                        for (int j = 0; j < order.Length && took < nb; j++)
                        {
                            int r = j + (int)(SplitMix64.Next(ref s) % (ulong)(order.Length - j));
                            (order[j], order[r]) = (order[r], order[j]);
                            var b = d.Bundles[order[j]];
                            bool clash = false;
                            foreach (int k in b.Axis) clash |= used[k];
                            if (clash) continue;
                            for (int q = 0; q < b.Axis.Length; q++)
                            {
                                int k = b.Axis[q];
                                used[k] = true;
                                ax[k] = b.Frac[q] * MaxAbs(d, k) * (BundleLo + (1f - BundleLo) * U01(ref s));
                            }
                            took++;
                        }
                        break;
                    }
                    // 묶음 없는 face.json: 후보 = distinctMax가 있는 축 (FaceData 순서). 부분 피셔-예이츠로 겹치지 않게 n개
                    int m = 0;
                    Span<int> cand = stackalloc int[ax.Length];
                    for (int i = 0; i < ax.Length; i++)
                        if (d.Axes[i].DistinctMax > 0f) cand[m++] = i;
                    int n = DistinctMin + (int)(SplitMix64.Next(ref s) % (ulong)(DistinctMax - DistinctMin + 1));
                    for (int j = 0; j < n && j < m; j++)
                    {
                        int r = j + (int)(SplitMix64.Next(ref s) % (ulong)(m - j));
                        (cand[j], cand[r]) = (cand[r], cand[j]);
                        int k = cand[j];
                        float mag = System.Math.Min(d.Axes[k].Range * (DistinctLo + (DistinctHi - DistinctLo) * U01(ref s)), d.Axes[k].DistinctMax);
                        ax[k] = mag * ((SplitMix64.Next(ref s) & 1UL) == 0 ? 1f : -1f);
                    }
                    break;
                }
            }
            for (int i = 0; i < ax.Length; i++)
            {
                float lim = MaxAbs(d, i);
                ax[i] = System.Math.Clamp(ax[i], -lim, lim);
            }
            return new FaceShape(ax);
        }

        /// <summary>축 i의 최대 |값|: 범위, 개성 확장 축이면 distinctMax까지 (088 T30m).</summary>
        public static float MaxAbs(FaceData d, int i) => System.Math.Max(d.Axes[i].Range, d.Axes[i].DistinctMax);

        static int IndexOf(FaceData d, string axis)
        {
            for (int i = 0; i < d.Axes.Count; i++)
                if (d.Axes[i].Id == axis) return i;
            return -1;
        }

        public static void GenerateGroup(FaceData d, FaceLandmarks lm, ReadOnlySpan<int> ids, FaceShape[] output)
        {
            if (output.Length < ids.Length) throw new ArgumentException("output이 작다");
            float min = lm.MinDistance;
            var vecs = new float[ids.Length][];
            for (int i = 0; i < ids.Length; i++)
            {
                FaceShape f = null;
                float[] v = null;
                for (int t = 0; t < MaxTries; t++)
                {
                    f = Generate(d, ids[i], t);
                    v = lm.DistanceVector(f);
                    bool ok = true;
                    for (int j = 0; j < i && ok; j++) ok = FaceLandmarks.Distance(v, vecs[j]) >= min;
                    if (ok) break;
                }
                vecs[i] = v;
                output[i] = f;
            }
        }
    }

    /// <summary>
    /// 얼굴 랜드마크 거리 벡터 (specs/056 AC-04). 랜드마크 위치 = 기준 + Σ 가중치 × 델타 (선형, 생성기와 같은 식).
    /// 거리 벡터 = 랜드마크 모든 쌍 거리. 두 얼굴의 거리 = 거리 벡터 차의 RMS (m).
    /// 기준 MinDistance = 무작위 400명(id 1..400) 평균 쌍 거리 × 25 % (2026-11-04 플랜 [제안] 승인).
    /// </summary>
    public sealed class FaceLandmarks
    {
        public const float MinFraction = 0.25f;
        public const int SampleIds = 400;
        readonly FaceData _d;
        float _min = -1f, _mean = -1f;
        // 062: 키 이름 사전 조회 대신 조밀한 표 [축][랜드마크] (pos·neg)·[바탕][랜드마크] — 1,000팀 검사·팀 재추첨이 수십만 번 부른다
        readonly Vec3[][] _pos, _neg, _base;

        public FaceLandmarks(FaceData d)
        {
            _d = d;
            int n = d.Landmarks.Count;
            Vec3[] Table(string key)
            {
                var t = new Vec3[n];
                for (int i = 0; i < n; i++)
                    if (key != null && d.Landmarks[i].Deltas.TryGetValue(key, out var dl)) t[i] = dl;
                return t;
            }
            _pos = new Vec3[d.Axes.Count][];
            _neg = new Vec3[d.Axes.Count][];
            for (int a = 0; a < d.Axes.Count; a++)
            {
                _pos[a] = Table(d.Axes[a].PosKey);
                _neg[a] = Table(d.Axes[a].NegKey);
            }
            _base = new Vec3[d.BaseHeads.Count][];
            for (int b = 0; b < d.BaseHeads.Count; b++) _base[b] = Table(d.BaseHeads[b]);
        }

        public Vec3[] Positions(FaceShape f) => Positions(f, null);

        /// <summary>062 FR-61: 얼굴 축 + 바탕 머리 혼합(외형의 천분율 가중치)까지 더한 랜드마크 위치. look이 null이면 축만(056 정의).</summary>
        public Vec3[] Positions(FaceShape f, PlayerAppearance? look)
        {
            int n = _d.Landmarks.Count;
            var p = new Vec3[n];
            for (int i = 0; i < n; i++) p[i] = _d.Landmarks[i].Base;
            for (int a = 0; a < f.Axis.Length; a++)
            {
                float v = f.Axis[a];
                if (v == 0f) continue;
                var t = v > 0f ? _pos[a] : _neg[a];
                float w = Math.Abs(v);
                for (int i = 0; i < n; i++) p[i] = p[i] + t[i] * w;
            }
            if (look.HasValue && _base.Length > 0)
            {
                var lk = look.Value;
                for (int k = 0; k < 3; k++)
                {
                    lk.Base(k, out int head, out int pm);
                    if (pm <= 0 || head < 0 || head >= _base.Length) continue;
                    var t = _base[head];
                    float w = pm * 0.001f;
                    for (int i = 0; i < n; i++) p[i] = p[i] + t[i] * w;
                }
            }
            return p;
        }

        public float[] DistanceVector(FaceShape f) => DistanceVector(f, null);

        public float[] DistanceVector(FaceShape f, PlayerAppearance? look)
        {
            var p = Positions(f, look);
            int n = p.Length;
            var v = new float[n * (n - 1) / 2];
            int k = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    v[k++] = (p[i] - p[j]).Length();
            return v;
        }

        public static float Distance(float[] a, float[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double d = a[i] - b[i];
                s += d * d;
            }
            return (float)Math.Sqrt(s / a.Length);
        }

        /// <summary>무작위 얼굴 평균 쌍 거리 (id 1..SampleIds, 인접 쌍 i·i+1과 i·i+200 — 결정론, O(n)).</summary>
        public float MeanPairDistance
        {
            get
            {
                if (_mean >= 0) return _mean;
                var vs = new float[SampleIds][];
                for (int i = 0; i < SampleIds; i++) vs[i] = DistanceVector(FaceGenerator.Generate(_d, i + 1));
                double s = 0;
                int c = 0;
                for (int i = 0; i < SampleIds; i++)
                {
                    s += Distance(vs[i], vs[(i + 1) % SampleIds]);
                    s += Distance(vs[i], vs[(i + SampleIds / 2) % SampleIds]);
                    c += 2;
                }
                return _mean = (float)(s / c);
            }
        }

        public float MinDistance => _min >= 0 ? _min : (_min = MeanPairDistance * MinFraction);
    }

    /// <summary>043 외형 체형 3축 → 056 셰이프키 가중치 (FR-10·11). Muscular 0.25~1 → MPFB 근육 0.6~0.9 구간(키 Muscular 0~1).
    /// 062: + Broad·Limb 셰이프키, 근육 지도 세기, 어깨 너비 근사.</summary>
    public static class BodyShapeMap
    {
        public static float Thin(in PlayerAppearance a) => Clamp01(a.Thin);
        public static float Heavy(in PlayerAppearance a) => Clamp01(a.Heavy);
        public static float Muscular(in PlayerAppearance a) => Clamp01((a.Muscular - 0.25f) / 0.75f);
        public static float Broad(in PlayerAppearance a) => Clamp01(a.Broad);
        public static float Limb(in PlayerAppearance a) => Clamp01(a.Limb);

        /// <summary>062 셰이더 근육 지도 세기 _MuscleDef = 0.5 + 0.5 × Muscular(0~1) — 프로 선수라 최소도 0.5 (T06).</summary>
        public static float MuscleDef(in PlayerAppearance a) => 0.5f + 0.5f * Muscular(a);

        // 062 FR-04: 정면 어깨 너비(어깨 관절 높이 ±4 cm 띠)/키 = 기준 + Σ 축 기여. 값은 Blender 생성기 기준 몸에서 잰 것
        // (2026-11-20 T10, 축 가중치 1에서의 차). 생성기 체형 키를 바꾸면 다시 잰다.
        public const float ShoulderWidthBase = 0.3448f;
        public const float ShoulderBroad = 0.0345f, ShoulderHeavy = 0.0043f, ShoulderLimb = 0.0030f,
            ShoulderMuscular = -0.0021f, ShoulderThin = -0.0098f;

        public static float ShoulderWidthRatio(in PlayerAppearance a) =>
            ShoulderWidthBase + ShoulderBroad * Broad(a) + ShoulderHeavy * Heavy(a) + ShoulderLimb * Limb(a)
            + ShoulderMuscular * Muscular(a) + ShoulderThin * Thin(a);

        static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
