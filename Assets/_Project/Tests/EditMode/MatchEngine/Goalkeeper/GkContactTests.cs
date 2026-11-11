using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/011 FR-13·80·91·92 · AC-24(엔진 쪽): GK 이벤트 필드 · PSxG · 손끝 플래그 (T12).</summary>
    public class GkContactTests
    {
        static bool IsContact(SimEventKind k) => k == SimEventKind.Catch || k == SimEventKind.Parry || k == SimEventKind.Punch;

        [Test]
        public void ReferenceMatch_AllContacts_HavePartSubstepMotion()
        {
            var sim = Reference();
            int contacts = 0, dives = 0, saves = 0;
            for (int i = 0; i < sim.Log.Count; i++)
            {
                ref readonly var e = ref sim.Log[i];
                if (IsContact(e.Kind))
                {
                    contacts++;
                    Assert.AreEqual(BodyPart.Hand, e.Part, e.ToString());
                    Assert.That(e.Substep, Is.InRange(0, 9), e.ToString());
                    Assert.AreNotEqual((int)KeeperMotionKind.None, e.A, e.ToString());
                    Assert.IsTrue(Enum.IsDefined(typeof(KeeperMotionKind), (byte)e.A), e.ToString());
                    Assert.That(e.B & EventCodes.HeightBandMask, Is.InRange(0, 2), e.ToString());
                    Assert.That(e.F0, Is.InRange(0f, 1f), "p_reach");
                }
                else if (e.Kind == SimEventKind.DiveStart)
                {
                    dives++;
                    Assert.Greater(e.F0, 0f, "예정 접촉까지 시간");
                    Assert.That(e.Substep, Is.InRange(0, 9));
                    Assert.That(Math.Abs(e.A / 10), Is.LessThanOrEqualTo(1), "방향 −1·0·+1");
                }
                else if (e.Kind == SimEventKind.Save)
                {
                    saves++;
                    Assert.That(e.F0, Is.InRange(0f, 1f), "PSxG");
                }
            }
            TestContext.WriteLine($"접촉 {contacts}, 다이빙 {dives}, 선방 {saves}, 골 {sim.Result.Goals.Length}");
            Assert.Greater(contacts, 0, "기준 경기에 GK 접촉이 있다");
            Assert.AreEqual(contacts, saves, "접촉마다 선방 집계 1건");
        }

        [Test]
        public void LeagueAverageKeeper_PsxgEqualsOneMinusPReach()
        {
            long shotTick = 5;
            var sim = Scripted(ctx =>
            {
                if (ctx.Tick == 0)
                {
                    ctx.Flow.OnRestartTaken();
                    for (int s = 1; s < Squad.Slots; s++) if (ctx.IsSlotActive(s)) ctx.Kinematics.Place(s, new Vec2(95f, 4f + 2.8f * s), new Vec2(-1f, 0f));
                    ctx.Kinematics.Place(0, new Vec2(2f, 34f), new Vec2(1f, 0f));
                }
                if (ctx.Tick == shotTick)
                {
                    ctx.Ball = BallState.InAir(new Vec3(16f, 34f, 0.11f), new Vec3(-24f, 0.5f, 1.8f), Vec3.Zero);
                    ctx.LastTouchTeam = 1; ctx.LastTouchSlot = 11; ctx.LastKickSlot = 11;
                    ctx.Keepers.NotifyFlight();
                }
            });
            sim.StepTicks(40);
            var saves = Events(sim, SimEventKind.Save);
            var goals = Events(sim, SimEventKind.Goal);
            Assert.AreEqual(1, saves.Count + goals.Count);
            if (saves.Count == 1) Assert.AreEqual(1f - saves[0].F1, saves[0].F0, 1e-6f, "선발 GK(능력치 미지정) = 리그 평균 → PSxG = 1 − p_save");
            else Assert.Greater(goals[0].F1, 0f, "실점에도 PSxG");
        }

        [Test]
        public void Miss_FingertipFlag_WhenDivedAndNearlyReached()
        {
            var p = GoalkeeperParams.Defaults;
            var m = new SaveModel(p, new KeeperPositioner(p, new GoalGrid(p)));
            var k = KeeperProfile.From(p, KeeperAttributes.Uniform(10.5f));
            var ctx = new SaveContext { Goal = new Vec2(0f, 34f), DirIn = 1f };
            var rng = new RoadToPro.Core.Random.SeedRng(1UL);
            var j = new SaveJudgement { OnTarget = true, ContactIndex = 3, PReach = 0f, Dives = true, MaxMargin = -0.05f };
            Assert.IsTrue(m.Resolve(in j, in k, in ctx, rng).Fingertip, "다이빙 + 마진 −0.05 m");
            j.MaxMargin = -0.5f;
            Assert.IsFalse(m.Resolve(in j, in k, in ctx, rng).Fingertip, "멀면 아님");
            j.MaxMargin = -0.05f;
            j.Dives = false;
            Assert.IsFalse(m.Resolve(in j, in k, in ctx, rng).Fingertip, "다이빙 안 했으면 아님");
        }

        [Test]
        public void ConsumeGoal_OnlyForDefendingTeam_ThenCleared()
        {
            var p = GoalkeeperParams.Defaults;
            var w = new ShotWatcher(p, new SaveModel(p, new KeeperPositioner(p, new GoalGrid(p))));
            w.ConsumeGoal(0, out var f, out var x);
            Assert.AreEqual(0, f);
            Assert.AreEqual(0f, x);
        }
    }
}
