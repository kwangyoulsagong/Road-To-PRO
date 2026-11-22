using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// 외형 팩 기준 (specs/062 FR-67~69, AC-16): 기준 머리(player.glb Head_Closeup)와 같은 픽스처는 통과, 뼈 이름 1개 다름·가중치 없는 정점·
    /// 뼈 5개 가중치·목 이음매를 허용 오차 밖으로 옮긴 픽스처는 각 사유로 거부, UV 배치가 바뀌면 버전이 오른다.
    /// </summary>
    public class AppearancePackSpecTests
    {
        const string RefPath = PlayerRigTests.Gen + "/head_reference.json";

        static HeadReference Ref()
        {
            var t = AssetDatabase.LoadAssetAtPath<TextAsset>(RefPath);
            Assert.IsNotNull(t, RefPath);
            return HeadReference.Parse(t.text);
        }

        /// <summary>Unity 메시 → PackMesh. glTFast 임포트 메시·바인드 포즈 좌표는 생성기 선수 좌표와 같다(T33에서 확인 — 반전 없음).</summary>
        static PackMesh FromGlb(int uvVersion)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRigTests.Gen + "/player.glb");
            var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == "Head_Closeup");
            var m = smr.sharedMesh;
            var r = Ref();
            int nb = smr.bones.Length;
            var p = new PackMesh
            {
                BoneNames = new string[nb], BoneParents = new string[nb], BoneHeads = new Vec3[nb], UvVersion = uvVersion,
            };
            var bind = m.bindposes;
            for (int b = 0; b < nb; b++)
            {
                var t = smr.bones[b];
                p.BoneNames[b] = t.name;
                p.BoneParents[b] = t.parent != null && r.ByName.ContainsKey(t.parent.name) ? t.parent.name : null;
                var h = bind[b].inverse.GetColumn(3);
                p.BoneHeads[b] = new Vec3(h.x, h.y, h.z);
            }
            var v = m.vertices;
            p.Positions = v.Select(x => new Vec3(x.x, x.y, x.z)).ToArray();
            p.Triangles = m.triangles;
            var per = m.GetBonesPerVertex();
            var all = m.GetAllBoneWeights();
            p.Weights = new KeyValuePair<int, float>[v.Length][];
            int k = 0;
            for (int i = 0; i < v.Length; i++)
            {
                p.Weights[i] = new KeyValuePair<int, float>[per[i]];
                for (int j = 0; j < per[i]; j++, k++) p.Weights[i][j] = new KeyValuePair<int, float>(all[k].boneIndex, all[k].weight);
            }
            return p;
        }

        static List<PackReject> Kinds(PackMesh pack)
        {
            var r = Ref();
            return AppearancePackSpec.Validate(r, FromGlb(r.UvVersion), pack).Select(x => x.Kind).Distinct().ToList();
        }

        [Test]
        public void Reference_Passes()
        {
            var r = Ref();
            Assert.Greater(r.SeamTolerance, 0f);
            Assert.AreEqual(4, r.MaxInfluences);
            Assert.GreaterOrEqual(r.UvVersion, 1);
            var reasons = AppearancePackSpec.Validate(r, FromGlb(r.UvVersion), FromGlb(r.UvVersion));
            Assert.AreEqual(0, reasons.Count, string.Join("\n", reasons));
            Assert.Greater(AppearancePackSpec.NeckSeam(FromGlb(1).Positions, FromGlb(1).Triangles).Count, 8, "목 고리");
        }

        [Test]
        public void WrongBoneName_Rejected()
        {
            var p = FromGlb(Ref().UvVersion);
            int h = System.Array.IndexOf(p.BoneNames, "Head");
            Assert.GreaterOrEqual(h, 0);
            p.BoneNames[h] = "Head_Custom";
            CollectionAssert.AreEqual(new[] { PackReject.BoneUnknown }, Kinds(p));
        }

        [Test]
        public void UnweightedVertex_Rejected()
        {
            var p = FromGlb(Ref().UvVersion);
            p.Weights[0] = new KeyValuePair<int, float>[0];
            CollectionAssert.AreEqual(new[] { PackReject.Unweighted }, Kinds(p));
        }

        [Test]
        public void FiveBoneWeights_Rejected()
        {
            var p = FromGlb(Ref().UvVersion);
            p.Weights[0] = Enumerable.Range(0, 5).Select(b => new KeyValuePair<int, float>(b, 0.2f)).ToArray();
            CollectionAssert.AreEqual(new[] { PackReject.TooManyInfluences }, Kinds(p));
        }

        [Test]
        public void SeamOutOfTolerance_Rejected()
        {
            var r = Ref();
            var p = FromGlb(r.UvVersion);
            var seam = AppearancePackSpec.NeckSeam(p.Positions, p.Triangles);
            float y0 = seam.Min(s => s.Y);
            float off = r.SeamTolerance * 2f + 0.001f;
            // 목 고리 둘레(아래 6 cm)를 앞으로 민다 — 허용 오차 × 2 + 1 mm
            for (int i = 0; i < p.Positions.Length; i++)
                if (p.Positions[i].Y < y0 + 0.06f) p.Positions[i] = p.Positions[i] + new Vec3(0f, 0f, off);
            CollectionAssert.AreEqual(new[] { PackReject.SeamOutOfTolerance }, Kinds(p));
            // 허용 오차 안(절반)이면 통과
            var q = FromGlb(r.UvVersion);
            for (int i = 0; i < q.Positions.Length; i++)
                if (q.Positions[i].Y < y0 + 0.06f) q.Positions[i] = q.Positions[i] + new Vec3(0f, 0f, r.SeamTolerance * 0.5f);
            CollectionAssert.IsEmpty(Kinds(q));
        }

        [Test]
        public void UvLayoutChange_BumpsVersion()
        {
            var r = Ref();
            Assert.AreEqual(r.UvVersion, AppearancePackSpec.UvVersionFor(r, r.UvHash), "현재 해시 = 현재 버전");
            int max = r.UvHistory.Max(kv => kv.Key);
            Assert.AreEqual(r.UvVersion, max, "기준은 이력의 최신 버전");
            Assert.AreEqual(max + 1, AppearancePackSpec.VersionAfterChange(r, r.UvHash + "-changed"), "배치가 바뀌면 버전 + 1");
            Assert.AreEqual(r.UvVersion, AppearancePackSpec.VersionAfterChange(r, r.UvHash));
            // 이전 버전 팩은 거부 (049가 변환하거나 사유를 보인다)
            CollectionAssert.AreEqual(new[] { PackReject.UvVersion }, Kinds(FromGlb(r.UvVersion - 1)));
        }
    }
}
