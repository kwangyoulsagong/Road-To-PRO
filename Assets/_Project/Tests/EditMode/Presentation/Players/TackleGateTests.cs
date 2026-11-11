using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>specs/058 FR-50~52, AC-09: 태클 동작은 상대가 2.0 m 안일 때만.</summary>
    public class TackleGateTests
    {
        /// <summary>엔티티 3(홈)과 14(원정)가 dist만큼 떨어져 서 있는 1초 장면.</summary>
        static HighlightData Data(float dist)
        {
            var d = new HighlightData(10f, 31);
            for (int f = 0; f < 31; f++)
                for (int e = 0; e < HighlightData.Entities; e++)
                {
                    float x = e == 3 ? 0f : e == 14 ? dist : 40f + e, z = e == 3 || e == 14 ? 0f : 30f;
                    d.Set(f, e, x, z, 0f, true);
                }
            return d;
        }

        static AnimEvent Ev(SimEventKind k, int other = -1) => new AnimEvent(10.5f, 1, k, 3, other, BodyPart.RightFoot, ActionOutcome.Success, Vec3.Zero, Vec3.Zero);

        static bool HasTackle(List<int> l)
        {
            foreach (var i in l) if (AnimTestKit.Lib.Get(i).Id.StartsWith("tackle_")) return true;
            return false;
        }

        [Test]
        public void FarOpponent_Interception_NoTackleClip()
        {
            var into = new List<int>();
            foreach (var k in new[] { SimEventKind.Interception, SimEventKind.Block, SimEventKind.BallRecovered })
            {
                var fb = TackleGate.Filter(AnimTestKit.Lib, AnimTestKit.Map.Candidates(k), Data(5f), Ev(k), 3, TackleGate.DefaultMaxDistance, into, null);
                Assert.That(HasTackle(into), Is.False, k.ToString());
                Assert.That(into.Count, Is.GreaterThan(0));
                Assert.That(fb, Is.False);
            }
        }

        [Test]
        public void NearOpponent_Interception_TackleAllowed()
        {
            var into = new List<int>();
            TackleGate.Filter(AnimTestKit.Lib, AnimTestKit.Map.Candidates(SimEventKind.Interception), Data(1.5f), Ev(SimEventKind.Interception), 3, TackleGate.DefaultMaxDistance, into, null);
            Assert.That(HasTackle(into), Is.True);
        }

        [Test]
        public void Tackle_NearOnly_FarFallsBackToTouch()
        {
            var into = new List<int>();
            var near = TackleGate.Filter(AnimTestKit.Lib, AnimTestKit.Map.Candidates(SimEventKind.Tackle), Data(1.2f), Ev(SimEventKind.Tackle, 14), 3, TackleGate.DefaultMaxDistance, into, null);
            Assert.That(near, Is.False);
            Assert.That(HasTackle(into), Is.True);
            var far = TackleGate.Filter(AnimTestKit.Lib, AnimTestKit.Map.Candidates(SimEventKind.Tackle), Data(4f), Ev(SimEventKind.Tackle, 14), 3, TackleGate.DefaultMaxDistance, into, null);
            Assert.That(far, Is.True, "남는 후보 없음 → 대체");
            Assert.That(HasTackle(into), Is.False);
            Assert.That(into.Count, Is.EqualTo(1));
        }

        [Test]
        public void MaxDistance_EqualsTuningFile()
        {
            var reg = TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning", "highlight.json"));
            Assert.That(TuningBundle.Defaults(reg).Get("highlight.tackleMaxDistance"), Is.EqualTo(TackleGate.DefaultMaxDistance));
        }
    }
}
