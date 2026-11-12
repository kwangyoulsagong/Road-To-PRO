using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 FR-40·41 결정 LOD (AC-08).</summary>
    public class LodSchedulerTests
    {
        sealed class FakeView : IMatchView
        {
            public readonly Vec2[] Pos = new Vec2[22];
            public BallState B = BallState.Controlled(6, new Vec3(45f, 26f, 0.11f));
            public FlowState S = FlowState.InPlay;
            public long Tick { get; set; }
            public int Period => 0;
            public FlowState State => S;
            public MatchTime Time => default;
            public int Score(int team) => 0;
            public bool IsSlotActive(int slot) => true;
            public int PlayerAtSlot(int slot) => slot;
            public PlayerMotionState SlotState(int slot) => new PlayerMotionState { Id = slot, Position = Pos[slot] };
            public BallState Ball => B;
            public int AttackDir(int team) => team == 0 ? 1 : -1;
        }

        sealed class FakeCast : IIntentSource
        {
            public int Slot = -1;
            public void Fill(RoadToPro.MatchEngine.Match.MatchContext ctx, DecisionWorld w) { }
            public bool NotifyOpportunistic(RoadToPro.MatchEngine.Match.MatchContext ctx, int slot) => false;
            public bool IsCast(int slot) => slot == Slot;
        }

        static FakeView Spread()
        {
            var v = new FakeView();
            for (int s = 0; s < 22; s++) v.Pos[s] = new Vec2(5f + 4.5f * s, s % 2 == 0 ? 10f : 58f);
            v.Pos[6] = new Vec2(45f, 26f);
            return v;
        }

        static int[] Counts(LodDecisionScheduler sch, FakeView v, int ticks, long start = 0)
        {
            var counts = new int[22];
            var buf = new int[22];
            for (long t = start; t < start + ticks; t++)
            {
                v.Tick = t;
                var n = sch.Select(t, v, buf);
                for (int i = 0; i < n; i++) counts[buf[i]]++;
            }
            return counts;
        }

        [Test]
        public void FarPlayers_DecideTwoToFourPer10Ticks_AC08()
        {
            var v = Spread();
            var sch = new LodDecisionScheduler(DecisionParams.Defaults);
            Counts(sch, v, 1); // 첫 틱 전원
            var c = Counts(sch, v, 10, 1);
            for (int s = 0; s < 22; s++)
            {
                var d = (v.Pos[s] - v.B.Position.XY).Length();
                if (s == 6 || d <= DecisionParams.Defaults.LodRadius) Assert.AreEqual(10, c[s], $"슬롯 {s} 근처 = 매 틱");
                else Assert.That(c[s], Is.InRange(2, 4), $"슬롯 {s} 거리 {d:F0} m");
            }
        }

        [Test]
        public void PossessionChange_AllDecideNextTick_AC08()
        {
            var v = Spread();
            var sch = new LodDecisionScheduler(DecisionParams.Defaults);
            Counts(sch, v, 5);
            v.B = BallState.Controlled(14, new Vec3(v.Pos[14].X, v.Pos[14].Y, 0.11f));
            var buf = new int[22];
            Assert.AreEqual(22, sch.Select(5, v, buf), "소유 변화 다음 틱 전원");
            Assert.IsTrue(sch.WasTriggered(3));
            Assert.Less(sch.Select(6, v, buf), 22, "그다음 틱은 LOD");
        }

        [Test]
        public void ConcurrentCap_WithoutTrigger_AC08()
        {
            var v = new FakeView();
            for (int s = 0; s < 22; s++) v.Pos[s] = new Vec2(100f, 5f + 2.5f * s); // 전원 공에서 먼 곳
            v.B = BallState.Controlled(0, new Vec3(5f, 34f, 0.11f));
            v.Pos[0] = new Vec2(5f, 34f);
            var p = DecisionFixtureParams.Cap(4);
            var sch = new LodDecisionScheduler(p);
            var buf = new int[22];
            sch.Select(0, v, buf);
            for (long t = 1; t < 40; t++)
            {
                var n = sch.Select(t, v, buf);
                var far = 0;
                for (int i = 0; i < n; i++) if (buf[i] != 0) far++;
                Assert.LessOrEqual(far, 4, $"tick {t}");
            }
        }

        [Test]
        public void Cast_EveryTick_Request_NextTick()
        {
            var v = Spread();
            var cast = new FakeCast { Slot = 20 };
            var sch = new LodDecisionScheduler(DecisionParams.Defaults) { Cast = cast };
            Counts(sch, v, 1);
            Assert.AreEqual(10, Counts(sch, v, 10, 1)[20], "배역 = 매 틱");
            sch.Request(21);
            var buf = new int[22];
            var n = sch.Select(11, v, buf);
            var found = false;
            for (int i = 0; i < n; i++) found |= buf[i] == 21;
            Assert.IsTrue(found && sch.WasTriggered(21));
        }

        [Test]
        public void DeadBall_AllEveryTick()
        {
            var v = Spread();
            v.S = FlowState.DeadBall;
            var sch = new LodDecisionScheduler(DecisionParams.Defaults);
            var c = Counts(sch, v, 5);
            for (int s = 0; s < 22; s++) Assert.AreEqual(5, c[s]);
        }
    }

    static class DecisionFixtureParams
    {
        public static DecisionParams Cap(int cap) => DecisionFixture.With(("dec.lod.cap", cap));
    }
}
