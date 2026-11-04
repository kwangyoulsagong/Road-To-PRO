using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 선수별 메시 굽기 (specs/056 plan "로딩 때 굽기"). 하이라이트 로딩 중에 한 번:
    /// 정점 = 기준 + Σ 가중치 × 블렌드셰이프 델타(체형·얼굴·수염), 선수 헤어 조각을 Body 서브메시(0)에 합친다.
    /// 결과 메시는 블렌드셰이프 `HideHead` 하나만 남긴다(클로즈업 머리 전환, FR-61) → 재생 중 블렌드셰이프 비용이 043보다 적다.
    /// 법선은 같은 위치 정점을 묶어 다시 계산(UV 이음매에서 갈라지지 않게), 탄젠트는 Unity 계산.
    /// 같은 입력 → 같은 정점 배열(합산 순서 고정, AC-11). 재생 중 호출은 진단 카운터로 센다(043 KitCompositor 규칙).
    /// </summary>
    public static class PlayerMeshBaker
    {
        public const string HideHead = "HideHead";

        /// <summary>재생 중이면 true (PlayerAnimationSystem·하이라이트 플레이어가 켠다). 이때 Bake를 부르면 카운터가 오른다.</summary>
        public static bool PlaybackActive;
        public static int BakesDuringPlayback { get; private set; }

        public static void ResetDiagnostics() => BakesDuringPlayback = 0;

        /// <summary>
        /// body(필수)와 hair(없으면 null)를 weights(블렌드셰이프 이름 → 0~1)로 굽는다. 두 메시는 같은 뼈대(같은 bindposes)여야 한다.
        /// </summary>
        public static Mesh Bake(Mesh body, Mesh hair, IReadOnlyDictionary<string, float> weights, string name = null)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (PlaybackActive)
            {
                BakesDuringPlayback++;
                Debug.LogWarning("[players] 재생 중 메시 굽기 — 로딩 단계에서 해야 한다");
            }
            var parts = hair != null ? new[] { body, hair } : new[] { body };
            if (hair != null && hair.bindposeCount != body.bindposeCount)
                throw new InvalidOperationException($"뼈대가 다르다: {body.name} {body.bindposeCount} vs {hair.name} {hair.bindposeCount}");

            int total = 0;
            foreach (var m in parts) total += m.vertexCount;
            var verts = new Vector3[total];
            var uvs = new List<Vector2>(total);
            var hide = new Vector3[total];
            var tris0 = new List<int>();
            var tris1 = new List<int>();
            var bpv = new List<byte>(total);
            var bw = new List<BoneWeight1>(total * 4);
            int off = 0;
            var tmpUv = new List<Vector2>();
            foreach (var m in parts)
            {
                int n = m.vertexCount;
                var v = m.vertices;
                var delta = new Vector3[n];
                // 블렌드셰이프 이름 순서(생성기 정렬)대로 더한다 → 결정론
                for (int s = 0; s < m.blendShapeCount; s++)
                {
                    string key = m.GetBlendShapeName(s);
                    int fr = m.GetBlendShapeFrameCount(s) - 1;
                    m.GetBlendShapeFrameVertices(s, fr, delta, null, null);
                    // glTFast 프레임 가중치는 1 (FBX 관례 100이 아님) → 델타 = 프레임 델타 / 프레임 가중치
                    float fw = m.GetBlendShapeFrameWeight(s, fr);
                    float inv = fw != 0f ? 1f / fw : 1f;
                    if (key == HideHead)
                    {
                        for (int i = 0; i < n; i++) hide[off + i] = delta[i] * inv;
                        continue;
                    }
                    if (!weights.TryGetValue(key, out float w) || w == 0f) continue;
                    w = Mathf.Clamp01(w) * inv;
                    for (int i = 0; i < n; i++) v[i] += delta[i] * w;
                }
                Array.Copy(v, 0, verts, off, n);
                tmpUv.Clear();
                m.GetUVs(0, tmpUv);
                if (tmpUv.Count != n) throw new InvalidOperationException(m.name + " UV 수가 정점 수와 다르다");
                uvs.AddRange(tmpUv);
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    var t = m.GetTriangles(sm);
                    var dst = m == body && sm == 1 ? tris1 : tris0; // 몸 서브메시 1 = Kit, 나머지(몸 0·헤어 전부) = Body
                    for (int i = 0; i < t.Length; i++) dst.Add(t[i] + off);
                }
                using (var per = m.GetBonesPerVertex())
                using (var all = m.GetAllBoneWeights())
                {
                    foreach (var c in per) bpv.Add(c);
                    foreach (var x in all) bw.Add(x);
                }
                off += n;
            }

            var mesh = new Mesh { name = name ?? body.name + " (baked)" };
            mesh.indexFormat = total > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tris0, 0, false);
            mesh.SetTriangles(tris1, 1, false);
            mesh.normals = WeldedNormals(verts, tris0, tris1);
            mesh.RecalculateTangents();
            mesh.bindposes = body.bindposes;
            using (var per = new NativeArray<byte>(bpv.ToArray(), Allocator.Temp))
            using (var all = new NativeArray<BoneWeight1>(bw.ToArray(), Allocator.Temp))
                mesh.SetBoneWeights(per, all);
            mesh.AddBlendShapeFrame(HideHead, 1f, hide, null, null);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        /// <summary>면 법선(면적 가중)을 같은 위치(1e-5 m 격자)의 정점끼리 모아 평균 → UV 이음매에서도 매끈하다.</summary>
        public static Vector3[] WeldedNormals(Vector3[] v, List<int> a, List<int> b)
        {
            var key = new long[v.Length];
            var group = new Dictionary<long, int>(v.Length);
            var gid = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                long k = Quant(v[i]);
                if (!group.TryGetValue(k, out int g))
                {
                    g = group.Count;
                    group.Add(k, g);
                }
                gid[i] = g;
            }
            var acc = new Vector3[group.Count];
            void Add(List<int> t)
            {
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    Vector3 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]];
                    var n = Vector3.Cross(p1 - p0, p2 - p0);
                    acc[gid[t[i]]] += n;
                    acc[gid[t[i + 1]]] += n;
                    acc[gid[t[i + 2]]] += n;
                }
            }
            Add(a);
            Add(b);
            var outN = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) outN[i] = acc[gid[i]].normalized;
            return outN;
        }

        static long Quant(Vector3 p)
        {
            long x = (long)Mathf.Round(p.x * 1e5f) & 0x1FFFFF;
            long y = (long)Mathf.Round(p.y * 1e5f) & 0x1FFFFF;
            long z = (long)Mathf.Round(p.z * 1e5f) & 0x1FFFFF;
            return (x << 42) | (y << 21) | z;
        }

        /// <summary>정점 배열 해시 (AC-11 결정론 비교용, FNV-1a 64 비트).</summary>
        public static ulong Hash(Mesh m)
        {
            ulong h = 1469598103934665603UL;
            foreach (var p in m.vertices)
            {
                h = Mix(h, BitConverter.SingleToInt32Bits(p.x));
                h = Mix(h, BitConverter.SingleToInt32Bits(p.y));
                h = Mix(h, BitConverter.SingleToInt32Bits(p.z));
            }
            return h;
        }

        static ulong Mix(ulong h, int x)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    h ^= (byte)(x >> (8 * i));
                    h *= 1099511628211UL;
                }
            }
            return h;
        }
    }
}
