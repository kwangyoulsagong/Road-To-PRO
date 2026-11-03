using System;
using System.Linq;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-18: 구독자 예외 격리, 구독자는 시뮬 상태를 바꿀 수 없다.</summary>
    public class SubscriberTests
    {
        sealed class Throwing : ISimEventSubscriber
        {
            public int Seen;
            public void OnEvent(in SimEvent e, IMatchView view)
            {
                Seen++;
                var _ = view.SlotState(3).Position.X + view.Ball.Position.X + view.Score(0); // 읽기만
                if (e.Kind == SimEventKind.Pass) throw new InvalidOperationException("구독자 버그");
            }
        }

        [Test]
        public void ThrowingSubscriber_SimContinues_SameHash()
        {
            var a = Sim(21UL);
            var b = Sim(21UL);
            var sub = new Throwing();
            b.Subscribe(sub);
            a.RunUntilTick(4000);
            b.RunUntilTick(4000);
            Assert.That(sub.Seen, Is.EqualTo(b.Log.Count), "모든 이벤트를 받았다");
            Assert.That(b.Context.Publisher.SubscriberFaults, Is.GreaterThan(0));
            Assert.That(b.Log.Hash, Is.EqualTo(a.Log.Hash));
            Assert.That(b.StateHash(), Is.EqualTo(a.StateHash()));
            Assert.That(b.IsFinished, Is.False);
        }

        [Test]
        public void MatchView_HasNoMutators()
        {
            var t = typeof(IMatchView);
            Assert.That(t.GetProperties().All(p => !p.CanWrite), "쓰기 속성 없음");
            var allowed = new[] { "Score", "IsSlotActive", "PlayerAtSlot", "SlotState", "AttackDir" };
            foreach (var m in t.GetMethods().Where(m => !m.IsSpecialName))
                Assert.That(allowed, Does.Contain(m.Name), $"{m.Name}는 조회 메서드 목록에 없다");
            foreach (var m in t.GetMethods().Where(m => !m.IsSpecialName))
                Assert.That(m.ReturnType.IsByRef, Is.False, "ref 반환 없음 (값 복사본만)");
            var sub = typeof(ISimEventSubscriber).GetMethod("OnEvent");
            Assert.That(sub.GetParameters()[0].IsIn, "이벤트는 in (읽기 전용) 인자");
        }
    }
}
