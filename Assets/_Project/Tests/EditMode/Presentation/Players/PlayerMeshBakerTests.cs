using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>선수별 메시 굽기 (specs/056 plan, AC-02·05·11, FR-61).</summary>
    public class PlayerMeshBakerTests
    {
        static PlayerRig Rig(int id, out FaceShape face, out FaceData data)
        {
            data = FaceGeneratorTests.Data;
            face = FaceGenerator.Generate(data, id);
            var rig = PlayerRigTests.NewRig();
            rig.ApplyAppearance(AppearanceGenerator.Generate(new BodyInput(id)), face, data);
            return rig;
        }

        [Test]
        public void SameSeed_SameMeshHash()
        {
            var a = Rig(77, out _, out _);
            var b = Rig(77, out _, out _);
            var c = Rig(78, out _, out _);
            try
            {
                for (int l = 0; l < 3; l++)
                    Assert.AreEqual(PlayerMeshBaker.Hash(a.Lod(l).sharedMesh), PlayerMeshBaker.Hash(b.Lod(l).sharedMesh), "LOD" + l);
                Assert.AreNotEqual(PlayerMeshBaker.Hash(a.Lod(0).sharedMesh), PlayerMeshBaker.Hash(c.Lod(0).sharedMesh));
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
                Object.DestroyImmediate(c.gameObject);
            }
        }

        [Test]
        public void Height_Within1cm()
        {
            foreach (int id in new[] { 3, 40, 512, 9001 })
            {
                var rig = Rig(id, out _, out _);
                try
                {
                    var m = rig.Lod(0).sharedMesh;
                    // 머리카락을 뺀 몸 최고점 = 정수리. 루트 스케일을 곱하면 키
                    float top = 0f;
                    var v = m.vertices;
                    var body = new HashSet<int>(m.GetTriangles(0).Concat(m.GetTriangles(1)));
                    var uv = new List<Vector2>();
                    m.GetUVs(0, uv);
                    foreach (int i in body)
                        if (uv[i].x < 1f) top = Mathf.Max(top, v[i].y);
                    float cm = top * rig.transform.localScale.y * 100f;
                    Assert.AreEqual(rig.Appearance.HeightCm, cm, 1.0f, "id " + id);
                }
                finally { Object.DestroyImmediate(rig.gameObject); }
            }
        }

        [Test]
        public void KeepsHideHeadAndExpr()
        {
            // 062 FR-70·71: 구운 LOD0·클로즈업 머리 = HideHead(0번, SetCloseup이 0번을 쓴다) + 표정 4 (Expr_*), 서브메시 2
            var rig = Rig(5, out _, out _);
            try
            {
                foreach (var m in new[] { rig.Lod(0).sharedMesh, rig.CloseupRenderer.sharedMesh })
                {
                    Assert.AreEqual(PlayerMeshBaker.HideHead, m.GetBlendShapeName(0), m.name);
                    var names = new List<string>();
                    for (int i = 1; i < m.blendShapeCount; i++) names.Add(m.GetBlendShapeName(i));
                    CollectionAssert.AreEqual(ExpressionLogic.Keys, names, m.name);
                    Assert.AreEqual(2, m.subMeshCount);
                }
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }

        [Test]
        public void Lod1_NoExpressionShapes()
        {
            // FR-71: LOD1·LOD2는 HideHead 하나만 (표정 없음 — 무표정 고정)
            var rig = Rig(6, out _, out _);
            try
            {
                for (int l = 1; l < 3; l++)
                {
                    var m = rig.Lod(l).sharedMesh;
                    Assert.AreEqual(1, m.blendShapeCount, "LOD" + l);
                    Assert.AreEqual(PlayerMeshBaker.HideHead, m.GetBlendShapeName(0));
                    Assert.AreEqual(2, m.subMeshCount);
                }
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }

        [Test]
        public void HairStyle_AddsHairTriangles_BaldNone()
        {
            var a = AppearanceGenerator.Generate(new BodyInput(9));
            var afro = a.WithHair(6, 0, 0);   // 062: 조합 번호에 컬러 포인트가 들어가 직접 계산하지 않는다
            var bald = a.WithHair(9, 0, 0);
            var r1 = PlayerRigTests.NewRig();
            var r2 = PlayerRigTests.NewRig();
            try
            {
                r1.ApplyAppearance(afro);
                r2.ApplyAppearance(bald);
                Assert.Greater(r1.Lod(0).sharedMesh.triangles.Length, r2.Lod(0).sharedMesh.triangles.Length + 300 * 3);
                Assert.LessOrEqual(r1.Lod(0).sharedMesh.triangles.Length / 3, 30_000, "088 FR-50 LOD0 ≤ 30k (헤어 포함)");
                Assert.LessOrEqual(r1.Lod(1).sharedMesh.triangles.Length / 3, 3_000);
                Assert.LessOrEqual(r1.Lod(2).sharedMesh.triangles.Length / 3, 1_000);
            }
            finally
            {
                Object.DestroyImmediate(r1.gameObject);
                Object.DestroyImmediate(r2.gameObject);
            }
        }

        [Test]
        public void Bake_Extras_MergedIntoBodySubmesh()
        {
            // 088 FR-53 (T28): 헤어·눈썹·속눈썹 등 여러 조각을 순서대로 Body 서브메시(0)에 합친다 (Kit 서브메시 1은 몸만)
            var root = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Presentation/Players/Generated/player.glb");
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var body = smrs.First(s => s.name == "Player_LOD0").sharedMesh;
            var a = smrs.First(s => s.name == "Hair_Short_LOD0").sharedMesh;
            var b = smrs.First(s => s.name == "Hair_Afro_LOD0").sharedMesh;
            var baked = PlayerMeshBaker.Bake(body, new[] { a, b }, new Dictionary<string, float>(), "t");
            try
            {
                Assert.AreEqual(body.vertexCount + a.vertexCount + b.vertexCount, baked.vertexCount);
                Assert.AreEqual(body.GetTriangles(0).Length + a.triangles.Length + b.triangles.Length, baked.GetTriangles(0).Length);
                Assert.AreEqual(body.GetTriangles(1).Length, baked.GetTriangles(1).Length);
                var one = PlayerMeshBaker.Bake(body, a, new Dictionary<string, float>(), "t1");   // 헤어 하나 오버로드 = 목록 하나
                Assert.AreEqual(body.vertexCount + a.vertexCount, one.vertexCount);
                Object.DestroyImmediate(one);
            }
            finally { Object.DestroyImmediate(baked); }
        }

        [Test]
        public void Closeup_TogglesHeadRendererAndHideHead()
        {
            var rig = Rig(11, out _, out _);
            try
            {
                Assert.IsNotNull(rig.CloseupRenderer);
                Assert.IsFalse(rig.CloseupRenderer.enabled);
                Assert.LessOrEqual(rig.CloseupRenderer.sharedMesh.triangles.Length / 3, 100_000, "클로즈업 머리 + 헤어 ≤ 100k (088 FR-50)");
                rig.SetCloseup(true);
                Assert.IsTrue(rig.CloseupRenderer.enabled);
                Assert.AreEqual(1f, rig.Lod(0).GetBlendShapeWeight(0));
                rig.SetCloseup(false);
                Assert.AreEqual(0f, rig.Lod(0).GetBlendShapeWeight(0));
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }

        [Test]
        public void BakeDuringPlayback_Counted()
        {
            var rig = PlayerRigTests.NewRig();
            PlayerMeshBaker.ResetDiagnostics();
            try
            {
                PlayerMeshBaker.PlaybackActive = true;
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                rig.ApplyAppearance(AppearanceGenerator.Generate(new BodyInput(1)));
                Assert.Greater(PlayerMeshBaker.BakesDuringPlayback, 0);
            }
            finally
            {
                PlayerMeshBaker.PlaybackActive = false;
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
                Object.DestroyImmediate(rig.gameObject);
            }
        }

        // 057 AC-10: 하이라이트 로딩 시 선수 25명 굽기 시간 ≤ 056 측정 × 1.2. 062 FR-84: 057 예산(× 1.2) 대비 +30 % (바탕 머리·표정 키·입 안).
        // Mono EditMode ms 값은 부하에 흔들려 5회 최소값으로 판정한다. 056 기준은 057 T03에서 056 산출물로 측정.
        internal const double Bake25Ms056 = 309.5;  // 2026-11-05 056 산출물, Mono EditMode 5회 최소

        [Test]
        public void Bake25Players_Time_Within057Plus30()
        {
            var data = FaceGeneratorTests.Data;
            var rigs = new PlayerRig[25];
            for (int i = 0; i < rigs.Length; i++) rigs[i] = PlayerRigTests.NewRig();
            try
            {
                double best = double.MaxValue;
                for (int rep = 0; rep < 5; rep++)
                {
                    // 앞선 테스트(경기 여러 판을 돌리는 엔진 테스트)가 남긴 힙 때문에 GC가 측정 중에 끼지 않게 (2026-11-13, 008 전체 실행에서 395 ms로 튐)
                    System.GC.Collect();
                    System.GC.WaitForPendingFinalizers();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < rigs.Length; i++)
                    {
                        int id = 3_000 + i * 11 + rep * 400;
                        rigs[i].ApplyAppearance(AppearanceGenerator.Generate(new BodyInput(id)), FaceGenerator.Generate(data, id), data);
                    }
                    best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds);
                }
                TestContext.WriteLine($"[057] bake25Ms={best:0.0}");
                UnityEngine.Debug.Log($"[057] bake25Ms={best:0.0}");
                if (Bake25Ms056 > 0) Assert.LessOrEqual(best, Bake25Ms056 * 1.2 * 1.3, "062 FR-84 굽기 시간 057 예산 대비 +30%");
            }
            finally
            {
                foreach (var r in rigs) Object.DestroyImmediate(r.gameObject);
            }
        }
    }
}
