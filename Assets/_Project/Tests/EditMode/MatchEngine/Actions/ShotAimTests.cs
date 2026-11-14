using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Goalkeeper;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 FR-13 · AC-07: 조준 셀 선택.</summary>
    public class ShotAimTests
    {
        static ActionParams P => ActionTestKit.P;
        static readonly Vec2 Goal = new Vec2(105f, 34f);

        /// <summary>GK "왼쪽" = 골문 중앙 기준 y가 작은 쪽(셀 열 0 쪽). 오른쪽 셀 = 열 4~6.</summary>
        static bool RightCell(int cell) => cell % GoalGrid.Cols >= 4;

        [Test]
        public void GkLeft1m_RightCellsMore_Binomial_1000()
        {
            var aim = new ShotAim();
            var keeper = new StandInKeeperBeat(P);
            int Count(float gkY)
            {
                var rng = new SeedRng(7107);
                var k = 0;
                for (int i = 0; i < 1000; i++)
                {
                    var from = new Vec2(rng.Range(83f, 99f), rng.Range(24f, 44f));
                    var speed = ShotExecution.SpeedFor(P, rng.Range(0.3f, 0.9f));
                    var sigma = ShotExecution.SigmaAimDeg(P, rng.Range(0.2f, 0.9f), speed, rng.Range(0f, 0.6f), 0.5f, 1f, false);
                    if (RightCell(aim.Choose(from, Goal, speed, sigma, gkY, keeper))) k++;
                }
                return k;
            }
            var left = Count(-1f);
            var center = Count(0f);
            Assert.Greater(ShotTestKit.TwoPropZ(left, 1000, center, 1000), ShotTestKit.ZCrit, $"GK 왼쪽 {left} vs 중앙 {center}");
        }

        [Test]
        public void ChosenCell_IsArgmax_BeforeError()
        {
            var aim = new ShotAim();
            var keeper = new StandInKeeperBeat(P);
            var rng = new SeedRng(71071);
            for (int i = 0; i < 1000; i++)
            {
                var from = new Vec2(rng.Range(80f, 100f), rng.Range(20f, 48f));
                var gkY = rng.Range(-2f, 2f);
                var cell = aim.Choose(from, Goal, 25f, 4f, gkY, keeper);
                var best = 0f;
                for (int c = 0; c < ShotAim.Cells; c++) if (aim.Score[c] > best) best = aim.Score[c];
                Assert.AreEqual(best, aim.Score[cell], 0f, $"#{i}");
                for (int c = 0; c < cell; c++) Assert.Less(aim.Score[c], best, "같으면 낮은 번호");
            }
        }

        [Test]
        public void OnTarget_LowerForSmallerFinishing_And_CornerHigh()
        {
            var aim = new ShotAim();
            var corner = GoalGrid.Cell(2, 0);
            var mid = GoalGrid.Cell(0, 3);
            Assert.Less(aim.OnTarget(corner, 20f, 1f, 5f), aim.OnTarget(mid, 20f, 1f, 5f));
            Assert.Less(aim.OnTarget(corner, 20f, 1f, 8f), aim.OnTarget(corner, 20f, 1f, 2f));
        }

        [Test]
        public void AllZero_DefaultFarLowCorner()
        {
            Assert.AreEqual(GoalGrid.Cell(0, 0), ShotAim.DefaultCell(1f));
            Assert.AreEqual(GoalGrid.Cell(0, GoalGrid.Cols - 1), ShotAim.DefaultCell(-1f));
            Assert.AreEqual(GoalGrid.Cell(0, GoalGrid.Cols - 1), ShotAim.DefaultCell(float.NaN));
        }
    }
}
