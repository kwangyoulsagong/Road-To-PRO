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
        }

        public sealed class Landmark
        {
            public string Name;
            public Vec3 Base;
            public readonly Dictionary<string, Vec3> Deltas = new Dictionary<string, Vec3>();
        }

        public readonly List<Axis> Axes = new List<Axis>();
        public readonly List<string> Beards = new List<string>();
        public readonly List<Landmark> Landmarks = new List<Landmark>();

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
                if (!byAxis.TryGetValue((string)ax, out var a))
                {
                    a = new Axis { Id = (string)ax, Range = F(mx) };
                    byAxis.Add(a.Id, a);
                    d.Axes.Add(a);
                }
                if ((double)sg > 0) a.PosKey = (string)id; else a.NegKey = (string)id;
                a.Range = Math.Min(a.Range, F(mx));
            }
            foreach (var a in d.Axes)
                if (a.PosKey == null || a.NegKey == null) throw new FormatException("face.json 축 쌍이 모자람: " + a.Id);
            root.TryGet("beards", out var bd);
            foreach (var x in (List<object>)bd)
            {
                ((JsonObject)x).TryGet("id", out var id);
                d.Beards.Add((string)id);
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

    /// <summary>선수 한 명의 얼굴: 축 값(부호 있음, FaceData.Axes 순서)과 수염 종류(FaceData.Beards 인덱스).</summary>
    public sealed class FaceShape : IEquatable<FaceShape>
    {
        public readonly float[] Axis;
        public readonly int Beard;

        public FaceShape(float[] axis, int beard)
        {
            Axis = axis;
            Beard = beard;
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
            if (o == null || o.Beard != Beard || o.Axis.Length != Axis.Length) return false;
            for (int i = 0; i < Axis.Length; i++)
                if (!Axis[i].Equals(o.Axis[i])) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is FaceShape o && Equals(o);

        public override int GetHashCode()
        {
            int h = Beard;
            foreach (var a in Axis) h = h * 31 + a.GetHashCode();
            return h;
        }
    }

    /// <summary>
    /// 선수 id → 얼굴 (specs/056 FR-22·23·24). 시드 = StableHasher(태그, id, 시도 번호) → SplitMix64. 실존 인물 입력 경로 없음.
    /// 축 값: 두 균등의 합(삼각 분포, 평균 얼굴 쪽이 많다) × 범위. 수염: 4종 균등 (2026-11-04 플랜 승인, FR-23 [미정] 기본안).
    /// GenerateGroup: 한 장면(팀 22명 등)에서 앞 선수와 랜드마크 거리가 기준(FaceLandmarks.MinDistance)보다 가까우면 시도 번호를 올려 다시 뽑는다(AC-04).
    /// </summary>
    public static class FaceGenerator
    {
        const uint Tag = 0x0056FACEu;
        public const int MaxTries = 32;

        static float U01(ref ulong s) => (float)(SplitMix64.Next(ref s) >> 40) / (1 << 24);

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
            int beard = (int)(SplitMix64.Next(ref s) % (ulong)d.Beards.Count);
            return new FaceShape(ax, beard);
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

        public FaceLandmarks(FaceData d)
        {
            _d = d;
        }

        public Vec3[] Positions(FaceShape f)
        {
            var p = new Vec3[_d.Landmarks.Count];
            for (int i = 0; i < p.Length; i++)
            {
                var l = _d.Landmarks[i];
                var v = l.Base;
                for (int a = 0; a < f.Axis.Length; a++)
                {
                    f.Key(_d, a, out var key, out var w);
                    if (w > 0f && l.Deltas.TryGetValue(key, out var dl)) v = v + dl * w;
                }
                p[i] = v;
            }
            return p;
        }

        public float[] DistanceVector(FaceShape f)
        {
            var p = Positions(f);
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

    /// <summary>043 외형 체형 3축 → 056 셰이프키 가중치 (FR-10·11). Muscular 0.25~1 → MPFB 근육 0.6~0.9 구간(키 Muscular 0~1).</summary>
    public static class BodyShapeMap
    {
        public static float Thin(in PlayerAppearance a) => Clamp01(a.Thin);
        public static float Heavy(in PlayerAppearance a) => Clamp01(a.Heavy);
        public static float Muscular(in PlayerAppearance a) => Clamp01((a.Muscular - 0.25f) / 0.75f);

        static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
