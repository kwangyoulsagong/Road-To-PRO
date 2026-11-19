using NUnit.Framework;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Referee;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-09 일부 (FR-22·26·28·29): 경고 누적·보류 카드·행동 신고. 실경기 퇴장·7명 미만은 <see cref="RefereeStageTests"/>.</summary>
    public class DisciplineTests
    {
        [Test]
        public void Law12_SecondYellow_IsSendOff()
        {
            var d = new Discipline();
            Assert.AreEqual(CardOutcome.Yellow, d.Book(7, EventCodes.CardYellow));
            Assert.AreEqual(CardOutcome.SecondYellow, d.Book(7, EventCodes.CardYellow), "두 번째 경고 → 퇴장");
            Assert.IsTrue(d.IsSentOff(7));
            Assert.AreEqual(CardOutcome.Ignored, d.Book(7, EventCodes.CardYellow), "퇴장한 선수의 보류 카드는 무시");
            Assert.AreEqual(CardOutcome.Red, d.Book(8, EventCodes.CardRed));
        }

        [Test]
        public void Law12_MitigatedDogsoYellow_PlusExisting_SendOff()
        {
            var d = new Discipline();
            d.Book(3, EventCodes.CardYellow);
            // 박스 안 DOGSO 완화 경고 = 경고 → 두 번째 경고
            Assert.AreEqual(CardOutcome.SecondYellow, d.Book(3, EventCodes.CardYellow));
        }

        [Test]
        public void Deferred_And_RepeatFouls_And_Conduct()
        {
            var d = new Discipline();
            Assert.IsTrue(d.Defer(4, EventCodes.CardYellow, EventCodes.ReasonReckless));
            Assert.AreEqual(1, d.DeferredCount);
            d.DeferredAt(0, out var id, out var color, out var reason);
            Assert.AreEqual((4, EventCodes.CardYellow, EventCodes.ReasonReckless), (id, color, reason));
            d.ClearDeferred();
            Assert.AreEqual(0, d.DeferredCount);
            d.AddFoul(4); d.AddFoul(4);
            Assert.AreEqual(2, d.Fouls(4));
            Assert.AreEqual(EventCodes.CardYellow, d.Conduct(ConductKind.Dissent, 0, out var r1));
            Assert.AreEqual(EventCodes.ReasonDissent, r1);
            Assert.AreEqual(EventCodes.CardYellow, d.Conduct(ConductKind.ShirtOff, 0, out var r2));
            Assert.AreEqual(EventCodes.ReasonShirtOff, r2);
            Assert.AreEqual(EventCodes.CardYellow, d.Conduct(ConductKind.BenchDissent, 1, out _), "감독 항의 1회 = 경고");
            Assert.AreEqual(EventCodes.CardRed, d.Conduct(ConductKind.BenchDissent, 1, out _), "누적 2회 = 벤치 퇴장 (FR-28)");
            Assert.IsTrue(d.BenchSentOff(1));
            Assert.AreEqual(0, d.Conduct(ConductKind.BenchDissent, 1, out _), "이미 퇴장");
        }
    }
}
