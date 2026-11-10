using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    /// <summary>컷 규칙·앵글 배치·골 시퀀스·모드 (specs/042 AC-02~06·08).</summary>
    public class DirectionTests
    {
        static List<ShotPlan> Cuts(LiveCutRequest[] requests, DeadBallWindow[] windows, double sceneStart = 0d)
        {
            var list = new List<ShotPlan>();
            LiveCutRules.Plan(requests, windows, sceneStart, 2.5d, list);
            return list;
        }

        [Test]
        public void LiveCut_RejectsInPlay_AcceptsFirstDeadBall_RespectsMinShot()
        {
            var cuts = Cuts(new[]
            {
                new LiveCutRequest(5d, ShotKind.Closeup, 1),
                new LiveCutRequest(11d, ShotKind.Closeup, 1),
                new LiveCutRequest(12d, ShotKind.Closeup, 1),
            }, new[] { new DeadBallWindow(10d, 16d) });
            Assert.AreEqual(1, cuts.Count);
            Assert.AreEqual(11d, cuts[0].Start);
            Assert.AreEqual(16d, cuts[0].End);
        }

        [Test]
        public void LiveCut_WithinMinShotOfLastCut_Rejected()
        {
            var cuts = Cuts(new[]
            {
                new LiveCutRequest(11d, ShotKind.Closeup, 1),
                new LiveCutRequest(14.5d, ShotKind.Closeup, 1),
                new LiveCutRequest(16.2d, ShotKind.Closeup, 1),
            }, new[] { new DeadBallWindow(10d, 13.6d), new DeadBallWindow(14d, 20d) });
            Assert.AreEqual(2, cuts.Count);
            Assert.AreEqual(16.2d, cuts[1].Start);
        }

        [Test]
        public void LiveCut_WindowShorterThanMinShot_Rejected() =>
            Assert.AreEqual(0, Cuts(new[] { new LiveCutRequest(11d, ShotKind.Closeup, 1) }, new[] { new DeadBallWindow(10d, 12d) }).Count);

        [Test]
        public void LiveCut_NeverFarSide() =>
            Assert.AreEqual(0, Cuts(new[] { new LiveCutRequest(11d, ShotKind.Closeup, 1, farSide: true) }, new[] { new DeadBallWindow(10d, 20d) }).Count);

        [Test]
        public void LiveCut_SameTime_HigherPriorityWins()
        {
            var cuts = Cuts(new[]
            {
                new LiveCutRequest(11d, ShotKind.Closeup, 1, focus: 1),
                new LiveCutRequest(11d, ShotKind.Closeup, 5, focus: 2),
            }, new[] { new DeadBallWindow(10d, 20d) });
            Assert.AreEqual(1, cuts.Count);
            Assert.AreEqual(2, cuts[0].Focus);
        }

        [Test]
        public void Replay_FarSide_SetsReverseFlag()
        {
            Assert.IsTrue(ReplayAnglePlacer.IsReverse(ShotKind.ReplayReverse));
            var p = ReplayAnglePlacer.Place(ShotKind.ReplayReverse, new Vec3(40f, 0f, 3f), CameraRigData.Default());
            Assert.Greater(p.Position.Z, 0f, "반대편");
        }

        [TestCase(ShotKind.ReplayHigh)]
        [TestCase(ShotKind.ReplayShooter)]
        [TestCase(ShotKind.ReplayReverse)]
        public void ReplayAngles_MirroredGoal_Symmetric(ShotKind kind)
        {
            var rig = CameraRigData.Default();
            var ev = new Vec3(38f, 0f, 6f);
            var a = ReplayAnglePlacer.Place(kind, ev, rig).Position;
            var b = ReplayAnglePlacer.Place(kind, BroadcastTestKit.Mirror(ev), rig).Position;
            Assert.AreEqual(a.X, -b.X, 0.1f);
            Assert.AreEqual(a.Y, b.Y, 0.1f);
            Assert.AreEqual(a.Z, b.Z, 0.1f);
        }

        [Test]
        public void ReplayAngles_PostOccludes_MovesToNextCandidate()
        {
            var rig = CameraRigData.Default();
            var ev = new Vec3(40f, 0f, 0f);
            Assert.AreEqual(0, ReplayAnglePlacer.Place(ShotKind.ReplayHigh, ev, rig).Candidate);

            rig.Posts.Add(new CameraPost(47.75f, 0f, 0.5f, 8f));
            var moved = ReplayAnglePlacer.Place(ShotKind.ReplayHigh, ev, rig);
            Assert.AreEqual(1, moved.Candidate);
            Assert.IsFalse(CameraOcclusion.IsBlocked(rig, moved.Position, ev));
        }

        [Test]
        public void ReplayAngles_AllBlocked_FallsBackToHigh()
        {
            var rig = new CameraRigData();
            rig.NoGo.Add(new CameraBox(new Vec3(46f, 0f, -40f), new Vec3(60f, 20f, 40f)));
            var p = ReplayAnglePlacer.Place(ShotKind.ReplayHigh, new Vec3(52f, 0f, -33f), rig);
            Assert.IsTrue(p.Fallback);
        }

        static List<ShotPlan> Plan(SceneBrief b, double start, double end)
        {
            var list = new List<ShotPlan>();
            GoalSequenceDirector.Plan(b, start, end, ShotKind.Main, new CameraTuning(), list);
            return list;
        }

        [Test]
        public void GoalSequence_ShotOrder()
        {
            var kinds = new List<ShotKind>();
            const double goal = 100d;
            void Add(SceneBrief b, double s, double e)
            {
                foreach (var p in Plan(b, s, e)) kinds.Add(p.Kind);
            }

            Add(new SceneBrief { Role = SceneRole.GoalMain, EventTime = goal, Scorer = 9 }, goal - 8d, goal + 6d);
            for (int i = 0; i < 3; i++)
                Add(new SceneBrief { Role = SceneRole.ReplayAngle, AngleIndex = i, AngleCount = 3 }, goal - 2d, goal + 1d);
            Add(new SceneBrief { Role = SceneRole.GoalTail, Scorer = 9 }, goal + 6d, goal + 11d);

            CollectionAssert.AreEqual(new[]
            {
                ShotKind.Main, ShotKind.Closeup, ShotKind.Celebration, ShotKind.ReplayHigh, ShotKind.ReplayShooter,
                ShotKind.ReplayReverse, ShotKind.ReactionWide, ShotKind.KickoffWide,
            }, kinds);
        }

        [Test]
        public void GoalSequence_Durations()
        {
            var shots = Plan(new SceneBrief { Role = SceneRole.GoalMain, EventTime = 100d, Scorer = 9 }, 92d, 106d);
            Assert.AreEqual(2.5d, shots[1].End - shots[1].Start, 1e-6d);
            Assert.AreEqual(3.5d, shots[2].End - shots[2].Start, 1e-6d);
            var tail = Plan(new SceneBrief { Role = SceneRole.GoalTail, Scorer = 9 }, 106d, 111d);
            Assert.AreEqual(2.5d, tail[0].End - tail[0].Start, 1e-6d);
            Assert.AreEqual(2.5d, tail[1].End - tail[1].Start, 1e-6d);
        }

        [Test]
        public void GoalSequence_ShortTail_KeepsOrderWithoutException()
        {
            var shots = Plan(new SceneBrief { Role = SceneRole.GoalMain, EventTime = 100d, Scorer = 9 }, 92d, 101d);
            Assert.AreEqual(2, shots.Count);
            Assert.AreEqual(ShotKind.Closeup, shots[1].Kind);
            Assert.AreEqual(101d, shots[1].End, 1e-6d);
        }

        [Test]
        public void GoalSequence_SceneEndsAtGoal_NoGoalShots()
        {
            var shots = Plan(new SceneBrief { Role = SceneRole.GoalMain, EventTime = 100d, Scorer = 9 }, 92d, 100d);
            Assert.AreEqual(1, shots.Count);
            Assert.AreEqual(ShotKind.Main, shots[0].Kind);
        }

        [Test]
        public void ReplayKind_TwoAngles_NoReverse()
        {
            Assert.AreEqual(ShotKind.ReplayHigh, GoalSequenceDirector.ReplayKind(new SceneBrief { AngleIndex = 0, AngleCount = 2 }));
            Assert.AreEqual(ShotKind.ReplayShooter, GoalSequenceDirector.ReplayKind(new SceneBrief { AngleIndex = 1, AngleCount = 2 }));
        }

        [Test]
        public void ReplayKind_LongPath_FirstAngleHigh() =>
            Assert.AreEqual(ShotKind.FallbackHigh, GoalSequenceDirector.ReplayKind(new SceneBrief { AngleIndex = 0, AngleCount = 3, LongPath = true }));

        [Test]
        public void MyPlayerMode_CentralThird_BallVisible()
        {
            var cam = new MyPlayerCamera();
            for (int f = 0; f < 300; f++)
            {
                var player = new Vec3(-30f + f * 0.25f, 0f, 10f);
                var ball = new Vec3(-30f + f * 0.1f, 0f, 10f - 30f * f / 300f);
                var pose = cam.Step(player, ball);
                var p = CameraMath.Viewport(pose, BroadcastTestKit.Aspect, 20f, 85f, player);
                var b = CameraMath.Viewport(pose, BroadcastTestKit.Aspect, 20f, 85f, ball);
                Assert.That(p.X, Is.InRange(1f / 3f, 2f / 3f), $"선수 X, 프레임 {f}");
                Assert.That(p.Y, Is.InRange(1f / 3f, 2f / 3f), $"선수 Y, 프레임 {f}");
                Assert.That(b.X, Is.InRange(0f, 1f), $"공 X, 프레임 {f}");
                Assert.That(b.Y, Is.InRange(0f, 1f), $"공 Y, 프레임 {f}");
            }
        }

        [Test]
        public void TopView_BallAnywhere_BallAndHalfPitchInFrame()
        {
            var cam = new TopViewCamera(CameraRigData.Default());
            CameraPose pose = default;
            var ball = new Vec3(50f, 0f, 30f);
            for (int f = 0; f < 120; f++) pose = cam.Step(ball);
            var v = CameraMath.Viewport(pose, BroadcastTestKit.Aspect, 20f, 90f, ball);
            Assert.That(v.X, Is.InRange(0f, 1f));
            Assert.That(v.Y, Is.InRange(0f, 1f));
        }
    }
}
