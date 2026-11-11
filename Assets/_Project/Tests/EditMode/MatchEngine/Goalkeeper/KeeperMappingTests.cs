using System;
using System.Reflection;
using NUnit.Framework;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/011 FR-70 매핑 · 튜닝 기본값 · 7×3 격자 (T01·T02).</summary>
    public class KeeperMappingTests
    {
        static GoalkeeperParams P => GoalkeeperParams.Defaults;

        [Test]
        public void FileDefaults_EqualCodeDefaults()
        {
            var bundle = MatchTestKit.Goalkeeper();
            var fromFile = GoalkeeperParams.Create(bundle);
            var fields = typeof(GoalkeeperParams).GetFields(BindingFlags.Public | BindingFlags.Instance);
            var checkedCount = 0;
            foreach (var f in fields)
            {
                if (f.FieldType != typeof(float)) continue;
                Assert.AreEqual((float)f.GetValue(GoalkeeperParams.Defaults), (float)f.GetValue(fromFile), f.Name);
                checkedCount++;
            }
            Assert.AreEqual(bundle.Registry.Count, checkedCount, "튜닝 정의 수 = 코드 필드 수");
        }

        [Test]
        public void Mapping_FormulasAtBounds()
        {
            Assert.AreEqual(0.32f, KeeperMapping.ReactTime(P, 0f), 1e-6f);
            Assert.AreEqual(0.18f, KeeperMapping.ReactTime(P, 1f), 1e-6f);
            Assert.AreEqual(3.0f, KeeperMapping.DiveSpeed(P, 0f), 1e-6f);
            Assert.AreEqual(4.6f, KeeperMapping.DiveSpeed(P, 1f), 1e-6f);
            Assert.AreEqual(2f, KeeperMapping.HandlingA(P, 0f), 1e-6f);
            Assert.AreEqual(6f, KeeperMapping.HandlingA(P, 1f), 1e-6f);
            Assert.AreEqual(6f, KeeperMapping.SpillMean(P, 0f), 1e-6f);
            Assert.AreEqual(3f, KeeperMapping.SpillMean(P, 1f), 1e-6f);
            Assert.AreEqual(0.6f, KeeperMapping.PositionSigma(P, 0f), 1e-6f);
            Assert.AreEqual(0.1f, KeeperMapping.PositionSigma(P, 1f), 1e-6f);
            Assert.AreEqual(0.15f, KeeperMapping.OneOnOneSigma(P, 0f), 1e-6f);
            Assert.AreEqual(0.2f, KeeperMapping.CommandDelay(P, 0f), 1e-6f);
            Assert.AreEqual(0f, KeeperMapping.CommandDelay(P, 1f), 1e-6f);
            Assert.AreEqual(0.4f, KeeperMapping.SweepMargin(P, 0f), 1e-6f);
            Assert.AreEqual(-0.1f, KeeperMapping.SweepMargin(P, 1f), 1e-6f);
            Assert.AreEqual(6f, KeeperMapping.DistributionSigmaDeg(P, 0f), 1e-6f);
            Assert.AreEqual(1.5f, KeeperMapping.DistributionSigmaDeg(P, 1f), 1e-6f);
            Assert.AreEqual(0.35f, KeeperMapping.Temperature(P, 0f), 1e-6f);
            Assert.AreEqual(0.05f, KeeperMapping.Temperature(P, 1f), 1e-6f);
            Assert.AreEqual(P.ReachRmaxBase + 0.3f, KeeperMapping.MaxReach(P, P.ReachRefHeight, 1f), 1e-6f);
            Assert.Less(KeeperMapping.CandidateCount(P, 0f), KeeperMapping.CandidateCount(P, 1f));
            Assert.AreEqual(15, KeeperMapping.CandidateCount(P, 1f));
        }

        [Test]
        public void Mapping_MidValue_IsBetween()
        {
            var mid = KeeperMapping.ReactTime(P, 0.5f);
            Assert.That(mid, Is.InRange(0.18f, 0.32f));
            Assert.AreEqual(0.25f, mid, 1e-6f);
        }

        [Test]
        public void Attributes_RejectOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new KeeperAttributes(101f, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 1.88f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new KeeperAttributes(float.NaN, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 1.88f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new KeeperAttributes(50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 2.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => KeeperAttributes.Uniform(0.5f));
            Assert.IsFalse(default(KeeperAttributes).IsSet);
            Assert.IsTrue(KeeperAttributes.Uniform(10.5f).IsSet);
            Assert.AreEqual(50f, KeeperAttributes.InternalFromDisplay(10.5f), 1e-4f);
        }

        [Test]
        public void Grid_CellCentersAndPrior()
        {
            var g = new GoalGrid(P);
            Assert.AreEqual(21, GoalGrid.Cells);
            float sum = 0f;
            for (int i = 0; i < GoalGrid.Cells; i++) sum += g.Prior(i);
            Assert.AreEqual(1f, sum, 1e-5f);
            Assert.AreEqual(0f, g.OffsetY(GoalGrid.Cell(1, 3)), 1e-5f, "가운데 열 중심 = 골문 중앙");
            Assert.AreEqual(-g.OffsetY(GoalGrid.Cell(0, 0)), g.OffsetY(GoalGrid.Cell(0, 6)), 1e-5f, "좌우 대칭");
            Assert.Less(g.OffsetY(GoalGrid.Cell(0, 6)), 3.66f);
            Assert.AreEqual(2.44f * 5f / 6f, g.Height(GoalGrid.Cell(2, 0)), 1e-5f);
            Assert.Greater(g.Prior(GoalGrid.Cell(0, 0)), g.Prior(GoalGrid.Cell(1, 0)), "낮은 구석 > 중간");
            Assert.Greater(g.Prior(GoalGrid.Cell(0, 0)), g.Prior(GoalGrid.Cell(0, 3)), "구석 > 중앙");
        }

        [Test]
        public void Profile_FromLeagueAverage()
        {
            var prof = KeeperProfile.From(P, KeeperAttributes.Uniform(10.5f));
            Assert.AreEqual(0.25f, prof.ReactTime, 1e-4f);
            Assert.AreEqual(3.8f, prof.DiveSpeed, 1e-4f);
            Assert.AreEqual(4.5f, prof.SpillMean, 1e-4f);
        }
    }
}
