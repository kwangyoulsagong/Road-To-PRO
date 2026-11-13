using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.Verification;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-01: 압박 강도 = 독립 참조 구현(Tools/devicefarm/ref/bekkers_pressure.py) ±1e-4.</summary>
    public class PressureModelTests
    {
        static ActionParams P => ActionParams.Defaults;

        [Test]
        public void MatchesReference_20Fixtures_1e4()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "devicefarm", "ref", "pressure-ref.json");
            var root = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(path));
            var w = System.Convert.ToSingle(root["pitchWidth"]);
            var cases = (System.Collections.Generic.List<object>)root["cases"];
            Assert.AreEqual(20, cases.Count);
            var pos = new Vec2[11];
            var vel = new Vec2[11];
            foreach (System.Collections.Generic.Dictionary<string, object> c in cases)
            {
                var t = (System.Collections.Generic.List<object>)c["target"];
                var tv = (System.Collections.Generic.List<object>)c["targetVel"];
                var defs = (System.Collections.Generic.List<object>)c["defenders"];
                for (int i = 0; i < defs.Count; i++)
                {
                    var d = (System.Collections.Generic.List<object>)defs[i];
                    pos[i] = new Vec2(F(d[0]), F(d[1]));
                    vel[i] = new Vec2(F(d[2]), F(d[3]));
                }
                var got = PressureModel.Team(P, new Vec2(F(t[0]), F(t[1])), new Vec2(F(tv[0]), F(tv[1])), pos, vel, defs.Count, w);
                Assert.AreEqual(F(c["expected"]), got, 1e-4f);
            }
        }

        static float F(object o) => System.Convert.ToSingle(o, System.Globalization.CultureInfo.InvariantCulture);

        [Test]
        public void SlowDefender_Zero()
        {
            var target = new Vec2(50f, 34f);
            Assert.AreEqual(0f, PressureModel.Pair(P, new Vec2(49f, 34f), new Vec2(1.99f, 0f), target, Vec2.Zero));
            Assert.Greater(PressureModel.Pair(P, new Vec2(49f, 34f), new Vec2(2.01f, 0f), target, Vec2.Zero), 0.5f);
            var pos = new[] { new Vec2(49f, 34f) };
            var vel = new[] { new Vec2(1f, 0f) };
            Assert.AreEqual(0f, PressureModel.Team(P, target, Vec2.Zero, pos, vel, 1, 68f));
        }

        [Test]
        public void Touchline_Bonus()
        {
            var pos = new Vec2[0];
            var vel = new Vec2[0];
            Assert.AreEqual(0f, PressureModel.Team(P, new Vec2(50f, 34f), Vec2.Zero, pos, vel, 0, 68f));
            Assert.AreEqual(0.15f, PressureModel.Team(P, new Vec2(50f, 2f), Vec2.Zero, pos, vel, 0, 68f), 1e-6f);
            Assert.AreEqual(0.15f, PressureModel.Team(P, new Vec2(50f, 66f), Vec2.Zero, pos, vel, 0, 68f), 1e-6f);
        }

        [Test]
        public void Carrier_TakesMaxOfPlayerAndBall()
        {
            var pos = new[] { new Vec2(52f, 34f) };
            var vel = new[] { new Vec2(-5f, 0f) };
            var a = PressureModel.Team(P, new Vec2(40f, 34f), Vec2.Zero, pos, vel, 1, 68f);
            var b = PressureModel.Team(P, new Vec2(50f, 34f), Vec2.Zero, pos, vel, 1, 68f);
            Assert.AreEqual(System.Math.Max(a, b), PressureModel.Carrier(P, new Vec2(40f, 34f), Vec2.Zero, new Vec2(50f, 34f), Vec2.Zero, pos, vel, 1, 68f), 1e-7f);
        }
    }
}
