using NUnit.Framework;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>선수 리그 (specs/043 FR-01·10·11·64, AC-02).</summary>
    public class PlayerRigTests
    {
        internal const string Gen = "Assets/_Project/Presentation/Players/Generated";

        internal static PlayerRig NewRig()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Gen + "/player.glb");
            var body = AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/Textures/body.png");
            var fabric = AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/Textures/kit_normal.png");
            return PlayerRig.Create(prefab, null, Shader.Find("RoadToPro/PlayerBody"), Shader.Find("RoadToPro/PlayerKit"), body, fabric);
        }

        [Test]
        public void Shaders_Exist()
        {
            Assert.IsNotNull(Shader.Find("RoadToPro/PlayerBody"));
            Assert.IsNotNull(Shader.Find("RoadToPro/PlayerKit"));
        }

        [Test]
        public void Create_SetsLodGroup_TwoMaterials()
        {
            var rig = NewRig();
            try
            {
                var g = rig.GetComponent<LODGroup>();
                Assert.IsNotNull(g);
                Assert.AreEqual(3, g.lodCount);
                Assert.AreEqual(LODFadeMode.None, g.fadeMode, "FR-13: 크로스페이드 대신 히스테리시스(UpdateLod)");
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsNotNull(rig.Lod(i));
                    Assert.AreEqual(2, rig.Lod(i).sharedMaterials.Length);
                }
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }

        [Test]
        public void Lod_Hysteresis_NoFlicker()
        {
            var rig = NewRig();
            var camGo = new GameObject("cam");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 30f;
                float Dist(float ratio) => 1.8f / (2f * ratio * Mathf.Tan(15f * Mathf.Deg2Rad));
                rig.SetRoot(Vector3.zero, 0f);
                cam.transform.position = new Vector3(0, 0.9f, -Dist(0.4f));
                Assert.AreEqual(0, rig.UpdateLod(cam));
                // 경계(0.25) 바로 아래로 → 히스테리시스 안이라 그대로 LOD0
                cam.transform.position = new Vector3(0, 0.9f, -Dist(0.24f));
                Assert.AreEqual(0, rig.UpdateLod(cam));
                cam.transform.position = new Vector3(0, 0.9f, -Dist(0.20f));
                Assert.AreEqual(1, rig.UpdateLod(cam));
                // 다시 경계 바로 위 → 그대로 LOD1 (왕복 깜빡임 없음)
                cam.transform.position = new Vector3(0, 0.9f, -Dist(0.26f));
                Assert.AreEqual(1, rig.UpdateLod(cam));
                cam.transform.position = new Vector3(0, 0.9f, -Dist(0.30f));
                Assert.AreEqual(0, rig.UpdateLod(cam));
                cam.transform.position = new Vector3(0, 0.9f, -Dist(0.01f));
                Assert.AreEqual(2, rig.UpdateLod(cam));
            }
            finally { Object.DestroyImmediate(rig.gameObject); Object.DestroyImmediate(camGo); }
        }

        [Test]
        public void ShadowProxy_SingleSubmesh_ShadowsOnly()
        {
            var rig = NewRig();
            try
            {
                SkinnedMeshRenderer proxy = null;
                foreach (var r in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.name == "ShadowProxy") proxy = r;
                Assert.IsNotNull(proxy);
                Assert.AreEqual(1, proxy.sharedMesh.subMeshCount);
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, proxy.shadowCastingMode);
                Assert.LessOrEqual(proxy.sharedMesh.triangles.Length / 3, 1000);
                for (int i = 0; i < 3; i++) Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, rig.Lod(i).shadowCastingMode);
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }

        [TestCase(160f, 45f)]
        [TestCase(160f, 110f)]
        [TestCase(200f, 45f)]
        [TestCase(200f, 110f)]
        public void ExtremeBodies_FootHeight(float h, float w)
        {
            var rig = NewRig();
            try
            {
                var a = AppearanceGenerator.Generate(new BodyInput(77, h, w));
                rig.ApplyAppearance(a);
                Assert.That(rig.transform.localScale.y, Is.EqualTo(h / 180f).Within(1e-5f));
                var mesh = new Mesh();
                rig.Lod(0).BakeMesh(mesh, true);
                float min = float.MaxValue;
                var m = rig.Lod(0).transform.localToWorldMatrix;
                foreach (var v in mesh.vertices)
                {
                    // BakeMesh(useScale) 결과는 렌더러 로컬(스케일 적용) — 위치·회전만 곱한다
                    var p = rig.Lod(0).transform.position + rig.Lod(0).transform.rotation * v;
                    min = Mathf.Min(min, p.y);
                }
                Object.DestroyImmediate(mesh);
                Assert.That(min, Is.EqualTo(0f).Within(0.02f), $"발 접지 (키 {h}, 체중 {w})");
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }

        [Test]
        public void Yaw_FacesSimDirection()
        {
            var rig = NewRig();
            try
            {
                rig.SetRoot(new Vector3(3, 0, 4), Mathf.PI / 2);
                var foot = rig.Bone("RightFoot").position;
                var toes = rig.Bone("RightToes").position;
                var d = toes - foot;
                Assert.Greater(d.x, 0.1f, "yaw 90° = +X를 본다(발끝이 +X)");
                Assert.That(Mathf.Abs(d.z), Is.LessThan(0.05f));
                Assert.Less(rig.Bone("RightUpperLeg").position.z, 4f, "오른쪽 = −Z (yaw 90°에서)");
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }
    }
}
