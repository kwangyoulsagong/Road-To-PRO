using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Tuning;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Stadium;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    /// <summary>경기장 입력·거치 위치 (specs/042 FR-01·06, AC-16).</summary>
    public class CameraRigDataTests
    {
        static readonly string[] Layouts =
        {
            "Data/tests/stadium-layout-min.json",
            "Assets/_Project/Presentation/Stadium/Generated/default/stadium.layout.json",
            "Data/tests/stadium-layout-max.json",
        };

        static readonly ShotKind[] Angles = { ShotKind.ReplayHigh, ShotKind.ReplayShooter, ShotKind.ReplayReverse };

        [Test]
        public void RigData_ThreeStadiums_MountAndNoGo([ValueSource(nameof(Layouts))] string path)
        {
            var rig = CameraRigData.From(StadiumLayout.Parse(File.ReadAllText(path)), null);
            var t = new CameraTuning();
            Assert.IsTrue(rig.MainMount.Contains(rig.MainPosition(t)), "메인 카메라는 거치 범위 안");

            for (float x = -50f; x <= 50f; x += 12.5f)
            for (float z = -32f; z <= 32f; z += 16f)
            foreach (var kind in Angles)
            {
                var place = ReplayAnglePlacer.Place(kind, new Vec3(x, 0f, z), rig);
                Assert.IsFalse(CameraOcclusion.InsideNoGo(rig, place.Position), $"{kind} ({x},{z}) 금지 영역");
            }
        }

        [Test]
        public void RigData_Missing_DefaultAndOneDiagnostic()
        {
            int logs = 0;
            var rig = CameraRigData.From(null, _ => logs++);
            Assert.AreEqual(1, logs);
            Assert.IsTrue(rig.MainMount.Contains(rig.MainPosition(new CameraTuning())));
        }

        [Test]
        public void MainPosition_DefaultStadium_TouchesFr01Range()
        {
            var layout = StadiumLayout.Parse(File.ReadAllText(Layouts[1]));
            var p = CameraRigData.From(layout, null).MainPosition(new CameraTuning());
            Assert.AreEqual(0f, p.X, 1e-4f);
            Assert.That(p.Y, Is.InRange(15f, 25f));
            Assert.That(-34f - p.Z, Is.InRange(30f, 45f));
        }

        [Test]
        public void MainPosition_RangeOutsideMount_PicksNearestMountEdge()
        {
            var rig = new CameraRigData { MainMount = new CameraBox(new Vec3(-6f, 7f, -49f), new Vec3(6f, 9f, -47f)) };
            var p = rig.MainPosition(new CameraTuning());
            Assert.AreEqual(9f, p.Y, 1e-4f);
            Assert.AreEqual(-49f, p.Z, 1e-4f);
        }

        [Test]
        public void Tuning_FileDefaults_MatchCodeDefaults()
        {
            var registry = TuningLoader.LoadDefinitionsFile("Data/tuning/camera.json");
            var t = CameraTuning.From(TuningBundle.Defaults(registry));
            var d = new CameraTuning();
            Assert.AreEqual(d.FovMinDeg, t.FovMinDeg);
            Assert.AreEqual(d.FovMaxDeg, t.FovMaxDeg);
            Assert.AreEqual(d.PanLimitDeg, t.PanLimitDeg);
            Assert.AreEqual(d.MinShotSeconds, t.MinShotSeconds, 1e-6d);
            Assert.AreEqual(d.HardShotSpeed, t.HardShotSpeed);
        }
    }
}
