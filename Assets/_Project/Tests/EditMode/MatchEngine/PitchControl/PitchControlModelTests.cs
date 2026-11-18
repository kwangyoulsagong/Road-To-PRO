using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.PitchControl;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.PitchControl
{
    /// <summary>specs/007 AC-02·03·04 (지점 질의). AC-04 허용 = 적분 이산 오차 1e-3 (스펙 AC-04 개정 T06: dt 0.04 오일러 적분은 가지치기 없이도 최대 ≈ 5e-4 감소).</summary>
    public class PitchControlModelTests
    {
        static PitchControlParams P => PitchControlParams.Defaults;

        [Test]
        public void SymmetricLayout_EqualControl()
        {
            foreach (var paper in new[] { true, false })
            {
                var pl = new ControlPlayers { PaperArrival = paper };
                var a = new[] { new Vec2(40f, 20f), new Vec2(40f, 48f), new Vec2(45f, 34f) };
                for (int i = 0; i < a.Length; i++)
                {
                    var b = new Vec2(105f - a[i].X, a[i].Y);
                    if (paper)
                    {
                        pl.SetPaper(1 + i, 0, false, a[i], Vec2.Zero, P);
                        pl.SetPaper(12 + i, 1, false, b, Vec2.Zero, P);
                    }
                    else
                    {
                        // AC-02는 λ·κ 동일 조건 — 능력치 연결(λ_def ← 태클)을 덮어 같은 λ로
                        PitchControlTestKit.Engine(pl, 1 + i, a[i], Vec2.Zero, lambdaOverride: P.Lambda);
                        PitchControlTestKit.Engine(pl, 12 + i, b, Vec2.Zero, lambdaOverride: P.Lambda);
                    }
                }
                var q = new PitchControlQuery(P);
                for (float y = 0f; y <= 68f; y += 4f)
                {
                    var r = q.Point(pl, 0, new Vec2(52.5f, y), 0.5f);
                    Assert.AreEqual(r.Att, r.Def, 1e-6f, $"paper={paper} y={y}");
                }
            }
        }

        /// <summary>T07: ActionWorld 뷰는 배열을 공유하고(위치를 바꾸면 질의가 바로 본다), λ_def는 태클 능력치에서(FR-06).</summary>
        [Test]
        public void ActionWorldView_SharesArrays_LambdaFromTackling()
        {
            var w = new ActionWorld();
            var kin = ActionTestKit.Kin;
            w.SetPlayer(1, KinematicAttributes.Uniform(12), kin, new Vec2(50f, 34f), Vec2.Zero, new TechnicalAttributes(50f, 50f, 50f));
            w.SetPlayer(12, KinematicAttributes.Uniform(12), kin, new Vec2(60f, 34f), Vec2.Zero, new TechnicalAttributes(50f, 50f, 50f));
            var view = w.ControlPlayersView;
            Assert.IsTrue(view.Active[1] && view.Active[12]);
            Assert.AreSame(w.Pos, view.Pos);
            Assert.AreSame(w.Tti, view.Tti);
            Assert.AreEqual(1, view.Team[12]);
            Assert.IsTrue(view.IsKeeper[11]);
            w.Tackling[12] = 1f;
            view.LambdaOf(P, 12, out _, out var ldHigh);
            w.Tackling[12] = 0f;
            view.LambdaOf(P, 12, out _, out var ldLow);
            Assert.AreEqual(P.LambdaDefMax, ldHigh, 1e-6f);
            Assert.AreEqual(P.LambdaDefMin, ldLow, 1e-6f);
            var q = new PitchControlQuery(P);
            var near1 = q.Point(view, 0, new Vec2(52f, 34f), 0.3f);
            Assert.Greater(near1.Att, near1.Def);
        }

        [Test]
        public void Point_SumsToOne_1000()
        {
            var rng = new SeedRng(7007);
            var pl = new ControlPlayers();
            var q = new PitchControlQuery(P);
            for (int i = 0; i < 1000; i++)
            {
                PitchControlTestKit.Random(pl, rng, i % 2 == 0);
                var r = q.Point(pl, i % 4 < 2 ? 0 : 1, PitchControlTestKit.RandomPoint(rng, 5f), 3f * rng.NextFloat());
                foreach (var v in new[] { r.Att, r.Def, r.None })
                {
                    Assert.IsFalse(float.IsNaN(v), $"#{i} NaN");
                    Assert.That(v, Is.InRange(0f, 1f), $"#{i}");
                }
                Assert.AreEqual(1f, r.Att + r.Def + r.None, 1e-5f, $"#{i}");
            }
        }

        [Test]
        public void MovingCloser_NeverDecreases_1000()
        {
            var rng = new SeedRng(7008);
            var pl = new ControlPlayers();
            var q = new PitchControlQuery(P);
            var worst = 0f;
            var checkedN = 0;
            for (int i = 0; i < 1000; i++)
            {
                var paper = i % 2 == 0;
                PitchControlTestKit.Random(pl, rng, paper);
                var target = PitchControlTestKit.RandomPoint(rng);
                var tb = 2f * rng.NextFloat();
                var j = (int)(rng.NextFloat() * 22f) % 22;
                if (!pl.Active[j]) continue;
                var d = target - pl.Pos[j];
                var len = d.Length();
                if (len <= 1f) continue;
                var team = pl.Team[j];
                var before = q.Point(pl, 0, target, tb);
                var bt = team == 0 ? before.Att : before.Def;
                var np = pl.Pos[j] + d / len;
                if (paper) pl.SetPaper(j, team, pl.IsKeeper[j], np, pl.Vel[j], P);
                else PitchControlTestKit.Engine(pl, j, np, pl.Vel[j], 12);
                var after = q.Point(pl, 0, target, tb);
                var at = team == 0 ? after.Att : after.Def;
                if (!paper)
                {
                    // 엔진 모드는 능력치(12로 고정)도 바뀌므로 같은 능력치로 이전 위치를 다시 잰다
                    PitchControlTestKit.Engine(pl, j, np - d / len, pl.Vel[j], 12);
                    var b2 = q.Point(pl, 0, target, tb);
                    bt = team == 0 ? b2.Att : b2.Def;
                }
                checkedN++;
                if (bt - at > worst) worst = bt - at;
                Assert.GreaterOrEqual(at, bt - 1e-3f, $"#{i} slot {j} paper={paper}");
            }
            TestContext.WriteLine($"AC-04 {checkedN}케이스, 최대 감소 {worst:E2}");
            Assert.Greater(checkedN, 500);
        }
    }
}
