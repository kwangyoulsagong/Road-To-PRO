using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>외형 결정론·중복 없음 (specs/043 FR-01~03, AC-01).</summary>
    public class AppearanceTests
    {
        [Test]
        public void SameId_SameAppearance()
        {
            for (int id = 1; id <= 200; id++)
            {
                var a = AppearanceGenerator.Generate(new BodyInput(id));
                var b = AppearanceGenerator.Generate(new BodyInput(id));
                Assert.AreEqual(a, b, "id " + id);
            }
        }

        [Test]
        public void GivenBody_IsUsed_AndClamped()
        {
            var a = AppearanceGenerator.Generate(new BodyInput(5, 183f, 78f));
            Assert.AreEqual(183f, a.HeightCm);
            Assert.AreEqual(78f, a.WeightKg);
            var lo = AppearanceGenerator.Generate(new BodyInput(5, 150f, 40f));
            var hi = AppearanceGenerator.Generate(new BodyInput(5, 215f, 140f));
            Assert.AreEqual(AppearanceGenerator.MinHeightCm, lo.HeightCm);
            Assert.AreEqual(AppearanceGenerator.MaxHeightCm, hi.HeightCm);
            Assert.That(lo.Thin, Is.EqualTo(1f).Within(1e-5f), "아주 마른 체형 = Thin 1");
            Assert.That(hi.Heavy, Is.EqualTo(1f).Within(1e-5f), "아주 무거운 체형 = Heavy 1");
        }

        [Test]
        public void DerivedValues_InRange()
        {
            for (int id = 1; id <= 1000; id++)
            {
                var a = AppearanceGenerator.Generate(new BodyInput(id));
                Assert.That(a.HeightCm, Is.InRange(AppearanceGenerator.MinHeightCm, AppearanceGenerator.MaxHeightCm));
                Assert.That(a.SkinTone, Is.InRange(0, AppearanceGenerator.SkinTones - 1));
                Assert.That(a.Face, Is.InRange(0, AppearanceGenerator.Faces - 1));
                Assert.That(a.HairStyle, Is.InRange(0, AppearanceGenerator.HairStyles - 1));
                Assert.That(a.HairColor, Is.InRange(0, AppearanceGenerator.HairColors - 1));
                Assert.That(a.Thin + a.Heavy, Is.LessThanOrEqualTo(1f + 1e-5f), "Thin과 Heavy는 동시에 켜지지 않는다");
            }
        }

        [Test]
        public void NoDuplicateCombos_InMatch()
        {
            var inputs = new BodyInput[25];
            for (int i = 0; i < inputs.Length; i++) inputs[i] = new BodyInput(1000 + i * 7);
            var outp = new PlayerAppearance[inputs.Length];
            AppearanceGenerator.GenerateGroup(inputs, outp);
            var seen = new HashSet<int>();
            foreach (var a in outp) Assert.IsTrue(seen.Add(a.ComboKey), "외형 조합 중복");
        }

        [Test]
        public void Collision_NextCombo()
        {
            // 같은 id 두 번 = 같은 조합 → 두 번째는 다음 조합으로 밀린다 (FR-03)
            var inputs = new[] { new BodyInput(42), new BodyInput(42), new BodyInput(42) };
            var outp = new PlayerAppearance[3];
            AppearanceGenerator.GenerateGroup(inputs, outp);
            Assert.AreEqual(AppearanceGenerator.Generate(inputs[0]), outp[0], "첫 선수는 그대로");
            Assert.AreNotEqual(outp[0].ComboKey, outp[1].ComboKey);
            Assert.AreNotEqual(outp[1].ComboKey, outp[2].ComboKey);
            Assert.AreNotEqual(outp[0].ComboKey, outp[2].ComboKey);
            Assert.AreEqual(outp[0].HeightCm, outp[1].HeightCm, "체형은 바꾸지 않는다 (외형 조합만)");
            var again = new PlayerAppearance[3];
            AppearanceGenerator.GenerateGroup(inputs, again);
            CollectionAssert.AreEqual(outp, again, "결정론");
        }
    }
}
