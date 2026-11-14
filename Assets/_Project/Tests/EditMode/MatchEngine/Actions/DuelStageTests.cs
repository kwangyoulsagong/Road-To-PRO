using NUnit.Framework;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 T07: 실경기(010 파이프라인)에서 결투가 켜지고, 테이크온·태클·몸싸움·접촉이 규칙대로 나온다.</summary>
    public class DuelStageTests
    {
        [Test]
        public void Match10min_DuelsActive_ContactsLinked()
        {
            var sim = ShapeTestKit.Sim();
            sim.StepTicks(6_000);
            var ctx = sim.Context;
            var d = ctx.Actions.Duels;
            Assert.IsTrue(d.Enabled);
            var kinds = new int[6];
            int contacts = 0, unlinked = 0, takeOnEvents = 0, tackles = 0;
            for (int i = 0; i < ctx.Log.Count; i++)
            {
                var e = ctx.Log[i];
                if (e.Kind == SimEventKind.Contact)
                {
                    contacts++;
                    kinds[e.A]++;
                    if (e.B <= 0 || ctx.Actions.Details.Contacts.EventSeq(e.B - 1) != e.Seq) unlinked++;
                    Assert.That(e.F0, Is.InRange(0f, 1f));
                    Assert.That(e.F1, Is.InRange(0f, 1f));
                }
                else if (e.Kind == SimEventKind.Dribble) takeOnEvents++;
                else if (e.Kind == SimEventKind.Tackle) tackles++;
            }
            TestContext.WriteLine($"터치 {d.Touches}, 탈취 {d.Steals}/{d.StealChecks}, 테이크온 {d.TakeOns}, 태클 판정 {d.TacklesResolved}(성공 {d.TacklesWon}, 늦은 {d.LateTackles}), 몸싸움 {d.ShieldChecks}(짐 {d.ShieldLosses}), 접촉 {contacts} [태클 {kinds[0]} 테이크온 {kinds[1]} 몸싸움 {kinds[3]}], 이격 해제 {d.GapReleases}");
            Assert.Greater(d.Touches, 20, "10분 경기 드리블 터치 (긴 터치는 이격이 2~4 s 열린다)");
            Assert.Greater(d.TacklesResolved, 0);
            Assert.AreEqual(d.TakeOns, takeOnEvents, "테이크온마다 Dribble 이벤트 1건");
            Assert.Greater(contacts, 0);
            Assert.AreEqual(0, unlinked, "모든 Contact 이벤트가 상세 기록에 연결 (FR-13·008 FR-72)");
            Assert.AreEqual(d.Contacts, contacts);
        }
    }
}
