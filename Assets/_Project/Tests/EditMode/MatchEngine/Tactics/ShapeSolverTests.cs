using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public class ShapeSolverTests
    {
        const float L = ShapeTestKit.PitchL, W = ShapeTestKit.PitchW;

        static int[] Lines(TeamShapeEngine e)
        {
            var xs = new List<float>();
            for (int s = 1; s < 11; s++) xs.Add(e.SlotTarget(s).X * L);
            xs.Sort();
            var r = new List<int>();
            int i = 0;
            while (i < xs.Count)
            {
                int j = i;
                while (j < xs.Count && xs[j] - xs[i] <= 6f) j++;
                r.Add(j - i);
                i = j;
            }
            return r.ToArray();
        }

        [TestCase("4-3-3", new[] { 3, 2, 5 }, new[] { 4, 5, 1 })]
        [TestCase("4-2-3-1", new[] { 3, 2, 4, 1 }, new[] { 4, 4, 2 })]
        [TestCase("4-4-2", new[] { 4, 2, 4 }, new[] { 4, 4, 2 })]
        [TestCase("3-5-2", new[] { 3, 1, 4, 2 }, new[] { 5, 3, 2 })]
        [TestCase("3-4-3", new[] { 3, 2, 5 }, new[] { 5, 4, 1 })]
        [TestCase("5-3-2", new[] { 3, 5, 2 }, new[] { 5, 3, 2 })]
        public void FormationShapes_MatchA4Table(string id, int[] buildUp, int[] defMid)
        {
            var ins = ShapeTestKit.Ins();
            var a = ShapeTestKit.Engine(id);
            ShapeTestKit.Settle(a, ShapeTestKit.Frame(TeamPhase.BuildUp, new Vec2(0.2f, 0.5f)), in ins);
            Assert.That(Lines(a), Is.EqualTo(buildUp), "BuildUp");
            var d = ShapeTestKit.Engine(id);
            ShapeTestKit.Settle(d, ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(0.5f, 0.5f)), in ins);
            Assert.That(Lines(d), Is.EqualTo(defMid), "DefMid");
        }

        [TestCase(1f / 6f, TeamPhase.DefLow, 22f, 32f, 25f, 30f, 35f, 40f)]
        [TestCase(0.5f, TeamPhase.DefMid, 33f, 45f, 28f, 35f, 38f, 45f)]
        [TestCase(5f / 6f, TeamPhase.DefHigh, 46f, 58f, 30f, 40f, 40f, 48f)]
        public void BlockPresets_SteadyStateInTable(float lineHeight, TeamPhase phase, float lineMin, float lineMax, float lenMin, float lenMax, float widMin, float widMax)
        {
            foreach (var id in new[] { "4-3-3", "4-2-3-1", "4-4-2", "3-5-2", "3-4-3", "5-3-2" })
            {
                var ins = ShapeTestKit.Ins(lineHeight);
                var e = ShapeTestKit.Engine(id);
                ShapeTestKit.Settle(e, ShapeTestKit.Frame(phase, new Vec2(0.5f, 0.5f)), in ins, 100);
                float minX = 1, maxX = 0, minY = 1, maxY = 0;
                for (int s = 1; s < 11; s++)
                {
                    var t = e.SlotTarget(s);
                    minX = Math.Min(minX, t.X); maxX = Math.Max(maxX, t.X); minY = Math.Min(minY, t.Y); maxY = Math.Max(maxY, t.Y);
                }
                Assert.That(e.LineX * L, Is.InRange(lineMin, lineMax), id + " 라인 높이");
                Assert.That((maxX - minX) * L, Is.InRange(lenMin, lenMax + 1e-3f), id + " 길이");
                Assert.That((maxY - minY) * W, Is.InRange(widMin, widMax + 1e-3f), id + " 폭");
            }
        }

        [Test]
        public void Compactness_Shrinks()
        {
            var ins = ShapeTestKit.Ins();
            var a = ShapeTestKit.Engine();
            ShapeTestKit.Settle(a, ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(0.5f, 0.5f)), in ins);
            var tight = ins; tight.Compactness = 1f;
            var b = ShapeTestKit.Engine();
            ShapeTestKit.Settle(b, ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(0.5f, 0.5f)), in tight);
            Assert.That(b.SlotTarget(9).X - b.SlotTarget(1).X, Is.LessThanOrEqualTo(a.SlotTarget(9).X - a.SlotTarget(1).X + 1e-5f));
        }

        [Test]
        public void Project_PullsFrontLine()
        {
            var p = ShapeTestKit.Params();
            var b = new SolvedBlock { Cx = .5f, Cy = .5f, L = 30, W = 40, LMax = 30, WMax = 45 };
            var t = new Vec2[11];
            var active = new bool[11];
            var skip = new bool[11];
            for (int i = 1; i < 11; i++) { active[i] = true; t[i] = new Vec2(0.3f + 0.05f * i, 0.5f); }
            ShapeSolver.Project(p, in b, active, skip, t);
            float lo = 1, hi = 0;
            for (int i = 1; i < 11; i++) { lo = Math.Min(lo, t[i].X); hi = Math.Max(hi, t[i].X); }
            Assert.That((hi - lo) * L, Is.LessThanOrEqualTo(30f + 1e-3f));
        }

        [Test]
        public void Blend_DurationFromTeamwork()
        {
            var ins = ShapeTestKit.Ins();
            var e = ShapeTestKit.Engine();
            var f = ShapeTestKit.Frame(TeamPhase.DefMid, new Vec2(0.5f, 0.5f));
            ShapeTestKit.Settle(e, f, in ins);
            var before = e.SlotTarget(9);
            f.Phase = TeamPhase.BuildUp;
            ShapeTestKit.Settle(e, f, in ins, 1);
            var mid = e.SlotTarget(9);
            ShapeTestKit.Settle(e, f, in ins, 20);
            var after = e.SlotTarget(9);
            Assert.That(Math.Abs(mid.X - before.X), Is.GreaterThan(0f));
            Assert.That(Math.Abs(mid.X - before.X), Is.LessThan(Math.Abs(after.X - before.X)));
        }
    }
}
