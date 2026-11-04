using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using Unity.PerformanceTesting;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.Performance.Presentation
{
    /// <summary>specs/043 AC-17: 키트 합성 팀당 ≤ 50 ms (로딩 중). 24명 칸을 다 채운 최악 경우.</summary>
    public class KitCompositorPerfTests
    {
        const string Fonts = "Assets/_Project/Presentation/Players/Fonts";
        public const double BudgetMs = 50.0;

        [Test, Performance]
        public void PerTeam_Under50ms()
        {
            var comp = new KitCompositor(GlyphAtlasData.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(Fonts + "/kit-glyphs.json").text),
                AssetDatabase.LoadAssetAtPath<Texture2D>(Fonts + "/kit-glyphs-names.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(Fonts + "/kit-glyphs-numbers.png"));
            var squad = new List<KitEntry>();
            for (int i = 0; i < 24; i++) squad.Add(new KitEntry(1 + i * 4, i % 2 == 0 ? "Alexandersen" : "김민준"));
            Object.DestroyImmediate(comp.ComposeTeam(squad, NullLogger.Instance)); // 데우기
            var samples = new List<double>();
            var g = new SampleGroup("KitCompose.PerTeam", SampleUnit.Millisecond);
            for (int r = 0; r < 7; r++)
            {
                var sw = Stopwatch.StartNew();
                var t = comp.ComposeTeam(squad, NullLogger.Instance);
                sw.Stop();
                Object.DestroyImmediate(t);
                samples.Add(sw.Elapsed.TotalMilliseconds);
                Measure.Custom(g, sw.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            double median = samples[samples.Count / 2];
            TestContext.WriteLine($"키트 합성 중앙값 {median:0.00} ms (예산 {BudgetMs} ms)");
            Assert.LessOrEqual(median, BudgetMs);
        }
    }
}
