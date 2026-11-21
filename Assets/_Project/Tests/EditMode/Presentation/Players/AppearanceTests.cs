using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>외형 결정론·중복 없음 (specs/043 FR-01~03, AC-01) + 헤어 30종·분포·장면 상한 (specs/057 FR-30~34, AC-04)
    /// + 062 헤어 35종·분류표·8색·컬러 포인트 (FR-50~55, AC-06·14).</summary>
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

        // 062 FR-52: 30~34 신규 (Art/Blender/scripts/player/hair.py STYLE_SPEC와 같은 순서)
        static readonly string[] Names062 =
            { "Hair_PartedTwoBlock", "Hair_CommaPerm", "Hair_VolumeCurlyLong", "Hair_StraightCornrows", "Hair_ZigzagCornrows" };
        // 057 분포 계열 (hair.py family): 30 짧은 · 31 중간 · 32 긴 · 33·34 짧은
        static readonly HairFamily[] Families062 = { HairFamily.Short, HairFamily.Medium, HairFamily.Long, HairFamily.Short, HairFamily.Short };

        [Test]
        public void HairStyles_ThirtyFive_WeightsSum_FamilyShares()
        {
            // 057 FR-30·31 + 062 FR-52: 35종, 신규 5종 가중치 = 같은 계열 평균 (T02 정리), 짧은 ≥ 55 %·긴·묶음 ≤ 15 % 유지
            Assert.AreEqual(35, AppearanceGenerator.HairStyles);
            for (int i = 0; i < Families062.Length; i++) Assert.AreEqual(Families062[i], AppearanceGenerator.HairFamilyOf(30 + i), "계열 " + (30 + i));
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
            for (int i = 0; i < Names062.Length; i++)
                Assert.AreEqual(Names062[i], RoadToPro.Presentation.Players.PlayerLook.HairMeshes[30 + i], "인덱스 " + (30 + i));
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
            // 상한 3 × 스타일 수를 넘지 않는 95명: 상한 3 그대로(062: 35종 → 105명까지), 무한 루프 없음
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

        // ---------------------------------------------------------------- specs/062 체형 5축 (T10)

        [Test]
        public void BodyShoulderWidth_ThousandTeams_MeanStdDev()
        {
            // FR-04 개정 (2026-11-20): 1,000팀 × 22명, 팀 안 정면 어깨 너비/키 표준편차의 1,000팀 평균 ≥ 0.010
            var roles = new[] { BodyRole.Goalkeeper, BodyRole.Goalkeeper, BodyRole.Defender, BodyRole.Defender, BodyRole.Defender,
                BodyRole.Defender, BodyRole.Defender, BodyRole.Defender, BodyRole.Defender, BodyRole.Midfielder, BodyRole.Midfielder,
                BodyRole.Midfielder, BodyRole.Midfielder, BodyRole.Midfielder, BodyRole.Midfielder, BodyRole.Midfielder,
                BodyRole.Forward, BodyRole.Forward, BodyRole.Forward, BodyRole.Forward, BodyRole.Forward, BodyRole.Forward };
            double sum = 0;
            var w = new double[22];
            for (int team = 0; team < 1000; team++)
            {
                double mean = 0;
                for (int k = 0; k < 22; k++)
                {
                    var a = AppearanceGenerator.Generate(new BodyInput(team * 22 + k + 1, role: roles[k]));
                    w[k] = BodyShapeMap.ShoulderWidthRatio(a);
                    mean += w[k] / 22.0;
                }
                double v = 0;
                for (int k = 0; k < 22; k++) v += (w[k] - mean) * (w[k] - mean) / 22.0;
                sum += System.Math.Sqrt(v);
            }
            Assert.GreaterOrEqual(sum / 1000.0, 0.010);
        }

        [Test]
        public void SameHeight_DifferentSilhouette()
        {
            // FR-04: 같은 키 ±2 cm 선수끼리도 근육·체중·어깨 축 차이로 실루엣이 달라진다 — 그런 쌍의 절반 이상이 어깨 너비 차 ≥ 0.005
            var list = new List<PlayerAppearance>();
            for (int id = 1; id <= 400; id++) list.Add(AppearanceGenerator.Generate(new BodyInput(id)));
            int pairs = 0, differ = 0;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (System.Math.Abs(list[i].HeightCm - list[j].HeightCm) > 2f) continue;
                    pairs++;
                    if (System.Math.Abs(BodyShapeMap.ShoulderWidthRatio(list[i]) - BodyShapeMap.ShoulderWidthRatio(list[j])) >= 0.005f) differ++;
                }
            Assert.Greater(pairs, 100);
            Assert.GreaterOrEqual(differ, pairs / 2);
        }

        [Test]
        public void BroadLimb_InRange_MuscleDefHalfToOne()
        {
            for (int id = 1; id <= 500; id++)
            {
                var a = AppearanceGenerator.Generate(new BodyInput(id));
                Assert.That(a.Broad, Is.InRange(0f, 1f));
                Assert.That(a.Limb, Is.InRange(0f, 1f));
                Assert.That(BodyShapeMap.MuscleDef(a), Is.InRange(0.5f, 1f));
            }
        }

        // ---------------------------------------------------------------- specs/062 헤어 35종·분류표·8색·컬러 포인트 (T23, AC-06·14)
        // T24가 구현할 API: HairStyles 35, HairColors 8 (0~5 자연 · 5 회색 · 6 탈색 금발 · 7 백금), ColorPoints 4 (0 없음 + 3색),
        // PlayerAppearance.ColorPoint, AppearanceGenerator.IsBleached·ColorPointTarget·HairColorGrey/Bleach/Platinum,
        // HairTaxonomy.Types (FR-51 분류표 데이터) · HairTaxonomy.GroupOf(style), PlayerLook.ColorPointColors (가상 팔레트 3색).

        // FR-51 계열 (hair.py STYLE_SPEC group과 같다)
        static readonly HairGroup[] Groups062 =
        {
            HairGroup.Short, HairGroup.Short, HairGroup.Long, HairGroup.Curly, HairGroup.Braid, HairGroup.Fade, HairGroup.Curly,
            HairGroup.Fade, HairGroup.Braid, HairGroup.Bald, HairGroup.Short, HairGroup.Short, HairGroup.Fade, HairGroup.Medium,
            HairGroup.Medium, HairGroup.Medium, HairGroup.Short, HairGroup.Short, HairGroup.Curly, HairGroup.Braid, HairGroup.Curly,
            HairGroup.Braid, HairGroup.Braid, HairGroup.Medium, HairGroup.Short, HairGroup.Short, HairGroup.Short, HairGroup.Curly,
            HairGroup.Braid, HairGroup.Long, HairGroup.Fade, HairGroup.Medium, HairGroup.Long, HairGroup.Braid, HairGroup.Braid,
        };
        // FR-54 컬러 포인트 대상 (hair.py cp가 있는 스타일: 페이드·모히칸·언더컷·곱슬 탑·박스 브레이드·가르마 투블록)
        static readonly int[] ColorPointStyles = { 5, 7, 12, 27, 28, 30 };
        const int N100K = 100_000;

        static PlayerAppearance[] Population(int n)
        {
            var arr = new PlayerAppearance[n];
            for (int id = 1; id <= n; id++) arr[id - 1] = AppearanceGenerator.Generate(new BodyInput(id));
            return arr;
        }

        [Test]
        public void HairTaxonomy_StarTypes_Mapped()
        {
            // FR-51: 기존 30종 + 신규 5종이 전부 분류표 유형에 매핑되고, ★ 필수 유형 10개가 스타일(또는 스타일 + 색 조합)로 존재한다
            var mapped = new bool[AppearanceGenerator.HairStyles];
            int stars = 0;
            foreach (var t in HairTaxonomy.Types)
            {
                Assert.IsFalse(string.IsNullOrEmpty(t.Name));
                Assert.Greater(t.Styles.Length, 0, t.Name);
                foreach (int st in t.Styles)
                {
                    Assert.That(st, Is.InRange(0, AppearanceGenerator.HairStyles - 1), t.Name);
                    mapped[st] = true;
                }
                if (t.Star) stars++;
            }
            for (int i = 0; i < mapped.Length; i++) Assert.IsTrue(mapped[i], "분류표에 없는 스타일 " + i);
            Assert.AreEqual(10, stars, "FR-51 ★ 유형 10개");
            for (int i = 0; i < Groups062.Length; i++) Assert.AreEqual(Groups062[i], HairTaxonomy.GroupOf(i), "계열 " + i);
            // 신규 5종은 ★ 유형에 들어 있다 (가르마 투블록·쉼표머리 펌·볼륨 곱슬 장발·브레이드 변형 2)
            for (int st = 30; st < 35; st++)
            {
                bool inStar = false;
                foreach (var t in HairTaxonomy.Types) if (t.Star && System.Array.IndexOf(t.Styles, st) >= 0) inStar = true;
                Assert.IsTrue(inStar, "신규 스타일 " + st + "이 ★ 유형에 없다");
            }
            // 색 조건이 붙은 ★ 유형(탈색 곱슬·탈색 투블록/모히칸·탈색/염색 브레이드)은 실제 생성에서 나온다
            var pop = Population(N100K);
            foreach (var t in HairTaxonomy.Types)
            {
                if (t.Color == HairColorReq.Any) continue;
                bool found = false;
                foreach (var a in pop)
                {
                    if (System.Array.IndexOf(t.Styles, a.HairStyle) < 0) continue;
                    bool bleached = AppearanceGenerator.IsBleached(a.HairColor);
                    if (t.Color == HairColorReq.Bleached && bleached) found = true;
                    if (t.Color == HairColorReq.ColorPoint && a.ColorPoint > 0) found = true;
                    if (t.Color == HairColorReq.BleachedOrColorPoint && (bleached || a.ColorPoint > 0)) found = true;
                    if (found) break;
                }
                Assert.IsTrue(found, t.Name + " 조합이 10만 명에서 한 번도 안 나온다");
            }
        }

        [Test]
        public void HairColors_Eight_Bleach_Platinum()
        {
            // FR-53: 자연 6색 + 탈색 금발·백금 = 8색. 탈색 금발은 자연 금발보다 밝고, 백금은 더 밝고 채도가 낮다
            Assert.AreEqual(8, AppearanceGenerator.HairColors);
            var pal = RoadToPro.Presentation.Players.PlayerLook.HairColors;
            Assert.AreEqual(AppearanceGenerator.HairColors, pal.Length);
            Assert.AreEqual(5, AppearanceGenerator.HairColorGrey);
            Assert.AreEqual(6, AppearanceGenerator.HairColorBleach);
            Assert.AreEqual(7, AppearanceGenerator.HairColorPlatinum);
            for (int c = 0; c < 8; c++) Assert.AreEqual(c >= 6, AppearanceGenerator.IsBleached(c), "탈색 구분 " + c);
            float Lum(UnityEngine.Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            float Sat(UnityEngine.Color c) { UnityEngine.Color.RGBToHSV(c, out _, out float sv, out _); return sv; }
            Assert.Greater(Lum(pal[6]), Lum(pal[3]), "탈색 금발 > 자연 금발 밝기");
            Assert.Greater(Lum(pal[7]), Lum(pal[6]), "백금 > 탈색 금발 밝기");
            Assert.Less(Sat(pal[7]), Sat(pal[6]), "백금은 채도가 낮다");
            // FR-54 컬러 포인트 가상 팔레트 3색 (백금·애시·붉은 계열)
            var cp = RoadToPro.Presentation.Players.PlayerLook.ColorPointColors;
            Assert.AreEqual(AppearanceGenerator.ColorPoints - 1, cp.Length);
            Assert.AreEqual(3, cp.Length);
            Assert.Greater(cp[2].r, cp[2].b + 0.15f, "세 번째는 붉은 계열");
            // 8색 전부 실제로 나온다
            var seen = new int[8];
            foreach (var a in Population(20_000)) seen[a.HairColor]++;
            for (int c = 0; c < 8; c++) Assert.Greater(seen[c], 0, "안 나오는 색 " + c);
        }

        [Test]
        public void ColorPoint_Rates_WithinCaps()
        {
            // FR-54 (T02 정리): 10만 명에서 컬러 포인트 ≤ 8 %, 탈색(전체 탈색 색) ≤ 10 %, 둘 다 실제로 나온다.
            // 컬러 포인트는 대상 스타일에만, 대상 스타일 6종 전부에서 나온다.
            int cpN = 0, bleachN = 0;
            var perStyle = new int[AppearanceGenerator.HairStyles];
            foreach (var a in Population(N100K))
            {
                Assert.That(a.ColorPoint, Is.InRange(0, AppearanceGenerator.ColorPoints - 1));
                if (AppearanceGenerator.IsBleached(a.HairColor)) bleachN++;
                if (a.ColorPoint == 0) continue;
                cpN++;
                perStyle[a.HairStyle]++;
                Assert.IsTrue(AppearanceGenerator.ColorPointTarget(a.HairStyle), "대상 아닌 스타일에 컬러 포인트 " + a.HairStyle);
            }
            Assert.LessOrEqual(cpN, N100K * 8 / 100, "컬러 포인트 ≤ 8 %");
            Assert.LessOrEqual(bleachN, N100K * 10 / 100, "탈색 ≤ 10 %");
            Assert.Greater(cpN, N100K / 200, "컬러 포인트가 0.5 % 이상은 나온다");
            Assert.Greater(bleachN, N100K / 200, "탈색이 0.5 % 이상은 나온다");
            for (int st = 0; st < AppearanceGenerator.HairStyles; st++)
                Assert.AreEqual(System.Array.IndexOf(ColorPointStyles, st) >= 0, AppearanceGenerator.ColorPointTarget(st), "대상 표 " + st);
            foreach (int st in ColorPointStyles) Assert.Greater(perStyle[st], 0, "컬러 포인트가 안 나오는 대상 스타일 " + st);
        }

        [Test]
        public void NoGreyWithColorPoint()
        {
            // plan '어색한 조합 제외': 회색 머리 + 컬러 포인트 없음 (삭발도 컬러 포인트 없음)
            foreach (var a in Population(N100K))
            {
                if (a.ColorPoint == 0) continue;
                Assert.AreNotEqual(AppearanceGenerator.HairColorGrey, a.HairColor, a.ToString());
                Assert.AreNotEqual(9, a.HairStyle, "삭발 + 컬러 포인트");
            }
        }

        [Test]
        public void GenerateGroup_Cap_35Styles_Terminates()
        {
            // 상한 3 × 35 = 105명을 넘는 110명: 상한 ⌈110/35⌉ = 4로 늘어나고 끝난다, 조합 중복 없음(컬러 포인트 포함 조합 번호)
            var inputs = new BodyInput[110];
            for (int i = 0; i < inputs.Length; i++) inputs[i] = new BodyInput(9 + i * 3);
            var outp = new PlayerAppearance[inputs.Length];
            AppearanceGenerator.GenerateGroup(inputs, outp);
            var cnt = new int[AppearanceGenerator.HairStyles];
            var seen = new HashSet<long>();
            foreach (var a in outp)
            {
                cnt[a.HairStyle]++;
                Assert.IsTrue(seen.Add(a.ComboKey), "외형 조합 중복");
                Assert.IsTrue(a.ColorPoint == 0 || AppearanceGenerator.ColorPointTarget(a.HairStyle), "밀린 조합도 컬러 포인트 대상 규칙을 지킨다");
                Assert.IsTrue(a.ColorPoint == 0 || a.HairColor != AppearanceGenerator.HairColorGrey, "밀린 조합도 회색 + 컬러 포인트 없음");
            }
            foreach (int c in cnt) Assert.LessOrEqual(c, 4);
        }

        [Test]
        public void Generate_SameId_Same()
        {
            // FR-55: 같은 id → 같은 스타일·색·컬러 포인트, 조합 번호 왕복(WithCombo)도 컬러 포인트를 보존한다
            for (int id = 1; id <= 2000; id++)
            {
                var a = AppearanceGenerator.Generate(new BodyInput(id));
                var b = AppearanceGenerator.Generate(new BodyInput(id));
                Assert.AreEqual(a, b, "id " + id);
                Assert.AreEqual(a.ColorPoint, b.ColorPoint);
                Assert.AreEqual(a, a.WithCombo(a.ComboKey), "조합 왕복 id " + id);
            }
        }
    }
}
