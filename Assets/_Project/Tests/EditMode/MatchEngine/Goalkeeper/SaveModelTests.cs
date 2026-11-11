using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.Tests.EditMode.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/011 선방 판정 FR-03·09~14 (AC-03·04·06·19·26).</summary>
    public class SaveModelTests
    {
        static readonly Vec2 Goal = new Vec2(0f, 34f);
        static GoalkeeperParams P => GoalkeeperParams.Defaults;
        static KeeperProfile Avg => KeeperProfile.From(P, KeeperAttributes.Uniform(10.5f));
        static readonly BallParams Bp = BallTestKit.Params();

        static SaveModel Model()
        {
            var m = new SaveModel(P, new KeeperPositioner(P, new GoalGrid(P)));
            m.Bind(Bp);
            return m;
        }

        static SaveContext Ctx(float extra = 0f, int crowd = 0) => new SaveContext { Goal = Goal, DirIn = 1f, ExtraDelay = extra, Crowd = crowd };
        static KeeperState At(Vec2 p, Vec2 v = default) => new KeeperState { Position = p, Velocity = v };

        static Vec2 Optimal(SaveModel m, Vec2 ball) =>
            m.Positioner.Target(new PositionInput { Goal = Goal, DirIn = 1f, Ball = ball, LineHeight = 30f }, Avg);

        /// <summary>같은 슛 n개를 두 GK에게 — (on-target 수, 실점 수).</summary>
        static (int onTarget, int conceded) Shoot(SaveModel m, Func<int, BallState> shot, int n, KeeperState gk, in KeeperProfile k, ulong seed)
        {
            var rng = new SeedRng(seed);
            int on = 0, goals = 0;
            for (int i = 0; i < n; i++)
            {
                var s = shot(i);
                if (!m.Predict(in s, Bp, Goal, 1f)) { rng.NextFloat(); rng.NextFloat(); rng.NextFloat(); continue; }
                on++;
                var j = m.Evaluate(in gk, in k, Ctx());
                var o = m.Resolve(in j, in k, Ctx(), rng);
                if (o.Kind == SaveOutcomeKind.Miss) goals++;
            }
            return (on, goals);
        }

        /// <summary>두 비율 차 단측 z 검정: a가 b보다 크면 z &gt; 2.33 (p &lt; 0.01).</summary>
        static double Z(int xa, int na, int xb, int nb)
        {
            double pa = (double)xa / na, pb = (double)xb / nb, pp = (double)(xa + xb) / (na + nb);
            var se = Math.Sqrt(pp * (1 - pp) * (1.0 / na + 1.0 / nb));
            return se <= 0 ? 0 : (pa - pb) / se;
        }

        static Func<int, BallState> RandomCells(SaveModel m, Vec3 origin, float speed, ulong seed)
        {
            var cells = new int[1000];
            var rng = new SeedRng(seed);
            var w = new float[GoalGrid.Cells];
            for (int c = 0; c < GoalGrid.Cells; c++) w[c] = m.Positioner.Grid.Prior(c);
            for (int i = 0; i < cells.Length; i++) cells[i] = rng.PickWeighted(w);
            return i => m.AimedShot(origin, cells[i], speed, Goal, Bp);
        }

        [Test]
        public void OptimalPosition_BeatsLineCenter_Binomial()
        {
            var m = Model();
            var ball = new Vec2(14f, 24f);
            var origin = new Vec3(ball.X, ball.Y, 0.11f);
            var shots = RandomCells(m, origin, 24f, 31UL);
            var opt = Shoot(m, shots, 1000, At(Optimal(m, ball)), Avg, 7UL);
            var line = Shoot(m, shots, 1000, At(new Vec2(0.3f, 34f)), Avg, 7UL);
            TestContext.WriteLine($"P* 실점 {opt.conceded}/{opt.onTarget}, 골라인 중앙 {line.conceded}/{line.onTarget}");
            Assert.Greater(Z(line.conceded, line.onTarget, opt.conceded, opt.onTarget), 2.33);
        }

        [Test]
        public void ChipShot_DeepKeeperConcedesMore()
        {
            var m = Model();
            var ball = new Vec2(22f, 34f);
            var rng = new SeedRng(12UL);
            var shots = new BallState[1000];
            for (int i = 0; i < shots.Length; i++)
            {
                // 크로스바 아래로 떨어지는 칩: 비행 1.4~1.7 s, 목표 높이 1.4~2.2 m
                var t = rng.Range(1.4f, 1.7f);
                var ty = Goal.Y + rng.Range(-3f, 3f);
                var tz = rng.Range(1.4f, 2.2f);
                var dx = -ball.X;
                var dy = ty - ball.Y;
                var vz = (tz - 0.11f + 0.5f * Bp.Gravity * t * t) / t;
                shots[i] = BallState.InAir(new Vec3(ball.X, ball.Y, 0.11f), new Vec3(dx / t * 1.12f, dy / t * 1.12f, vz), Vec3.Zero);
            }
            var deep = Shoot(m, i => shots[i], 1000, At(new Vec2(6f, 34f)), Avg, 9UL);
            var shallow = Shoot(m, i => shots[i], 1000, At(new Vec2(2f, 34f)), Avg, 9UL);
            TestContext.WriteLine($"깊이 6 실점 {deep.conceded}/{deep.onTarget}, 깊이 2 {shallow.conceded}/{shallow.onTarget}");
            Assert.GreaterOrEqual(deep.onTarget, 300, "칩 픽스처가 골문으로 충분히 간다");
            Assert.Greater(Z(deep.conceded, deep.onTarget, shallow.conceded, shallow.onTarget), 2.33);
        }

        static double SaveRate(SaveModel m, int row, int[] cols, float speed, ulong seed, int n = 1000)
        {
            var rng = new SeedRng(seed);
            var drng = new SeedRng(seed + 1);
            int on = 0, saved = 0;
            for (int i = 0; i < n; i++)
            {
                var ball = new Vec2(rng.Range(14f, 20f), Goal.Y + rng.Range(-6f, 6f));
                var cell = GoalGrid.Cell(row, cols[i % cols.Length]);
                var s = m.AimedShot(new Vec3(ball.X, ball.Y, 0.11f), cell, speed, Goal, Bp);
                var gk = At(Optimal(m, ball));
                if (!m.Predict(in s, Bp, Goal, 1f)) continue;
                on++;
                var j = m.Evaluate(in gk, Avg, Ctx());
                var o = m.Resolve(in j, Avg, Ctx(), drng);
                if (o.Kind != SaveOutcomeKind.Miss) saved++;
            }
            Assert.Greater(on, n / 2, "대부분 유효 슛");
            return (double)saved / on;
        }

        [Test]
        public void CenterShot_SpeedIndependent()
        {
            var m = Model();
            var r20 = SaveRate(m, 1, new[] { 3 }, 20f, 41UL);
            var r30 = SaveRate(m, 1, new[] { 3 }, 30f, 41UL);
            TestContext.WriteLine($"중앙 20 m/s {r20:P1}, 30 m/s {r30:P1}");
            Assert.LessOrEqual(Math.Abs(r20 - r30), 0.05);
            Assert.Greater(r20, 0.85, "중앙 슛은 높은 선방률");
        }

        [Test]
        public void HighCorner_Below15_LowAboveHigh()
        {
            var m = Model();
            var high = SaveRate(m, 2, new[] { 0, 6 }, 26f, 43UL);
            var low = SaveRate(m, 0, new[] { 0, 6 }, 26f, 43UL);
            TestContext.WriteLine($"높은 구석 {high:P1}, 낮은 구석 {low:P1}");
            Assert.LessOrEqual(high, 0.15);
            Assert.Greater(low, high);
        }

        [Test, Explicit("리포트용 셀별 선방률 표")]
        public void Report_CellSaveRates()
        {
            var m = Model();
            var sb = new System.Text.StringBuilder();
            for (int row = 2; row >= 0; row--)
            {
                for (int col = 0; col < GoalGrid.Cols; col++) sb.Append($"{SaveRate(m, row, new[] { col }, 25f, 50UL, 400):P0}\t");
                sb.AppendLine();
            }
            TestContext.WriteLine(sb.ToString());
        }

        [Test]
        public void Handling_RaisesCatch()
        {
            var m = Model();
            var good = KeeperProfile.From(P, Handling(100f));
            var bad = KeeperProfile.From(P, Handling(0f));
            var origin = new Vec3(16f, 32f, 0.11f);
            var shots = RandomCells(m, origin, 22f, 61UL);
            var gk = At(Optimal(m, new Vec2(origin.X, origin.Y)));
            int cg = 0, cb = 0, n = 0;
            var rg = new SeedRng(5UL);
            var rb = new SeedRng(5UL);
            for (int i = 0; i < 1000; i++)
            {
                var s = shots(i);
                if (!m.Predict(in s, Bp, Goal, 1f)) continue;
                n++;
                var jg = m.Evaluate(in gk, in good, Ctx());
                if (m.Resolve(in jg, in good, Ctx(), rg).Kind == SaveOutcomeKind.Catch) cg++;
                var jb = m.Evaluate(in gk, in bad, Ctx());
                if (m.Resolve(in jb, in bad, Ctx(), rb).Kind == SaveOutcomeKind.Catch) cb++;
            }
            TestContext.WriteLine($"잡기 s=1 {cg}/{n}, s=0 {cb}/{n}");
            Assert.Greater(Z(cg, n, cb, n), 2.33);
        }

        static KeeperAttributes Handling(float h) => new KeeperAttributes(50, 50, h, 50, 50, 50, 50, 50, 50, 50, 50, 1.88f);

        [TestCase(0f, 6f)]
        [TestCase(100f, 3f)]
        public void SpillMean_MatchesFormula(float handling, float expected)
        {
            var m = Model();
            var k = KeeperProfile.From(P, Handling(handling));
            var s = m.AimedShot(new Vec3(14f, 34f, 0.11f), GoalGrid.Cell(1, 1), 26f, Goal, Bp);
            Assert.IsTrue(m.Predict(in s, Bp, Goal, 1f));
            var j = m.Evaluate(At(new Vec2(1.5f, 34f)), in k, Ctx());
            j.PCatch = 0f;
            j.PParrySafe = 0f;
            j.PReach = 1f;
            var rng = new SeedRng(3UL);
            double sum = 0;
            for (int i = 0; i < 2000; i++)
            {
                var o = m.Resolve(in j, in k, Ctx(), rng);
                Assert.AreEqual(SaveOutcomeKind.ParryDanger, o.Kind);
                Assert.That(o.ReboundDepth, Is.InRange(3f, 8f));
                sum += o.ReboundDepth;
            }
            Assert.AreEqual(expected, sum / 2000, 0.3);
        }

        [Test]
        public void Rebound_NeverBehindGoalLine()
        {
            var m = Model();
            var rng = new SeedRng(77UL);
            var srng = new SeedRng(78UL);
            var ev = new BallEventBuffer(16);
            for (int i = 0; i < 400; i++)
            {
                var origin = new Vec3(srng.Range(8f, 24f), Goal.Y + srng.Range(-12f, 12f), 0.11f);
                var s = m.AimedShot(origin, (int)(srng.NextFloat() * GoalGrid.Cells), srng.Range(16f, 30f), Goal, Bp);
                if (!m.Predict(in s, Bp, Goal, 1f)) continue;
                var gk = At(Optimal(m, new Vec2(origin.X, origin.Y)));
                var j = m.Evaluate(in gk, Avg, Ctx());
                j.PReach = 1f;
                var o = m.Resolve(in j, Avg, Ctx(), rng);
                if (o.Kind != SaveOutcomeKind.ParryDanger && o.Kind != SaveOutcomeKind.ParrySafe && o.Kind != SaveOutcomeKind.Punch) continue;
                var b = BallState.InAir(j.ContactPoint, o.ReboundVelocity, Vec3.Zero);
                for (int step = 0; step < 600 && b.Phase != BallPhase.Rest; step++)
                {
                    ev.Clear();
                    BallStepper.Step(ref b, Bp, ev, out _);
                    for (int e = 0; e < ev.Count; e++)
                    {
                        if (ev[e].Kind != BallEventKind.BoundaryCrossed) continue;
                        Assert.IsFalse(ev[e].InGoalMouth, $"리바운드가 골문 안 ({o.Kind}, shot {i})");
                        if (o.Kind == SaveOutcomeKind.ParryDanger) Assert.AreNotEqual(BallBoundary.GoalLineLow, ev[e].Boundary, $"위험한 쳐내기가 골라인 뒤 (shot {i})");
                    }
                }
            }
        }

        [Test]
        public void Evaluate_MatchesResolve_100k_NoRngChange()
        {
            var m = Model();
            var origin = new Vec3(17f, 30f, 0.11f);
            var gk = At(Optimal(m, new Vec2(17f, 30f)));
            // 실점 확률이 극단이 아닌 첫 셀을 고정 픽스처로 (셀 순서 고정 → 결정론)
            int cell = -1;
            for (int c = 0; c < GoalGrid.Cells && cell < 0; c++)
            {
                var b = m.BeatProbability(origin, c, 25f, in gk, Avg, Ctx(), Bp);
                if (b > 0.2f && b < 0.8f) cell = c;
            }
            Assert.GreaterOrEqual(cell, 0, "중간 확률 셀 없음");
            var s = m.AimedShot(origin, cell, 25f, Goal, Bp);
            var probe = new SeedRng(99UL);
            var before = probe.SaveState();
            var beat = m.BeatProbability(origin, cell, 25f, in gk, Avg, Ctx(), Bp);
            Assert.AreEqual(before, probe.SaveState(), "평가 함수는 RNG를 쓰지 않는다");
            Assert.IsTrue(m.Predict(in s, Bp, Goal, 1f));
            var j = m.Evaluate(in gk, Avg, Ctx());
            Assert.AreEqual(j.BeatProbability, beat, 1e-6f);
            var rng = new SeedRng(1234UL);
            int goals = 0;
            const int N = 100_000;
            for (int i = 0; i < N; i++) if (m.Resolve(in j, Avg, Ctx(), rng).Kind == SaveOutcomeKind.Miss) goals++;
            TestContext.WriteLine($"P_beatGK {beat:F4}, 실제 {goals / (double)N:F4}");
            Assert.AreEqual(beat, goals / (double)N, 0.01);
        }

        [Test]
        public void MovingKeeper_ReachPenalty()
        {
            var m = Model();
            var s = m.AimedShot(new Vec3(16f, 30f, 0.11f), GoalGrid.Cell(1, 5), 24f, Goal, Bp);
            Assert.IsTrue(m.Predict(in s, Bp, Goal, 1f));
            var pos = new Vec2(2f, 33f);
            var still = m.Evaluate(At(pos), Avg, Ctx());
            var moving = m.Evaluate(At(pos, new Vec2(0f, 2f)), Avg, Ctx());
            var slow = m.Evaluate(At(pos, new Vec2(0f, 0.9f)), Avg, Ctx());
            Assert.IsTrue(moving.MovingPenalty);
            Assert.AreEqual(P.StillMovingPenalty, still.MaxMargin - moving.MaxMargin, 1e-6f);
            Assert.IsFalse(slow.MovingPenalty);
            Assert.AreEqual(still.MaxMargin, slow.MaxMargin, 0f);
        }

        [Test]
        public void UltraClose_UsesR0()
        {
            var m = Model();
            var s = m.AimedShot(new Vec3(3f, 34f, 0.11f), GoalGrid.Cell(0, 6), 28f, Goal, Bp);
            Assert.IsTrue(m.Predict(in s, Bp, Goal, 1f));
            var j = m.Evaluate(At(new Vec2(1f, 34f)), Avg, Ctx());
            Assert.Less(j.CrossTime, Avg.ReactTime, "비행 시간 < 반응 시간");
            Assert.AreEqual(P.ReachR0, j.Reach, 1e-6f);
            Assert.IsFalse(float.IsNaN(j.PReach));
        }

        [Test]
        public void OffTarget_NotJudged()
        {
            var m = Model();
            var s = BallState.InAir(new Vec3(16f, 34f, 0.11f), new Vec3(-20f, 10f, 1f), Vec3.Zero);
            Assert.IsFalse(m.Predict(in s, Bp, Goal, 1f));
            var j = m.Evaluate(At(new Vec2(2f, 34f)), Avg, Ctx());
            Assert.AreEqual(0f, j.PReach);
            Assert.AreEqual(0f, j.BeatProbability);
        }
    }
}
