using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Highlight.Selection;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Selection
{
    /// <summary>specs/058 FR-20·24·48 ③: 선정 결과 → 042 연출 입력.</summary>
    public class LiveSceneBriefsTests
    {
        static LiveSceneList List()
        {
            var l = new LiveSceneList
            {
                Scenes = new[]
                {
                    new HighlightScene(1, 92, 107, groupId: 1),
                    new HighlightScene(2, 97.5, 101, isReplay: true, groupId: 1),
                    new HighlightScene(3, 120, 125, groupId: 1),
                    new HighlightScene(4, 300, 311),
                },
                DeadBalls = new[] { new DeadBallWindow(100, 124), new DeadBallWindow(500, 510) },
            };
            l.Info[1] = new LiveSceneInfo { SceneId = 1, Role = SceneRole.GoalMain, Kind = LiveSceneKind.Goal, EventTick = 1000, EventTime = 100, Team = 1, PlayerId = 110, ShooterId = 110, EventX = 0.02f, EventY = 0.5f };
            l.Info[2] = new LiveSceneInfo { SceneId = 2, Role = SceneRole.ReplayAngle, Kind = LiveSceneKind.Goal, EventTick = 1000, EventTime = 100, Team = 1, PlayerId = 110, AngleIndex = 1, AngleCount = 3 };
            l.Info[3] = new LiveSceneInfo { SceneId = 3, Role = SceneRole.GoalTail, Kind = LiveSceneKind.Goal, EventTick = 1000, EventTime = 100, Team = 1, PlayerId = 110, EndWide = true };
            l.Info[4] = new LiveSceneInfo { SceneId = 4, Role = SceneRole.Live, Kind = LiveSceneKind.Defensive, EventTick = 3080, EventTime = 308, Team = 0, PlayerId = 13, MyPlayerInvolved = true };
            return l;
        }

        /// <summary>id 110 → 슬롯 20, 내 선수 13 → 슬롯 8 (틱 2000 뒤에만 출전).</summary>
        static int SlotOf(long tick, int id) => id == 110 ? 20 : id == 13 ? (tick >= 2000 ? 8 : -1) : -1;

        [Test]
        public void ConcededGoal_ScorerSlot_NoMyPlayerCelebration()
        {
            var b = new LiveSceneBriefs(List(), SlotOf, 13, PitchSpec.Default);
            var main = b.Brief(1);
            Assert.That(main.Role, Is.EqualTo(SceneRole.GoalMain));
            Assert.That(main.Scorer, Is.EqualTo(20));
            Assert.That(main.MyPlayer, Is.EqualTo(-1), "투입 전이라 내 선수 없음");
            Assert.That(main.MyPlayerInvolved, Is.False);
            Assert.That(main.DeadBalls.Length, Is.EqualTo(1));
            Assert.That(b.Brief(2).AngleIndex, Is.EqualTo(1));
            Assert.That(b.Brief(2).AngleCount, Is.EqualTo(3));
            Assert.That(b.Brief(3).EndWide, Is.True);
            Assert.That(b.Brief(99), Is.Null);
        }

        [Test]
        public void MyPlayerOnPitch_Involved_TrackingAllowed()
        {
            var b = new LiveSceneBriefs(List(), SlotOf, 13, PitchSpec.Default);
            var live = b.Brief(4);
            Assert.That(live.MyPlayer, Is.EqualTo(8));
            Assert.That(live.MyPlayerInvolved, Is.True);
        }

        [Test]
        public void NotPlayed_MyPlayerModeFallsBack()
        {
            var b = new LiveSceneBriefs(List(), (t, id) => id == 110 ? 20 : -1, 13, PitchSpec.Default);
            var live = b.Brief(4);
            Assert.That(live.MyPlayer, Is.EqualTo(-1));
            Assert.That(live.MyPlayerInvolved, Is.False, "미출전이면 내 선수 추적 → 방송 모드");
        }
    }
}
