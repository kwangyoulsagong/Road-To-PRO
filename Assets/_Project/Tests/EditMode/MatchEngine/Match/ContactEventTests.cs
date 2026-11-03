using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-23 (FR-82): 공 접촉 이벤트 1만 건 표본에서 사용 부위·서브스텝 시각 누락 0, 재실행 시 비트 단위로 같다.</summary>
    public class ContactEventTests
    {
        static bool IsContact(SimEventKind k) =>
            k == SimEventKind.Pass || k == SimEventKind.Shot || k == SimEventKind.Tackle || k == SimEventKind.Clearance
            || k == SimEventKind.PassReceived || k == SimEventKind.Interception || k == SimEventKind.BallRecovered;

        static ulong ContactHash(MatchSim sim, ref int count, ref int missing)
        {
            var h = StableHasher.Create();
            for (int i = 0; i < sim.Log.Count; i++)
            {
                var e = sim.Log[i];
                if (!IsContact(e.Kind)) continue;
                count++;
                if (e.Part == BodyPart.None || e.Substep < 0 || e.Substep > 9) missing++;
                e.AddTo(ref h);
            }
            return h.Value;
        }

        [Test]
        public void TenThousandContacts_NoMissingPartOrSubstep_Repeatable()
        {
            int count = 0, missing = 0;
            ContactHash(Reference(), ref count, ref missing);
            ulong again = 0, first = 0;
            for (ulong seed = 61; count < 10_000; seed++)
            {
                var sim = Sim(seed);
                sim.RunToEnd();
                var c0 = count;
                var h = ContactHash(sim, ref count, ref missing);
                if (seed == 61)
                {
                    first = h;
                    var re = Sim(seed);
                    re.RunToEnd();
                    int c2 = 0, m2 = 0;
                    again = ContactHash(re, ref c2, ref m2);
                    Assert.That(c2, Is.EqualTo(count - c0));
                }
            }
            Assert.That(count, Is.GreaterThanOrEqualTo(10_000));
            Assert.That(missing, Is.EqualTo(0));
            Assert.That(again, Is.EqualTo(first), "같은 경기 재실행 시 접촉 이벤트 비트 동일");
        }
    }
}
