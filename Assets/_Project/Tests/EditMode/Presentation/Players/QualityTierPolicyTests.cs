using NUnit.Framework;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// 선수 품질 등급 (specs/062 FR-85~89). 061 TopTier(최대 3명) → 클로즈업 머리 최대 2명(CloseupSelector) + 나머지 LOD0 고정·고해상도,
    /// TopTier가 비면 기존 CloseupSelector만. 대상 교체는 ±15 % 히스테리시스. 재질 값은 등급과 상관없이 같고 텍스처만 다르다(FR-88).
    /// </summary>
    public class QualityTierPolicyTests
    {
        static void Run(float[] r, int[] top, bool[] curC, bool[] curP, out bool[] c, out bool[] p)
        {
            c = new bool[r.Length];
            p = new bool[r.Length];
            QualityTierPolicy.Select(r, top, curC, curP, c, p);
        }

        [Test]
        public void TopTier_TwoCloseup_ThirdLod0()
        {
            // FR-86: 카메라 근처 3명 → 화면 비율 큰 2명 클로즈업 머리, 3번째는 LOD0 고정(클로즈업 머리 없음), 나머지 일반
            var r = new[] { 0.10f, 0.40f, 0.35f, 0.05f, 0.30f };
            Run(r, new[] { 1, 2, 4 }, new bool[5], new bool[5], out var c, out var p);
            CollectionAssert.AreEqual(new[] { false, true, true, false, false }, c);
            CollectionAssert.AreEqual(new[] { false, false, false, false, true }, p);
            // 등급 대상이 클로즈업 문턱 아래(중간 거리 연출)면 클로즈업 머리 없이 3명 모두 LOD0 고정
            Run(new[] { 0.12f, 0.10f, 0.08f, 0.01f }, new[] { 0, 1, 2 }, new bool[4], new bool[4], out c, out p);
            CollectionAssert.AreEqual(new bool[4], c);
            CollectionAssert.AreEqual(new[] { true, true, true, false }, p);
        }

        [Test]
        public void NoCinematic_FallsBackToCloseupSelector()
        {
            // plan 위험: TopTier가 빈 일반 중계 → 기존 CloseupSelector와 같은 결과, LOD0 고정 없음
            var r = new[] { 0.5f, 0.9f, 0.4f, 0.7f, 0.1f };
            var cur = new[] { false, false, true, false, false };
            var want = new bool[5];
            CloseupSelector.Select(r, cur, want);
            Run(r, new int[0], cur, new bool[5], out var c, out var p);
            CollectionAssert.AreEqual(want, c);
            CollectionAssert.AreEqual(new bool[5], p);
        }

        [Test]
        public void Hysteresis_NoFlicker()
        {
            // FR-87: 061 TopTier(거리 순, 히스테리시스 없음)가 비슷한 두 선수 사이에서 프레임마다 바뀌어도 LOD0 고정 대상이 깜빡이지 않는다
            var r = new[] { 0.50f, 0.45f, 0.20f, 0.21f };
            var curC = new bool[4];
            var curP = new bool[4];
            int flips = 0;
            bool last2 = false;
            for (int f = 0; f < 20; f++)
            {
                var top = f % 2 == 0 ? new[] { 0, 1, 2 } : new[] { 0, 1, 3 };
                Run(r, top, curC, curP, out var c, out var p);
                Assert.IsTrue(c[0] && c[1], "클로즈업 2명 유지 f" + f);
                Assert.AreEqual(1, (p[2] ? 1 : 0) + (p[3] ? 1 : 0), "LOD0 고정 1명 f" + f);
                if (f > 0 && p[2] != last2) flips++;
                last2 = p[2];
                curC = c;
                curP = p;
            }
            Assert.AreEqual(0, flips, "깜빡임");
            // 차이가 히스테리시스(15 %)를 넘으면 바뀐다
            r[3] = 0.30f;
            Run(r, new[] { 0, 1, 3 }, curC, curP, out _, out var p2);
            Assert.IsTrue(p2[3] && !p2[2], "충분히 커진 선수로 교체");
        }

        [Test]
        public void MaterialValues_SameAcrossTiers()
        {
            // FR-88: 고품질 등급(클로즈업 머리·LOD0 고정)과 일반 등급은 같은 재질 값(색·반사·거칠기·수염·땀…)을 쓰고 텍스처만 다르다
            var rig = PlayerRigTests.NewRig();
            try
            {
                rig.ApplyAppearance(AppearanceGenerator.Generate(new BodyInput(77)), FaceGenerator.Generate(FaceGeneratorTests.Data, 77),
                    FaceGeneratorTests.Data);
                rig.SetSweat(1f);
                rig.SetTopTier(true);
                Assert.AreEqual(0, rig.UpdateLod(null), "LOD0 고정");
                Assert.AreEqual(1f, rig.BodyMaterial.GetFloat("_Sweat"), "땀은 등급 대상에 (두 머티리얼 같은 값)");
                var hi = rig.Lod(0).sharedMaterials[0];
                Assert.AreNotSame(rig.BodyMaterial, hi, "고해상도 머티리얼로 교체");
                Assert.AreSame(rig.CloseupRenderer.sharedMaterials[0], hi, "클로즈업 머리와 같은 고해상도 머티리얼");
                var lo = rig.BodyMaterial;
                var sh = lo.shader;
                Assert.AreSame(sh, hi.shader);
                int diffTex = 0;
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    string n = sh.GetPropertyName(i);
                    switch (sh.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color:
                            Assert.AreEqual(lo.GetColor(n), hi.GetColor(n), n);
                            break;
                        case ShaderPropertyType.Vector:
                            Assert.AreEqual(lo.GetVector(n), hi.GetVector(n), n);
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            Assert.AreEqual(lo.GetFloat(n), hi.GetFloat(n), n);
                            break;
                        case ShaderPropertyType.Texture:
                            if (lo.GetTexture(n) != hi.GetTexture(n)) diffTex++;
                            break;
                    }
                }
                Assert.AreEqual(0, diffTex, "088 T04: LOD0·클로즈업이 같은 2K MakeHuman 피부 세트를 쓴다 (해상도 차이 없음)");
                rig.SetTopTier(false);
                Assert.AreSame(rig.BodyMaterial, rig.Lod(0).sharedMaterials[0], "일반 등급 복귀");
                // 클로즈업 머리 전환과 함께 써도 값은 그대로
                rig.SetCloseup(true);
                Assert.AreEqual(rig.BodyMaterial.GetColor("_HairColor"), rig.CloseupRenderer.sharedMaterials[0].GetColor("_HairColor"));
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }
    }
}
