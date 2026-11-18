using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.PitchControl;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.PitchControl
{
    /// <summary>specs/007 AC-07 (FR-20~23): 008 수신 컨트롤 = 지점 질의, 072 상위 K 패스 후보 궤적 재평가.</summary>
    public class PassRefineTests
    {
        static ActionParams P => ActionTestKit.P;

        static PitchControlParams Legacy()
        {
            var reg = MatchTestKit.ActionTuning().Registry;
            return PitchControlParams.Create(TuningLoader.ParseBundle(reg, "{\"version\": 1, \"values\": [{\"id\": \"pcm.legacy\", \"value\": 1}]}"));
        }

        static ActionWorld Scene(bool blocked, bool legacy)
        {
            var w = ActionTestKit.World();
            w.PrepareRoll(P, ActionTestKit.Ball);
            if (legacy) w.SetPitchControl(new PitchControlQuery(Legacy()));
            ActionTestKit.Put(w, 1, new Vec2(40f, 34f));
            ActionTestKit.Put(w, 2, new Vec2(58f, 34f));
            ActionTestKit.Put(w, 12, blocked ? new Vec2(49f, 34.6f) : new Vec2(48f, 12f));
            ActionTestKit.Put(w, 13, new Vec2(75f, 55f));
            return w;
        }

        static float Judged(ActionWorld w)
        {
            var o = PassModel.Best(P, ActionTestKit.Ball, w, 1, 2, w.Pos[1], w.Pos[2], 0.2f);
            Assert.IsTrue(o.Valid);
            return PassModel.Refine(P, ActionTestKit.Ball, w, 1, 2, w.Pos[1], in o);
        }

        static float Logistic(ActionWorld w) => PassModel.Best(P, ActionTestKit.Ball, w, 1, 2, w.Pos[1], w.Pos[2], 0.2f).XPass;

        [Test]
        public void BlockedLane_LowersP()
        {
            var before = Logistic(Scene(true, true));
            var after = Judged(Scene(true, false));
            TestContext.WriteLine($"막힌 레인: 연결 전 {before:F3} → 연결 뒤 {after:F3}");
            Assert.Less(after, before);
        }

        [Test]
        public void OpenLane_DropsAtMost005()
        {
            var before = Logistic(Scene(false, true));
            var after = Judged(Scene(false, false));
            TestContext.WriteLine($"열린 레인: 연결 전 {before:F3} → 연결 뒤 {after:F3}");
            Assert.GreaterOrEqual(after, before - 0.05f);
        }

        [Test]
        public void LegacyMode_SameAsBefore()
        {
            var w = Scene(true, true);
            Assert.AreEqual(w.Control(P, 2, w.Pos[2]), w.ReceiverControl(P, 2, w.Pos[2], w.Pos[1], 1));
            var n = Scene(true, false);
            Assert.AreNotEqual(n.Control(P, 2, n.Pos[2]), n.ReceiverControl(P, 2, n.Pos[2], n.Pos[1], 1), "연결 모드는 지점 질의");
        }

        static OnBallFixture Midfield()
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(50f, 34f));
            f.GiveBall(6);
            return f;
        }

        static bool Refinable(OnBallItem k) => OnBallItems.IsPass(k) && k != OnBallItem.Cross;

        [Test]
        public void UnrefinedBest_GetsRefined()
        {
            var f = Midfield();
            var rng = new SeedRng(77);
            var checkedN = 0;
            for (int rep = 0; rep < 20; rep++)
            {
                f.F.ResetState(6);
                f.Decide(rng, 6);
                var c = f.Decider.Candidates;
                var best = -1;
                for (int i = 0; i < c.Count; i++)
                    if (Refinable(c[i].Item) && c[i].Mult > 0f && c[i].Target >= 0 && (best < 0 || c[i].Utility > c[best].Utility)) best = i;
                if (best < 0) continue;
                checkedN++;
                Assert.IsTrue(f.Decider.Refined(best), "효용 최고 패스는 재평가된 값이어야 한다");
                var pc = PitchControlParams.Defaults;
                Assert.LessOrEqual(f.Decider.LastRefined, pc.RefineCount + pc.RefineExtraCount);
                Assert.Greater(f.Decider.LastRefined, 0);
            }
            Assert.Greater(checkedN, 0);
        }

        [Test]
        public void Cross_NotRefined()
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(90f, 60f));
            f.Place(9, new Vec2(95f, 34f));
            f.Place(10, new Vec2(97f, 40f));
            f.GiveBall(6);
            f.Decide(new SeedRng(5), 6);
            var c = f.Decider.Candidates;
            var crosses = 0;
            for (int i = 0; i < c.Count; i++)
            {
                if (c[i].Item != OnBallItem.Cross) continue;
                crosses++;
                Assert.IsFalse(f.Decider.Refined(i), "크로스는 재평가하지 않는다 (FR-23)");
            }
            Assume.That(crosses, Is.GreaterThan(0), "고정 장면: 크로스 후보");
        }

        [Test]
        public void IntentXPass_IsRefinedP()
        {
            var f = Midfield();
            var rng = new SeedRng(91);
            var seen = 0;
            for (int rep = 0; rep < 200 && seen < 5; rep++)
            {
                f.F.ResetState(6);
                var r = f.Decide(rng, 6);
                if (!Refinable(r.Item)) continue;
                var pick = f.Decider.LastPick;
                if (!f.Decider.Refined(pick)) continue;
                seen++;
                ref var ch = ref f.Decider.Candidates[pick];
                Assert.AreEqual(ch.Pass.XPass, r.Pass.XPass, "의도 패스의 xPass = 재평가된 판단 P");
                var a = ActionSystem.IntentAction(6, r.Target, in r.Pass, f.W.Ball, false);
                Assert.AreEqual(ch.Pass.XPass, a.XPass, "이벤트(F1)에 기록되는 값");
            }
            Assert.Greater(seen, 0);
        }
    }
}
