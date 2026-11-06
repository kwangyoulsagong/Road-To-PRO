using System;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Logic.Motion;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>선수 간 상호작용 (specs/054 AC-07·08, FR-40~45·83·85·86).</summary>
    public class InteractionPlannerTests
    {
        [TestCaseSource(typeof(MotionScenesTests), nameof(MotionScenesTests.AllIds))]
        public void NoPenetration(string id)
        {
            var s = MotionStandardsTests.Load();
            var r = MotionRun.Measure(id);
            Assert.LessOrEqual(r.Max("torsoOverlap"), s.Max("torsoOverlap"), id);
            Assert.LessOrEqual(r.Max("limbPenetration"), s.Max("limbPenetration"), id);
            Assert.LessOrEqual(r.Max("postPenetration"), s.Max("postPenetration"), id);
        }

        static HighlightData Pair(float gap)
        {
            var d = new HighlightData(0f, 61);
            for (int f = 0; f < 61; f++)
            {
                d.Set(f, 0, 0f, 0f, 0f);
                d.Set(f, 1, gap, 0f, 0f);
            }
            return d;
        }

        [Test]
        public void TooClose_PushedApart_SymmetricAndSmooth()
        {
            var ip = new InteractionPlanner(Pair(0.1f), null);
            ip.At(0, 1f, out var ax, out var az, out _);
            ip.At(1, 1f, out var bx, out var bz, out _);
            Assert.Less(ax, 0f);
            Assert.Greater(bx, 0f);
            Assert.AreEqual(-ax, bx, 1e-4f);
            Assert.GreaterOrEqual(0.1f + bx - ax, InteractionPlanner.MinRootGap - 1e-3f, "떨어져 몸통 허용 겹침 안");
        }

        [Test]
        public void FarApart_NoPush_NoTwist()
        {
            var ip = new InteractionPlanner(Pair(3f), null);
            ip.At(0, 1f, out var ax, out var az, out var tw);
            Assert.AreEqual(0f, ax);
            Assert.AreEqual(0f, az);
            Assert.AreEqual(0f, tw);
        }

        [Test]
        public void PassingClose_Twists_Evasion()
        {
            var ip = new InteractionPlanner(Pair(0.6f), null);
            ip.At(0, 1f, out _, out _, out var a);
            ip.At(1, 1f, out _, out _, out var b);
            Assert.Less(Math.Abs(a), InteractionPlanner.MaxTwist + 1e-4f);
            Assert.Greater(Math.Abs(a), 0.05f, "FR-45 어깨를 돌린다");
            Assert.Greater(Math.Abs(b), 0.05f);
        }

        [Test]
        public void GoalPost_PushesOut()
        {
            var d = new HighlightData(0f, 61);
            for (int f = 0; f < 61; f++) d.Set(f, 0, -52.5f + 0.02f, -3.66f, 0f);
            var ip = new InteractionPlanner(d, new[] { -52.5f, -3.66f });
            ip.At(0, 1f, out var ox, out var oz, out _);
            Assert.GreaterOrEqual((float)Math.Sqrt((0.02f + ox) * (0.02f + ox) + oz * oz), InteractionPlanner.MinPostGap - 1e-3f);
        }

        [Test]
        public void Present_False_NoInteraction()
        {
            var d = Pair(0.1f);
            for (int f = 0; f < 61; f++) d.Set(f, 1, 0.1f, 0f, 0f, false);
            var ip = new InteractionPlanner(d, null);
            ip.At(0, 1f, out var ax, out _, out _);
            Assert.AreEqual(0f, ax, "FR-85: 없는 선수와 겹치지 않는다");
        }

        // --- AC-08 ---

        static Vector3 FootPoint(AnimRigKit kit, int e, string clip)
        {
            var c = AnimTestKit.Lib.Get(AnimTestKit.Lib.IndexOf(clip));
            var p = kit.ContactPoint(e, c.Rule);
            return new Vector3(p.X, p.Y, p.Z);
        }

        [Test]
        public void OutcomeOrder_TackleWin_BallFirst()
        {
            var m = MotionSceneKit.Scene("tackle");
            var std = MotionStandardsTests.Load();
            using var kit = new AnimRigKit(m.Data, m.Actors);
            var win = m.Data.Events[0];
            Assert.AreEqual(ActionOutcome.Win, win.Outcome);
            kit.System.ApplyAt(win.Time);
            var foot = FootPoint(kit, 1, "tackle_win_r");
            var ball = kit.Plan.BallVisual(win.Time - 1e-3f);
            Assert.LessOrEqual((foot - new Vector3(ball.X, ball.Y, ball.Z)).magnitude, std.Max("tackleFootDist"), "성공 태클: 공에 닿는다");
        }

        [Test]
        public void OutcomeOrder_TackleFoul_LegNotBall_VictimFalls()
        {
            var m = MotionSceneKit.Scene("tackle");
            var std = MotionStandardsTests.Load();
            using var kit = new AnimRigKit(m.Data, m.Actors);
            var tk = m.Data.Events[1];
            var foul = m.Data.Events[2];
            Assert.AreEqual(ActionOutcome.Fail, tk.Outcome);
            kit.System.ApplyAt(tk.Time);
            var foot = FootPoint(kit, 3, "tackle_slide_r");
            var ballNow = m.Data.BallAt(tk.Time);
            Assert.Greater((foot - new Vector3(ballNow.X, ballNow.Y, ballNow.Z)).magnitude, std.Max("tackleFootDist"), "파울: 공에 닿지 않는다");
            // 상대 정강이·발 캡슐 표면까지 (FR-41 10 cm)
            float leg = float.MaxValue;
            foreach (var (a, b) in new[] { ("LeftLowerLeg", "LeftFoot"), ("RightLowerLeg", "RightFoot"), ("LeftFoot", "LeftToes"), ("RightFoot", "RightToes") })
            {
                Vec3 V(Vector3 x) => new Vec3(x.x, x.y, x.z);
                var fp = V(foot);
                leg = Mathf.Min(leg, BodyCapsules.SegmentDistance(fp, fp, V(kit.Rigs[2].Bone(a).position), V(kit.Rigs[2].Bone(b).position)) - 0.05f);
            }
            Assert.LessOrEqual(leg, std.Max("tackleFootDist"), "파울: 상대 다리에 닿는다");
            // 넘어짐: 접촉 방향과 45° 이내 (FR-42)
            var tl = kit.Plan.Timeline(2);
            ClipInfo fall = null;
            for (int k = 0; k < tl.Count; k++)
                if (kit.Plan.Library.Get(tl.Get(k).Clip).Group == "fall") fall = kit.Plan.Library.Get(tl.Get(k).Clip);
            Assert.IsNotNull(fall, "피해자가 넘어진다");
            var push = m.Data.Pos(2, foul.Time) - m.Data.Pos(3, foul.Time);
            float pushDeg = (float)(Math.Atan2(push.X, push.Z) * 180.0 / Math.PI);
            float fallDeg = m.Data.YawAt(2, foul.Time) * Mathf.Rad2Deg + fall.HitDir;
            Assert.LessOrEqual(Math.Abs(Mathf.DeltaAngle(pushDeg, fallDeg)), std.Max("fallAngle"));
        }

        [Test]
        public void OutcomeOrder_HeaderDuel_WinnerTouches_LoserMisses()
        {
            var std = MotionStandardsTests.Load();
            var r = MotionRun.Measure("header");
            Assert.AreEqual(2, r.Contacts.Count);
            ref readonly var _ = ref r.Contacts.ToArray()[0];
            foreach (var c in r.Contacts)
            {
                if (c.Entity == 0) Assert.LessOrEqual(c.Distance, std.Max("contactDistHead"), "승자");
                else
                {
                    Assert.IsTrue(c.Missed);
                    Assert.GreaterOrEqual(c.Distance, std.Min("loserBallGap"), "패자 머리는 공에 닿지 않는다");
                    Assert.Greater(c.TimeError, 0f, "패자 정점이 늦다");
                }
            }
        }

        [Test]
        public void OutcomeOrder_Keeper_SaveTouches_GoalMisses()
        {
            var std = MotionStandardsTests.Load();
            var r = MotionRun.Measure("keeper");
            Assert.AreEqual(2, r.Contacts.Count);
            Assert.LessOrEqual(r.Contacts[0].Distance, std.Max("contactDistHand"), "선방");
            Assert.IsTrue(r.Contacts[1].Missed);
            Assert.GreaterOrEqual(r.Contacts[1].Distance, std.Min("loserBallGap"), "실점: 손이 안 닿는다");
        }

        [Test]
        public void SameFrame_LoserFootAwayFromBall()
        {
            var std = MotionStandardsTests.Load();
            var r = MotionRun.Measure("sameFrame");
            foreach (var c in r.Contacts)
                if (c.Missed) Assert.GreaterOrEqual(c.Distance, std.Min("loserBallGap"), "FR-83");
        }

        /// <summary>FR-45: 실제 리그에서 비켜 가는 동안 어깨가 돈다.</summary>
        [Test]
        public void Evade_RealRig_ShouldersTurn()
        {
            var m = MotionSceneKit.Scene("evade");
            using var kit = new AnimRigKit(m.Data, m.Actors);
            float best = 0f;
            for (float t = m.Data.StartTime + 1f; t < m.Data.EndTime - 1f; t += 0.05f)
            {
                kit.System.ApplyAt(t);
                var rig = kit.Rigs[0];
                var hip = rig.Bone("RightUpperLeg").position - rig.Bone("LeftUpperLeg").position;
                var sh = rig.Bone("RightUpperArm").position - rig.Bone("LeftUpperArm").position;
                float a = Vector3.SignedAngle(new Vector3(hip.x, 0, hip.z), new Vector3(sh.x, 0, sh.z), Vector3.up);
                var s = new PoseSample();
                kit.Plan.Evaluate(0, t, ref s);
                if (Mathf.Abs(s.Twist) > 0.05f) best = Mathf.Max(best, Mathf.Abs(a));
            }
            Assert.Greater(best, 5f, "회피 구간의 어깨-골반 각");
        }

        [Test]
        public void GoalPosts_MatchPitchSpec()
        {
            var p = AnimPlan.GoalPosts;
            float half = RoadToPro.Core.Pitch.PitchSpec.Default.LengthM * 0.5f;
            Assert.AreEqual(half, Math.Abs(p[0]), 1e-4f);
            Assert.AreEqual(half, Math.Abs(p[4]), 1e-4f);
            Assert.AreEqual(RoadToPro.MatchEngine.Ball.BallParams.GoalWidth * 0.5f, Math.Abs(p[1]), 1e-3f, "004 골대 폭");
        }

        [Test]
        public void PostGraze_RootPushedAroundPost_InsideGoalAfter()
        {
            var r = MotionRun.Measure("postGraze");
            Assert.Greater(r.Metrics["postPenetration"].Count, 0);
            Assert.LessOrEqual(r.Max("postPenetration"), MotionStandardsTests.Load().Max("postPenetration"));
        }

        [Test]
        public void Substitution_NearPassing_NoOverlapAtSwap()
        {
            var r = MotionRun.Measure("substitution");
            Assert.Greater(r.Metrics["torsoOverlap"].Count, 0, "교체 지점 옆을 지나간다");
            Assert.LessOrEqual(r.Max("torsoOverlap"), MotionStandardsTests.Load().Max("torsoOverlap"));
        }

        [Test]
        public void Substitution_PresentAndAbsent()
        {
            var m = MotionSceneKit.Scene("substitution");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            var s = new PoseSample();
            p.Evaluate(5, m.Data.StartTime + 0.5f, ref s);
            Assert.IsFalse(s.Present);
            p.Evaluate(5, m.Data.StartTime + 2f, ref s);
            Assert.IsTrue(s.Present);
            p.Evaluate(6, m.Data.StartTime + 2f, ref s);
            Assert.IsFalse(s.Present);
        }

        [Test]
        public void InNet_StaysInsideGoal()
        {
            var m = MotionSceneKit.Scene("inNet");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            var s = new PoseSample();
            bool inside = false;
            for (float t = m.Data.StartTime; t <= m.Data.EndTime; t += 0.05f)
            {
                p.Evaluate(0, t, ref s);
                if (s.Root.X < -52.5f) inside = true;
                if (inside)
                {
                    Assert.Less(s.Root.X, -52.5f, $"t={t}: 한번 들어가면 안쪽에 머문다");
                    Assert.Less(Math.Abs(s.Root.Z), 3.66f, $"t={t}: 기둥 사이");
                }
            }
            Assert.IsTrue(inside);
        }
    }
}
