using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>클로즈업 머리 선택 (specs/056 FR-61): 25 % 이상, 최대 2명, 히스테리시스 ±15 %.</summary>
    public class CloseupHeadsTests
    {
        static bool[] Run(float[] r, bool[] cur, out int n)
        {
            var next = new bool[r.Length];
            n = CloseupSelector.Select(r, cur, next);
            return next;
        }

        [Test]
        public void Select_AtMostTwo_Largest()
        {
            var next = Run(new[] { 0.5f, 0.9f, 0.4f, 0.7f, 0.1f }, new bool[5], out int n);
            Assert.AreEqual(2, n);
            CollectionAssert.AreEqual(new[] { false, true, false, true, false }, next);
        }

        [Test]
        public void Select_BelowThreshold_None()
        {
            Run(new[] { 0.2f, 0.28f, 0.1f }, new bool[3], out int n);
            Assert.AreEqual(0, n, "켜지려면 25 % × 1.15 = 28.75 % 넘어야 한다");
        }

        [Test]
        public void Select_Hysteresis_StaysOnUntil85Percent()
        {
            var cur = new[] { true, false };
            var next = Run(new[] { 0.22f, 0.27f }, cur, out _);
            Assert.IsTrue(next[0], "켜진 선수는 21.25 % 아래로 내려가야 꺼진다");
            Assert.IsFalse(next[1]);
            next = Run(new[] { 0.21f, 0.27f }, cur, out _);
            Assert.IsFalse(next[0]);
        }

        [Test]
        public void Select_Tie_LowerIndexFirst_NaNIgnored()
        {
            var next = Run(new[] { float.NaN, 0.5f, 0.5f, 0.5f }, new bool[4], out int n);
            Assert.AreEqual(2, n);
            CollectionAssert.AreEqual(new[] { false, true, true, false }, next);
        }

        [Test]
        public void Swap_KeepsExpression()
        {
            // plan 위험: 클로즈업 머리 ↔ LOD0 전환 때 표정이 끊기지 않는다 — 표정 상태는 선수 단위(PlayerRig.SetExpression)로 두 메시에 같은 값
            var rig = PlayerRigTests.NewRig();
            try
            {
                rig.ApplyAppearance(AppearanceGenerator.Generate(new BodyInput(21)), FaceGenerator.Generate(FaceGeneratorTests.Data, 21),
                    FaceGeneratorTests.Data);
                var w = new float[ExpressionLogic.Keys.Length];
                w[0] = 0.7f;   // 환호 70 %
                w[2] = 0.3f;   // 실망 30 %
                rig.SetExpression(w);
                foreach (bool on in new[] { true, false, true })
                {
                    rig.SetCloseup(on);
                    for (int k = 0; k < w.Length; k++)
                    {
                        // 프레임 가중치 1로 굽는다(HideHead와 같은 관례) → 블렌드셰이프 값 0~1
                        Assert.AreEqual(w[k], rig.Lod(0).GetBlendShapeWeight(1 + k), 1e-5f, $"LOD0 {ExpressionLogic.Keys[k]} closeup={on}");
                        Assert.AreEqual(w[k], rig.CloseupRenderer.GetBlendShapeWeight(1 + k), 1e-5f, $"클로즈업 {ExpressionLogic.Keys[k]} closeup={on}");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(rig.gameObject); }
        }
    }
}
