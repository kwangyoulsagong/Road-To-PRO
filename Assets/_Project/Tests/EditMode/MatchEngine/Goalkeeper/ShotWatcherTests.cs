using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/011 슛 감시 FR-11·15·17 (AC-05·23) · 다이빙 · 결과 적용 (T08).</summary>
    public class ShotWatcherTests
    {
        const int Gk = 0; // 전반 홈 GK, 자기 골 x = 0
        static readonly Vec2 Goal = new Vec2(0f, 34f);

        /// <summary>스크립트 경기: 틱 0에 인플레이, onShotTick 틱에 shot 콜백. GK 외 선수는 멀리 둔다.</summary>
        static MatchSim Arena(Func<MatchContext, bool> onTick)
        {
            MatchSim sim = null;
            sim = Scripted(ctx =>
            {
                if (ctx.Tick == 0)
                {
                    ctx.Flow.OnRestartTaken();
                    for (int s = 1; s < Squad.Slots; s++)
                        if (ctx.IsSlotActive(s)) ctx.Kinematics.Place(s, new Vec2(95f, 4f + 2.8f * (s % 22)), new Vec2(-1f, 0f));
                    ctx.Kinematics.Place(Gk, new Vec2(2f, 34f), new Vec2(1f, 0f));
                }
                onTick(ctx);
            });
            return sim;
        }

        static void Shoot(MatchContext ctx, Vec3 from, Vec3 vel, int kicker = 11)
        {
            ctx.Ball = BallState.InAir(from, vel, Vec3.Zero);
            ctx.LastTouchTeam = Squad.TeamOfSlot(kicker);
            ctx.LastTouchSlot = kicker;
            ctx.LastKickSlot = kicker;
            ctx.Keepers.NotifyFlight();
        }

        /// <summary>from에서 골문 (y, z)로 speed m/s 근사 조준.</summary>
        static Vec3 Aim(Vec3 from, float y, float z, float speed, float g = 9.81f)
        {
            var dx = Goal.X - from.X;
            var dy = y - from.Y;
            var h = (float)Math.Sqrt(dx * dx + dy * dy);
            var t = h / speed;
            var vz = (z - from.Z + 0.5f * g * t * t) / t;
            return new Vec3(dx / h * speed, dy / h * speed, vz);
        }

        [Test]
        public void LongShots1000_AlwaysJudged_ReactTimeExact()
        {
            var rng = new SeedRng(2311UL);
            int judged = 0, onTarget = 0;
            var sim = Arena(ctx => false);
            sim.StepTicks(2);
            var ctx = sim.Context;
            var dt = ctx.BallParams.Dt;
            for (int i = 0; i < 1000; i++)
            {
                var from = new Vec3(30f, 34f + rng.Range(-12f, 12f), 0.11f);
                var vel = Aim(from, 34f + rng.Range(-3.2f, 3.2f), rng.Range(0.3f, 2.1f), rng.Range(20f, 30f));
                Shoot(ctx, from, vel);
                var abs = ctx.Kinematics.Substep;
                if (!ctx.Keepers.Save.Predict(in ctx.Ball, ctx.BallParams, Goal, 1f)) continue; // 공기 저항으로 빗나간 픽스처는 제외
                onTarget++;
                ctx.Keepers.OnSubstep(ctx, abs, 0);
                var w = ctx.Keepers.Watcher;
                if (!w.Active) continue;
                judged++;
                var expected = (long)Math.Floor(w.Judgement.ReactTime / dt + 0.5);
                var diff = w.ReactSubstep - w.ShotSubstep;
                if (w.ReactSubstep < w.ContactSubstep) Assert.That(Math.Abs(diff - expected), Is.LessThanOrEqualTo(1), $"shot {i}");
            }
            Assert.Greater(onTarget, 700);
            Assert.AreEqual(onTarget, judged, "유효 슛은 거리와 상관없이 항상 판정 (FR-15)");
        }

        [Test]
        public void Occlusion_AddsReactDelay()
        {
            var sim = Arena(ctx => false);
            sim.StepTicks(2);
            var ctx = sim.Context;
            var from = new Vec3(20f, 34f, 0.11f);
            var vel = Aim(from, 36f, 0.5f, 24f);
            Shoot(ctx, from, vel);
            ctx.Keepers.OnSubstep(ctx, ctx.Kinematics.Substep, 0);
            var clear = ctx.Keepers.Watcher.Judgement.ReactTime;

            ctx.Kinematics.Place(5, new Vec2(10f, 34.6f), new Vec2(1f, 0f)); // GK(2, 34)–공 시야선 위 수비수
            Shoot(ctx, from, vel);
            ctx.Keepers.OnSubstep(ctx, ctx.Kinematics.Substep, 0);
            var blocked = ctx.Keepers.Watcher.Judgement.ReactTime;
            Assert.That(blocked - clear, Is.InRange(0.1f - 1e-5f, 0.2f + 1e-5f));
        }

        [Test]
        public void Deflection_ResetsTimer_Twice()
        {
            var sim = Arena(ctx => false);
            sim.StepTicks(2);
            var ctx = sim.Context;
            var w = ctx.Keepers.Watcher;
            var from = new Vec3(18f, 30f, 0.11f);
            Shoot(ctx, from, Aim(from, 35f, 0.4f, 22f));
            var abs0 = ctx.Kinematics.Substep;
            ctx.Keepers.OnSubstep(ctx, abs0, 0);
            Assert.AreEqual(abs0, w.ShotSubstep);
            var react = w.ReactSubstep - w.ShotSubstep;

            // 굴절 1·2: 같은 곳에서 방향이 바뀐 공 — 반응 타이머가 그 서브스텝부터 다시
            for (int k = 1; k <= 2; k++)
            {
                var at = abs0 + 5 * k;
                var p = new Vec3(14f - k, 31f, 0.3f);
                ctx.Ball = BallState.InAir(p, Aim(p, 33f + k, 0.6f, 20f), Vec3.Zero);
                ctx.Keepers.NotifyFlight();
                ctx.Keepers.OnSubstep(ctx, at, 0);
                Assert.IsTrue(w.Active);
                Assert.AreEqual(at, w.ShotSubstep, $"굴절 {k}");
                Assert.AreEqual(react, w.ReactSubstep - w.ShotSubstep, $"굴절 {k} 반응 시간 다시");
            }
        }

        [Test]
        public void Shot_InMatch_DiveStartThenContact_Recovers()
        {
            long shotTick = 5;
            var sim = Arena(ctx =>
            {
                if (ctx.Tick == shotTick)
                {
                    var from = new Vec3(16f, 28f, 0.11f);
                    Shoot(ctx, from, Aim(from, 36.6f, 0.5f, 22f));
                }
                return false;
            });
            sim.StepTicks(60);
            var dive = Events(sim, SimEventKind.DiveStart);
            Assert.AreEqual(1, dive.Count, "다이빙 1회");
            var contact = new List<SimEvent>();
            contact.AddRange(Events(sim, SimEventKind.Catch));
            contact.AddRange(Events(sim, SimEventKind.Parry));
            contact.AddRange(Events(sim, SimEventKind.Punch));
            var goals = Events(sim, SimEventKind.Goal);
            Assert.AreEqual(1, contact.Count + goals.Count, "접촉 또는 실점 하나");
            if (contact.Count == 1)
            {
                Assert.AreEqual(BodyPart.Hand, contact[0].Part);
                Assert.Greater(contact[0].Tick * 10 + contact[0].Substep, dive[0].Tick * 10 + dive[0].Substep, "다이빙 뒤 접촉");
                Assert.AreEqual(1, Events(sim, SimEventKind.Save).Count);
            }
            Assert.AreEqual(1, Events(sim, SimEventKind.KeeperRecovered).Count, "일어나기 끝");
            Assert.IsFalse(sim.Context.Keepers.IsBusy(0));
        }

        [Test]
        public void SecondShotDuringDive_NoReaction()
        {
            long shotTick = 5;
            var sim = Arena(ctx =>
            {
                if (ctx.Tick == shotTick)
                {
                    var from = new Vec3(16f, 28f, 0.11f);
                    Shoot(ctx, from, Aim(from, 36.8f, 0.4f, 24f));
                }
                return false;
            });
            // 다이빙이 시작될 때까지
            for (int i = 0; i < 30 && !sim.Context.Keepers.IsBusy(0); i++) sim.StepTicks(1);
            var ctx = sim.Context;
            Assume.That(ctx.Keepers.IsBusy(0), "다이빙 시작");
            var from2 = new Vec3(10f, 33f, 0.11f);
            Shoot(ctx, from2, Aim(from2, 32f, 0.5f, 18f));
            ctx.Keepers.OnSubstep(ctx, ctx.Kinematics.Substep, 0);
            var w = ctx.Keepers.Watcher;
            Assert.IsTrue(w.Active);
            Assert.IsFalse(w.Judgement.Dives, "일어나기 전 새 다이빙 없음");
            Assert.GreaterOrEqual(w.Judgement.ReactTime, 10f, "반응 불가 → r0만");
            Assert.LessOrEqual(w.Judgement.Reach, ctx.Keepers.Params.ReachR0 + 1e-5f);
        }

        [Test]
        public void ContactBeforeGoalLine()
        {
            var sim = Arena(ctx => false);
            sim.StepTicks(2);
            var ctx = sim.Context;
            var from = new Vec3(25f, 34f, 0.11f);
            Shoot(ctx, from, Aim(from, 33f, 1.0f, 26f));
            Assert.IsTrue(ctx.Keepers.Save.Predict(in ctx.Ball, ctx.BallParams, Goal, 1f));
            for (int i = 0; i < ctx.Keepers.Save.PathCount; i++)
                Assert.Greater(ctx.Keepers.Save.PathPoint(i).X, -ctx.BallParams.Radius, "경로 샘플은 골라인 통과 전");
        }

        [Test]
        public void OffTarget_NoJudgement_EmptyGoalSkips()
        {
            var sim = Arena(ctx => false);
            sim.StepTicks(2);
            var ctx = sim.Context;
            var from = new Vec3(20f, 34f, 0.11f);
            Shoot(ctx, from, Aim(from, 44f, 0.5f, 24f));
            ctx.Keepers.OnSubstep(ctx, ctx.Kinematics.Substep, 0);
            Assert.IsFalse(ctx.Keepers.Watcher.Active, "빗나가는 슛은 판정 안 함");
        }

        [Test]
        public void OcclusionExtra_Formula()
        {
            var p = GoalkeeperParams.Defaults;
            Assert.AreEqual(0f, ShotWatcher.OcclusionExtra(p, 0f));
            Assert.AreEqual(0.1f + 0.1f * 0.5f, ShotWatcher.OcclusionExtra(p, 0.1f), 1e-5f);
            Assert.AreEqual(0.2f, ShotWatcher.OcclusionExtra(p, 1f), 1e-5f);
        }
    }
}
