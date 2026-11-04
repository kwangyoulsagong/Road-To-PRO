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
        public void Data_AtLeast20Keys_4Beards_Landmarks()
        {
            Assert.GreaterOrEqual(Data.Axes.Count * 2, 20);
            CollectionAssert.AreEqual(new[] { "none", "stubble", "short", "full" }, Data.Beards);
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

        [Test]
        public void Beard_RoughlyUniform()
        {
            var n = new int[Data.Beards.Count];
            for (int id = 1; id <= 4000; id++) n[FaceGenerator.Generate(Data, id).Beard]++;
            foreach (var c in n) Assert.That(c, Is.InRange(850, 1150), string.Join(",", n));
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
