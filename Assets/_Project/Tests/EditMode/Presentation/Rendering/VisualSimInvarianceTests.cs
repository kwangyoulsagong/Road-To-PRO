using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Rendering;
using RoadToPro.Presentation.Rendering.Logic;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>
    /// specs/061 FR-57·87, AC-10: 061 기능·고품질 모드는 표현 전용 — 재생 데이터(시뮬에서 온 위치·공·이벤트)와 카메라 경로를 바꾸지 않는다.
    /// 시뮬 자체는 Presentation을 참조하지 않으므로(asmdef 의존 방향) 시뮬 해시는 구조로 불변이고, 여기서는 재생 입력이 읽기만 되는지 본다.
    /// </summary>
    public class VisualSimInvarianceTests
    {
        static HighlightData Data()
        {
            var d = new HighlightData(10f, 90);
            for (int f = 0; f < 90; f++)
            {
                d.Ball[f] = new Vec3(f * 0.1f, 0.11f, 0f);
                for (int e = 0; e < HighlightData.Players; e++) d.Set(f, e, e + f * 0.05f, e * 0.5f, 0.1f * e);
            }
            return d;
        }

        static CameraTrack Track()
        {
            var t = new CameraTrack(10d, 90, 8f, 30f);
            for (int f = 0; f < 90; f++)
            {
                var pos = new Vec3(0f, 1.5f, f < 45 ? -40f : -6f);
                t.Poses[f] = new CameraPose(pos, (new Vec3(0f, 0.11f, 0f) - pos).Normalized(), 0.2f, 0.36f);
                t.Shots[f] = f < 45 ? ShotKind.Main : ShotKind.Closeup;
                t.CutAt[f] = f == 45;
            }
            return t;
        }

        static ulong HashData(HighlightData d)
        {
            var h = StableHasher.Create();
            foreach (var v in d.X) h.Add(v);
            foreach (var v in d.Z) h.Add(v);
            foreach (var v in d.Yaw) h.Add(v);
            foreach (var b in d.Ball) { h.Add(b.X); h.Add(b.Y); h.Add(b.Z); }
            h.Add(d.Events.Count);
            return h.Value;
        }

        static ulong HashTrack(CameraTrack t)
        {
            var h = StableHasher.Create();
            for (int f = 0; f < t.Frames; f++)
            {
                h.Add(t.Poses[f].Position.X); h.Add(t.Poses[f].Position.Z); h.Add(t.Poses[f].TanV);
                h.Add((int)t.Shots[f]); h.Add(t.CutAt[f]);
            }
            return h.Value;
        }

        [Test]
        public void FeaturesOnOff_SameSimHashEventsBallCamera()
        {
            var data = Data();
            var track = Track();
            ulong d0 = HashData(data), t0 = HashTrack(track);
            var cam = new GameObject("cam").AddComponent<Camera>();
            var c = new CinematicModeController(cam, PresetId.High, null);
            try
            {
                c.OnSceneBuilt(1, track, data, 3);
                for (double s = 10d; s < 13d; s += 1d / 60d) c.Apply(1, s, data);
            }
            finally
            {
                c.Dispose();
                Object.DestroyImmediate(cam.gameObject);
            }
            Assert.AreEqual(d0, HashData(data), "재생 데이터(시뮬 위치·공·이벤트)가 바뀌지 않음");
            Assert.AreEqual(t0, HashTrack(track), "카메라 경로가 바뀌지 않음 (042 결정론)");
        }

        [Test]
        public void CinematicOnOff_SamePlaybackLength()
        {
            // 모드 판단은 트랙 길이·장면 구간을 줄이거나 늘리지 않는다 — 구간은 트랙 안에만 있다
            var track = Track();
            var tl = CinematicModeLogic.Build(track, _ => new Vec3(0f, 0.11f, 0f), null);
            double end = track.StartTime + track.Frames / (double)HighlightData.FrameRate;
            foreach (var s in tl.Spans)
            {
                Assert.GreaterOrEqual(s.Start, track.StartTime);
                Assert.LessOrEqual(s.End, end + 1e-9);
            }
        }
    }
}
