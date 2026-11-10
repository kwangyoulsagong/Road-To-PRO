using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    /// <summary>트랙 생성·샘플·흔들림·결정론 (specs/042 AC-09·10·15, FR-05·14·41).</summary>
    public class CameraTrackTests
    {
        const float Start = 90f;

        static HighlightData Data(int frames = 420) =>
            BroadcastTestKit.Scene(Start, frames, f => new Vec3(10f + f * 0.08f, f < 240 ? 0.3f : 0.5f, 4f));

        static SceneBrief GoalBrief() => new SceneBrief
        {
            SceneId = 7,
            Role = SceneRole.GoalMain,
            EventTime = Start + 8d,
            EventPos = new Vec3(30f, 0f, 4f),
            Scorer = 9,
            Shakes = new[] { new ShakeSource(Start + 8d, 0.06f) },
        };

        static CameraTrack Build(SceneBrief brief, CameraSettings settings = null, HighlightData d = null, CameraPose? from = null) =>
            new CameraTrackBuilder(CameraRigData.Default(), new CameraTuning(), settings ?? new CameraSettings())
                .Build(d ?? Data(), brief, from, false);

        [Test, Category("Determinism")]
        public void Track_TwoBuilds_SameHash()
        {
            Assert.AreEqual(BroadcastTestKit.Hash(Build(GoalBrief())), BroadcastTestKit.Hash(Build(GoalBrief())));
        }

        [Test]
        public void Track_DifferentScene_DifferentShake()
        {
            var other = GoalBrief();
            other.SceneId = 8;
            Assert.AreNotEqual(BroadcastTestKit.Hash(Build(GoalBrief())), BroadcastTestKit.Hash(Build(other)));
        }

        [Test, Category("Determinism")]
        public void Track_30vs60fps_SameAtSameTime()
        {
            var track = Build(GoalBrief());
            var samples = new Dictionary<long, CameraSample>();
            void Run(double dt)
            {
                var rig = new SequencerRig(0d);
                rig.StartLoaded(new[] { new HighlightScene(7, Start, Start + 13d) });
                for (int i = 0; i < 2000 && rig.Seq.State == SequencerState.Playing; i++)
                {
                    rig.Seq.Tick(dt);
                    double t = rig.Seq.Clock.Position;
                    long key = (long)System.Math.Round(t * 1000d);
                    var s = track.Sample(t);
                    if (samples.TryGetValue(key, out var seen))
                    {
                        Assert.AreEqual(seen.Pose.Position.X, s.Pose.Position.X, 1e-3f);
                        Assert.AreEqual(seen.Pose.Forward.X, s.Pose.Forward.X, 1e-3f);
                        Assert.AreEqual(seen.Pose.TanV, s.Pose.TanV, 1e-3f);
                    }
                    else samples[key] = s;
                }
            }
            Run(1d / 30d);
            int count = samples.Count;
            Run(1d / 60d);
            Assert.Greater(count, 100, "30 fps 표본 수");
        }

        [Test]
        public void Shake_Disabled_AllOffsetsZero()
        {
            var track = Build(GoalBrief(), new CameraSettings { ShakeOff = true });
            foreach (var p in track.Poses) Assert.AreEqual(Vec3.Zero, p.Shake);
        }

        [Test]
        public void Shake_Enabled_CloseupShakes_MainDoesNot()
        {
            var track = Build(GoalBrief());
            bool closeupShook = false;
            for (int f = 0; f < track.Frames; f++)
            {
                if (track.Shots[f] == ShotKind.Main) Assert.AreEqual(Vec3.Zero, track.Poses[f].Shake, $"프레임 {f}");
                if (track.Shots[f] == ShotKind.Closeup && track.Poses[f].Shake != Vec3.Zero) closeupShook = true;
            }
            Assert.IsTrue(closeupShook);
        }

        [Test]
        public void Sample_AcrossCut_DoesNotInterpolate()
        {
            var track = Build(GoalBrief());
            int cut = System.Array.FindIndex(track.CutAt, c => c);
            Assert.Greater(cut, 0);
            double between = track.StartTime + (cut - 0.5d) / HighlightData.FrameRate;
            var s = track.Sample(between);
            Assert.AreEqual(track.Poses[cut - 1].Position, s.Pose.Position);
            Assert.AreEqual(ShotKind.Main, s.Shot);
        }

        [Test]
        public void Sample_BeyondEnd_ClampsToLastFrame()
        {
            var track = Build(GoalBrief());
            var s = track.Sample(track.StartTime + 1000d);
            Assert.AreEqual(track.Poses[track.Frames - 1].Position, s.Pose.Position);
        }

        [Test]
        public void Sample_NoAllocation()
        {
            var track = Build(GoalBrief());
            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++) track.Sample(Start + i * 0.01d);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void ReplayEntry_BlendsFromPreviousPose_OnlyFirstAngle()
        {
            var d = Data(90);
            var from = new CameraPose(new Vec3(0f, 26f, -79f), new Vec3(0f, -0.3f, 0.95f).Normalized(), 0.2f, 0.2f);
            var first = new SceneBrief { SceneId = 1, Role = SceneRole.ReplayAngle, AngleIndex = 0, AngleCount = 3, EventPos = new Vec3(30f, 0f, 4f), Shooter = 9 };
            var second = new SceneBrief { SceneId = 1, Role = SceneRole.ReplayAngle, AngleIndex = 1, AngleCount = 3, EventPos = new Vec3(30f, 0f, 4f), Shooter = 9 };

            var plainFirst = Build(first, d: d);
            var blended = Build(first, d: d, from: from);
            Assert.AreNotEqual(plainFirst.Poses[0].Position, blended.Poses[0].Position, "전환 시작");
            Assert.AreEqual(plainFirst.Poses[10].Position, blended.Poses[10].Position, "전환 끝 뒤");

            Assert.AreEqual(Build(second, d: d).Poses[0].Position, Build(second, d: d, from: from).Poses[0].Position);
        }

        [Test]
        public void Portrait_Live_UsesTopView_GoalShotsStay()
        {
            var track = new CameraTrackBuilder(CameraRigData.Default(), new CameraTuning(), new CameraSettings()).Build(Data(), GoalBrief(), null, true);
            Assert.AreEqual(ShotKind.TopView, track.Shots[0]);
            CollectionAssert.Contains(track.Shots, ShotKind.Closeup);
        }

        [Test]
        public void MyPlayerMode_NotInvolved_PlaysBroadcast()
        {
            var b = GoalBrief();
            b.MyPlayer = 3;
            b.MyPlayerInvolved = false;
            Assert.AreEqual(ShotKind.Main, Build(b, new CameraSettings { Mode = CameraMode.MyPlayer }).Shots[0]);
            b.MyPlayerInvolved = true;
            Assert.AreEqual(ShotKind.MyPlayer, Build(b, new CameraSettings { Mode = CameraMode.MyPlayer }).Shots[0]);
        }

        static AnimEvent Shot(float speed) => new AnimEvent(Start + 1f, 1, RoadToPro.MatchEngine.Match.Events.SimEventKind.Shot, 9, -1,
            RoadToPro.MatchEngine.Match.Events.BodyPart.RightFoot, default, Vec3.Zero, new Vec3(speed, 0f, 0f));

        [Test]
        public void FromEvents_ShotAtThreshold_Shakes_Slower_DoesNot()
        {
            Assert.AreEqual(1, CameraShake.FromEvents(new List<AnimEvent> { Shot(30f) }, 30f, null).Length);
            Assert.AreEqual(0, CameraShake.FromEvents(new List<AnimEvent> { Shot(29.9f) }, 30f, null).Length);
        }

        [Test]
        public void LongPath_SetsSlowMoMark()
        {
            var b = new SceneBrief { Role = SceneRole.ReplayAngle, AngleCount = 3, LongPath = true, EventTime = Start + 1d, EventPos = new Vec3(30f, 0f, 4f) };
            Assert.AreEqual(Start + 1d, Build(b, d: Data(90)).SlowMoMark, 1e-6d);
        }
    }
}
