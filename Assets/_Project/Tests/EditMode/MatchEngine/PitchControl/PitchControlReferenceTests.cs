using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.PitchControl;
using RoadToPro.Verification;

namespace RoadToPro.Tests.EditMode.MatchEngine.PitchControl
{
    /// <summary>specs/007 AC-01: 원 논문 모드 지점 질의 = 독립 참조 구현(Tools/pitchcontrol/reference.py) ±0.01.</summary>
    public class PitchControlReferenceTests
    {
        static float F(object o) => System.Convert.ToSingle(o);

        [Test]
        public void PaperMode_FiveLayouts_MatchReference()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Data", "tests", "pitchcontrol-reference.json");
            var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(path));
            var layouts = (List<object>)root["layouts"];
            Assert.AreEqual(5, layouts.Count);
            var p = PitchControlParams.PaperDefaults();
            var q = new PitchControlQuery(p);
            var pl = new ControlPlayers { PaperArrival = true };
            var worst = 0f;
            var n = 0;
            foreach (Dictionary<string, object> lay in layouts)
            {
                pl.Clear();
                var players = (List<object>)lay["players"];
                var counts = new int[2];
                foreach (Dictionary<string, object> o in players)
                {
                    var team = System.Convert.ToInt32(o["team"]);
                    var slot = team * 11 + counts[team]++;
                    pl.SetPaper(slot, team, (bool)o["gk"], new Vec2(F(o["x"]), F(o["y"])), new Vec2(F(o["vx"]), F(o["vy"])), p);
                }
                var ballL = (List<object>)lay["ball"];
                var ball = new Vec2(F(ballL[0]), F(ballL[1]));
                var attacking = System.Convert.ToInt32(lay["attacking"]);
                foreach (Dictionary<string, object> pt in (List<object>)lay["points"])
                {
                    var target = new Vec2(F(pt["x"]), F(pt["y"]));
                    var tb = (target - ball).Length() / p.GridBallSpeed;
                    var r = q.Point(pl, attacking, target, tb);
                    var ea = F(pt["att"]);
                    var ed = F(pt["defn"]);
                    var err = System.Math.Max(System.Math.Abs(r.Att - ea), System.Math.Abs(r.Def - ed));
                    if (err > worst) worst = err;
                    n++;
                    Assert.AreEqual(ea, r.Att, 0.01f, $"{lay["name"]} ({target.X},{target.Y}) 공격");
                    Assert.AreEqual(ed, r.Def, 0.01f, $"{lay["name"]} ({target.X},{target.Y}) 수비");
                }
            }
            TestContext.WriteLine($"AC-01 참조 대비 최대 오차 {worst:F5} ({n}지점)");
        }
    }
}
