using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>외형 결정론·중복 없음 (specs/043 FR-01~03, AC-01) + 헤어 30종·분포·장면 상한 (specs/057 FR-30~34, AC-04).</summary>
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

        // ---------------------------------------------------------------- 057 헤어 30종 (FR-30~34, AC-04)

        static readonly string[] Names056 =
            { "Hair_Buzz", "Hair_Short", "Hair_Long", "Hair_Curly", "Hair_Bun", "Hair_Fade", "Hair_Afro", "Hair_Mohawk", "Hair_Dreads", null };

        [Test]
        public void HairStyles_Thirty_WeightsSum_FamilyShares()
        {
            Assert.AreEqual(30, AppearanceGenerator.HairStyles);
            int sum = 0, shortW = 0, longW = 0;
            for (int i = 0; i < AppearanceGenerator.HairStyles; i++)
            {
                int w = AppearanceGenerator.HairWeight(i);
                Assert.Greater(w, 0, "가중치 0인 스타일 " + i);
                sum += w;
                if (AppearanceGenerator.HairFamilyOf(i) == HairFamily.Short) shortW += w;
                if (AppearanceGenerator.HairFamilyOf(i) == HairFamily.Long) longW += w;
            }
            Assert.AreEqual(1000, sum);
            Assert.GreaterOrEqual(shortW, 550, "FR-31 짧은 계열 ≥ 55%");
            Assert.LessOrEqual(longW, 150, "FR-31 긴·묶음 계열 ≤ 15%");
            // 실제 생성 분포도 같은 범위 (20,000명)
            int s = 0, l = 0;
            var seen = new int[AppearanceGenerator.HairStyles];
            const int n = 20000;
            for (int id = 1; id <= n; id++)
            {
                int hs = AppearanceGenerator.Generate(new BodyInput(id)).HairStyle;
                seen[hs]++;
                if (AppearanceGenerator.HairFamilyOf(hs) == HairFamily.Short) s++;
                if (AppearanceGenerator.HairFamilyOf(hs) == HairFamily.Long) l++;
            }
            Assert.GreaterOrEqual(s, n * 55 / 100);
            Assert.LessOrEqual(l, n * 15 / 100);
            for (int i = 0; i < seen.Length; i++) Assert.Greater(seen[i], 0, "안 나오는 스타일 " + i);
        }

        [Test]
        public void HairStyles_056Order_Kept()
        {
            for (int i = 0; i < Names056.Length; i++)
                Assert.AreEqual(Names056[i], RoadToPro.Presentation.Players.PlayerLook.HairMeshes[i], "인덱스 " + i);
            Assert.AreEqual(AppearanceGenerator.HairStyles, RoadToPro.Presentation.Players.PlayerLook.HairMeshes.Length);
        }

        [Test]
        public void GenerateGroup_HairCapThree_ThousandTeams()
        {
            int bad = 0;
            var inputs = new BodyInput[22];
            var outp = new PlayerAppearance[22];
            var cnt = new int[AppearanceGenerator.HairStyles];
            for (int team = 0; team < 1000; team++)
            {
                for (int i = 0; i < 22; i++) inputs[i] = new BodyInput(100_000 + team * 37 + i * 1_013);
                AppearanceGenerator.GenerateGroup(inputs, outp);
                System.Array.Clear(cnt, 0, cnt.Length);
                int max = 0;
                foreach (var a in outp) max = System.Math.Max(max, ++cnt[a.HairStyle]);
                Assert.LessOrEqual(max, AppearanceGenerator.HairCapPerGroup, "팀 " + team);
                if (max >= 4) bad++;
            }
            Assert.LessOrEqual(bad, 50, "FR-32 같은 스타일 4명 이상 팀 ≤ 5%");
        }

        [Test]
        public void GenerateGroup_ManyPlayers_Terminates_CapRelaxed()
        {
            // 상한 3 × 30 = 90명을 넘으면 상한을 ⌈n/30⌉로 늘린다 — 무한 루프 없음
            var inputs = new BodyInput[95];
            for (int i = 0; i < inputs.Length; i++) inputs[i] = new BodyInput(7 + i);
            var outp = new PlayerAppearance[inputs.Length];
            AppearanceGenerator.GenerateGroup(inputs, outp);
            var cnt = new int[AppearanceGenerator.HairStyles];
            foreach (var a in outp) cnt[a.HairStyle]++;
            foreach (int c in cnt) Assert.LessOrEqual(c, 4);
            var seen = new HashSet<int>();
            foreach (var a in outp) Assert.IsTrue(seen.Add(a.ComboKey), "외형 조합 중복");
        }

        [Test]
        public void GenerateGroup_Deterministic_AndFirstUnchanged()
        {
            var inputs = new BodyInput[22];
            for (int i = 0; i < 22; i++) inputs[i] = new BodyInput(500 + i);
            var a = new PlayerAppearance[22];
            var b = new PlayerAppearance[22];
            AppearanceGenerator.GenerateGroup(inputs, a);
            AppearanceGenerator.GenerateGroup(inputs, b);
            CollectionAssert.AreEqual(a, b);
            Assert.AreEqual(AppearanceGenerator.Generate(inputs[0]), a[0]);
        }
    }
}
