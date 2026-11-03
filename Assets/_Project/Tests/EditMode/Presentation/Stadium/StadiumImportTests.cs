using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>경기장 에셋 임포트 검사 (specs/044 AC-02 외).</summary>
    public class StadiumImportTests
    {
        const string AxisTestPath = "Assets/_Project/Presentation/Stadium/Generated/axis-test.glb";

        internal static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, "임포트 실패: " + path);
            return go;
        }

        internal static Transform Find(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            Assert.Fail("노드 없음: " + name);
            return null;
        }

        internal static Bounds WorldBounds(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            Assert.IsNotNull(mf, t.name + "에 메시 없음");
            var b = mf.sharedMesh.bounds;
            var m = t.localToWorldMatrix;
            var w = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                w.Encapsulate(m.MultiplyPoint3x4(c));
            }
            return w;
        }

        static void Near(Vector3 expected, Vector3 actual, float tol, string what)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThanOrEqualTo(tol), $"{what}: 기대 {expected:F4}, 실제 {actual:F4}");
        }

        [Test]
        public void AxisTestCube_IsUnitAndYUp()
        {
            var root = Load(AxisTestPath);
            var cube = WorldBounds(Find(root, "UnitCube"));
            Near(Vector3.one, cube.size, 0.001f, "1 m 큐브 크기");
            Near(Vector3.zero, cube.center, 0.001f, "1 m 큐브 중심");
            Near(new Vector3(0, 2, 0), WorldBounds(Find(root, "Mark_Up")).center, 0.001f, "위 방향 = +Y");
        }

        [Test]
        public void AxisTest_LengthAlongX_WidthAlongZ()
        {
            var root = Load(AxisTestPath);
            Near(new Vector3(2, 0, 0), WorldBounds(Find(root, "Mark_Length")).center, 0.001f, "피치 길이 = +X");
            Near(new Vector3(0, 0, 2), WorldBounds(Find(root, "Mark_Width")).center, 0.001f, "피치 폭 = +Z");
        }

        [Test]
        public void AxisTest_GoalInnerDimensions()
        {
            var root = Load(AxisTestPath);
            var a = WorldBounds(Find(root, "Post_A"));
            var b = WorldBounds(Find(root, "Post_B"));
            var bar = WorldBounds(Find(root, "Crossbar"));
            Assert.That(Mathf.Abs(b.min.z - a.max.z - 7.32f), Is.LessThanOrEqualTo(0.005f), "골 안쪽 폭");
            Assert.That(Mathf.Abs(bar.min.y - 2.44f), Is.LessThanOrEqualTo(0.005f), "골 안쪽 높이");
            Assert.That(Mathf.Abs(a.center.x - 10f), Is.LessThanOrEqualTo(0.001f), "골대 x");
        }
    }
}

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    using RoadToPro.MatchEngine.Ball;
    using RoadToPro.Tests.EditMode.MatchEngine.Ball;

    /// <summary>기본 경기장 glb 임포트 검사 (specs/044 AC-01·08·16 Unity 쪽).</summary>
    public class StadiumDefaultImportTests
    {
        [Test]
        public void PitchAndGoalBounds()
        {
            var root = StadiumImportTests.Load(StadiumTestKit.DefaultGlb);
            var pitch = StadiumImportTests.WorldBounds(StadiumImportTests.Find(root, "Pitch"));
            Assert.AreEqual(115f, pitch.size.x, 0.01f, "피치 면 길이(런오프 포함)");
            Assert.AreEqual(78f, pitch.size.z, 0.01f, "피치 면 폭(런오프 포함)");
            Assert.AreEqual(0f, pitch.max.y, 0.001f);
            Assert.AreEqual(Vector3.zero.x, pitch.center.x, 0.001f);
            foreach (var (name, gx) in new[] { ("GoalFrame_L", -52.5f), ("GoalFrame_R", 52.5f) })
            {
                var g = StadiumImportTests.WorldBounds(StadiumImportTests.Find(root, name));
                Assert.AreEqual(gx, g.center.x, 0.005f, name + " 포스트 중심 = 골라인 중심");
                Assert.AreEqual(2.44f + 0.12f, g.max.y, 0.005f, name + " 크로스바 윗면");
                Assert.AreEqual(7.32f + 0.24f, g.size.z, 0.005f, name + " 바깥 폭");
            }
        }

        [Test]
        public void StaticMeshes_HaveUv2()
        {
            var root = StadiumImportTests.Load(StadiumTestKit.DefaultGlb);
            var n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var uv2 = new System.Collections.Generic.List<Vector2>();
                mf.sharedMesh.GetUVs(1, uv2);
                Assert.AreEqual(mf.sharedMesh.vertexCount, uv2.Count, mf.name + " UV2 없음");
                foreach (var u in uv2) Assert.IsTrue(u.x >= 0 && u.x <= 1 && u.y >= 0 && u.y <= 1, mf.name + " UV2 범위");
                n++;
            }
            Assert.Greater(n, 20);
        }

        [Test]
        public void GoalColliders_Match004BallParams()
        {
            var p = BallTestKit.Params();
            foreach (var g in StadiumTestKit.DefaultLayout.Goals)
            {
                Assert.AreEqual(p.PostRadius, g.PostRadius, 1e-6f, "포스트 반지름 = 004");
                var inner = Mathf.Abs(g.PostA.z - g.PostB.z) - 2f * g.PostRadius;
                Assert.AreEqual(BallParams.GoalWidth, inner, 0.01f, "골 폭 = 004");
                Assert.AreEqual(BallParams.GoalHeight, g.CrossbarCenterY - g.PostRadius, 0.01f, "골 높이 = 004");
                Assert.AreEqual(p.NetDepth, g.Net.size.x, 0.01f, "골망 깊이 = 004");
                Assert.AreEqual(52.5f * g.Side, g.PostA.x, 0.01f, "포스트 = 골라인(004 x = 0, 105)");
            }
        }
    }
}
