using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>선수별 액션 순서·끊기 규칙 (specs/043 FR-60).</summary>
    public class ActionTimelineTests
    {
        static ActionChoice A(int clip, float start, float contact, float end) => new ActionChoice(clip, start, 1f, contact, end, 1f, default, false);

        [Test]
        public void SecondAction_WaitsForFirstContact()
        {
            var tl = new ActionTimeline();
            tl.Add(A(1, 1.0f, 1.4f, 1.8f), false, 1);
            // 두 번째 액션이 첫 접촉 전에 시작하려 하면 첫 접촉 뒤로 밀린다
            var s = tl.EarliestStart(1.2f, false);
            Assert.That(s, Is.GreaterThanOrEqualTo(1.4f));
        }

        [Test]
        public void Hit_Interrupts_Immediately()
        {
            var tl = new ActionTimeline();
            tl.Add(A(1, 1.0f, 1.4f, 1.8f), false, 1);
            Assert.That(tl.EarliestStart(1.2f, true), Is.EqualTo(1.2f));
            tl.Add(A(2, 1.2f, 1.2f, 2.5f), true, 2);
            Assert.That(tl.Get(0).End, Is.EqualTo(1.2f).Within(1e-6f), "앞 액션은 피격 시각에서 끝난다");
        }

        [Test]
        public void Order_BySeq_WhenSameTime()
        {
            var tl = new ActionTimeline();
            tl.Add(A(5, 1.0f, 1.3f, 1.6f), false, 20);
            tl.Add(A(6, 1.0f, 1.3f, 1.6f), false, 10);
            Assert.AreEqual(6, tl.Get(0).Clip);
            Assert.AreEqual(5, tl.Get(1).Clip);
        }

        [Test]
        public void ActiveAt_ReturnsLatestStarted()
        {
            var tl = new ActionTimeline();
            tl.Add(A(1, 1.0f, 1.4f, 1.8f), false, 1);
            tl.Add(A(2, 2.0f, 2.4f, 2.8f), false, 2);
            Assert.AreEqual(-1, tl.ActiveAt(0.5f));
            Assert.AreEqual(0, tl.ActiveAt(1.5f));
            Assert.AreEqual(-1, tl.ActiveAt(1.9f));
            Assert.AreEqual(1, tl.ActiveAt(2.1f));
        }
    }
}

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    public class ActionTimelineReviewTests
    {
        static RoadToPro.Presentation.Players.Logic.ActionChoice A(int clip, float start, float contact, float end) =>
            new RoadToPro.Presentation.Players.Logic.ActionChoice(clip, start, 1f, contact, end, 1f, default, false);

        /// <summary>T17 리뷰: 한 번 밀린 뒤 다른 앞 액션과 다시 겹치면 또 민다.</summary>
        [Test]
        public void EarliestStart_RepeatsUntilStable()
        {
            var tl = new RoadToPro.Presentation.Players.Logic.ActionTimeline();
            tl.Add(A(1, 1.0f, 2.0f, 2.5f), false, 1);
            tl.Add(A(2, 0.5f, 1.2f, 1.5f), false, 2);
            Assert.That(tl.EarliestStart(0.8f, false), Is.EqualTo(2.0f).Within(1e-6f));
        }
    }
}
