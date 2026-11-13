using System;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-12(10만 액션 2회 해시 · RNG 고정 소비) · AC-17(진단 켬/끔 같은 해시, 끈 경로 할당 0).</summary>
    public class ActionDeterminismTests
    {
        static ActionParams P => ActionTestKit.P;

        static ulong RunActions(ulong seed, out bool fixedCount)
        {
            var rng = new SeedRng(seed);
            var shadow = new SeedRng(seed);
            var rnd = new SeedRng(seed ^ 0xABCDEFUL); // 입력 생성용 (분기를 흔든다)
            var h = StableHasher.Create();
            fixedCount = true;
            for (int i = 0; i < 100_000; i++)
            {
                var kind = i % 3;
                var aPass = rnd.NextFloat();
                var press = rnd.NextFloat();
                var oneTouch = rnd.NextFloat() < 0.3f;
                if (kind == 0)
                {
                    var v = PassExecution.Apply(new Vec3(rnd.Range(8f, 25f), 0f, oneTouch ? 4f : 0f),
                        PassExecution.SigmaAngleDeg(P, aPass, press, 0.5f, oneTouch ? 1.5f : 1f, PassExecution.HabitMul(P, oneTouch, 1)),
                        PassExecution.SigmaSpeed(P, aPass, 1f, 1f), rng, out _, out _);
                    h.Add(v.X); h.Add(v.Y); h.Add(v.Z);
                    for (int k = 0; k < PassExecution.RngPerPass; k++) shadow.NextFloat();
                }
                else if (kind == 1)
                {
                    var t = FirstTouchModel.Sample(P, aPass, rnd.Range(3f, 25f), press, new Vec2(1f, 0.2f), rng);
                    h.Add(t.Offset); h.Add(t.Direction.X); h.Add(t.Heavy);
                    for (int k = 0; k < FirstTouchModel.RngPerTouch; k++) shadow.NextFloat();
                }
                else
                {
                    var pr = BallControl.Prob(P, rnd.Range(0f, 1f), rnd.Range(1f, 25f), rnd.Range(0f, 2f), aPass, press, oneTouch, false);
                    h.Add(rng.NextFloat() < pr);
                    shadow.NextFloat();
                }
                if (!rng.SaveState().Equals(shadow.SaveState())) fixedCount = false;
            }
            return h.Value;
        }

        [Test]
        public void Actions100k_TwoRuns_SameHash_FixedRngCount()
        {
            var a = RunActions(12, out var fa);
            var b = RunActions(12, out var fb);
            Assert.AreEqual(a, b);
            Assert.IsTrue(fa && fb, "액션당 RNG 소비가 분기와 무관하게 고정 (패스 4 · 첫 터치 3 · 컨트롤 판정 1)");
            var m1 = MatchTestKit.Sim(404);
            var m2 = MatchTestKit.Sim(404);
            m1.RunUntilTick(9_000);
            m2.RunUntilTick(9_000);
            Assert.AreEqual(m1.Log.Hash, m2.Log.Hash, "경기 이벤트 로그");
            Assert.AreEqual(m1.StateHash(), m2.StateHash(), "경기 상태");
        }

        sealed class Sink : IActionDiagnostics
        {
            public int Candidates, Chosen, Contacts;
            public void OnPassCandidate(long tick, int passer, int receiver, in PassOption option, float deltaXt, float pressure, float score) => Candidates++;
            public void OnPassChosen(long tick, int passer, int receiver, in PassOption option) => Chosen++;
            public void OnContact(long tick, int substep, int slot, SimEventKind kind, float value) => Contacts++;
        }

        [Test]
        public void Diagnostics_OnOff_SameHashes_ZeroAllocWhenOff()
        {
            var off = MatchTestKit.Sim(9013);
            var on = MatchTestKit.Sim(9013);
            var sink = new Sink();
            on.Context.Actions.Diagnostics = sink;
            off.RunUntilTick(9_000);
            on.RunUntilTick(9_000);
            Assert.Greater(sink.Candidates, 0);
            Assert.Greater(sink.Chosen, 0);
            Assert.Greater(sink.Contacts, 0);
            Assert.AreEqual(off.Log.Hash, on.Log.Hash, "이벤트 로그");
            Assert.AreEqual(off.StateHash(), on.StateHash(), "상태");

            // 꺼진 경로 할당 0: 평가·실행 핫 경로 반복
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 1, new Vec2(40f, 34f));
            ActionTestKit.Put(w, 2, new Vec2(58f, 30f));
            ActionTestKit.Put(w, 12, new Vec2(50f, 33f), new Vec2(-3f, 0f));
            var rng = new SeedRng(1);
            var bp = ActionTestKit.Ball;
            PassModel.Best(P, bp, w, 1, 2, w.Pos[1], w.Pos[2], 0.2f);
            var defs = new Vec2[11];
            var vels = new Vec2[11];
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                PassModel.Best(P, bp, w, 1, 2, w.Pos[1], w.Pos[2], 0.2f);
                PressureModel.Team(P, w.Pos[1], w.Vel[1], defs, vels, 11, 68f);
                PassExecution.Apply(new Vec3(14f, 1f, 0f), 3f, 0.08f, rng, out _, out _);
                FirstTouchModel.Sample(P, 0.5f, 14f, 0.2f, new Vec2(1f, 0f), rng);
                BallControl.Prob(P, 0.4f, 12f, 0.1f, 0.5f, 0.2f, true, false);
            }
            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
