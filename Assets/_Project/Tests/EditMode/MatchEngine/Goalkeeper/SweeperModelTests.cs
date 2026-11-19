using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.PitchControl;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;
using static RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper.KeeperLeaveKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/073 스위퍼 FR-01~03 (AC-01·08 일부).</summary>
    public class SweeperModelTests
    {
        readonly float[] _t = new float[30];
        readonly Vec3[] _p = new Vec3[30];

        /// <summary>상대(슬롯 12)가 (30, 36)에서 −x로 7 m/s 뛰고 공이 (50, 34)에서 −x로 14 m/s — 라인 뒤 스루패스.</summary>
        int Through(ThroughScene scene, out RoadToPro.MatchEngine.Actions.ActionWorld w)
        {
            w = World(scene.Gk);
            ActionTestKit.Put(w, 12, scene.Att, new Vec2(-7f, 0f)); // 라인 뒤로 뛰어드는 공격수
            return StraightPath(new Vec2(50f, 34f), new Vec2(-14f, 0f), _t, _p);
        }

        public struct ThroughScene { public Vec2 Gk, Att; }
        static ThroughScene Scene(float gkX) => new ThroughScene { Gk = new Vec2(gkX, 34f), Att = new Vec2(30f, 36f) };

        [Test]
        public void Margin_Plus02_Rushes_Minus02_Waits()
        {
            var m = new SweeperModel(P);
            var n = Through(Scene(14f), out var w);
            var prof = Profile();
            var g0 = m.Gate(w, 0, in prof, KeeperRole.Standard, Goal, 1f, _t, _p, n, 0f, 2.5f);
            Assert.IsTrue(g0.Has, "첫 접촉점이 있어야 한다");
            var kappa = g0.OutsideBox ? P.SweepKappa : 0f;
            var rush = prof; rush.SweepMargin = g0.Delta - 0.2f - kappa;
            var wait = prof; wait.SweepMargin = g0.Delta + 0.2f - kappa;
            var a = m.Gate(w, 0, in rush, KeeperRole.Standard, Goal, 1f, _t, _p, n, 0f, 2.5f);
            var b = m.Gate(w, 0, in wait, KeeperRole.Standard, Goal, 1f, _t, _p, n, 0f, 2.5f);
            Assert.IsTrue(a.Pass, $"Δ {a.Delta:F3} > 마진 {a.Margin:F3}");
            Assert.IsFalse(b.Pass, $"Δ {b.Delta:F3} < 마진 {b.Margin:F3}");
            Assert.AreEqual(a.Point.X, b.Point.X, 1e-6f, "같은 첫 접촉점");
        }

        [Test]
        public void Sweeping20_RushesMoreThan_Sweeping1()
        {
            var m = new SweeperModel(P);
            var hi = Profile(sweeping: 100f);
            var lo = Profile(sweeping: 0f);
            // 두 GK가 갈리는 위치를 찾는다 (스위퍼 20은 통과, 1은 대기)
            float found = -1f;
            for (float x = 4f; x <= 30f && found < 0f; x += 0.5f)
            {
                var n = Through(Scene(x), out var w);
                var a = m.Gate(w, 0, in hi, KeeperRole.Standard, Goal, 1f, _t, _p, n, 0f, 2.5f);
                var b = m.Gate(w, 0, in lo, KeeperRole.Standard, Goal, 1f, _t, _p, n, 0f, 2.5f);
                if (a.Pass && !b.Pass) found = x;
            }
            Assert.Greater(found, 0f, "Sweeping 20만 통과하는 위치가 있어야 한다 (m = 0.4 − 0.5n)");
            var nn = Through(Scene(found), out var ww);
            var ch = new KeeperChooser(P);
            var cands = new KeeperCandidate[2];
            var rng = new SeedRng(7301UL);
            int rushHi = 0, rushLo = 0;
            for (int i = 0; i < 1000; i++)
            {
                rushHi += Pick(m, ww, in hi, nn, ch, cands, rng) ? 1 : 0;
                rushLo += Pick(m, ww, in lo, nn, ch, cands, rng) ? 1 : 0;
            }
            TestContext.WriteLine($"GK x {found}: 돌진 Sweeping20 {rushHi}/1000, Sweeping1 {rushLo}/1000");
            Assert.Greater(rushHi, rushLo);
        }

        bool Pick(SweeperModel m, RoadToPro.MatchEngine.Actions.ActionWorld w, in KeeperProfile prof, int n, KeeperChooser ch, KeeperCandidate[] cands, SeedRng rng)
        {
            var g = m.Gate(w, 0, in prof, KeeperRole.Standard, Goal, 1f, _t, _p, n, 0f, 2.5f);
            cands[0] = new KeeperCandidate { Kind = KeeperActionKind.Position, Utility = P.ChoicePositionUtility };
            var count = 1;
            if (g.Pass) cands[count++] = new KeeperCandidate { Kind = KeeperActionKind.Rush, Point = g.Point, Utility = m.Utility(0.8f, 0.05f, 0.6f) };
            return ch.Choose(cands, count, default, prof.DecisionsN, rng) == 1;
        }

        [Test]
        public void ThroughBall_FirstContactOutside_NoRush()
        {
            var m = new SweeperModel(P);
            var w = World(new Vec2(5f, 34f));
            ActionTestKit.Put(w, 12, new Vec2(90f, 60f)); // 아무도 못 닿는다
            var n = StraightPath(new Vec2(20f, 34f), new Vec2(-15f, 0f), _t, _p);
            var g = m.Gate(w, 0, Profile(), KeeperRole.Standard, Goal, 1f, _t, _p, n, 0f, 2.5f);
            Assert.IsFalse(g.Has, "공이 골라인 밖으로 나가기 전에 첫 접촉점이 없다");
        }

        [Test]
        public void Margin_BothInfinite_NoCandidate_NoNaN()
        {
            var m = new SweeperModel(P);
            var w = World(new Vec2(5f, 34f)); // 상대 없음
            var n = StraightPath(new Vec2(50f, 34f), new Vec2(-15f, 0f), _t, _p);
            var g = m.Gate(w, 0, Profile(), KeeperRole.Standard, Goal, 1f, _t, _p, n, 0f, 2.5f);
            Assert.IsFalse(g.Has);
            Assert.IsFalse(float.IsNaN(g.Delta));
            w.Active[0] = false; // GK 없음
            ActionTestKit.Put(w, 12, new Vec2(35f, 40f));
            Assert.IsFalse(m.Gate(w, 0, Profile(), KeeperRole.Standard, Goal, 1f, _t, _p, n, 0f, 2.5f).Has);
        }

        [Test]
        public void OutsideBox_AddsKappa_RoleOffsets()
        {
            var m = new SweeperModel(P);
            var k = Profile();
            Assert.AreEqual(P.SweepKappa, m.Margin(in k, KeeperRole.Standard, true) - m.Margin(in k, KeeperRole.Standard, false), 1e-6f);
            Assert.Less(m.Margin(in k, KeeperRole.Sweeper, true), m.Margin(in k, KeeperRole.Standard, true), "스위퍼 키퍼는 더 공격적");
            Assert.Greater(m.Margin(in k, KeeperRole.Traditional, true), m.Margin(in k, KeeperRole.Standard, true), "전통 골키퍼는 덜 나간다");
            Assert.AreEqual(0.4f - 0.5f * 1f, Profile(sweeping: 100f).SweepMargin, 1e-6f, "m = 0.4 − 0.5n");
        }

        [Test]
        public void NegativeEu_NotChosen()
        {
            var m = new SweeperModel(P);
            var u = m.Utility(0.1f, 0.05f, m.EmptyXg(new Vec2(20f, 34f), Goal));
            Assert.Less(u, 0f);
            var ch = new KeeperChooser(P);
            var cands = new[]
            {
                new KeeperCandidate { Kind = KeeperActionKind.Position, Utility = P.ChoicePositionUtility },
                new KeeperCandidate { Kind = KeeperActionKind.Rush, Utility = u },
            };
            var rng = new SeedRng(7302UL);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(0, ch.Choose(cands, 2, default, 0.5f, rng), "EU < 0 돌진은 고르지 않는다 (FR-02)");
        }

        /// <summary>AC-08: p_win은 007 지점 질의 1회(실물). 박스 밖은 GK 배수 없이 — GK 컨트롤이 박스 안 같은 기하보다 작고, GK 표시는 원래대로.</summary>
        [Test]
        public void UsesPitchControlPoint()
        {
            var w = World(new Vec2(10f, 34f));
            ActionTestKit.Put(w, 12, new Vec2(20f, 34f));
            var q = new PitchControlQuery(PitchControlParams.Defaults);
            var view = w.ControlPlayersView;
            var before = q.PointCount;
            var inBox = SweeperModel.PWin(q, view, 0, new Vec2(15f, 34f), 0.8f, false);
            Assert.AreEqual(before + 1, q.PointCount, "질의 1회");
            var outBox = SweeperModel.PWin(q, view, 0, new Vec2(15f, 34f), 0.8f, true);
            Assert.IsTrue(view.IsKeeper[0], "GK 표시 복원");
            Assert.Greater(inBox, outBox, "λ_gk(손)는 박스 안에서만");
            Assert.That(outBox, Is.InRange(0f, 1f));
        }
    }
}
