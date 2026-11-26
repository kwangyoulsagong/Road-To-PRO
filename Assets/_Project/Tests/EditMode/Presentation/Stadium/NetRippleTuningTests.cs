using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Core.Tuning;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Stadium.Net;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>specs/059 FR-56 · AC-11: 잘못된 그물 튜닝 → 정지 모양 + 경고 1회, 예외 0.</summary>
    public class NetRippleTuningTests
    {
        sealed class CountLogger : ILogger
        {
            public readonly List<string> Warnings = new List<string>();
            public bool IsEnabled(LogLevel level) => true;
            public void Log(LogLevel level, string message) { if (level == LogLevel.Warning) Warnings.Add(message); }
        }

        static TuningBundle Bundle(string file) =>
            TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(Directory.GetCurrentDirectory(), "Data", "tuning", file)));

        /// <summary>goalnet.json + ball.json 기본값.</summary>
        public static NetRippleTuning Defaults(ILogger log = null)
        {
            var net = Bundle("goalnet.json");
            var ball = Bundle("ball.json");
            return NetRippleTuning.Create(net.Get, ball.Get, log);
        }

        [Test]
        public void Defaults_Valid_NoWarning()
        {
            var log = new CountLogger();
            var t = Defaults(log);
            Assert.IsTrue(t.Valid, t.Problem);
            Assert.AreEqual(0, log.Warnings.Count);
            Assert.AreEqual(2f, t.Shape.Depth, 1e-6f, "ball.net.depth");
            Assert.AreEqual(0.12f, t.CellSize, 1e-6f);
        }

        /// <summary>튜닝 파일 없이 재생할 때 쓰는 클래스 기본값이 goalnet.json·ball.json 기본값과 같다 (두 원본이 어긋나지 않게).</summary>
        [Test]
        public void ForGoal_MatchesJsonDefaults()
        {
            var json = Defaults();
            var fallback = NetRippleTuning.ForGoal(GoalBox.Default(RoadToPro.Core.Pitch.PitchSpec.Default));
            Assert.IsTrue(fallback.Valid, fallback.Problem);
            foreach (var f in typeof(NetRippleTuning).GetFields())
                if (f.FieldType == typeof(float) || f.FieldType == typeof(int))
                    Assert.AreEqual(f.GetValue(json), f.GetValue(fallback), f.Name);
            foreach (var f in typeof(NetShapeParams).GetFields())
                Assert.AreEqual(f.GetValue(json.Shape), f.GetValue(fallback.Shape), "Shape." + f.Name);
        }

        static Func<string, float> Override(string id, float value)
        {
            var net = Bundle("goalnet.json");
            return k => k == id ? value : net.Get(k);
        }

        [TestCase("net.cellSize", 0f)]
        [TestCase("net.cellSize", -0.1f)]
        [TestCase("net.gridCell", 0f)]
        [TestCase("net.slackBack", 0.9f)]
        [TestCase("net.iterations", 0f)]
        public void Invalid_RestShape_WarnOnce_NoThrow(string id, float value)
        {
            var log = new CountLogger();
            NetRippleTuning t = null;
            Assert.DoesNotThrow(() => t = NetRippleTuning.Create(Override(id, value), Bundle("ball.json").Get, log));
            Assert.IsFalse(t.Valid);
            Assert.AreEqual(1, log.Warnings.Count, "경고 1회");
            // 잘못된 튜닝이면 굽지 않는다 → 그물은 정지 모양 (FR-56)
            var baker = new NetSceneBaker(t, GoalBox.Default(RoadToPro.Core.Pitch.PitchSpec.Default));
            var cs = new List<NetContact> { new NetContact(0d, 1, NetFace.Back, new Vec3(54.5f, 1f, 0f), 25f) };
            NetBake[] bakes = null;
            Assert.DoesNotThrow(() => bakes = baker.Bake(new RoadToPro.Presentation.Players.Logic.HighlightData(0f, 31), cs));
            Assert.IsNull(bakes[0]);
            Assert.IsNull(bakes[1]);
            Assert.AreEqual(1, log.Warnings.Count, "굽기 중 추가 경고 없음");
        }

        [TestCase(0.2f)]
        [TestCase(5f)]
        public void Invalid_Depth_OutOfRange(float depth)
        {
            var log = new CountLogger();
            var ball = Bundle("ball.json");
            var t = NetRippleTuning.Create(Bundle("goalnet.json").Get, k => k == "ball.net.depth" ? depth : ball.Get(k), log);
            Assert.IsFalse(t.Valid);
            Assert.AreEqual(1, log.Warnings.Count);
        }
    }
}
