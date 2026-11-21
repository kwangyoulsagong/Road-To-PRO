using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>얼굴 생성 (specs/056 FR-22·23·24, AC-04·11 로직).</summary>
    public class FaceGeneratorTests
    {
        public const string FacePath = "Assets/_Project/Presentation/Players/Generated/player.face.json";
        static FaceData _d;

        internal static FaceData Data
        {
            get
            {
                if (_d != null) return _d;
                var t = AssetDatabase.LoadAssetAtPath<TextAsset>(FacePath);
                Assert.IsNotNull(t, FacePath);
                return _d = FaceData.Parse(t.text);
            }
        }

        [Test]
        public void Data_AtLeast20Keys_7Beards_8BaseHeads_Landmarks()
        {
            // 062 FR-60·66: 수염 7종(056 4종 순서 + 신규 3종), 바탕 머리 8개(지역 태그), 표정 4종
            Assert.GreaterOrEqual(Data.Axes.Count * 2, 20);
            CollectionAssert.AreEqual(new[] { "none", "stubble", "short", "full", "mustache", "goatee", "heavy_stubble" }, Data.Beards);
            Assert.AreEqual(AppearanceGenerator.BaseHeads, Data.BaseHeads.Count);
            Assert.AreEqual(4, Data.Expressions.Count);
            Assert.GreaterOrEqual(Data.Landmarks.Count, 24);
        }

        [Test]
        public void SameId_SameFace()
        {
            for (int id = 1; id <= 300; id++)
                Assert.AreEqual(FaceGenerator.Generate(Data, id), FaceGenerator.Generate(Data, id), "id " + id);
            Assert.AreNotEqual(FaceGenerator.Generate(Data, 1), FaceGenerator.Generate(Data, 2));
        }

        [Test]
        public void AxisValues_WithinRange()
        {
            for (int id = 1; id <= 1000; id++)
            {
                var f = FaceGenerator.Generate(Data, id);
                for (int i = 0; i < f.Axis.Length; i++)
                    Assert.That(Mathf.Abs(f.Axis[i]), Is.LessThanOrEqualTo(Data.Axes[i].Range + 1e-6f), Data.Axes[i].Id);
            }
        }

        // 062: 수염은 PlayerAppearance.Beard로 옮겼다(plan '데이터') — 분포는 AppearanceTests.Beards_Seven_Distribution

        [Test]
        public void BaseHeads_BlendTwoToThree()
        {
            // FR-61·62: 선수 얼굴 = 바탕 머리 2~3개 혼합(정수 천분율 합 1000, 서로 다름, 주 바탕 = 가장 큰 가중치),
            // 주 바탕의 지역은 균등(지역 7개 각 1/7 ± 30 %, FR-62 [미정] 전까지 균등)
            const int n = 7000;
            var region = new int[AppearanceGenerator.Regions];
            int three = 0;
            for (int id = 1; id <= n; id++)
            {
                var a = AppearanceGenerator.Generate(new BodyInput(id));
                int[] hs = { a.BaseA, a.BaseB, a.BaseC };
                int[] ws = { a.WeightA, a.WeightB, a.WeightC };
                Assert.That(hs[0], Is.InRange(0, AppearanceGenerator.BaseHeads - 1));
                Assert.That(hs[1], Is.InRange(0, AppearanceGenerator.BaseHeads - 1));
                Assert.AreNotEqual(hs[0], hs[1]);
                Assert.Greater(ws[1], 0, "최소 2개");
                if (ws[2] > 0)
                {
                    three++;
                    Assert.That(hs[2], Is.InRange(0, AppearanceGenerator.BaseHeads - 1));
                    Assert.AreNotEqual(hs[2], hs[0]);
                    Assert.AreNotEqual(hs[2], hs[1]);
                }
                Assert.AreEqual(1000, ws[0] + ws[1] + ws[2], "천분율 합");
                Assert.GreaterOrEqual(ws[0], ws[1]);
                Assert.GreaterOrEqual(ws[1], ws[2]);
                region[AppearanceGenerator.RegionOf(hs[0])]++;
            }
            Assert.Greater(three, 0, "3개 혼합도 나온다");
            Assert.Less(three, n, "2개 혼합도 나온다");
            foreach (int c in region) Assert.That(c, Is.InRange(n / 7 * 7 / 10, n / 7 * 13 / 10), string.Join(",", region));
        }

        [Test]
        public void Inputs_NoPhotoOrName()
        {
            // FR-61: 생성 경로 입력에 사진·이름이 없다 — 외형·얼굴 생성 공개 메서드의 매개변수와 BodyInput 필드 형식을 검사
            var banned = new[] { typeof(string), typeof(byte[]), typeof(Texture), typeof(Texture2D), typeof(char[]) };
            var methods = new List<System.Reflection.MethodInfo>();
            methods.AddRange(typeof(AppearanceGenerator).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
            methods.AddRange(typeof(FaceGenerator).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
            methods.AddRange(typeof(AppearanceDistance).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
            foreach (var m in methods)
            {
                if (!m.Name.StartsWith("Generate")) continue;
                foreach (var prm in m.GetParameters())
                {
                    var t = prm.ParameterType.IsByRef ? prm.ParameterType.GetElementType() : prm.ParameterType;
                    CollectionAssert.DoesNotContain(banned, t, $"{m.Name}({prm.Name})");
                    string nm = prm.Name.ToLowerInvariant();
                    Assert.IsFalse(nm.Contains("name") || nm.Contains("photo") || nm.Contains("image"), $"{m.Name}({prm.Name})");
                }
            }
            foreach (var f in typeof(BodyInput).GetFields())
            {
                CollectionAssert.DoesNotContain(banned, f.FieldType, "BodyInput." + f.Name);
                Assert.IsFalse(f.Name.ToLowerInvariant().Contains("name"), "BodyInput." + f.Name);
            }
        }

        [Test]
        public void BodyShapeMap_Ranges()
        {
            for (int id = 1; id <= 500; id++)
            {
                var a = AppearanceGenerator.Generate(new BodyInput(id));
                foreach (var w in new[] { BodyShapeMap.Thin(a), BodyShapeMap.Muscular(a), BodyShapeMap.Heavy(a) })
                    Assert.That(w, Is.InRange(0f, 1f));
            }
            var m = new PlayerAppearance(180, 75, 0, 0.25f, 0, 0, 0, 0, 0);
            Assert.AreEqual(0f, BodyShapeMap.Muscular(m), 1e-6f);
            m = new PlayerAppearance(180, 75, 0, 1f, 0, 0, 0, 0, 0);
            Assert.AreEqual(1f, BodyShapeMap.Muscular(m), 1e-6f);
        }
    }

    /// <summary>AC-04: 같은 팀 22명 얼굴 랜드마크 거리 벡터 최소 쌍 거리 ≥ 평균 쌍 거리 25 %, 같은 id 재생성 동일.</summary>
    public class FaceLandmarkTests
    {
        static void Team(int start, FaceShape[] outp, FaceLandmarks lm)
        {
            var ids = new int[22];
            for (int i = 0; i < 22; i++) ids[i] = start + i * 7;
            FaceGenerator.GenerateGroup(FaceGeneratorTests.Data, lm, ids, outp);
        }

        [Test]
        public void Team22_MinPairDistance()
        {
            var lm = new FaceLandmarks(FaceGeneratorTests.Data);
            Assert.Greater(lm.MeanPairDistance, 0.0005f, "얼굴 축이 랜드마크를 거의 안 움직인다");
            var team = new FaceShape[22];
            foreach (int start in new[] { 1, 1000, 52_017, 777_777 })
            {
                Team(start, team, lm);
                float min = float.MaxValue;
                var v = new List<float[]>();
                foreach (var f in team) v.Add(lm.DistanceVector(f));
                for (int i = 0; i < 22; i++)
                    for (int j = i + 1; j < 22; j++)
                        min = Mathf.Min(min, FaceLandmarks.Distance(v[i], v[j]));
                Assert.GreaterOrEqual(min, lm.MinDistance, $"팀 {start}: 최소 {min * 1000:0.00} mm, 기준 {lm.MinDistance * 1000:0.00} mm");
            }
        }

        [Test]
        public void SameIds_SameTeam()
        {
            var lm = new FaceLandmarks(FaceGeneratorTests.Data);
            var a = new FaceShape[22];
            var b = new FaceShape[22];
            Team(5, a, lm);
            Team(5, b, lm);
            for (int i = 0; i < 22; i++) Assert.AreEqual(a[i], b[i], "선수 " + i);
        }
    }
}
