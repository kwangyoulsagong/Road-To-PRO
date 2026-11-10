using System.Linq;
using NUnit.Framework;
using RoadToPro.Presentation.Rendering.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>프레임 예산 표 (specs/048 FR-20·21·22, AC-16 로직).</summary>
    public class FrameBudgetTests
    {
        [Test]
        public void FrameBudget_SharesSumToTotals()
        {
            Assert.AreEqual(FrameBudget.GpuMs, FrameBudget.Shares.Sum(s => s.GpuMs), 1e-9);
            Assert.AreEqual(FrameBudget.DrawCallsLow, FrameBudget.Shares.Sum(s => s.DrawCalls));
            Assert.AreEqual(FrameBudget.Triangles, FrameBudget.Shares.Sum(s => s.Triangles));
            Assert.AreEqual(FrameBudget.CpuMainMs, FrameBudget.CpuShares.Sum(s => s.Ms), 1e-9);
        }

        [Test]
        public void FrameBudget_CitedSharesMatchOtherSpecs()
        {
            // 044: 경기장 합계 드로우콜 60·삼각형 10만, 043: 2 ms·75·15만, 045: 1.5 ms·20·10만, 039: 30
            FrameBudget.TryGetShare(FrameBudget.Pitch, out var pitch);
            FrameBudget.TryGetShare(FrameBudget.Structure, out var st);
            Assert.AreEqual(60, pitch.DrawCalls + st.DrawCalls);
            Assert.AreEqual(100_000, pitch.Triangles + st.Triangles);
            FrameBudget.TryGetShare(FrameBudget.Players, out var pl);
            Assert.AreEqual((2.0, 75, 150_000), (pl.GpuMs, pl.DrawCalls, pl.Triangles));
            FrameBudget.TryGetShare(FrameBudget.Crowd, out var cr);
            Assert.AreEqual((1.5, 20, 100_000), (cr.GpuMs, cr.DrawCalls, cr.Triangles));
            FrameBudget.TryGetShare(FrameBudget.Hud, out var hud);
            Assert.AreEqual(30, hud.DrawCalls);
        }

        [Test]
        public void FrameBudget_OverShare_Reported()
        {
            var over = FrameBudget.Overruns(new[]
            {
                new MeasuredShare(FrameBudget.Players, 2.5, 70, 160_000),
                new MeasuredShare(FrameBudget.Pitch, 0.4, 4, 2_000),
                new MeasuredShare(FrameBudget.Reserve, 99, 999, 9_999_999),
                new MeasuredShare("unknown", 99, 999, 9_999_999),
            });
            Assert.AreEqual(2, over.Count);
            Assert.IsTrue(over.All(o => o.System == FrameBudget.Players));
            CollectionAssert.AreEquivalent(new[] { "gpuMs", "triangles" }, over.Select(o => o.Metric));
        }

        [Test]
        public void FrameBudget_WithinShare_NoOverrun()
        {
            var measured = FrameBudget.Shares.Select(s => new MeasuredShare(s.System, s.GpuMs, s.DrawCalls, s.Triangles));
            Assert.IsEmpty(FrameBudget.Overruns(measured));
        }
    }
}
