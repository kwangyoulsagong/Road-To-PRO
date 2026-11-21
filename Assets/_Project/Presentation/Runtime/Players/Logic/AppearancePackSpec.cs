using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>
    /// 외형 팩 기준 (specs/062 FR-67~69, AC-16) — 생성기 head_reference.json. 049 외형 팩 검사기가 이 기준으로 판정한다(Q-P3: 판정 함수는 여기).
    /// 좌표는 선수 좌표(Y-up m, 생성기 기준). glTFast로 임포트한 Unity 메시·바인드 포즈 좌표와 같다(반전 없음, T33 확인).
    /// </summary>
    public sealed class HeadReference
    {
        public sealed class Bone
        {
            public string Name, Parent;
            public Vec3 Head, Tail;
        }

        public readonly List<Bone> Bones = new List<Bone>();
        public readonly Dictionary<string, Bone> ByName = new Dictionary<string, Bone>();
        public float SeamTolerance;
        public int MaxInfluences;
        public int UvVersion;
        public string UvHash;
        /// <summary>UV 규격 이력 (버전 → 해시), 버전 오름차순.</summary>
        public readonly List<KeyValuePair<int, string>> UvHistory = new List<KeyValuePair<int, string>>();

        static float F(object o) => (float)(double)o;

        static Vec3 V(object o)
        {
            var l = (List<object>)o;
            return new Vec3(F(l[0]), F(l[1]), F(l[2]));
        }

        public static HeadReference Parse(string json)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            if (!root.TryGet("schemaVersion", out var sv) || (double)sv != 1) throw new FormatException("head_reference.json schemaVersion 1 아님");
            var r = new HeadReference();
            root.TryGet("bones", out var bs);
            foreach (var x in (List<object>)bs)
            {
                var o = (JsonObject)x;
                o.TryGet("name", out var n);
                o.TryGet("parent", out var p);
                o.TryGet("head", out var h);
                o.TryGet("tail", out var t);
                var b = new Bone { Name = (string)n, Parent = p as string, Head = V(h), Tail = V(t) };
                r.Bones.Add(b);
                r.ByName[b.Name] = b;
            }
            root.TryGet("seamToleranceM", out var tol);
            r.SeamTolerance = F(tol);
            r.MaxInfluences = root.TryGet("maxInfluences", out var mi) ? (int)(double)mi : 4;
            root.TryGet("uv", out var uvo);
            var uv = (JsonObject)uvo;
            uv.TryGet("version", out var ver);
            uv.TryGet("hash", out var hsh);
            r.UvVersion = (int)(double)ver;
            r.UvHash = (string)hsh;
            if (uv.TryGet("history", out var hist))
                foreach (var x in (List<object>)hist)
                {
                    var o = (JsonObject)x;
                    o.TryGet("version", out var hv);
                    o.TryGet("hash", out var hh);
                    r.UvHistory.Add(new KeyValuePair<int, string>((int)(double)hv, (string)hh));
                }
            return r;
        }
    }

    /// <summary>검사할 팩 머리(또는 헤어) 메시 — 엔진 형식과 무관한 최소 데이터.</summary>
    public sealed class PackMesh
    {
        /// <summary>메시가 쓰는 뼈 (이름·부모·바인드 head). 가중치 뼈 번호는 이 목록 인덱스.</summary>
        public string[] BoneNames, BoneParents;
        public Vec3[] BoneHeads;
        public Vec3[] Positions;
        public int[] Triangles;
        /// <summary>정점마다 (뼈 번호, 가중치) 목록 — 길이 0 = 가중치 없음.</summary>
        public KeyValuePair<int, float>[][] Weights;
        public int UvVersion;
    }

    public enum PackReject { BoneUnknown, BoneParent, BindPose, Unweighted, TooManyInfluences, SeamOutOfTolerance, UvVersion }

    /// <summary>
    /// 외형 팩 머리 메시 판정 (FR-67·68·69). 사유를 모두 모아 돌려준다(빈 목록 = 통과). 결정론, 엔진 비참조.
    /// 목 이음매: 두 메시의 열린 경계 중 가장 낮은 고리(용접 1e-5 m) — 기준 고리 정점마다 팩 고리에 허용 오차 안 정점이 있고, 그 반대도.
    /// </summary>
    public static class AppearancePackSpec
    {
        public const float BindTolerance = 1e-4f, WeightEpsilon = 1e-6f;

        public readonly struct Reason
        {
            public readonly PackReject Kind;
            public readonly string Detail;

            public Reason(PackReject k, string d)
            {
                Kind = k;
                Detail = d;
            }

            public override string ToString() => Kind + ": " + Detail;
        }

        public static List<Reason> Validate(HeadReference r, PackMesh referenceMesh, PackMesh pack)
        {
            var out_ = new List<Reason>();
            // FR-67 뼈대: 이름·부모·바인드 포즈
            for (int b = 0; b < pack.BoneNames.Length; b++)
            {
                string n = pack.BoneNames[b];
                if (!r.ByName.TryGetValue(n, out var rb))
                {
                    out_.Add(new Reason(PackReject.BoneUnknown, n));
                    continue;
                }
                if (!string.Equals(rb.Parent ?? "", pack.BoneParents[b] ?? "", StringComparison.Ordinal))
                    out_.Add(new Reason(PackReject.BoneParent, $"{n}: {pack.BoneParents[b]} ≠ {rb.Parent}"));
                if ((pack.BoneHeads[b] - rb.Head).Length() > BindTolerance)
                    out_.Add(new Reason(PackReject.BindPose, $"{n}: ({pack.BoneHeads[b].X:0.####}, {pack.BoneHeads[b].Y:0.####}, {pack.BoneHeads[b].Z:0.####}) ≠ ({rb.Head.X:0.####}, {rb.Head.Y:0.####}, {rb.Head.Z:0.####})"));
            }
            // 가중치: 없는 정점·뼈 수 초과
            int unweighted = 0, tooMany = 0;
            for (int i = 0; i < pack.Weights.Length; i++)
            {
                int nz = 0;
                foreach (var kv in pack.Weights[i])
                    if (kv.Value > WeightEpsilon) nz++;
                if (nz == 0) unweighted++;
                if (nz > r.MaxInfluences) tooMany++;
            }
            if (unweighted > 0) out_.Add(new Reason(PackReject.Unweighted, unweighted + "개 정점"));
            if (tooMany > 0) out_.Add(new Reason(PackReject.TooManyInfluences, tooMany + "개 정점 > " + r.MaxInfluences));
            // FR-68 목 이음매
            var a = NeckSeam(referenceMesh.Positions, referenceMesh.Triangles);
            var bs = NeckSeam(pack.Positions, pack.Triangles);
            float worst = Math.Max(MaxNearest(a, bs), MaxNearest(bs, a));
            if (bs.Count == 0 || worst > r.SeamTolerance)
                out_.Add(new Reason(PackReject.SeamOutOfTolerance, $"{worst * 1000f:0.###} mm > {r.SeamTolerance * 1000f:0.###} mm"));
            // FR-69 UV 규격 버전
            if (pack.UvVersion != r.UvVersion) out_.Add(new Reason(PackReject.UvVersion, $"팩 {pack.UvVersion} ≠ 기준 {r.UvVersion}"));
            return out_;
        }

        static float MaxNearest(List<Vec3> from, List<Vec3> to)
        {
            if (from.Count == 0) return 0f;
            if (to.Count == 0) return float.MaxValue;
            float worst = 0f;
            foreach (var p in from)
            {
                float best = float.MaxValue;
                foreach (var q in to) best = Math.Min(best, (p - q).Length());
                worst = Math.Max(worst, best);
            }
            return worst;
        }

        /// <summary>열린 경계(용접 위치 기준 한 번만 쓰인 변) 정점 중 가장 낮은 것에서 6 cm 안 = 목 고리.</summary>
        public static List<Vec3> NeckSeam(Vec3[] pos, int[] tris)
        {
            var key = new Dictionary<(long, long, long), int>();
            var vid = new int[pos.Length];
            var rep = new List<Vec3>();
            for (int i = 0; i < pos.Length; i++)
            {
                var k = ((long)Math.Round(pos[i].X * 1e5), (long)Math.Round(pos[i].Y * 1e5), (long)Math.Round(pos[i].Z * 1e5));
                if (!key.TryGetValue(k, out int g))
                {
                    g = rep.Count;
                    key.Add(k, g);
                    rep.Add(pos[i]);
                }
                vid[i] = g;
            }
            var count = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = vid[tris[t + e]], b = vid[tris[t + (e + 1) % 3]];
                    if (a == b) continue;
                    var k = a < b ? (a, b) : (b, a);
                    count[k] = count.TryGetValue(k, out int c) ? c + 1 : 1;
                }
            var bv = new SortedSet<int>();
            foreach (var kv in count)
                if (kv.Value == 1)
                {
                    bv.Add(kv.Key.Item1);
                    bv.Add(kv.Key.Item2);
                }
            var res = new List<Vec3>();
            if (bv.Count == 0) return res;
            float y0 = float.MaxValue;
            foreach (int g in bv) y0 = Math.Min(y0, rep[g].Y);
            foreach (int g in bv)
                if (rep[g].Y < y0 + 0.06f) res.Add(rep[g]);
            return res;
        }

        /// <summary>FR-69: 해시가 이력에 있으면 그 버전, 없으면 −1.</summary>
        public static int UvVersionFor(HeadReference r, string hash)
        {
            foreach (var kv in r.UvHistory)
                if (kv.Value == hash) return kv.Key;
            return -1;
        }

        /// <summary>FR-69: UV 배치가 바뀌었을 때(이력에 없는 해시) 붙일 다음 버전 = 이력 최대 + 1. 이미 있는 해시면 그 버전.</summary>
        public static int VersionAfterChange(HeadReference r, string hash)
        {
            int v = UvVersionFor(r, hash);
            if (v >= 0) return v;
            int max = 0;
            foreach (var kv in r.UvHistory) max = Math.Max(max, kv.Key);
            return max + 1;
        }
    }
}
