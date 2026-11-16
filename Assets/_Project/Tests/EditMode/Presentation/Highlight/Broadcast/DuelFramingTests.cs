using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    /// <summary>specs/081 AC-06 (FR-31): 테이크온·태클이 대표 사건인 장면에서 메인 카메라가 두 선수를 담고 기본보다 당겨 잡는다.</summary>
    public class DuelFramingTests
    {
        const int A = 9, B = 14, Frames = 200;
        const float EventTime = 4f; // 프레임 120 (30 fps)

        static CameraRigData Rig() => CameraRigData.Default();

        /// <summary>중앙 근처(공 x = 5) 결투: 공격수 A가 공을 갖고, 수비수 B가 2.5 m 앞. farFrom 이후엔 B가 멀어진다.</summary>
        static HighlightData Scene(int farFrom = int.MaxValue, float far = 20f)
        {
            var d = BroadcastTestKit.Scene(0f, Frames, _ => new Vec3(5f, 0f, 0f), owner: A);
            for (int f = 0; f < Frames; f++) d.Set(f, B, 5f + (f >= farFrom ? far : 2.5f), 0.5f, 0f);
            return d;
        }

        static SceneBrief Brief() => new SceneBrief { EventTime = EventTime, DuelA = A, DuelB = B };

        static CameraPose Run(HighlightData d, SceneBrief b, CameraTuning t, int upTo, bool duel)
        {
            var solver = new FramingSolver(Rig(), t);
            var buf = new FramingTarget[FramingTargets.Capacity];
            CameraPose p = default;
            for (int f = 0; f <= upTo; f++)
            {
                var time = f / (double)HighlightData.FrameRate;
                if (duel && FramingTargets.Duel(d, f, time, b, t)) p = solver.Step(buf, FramingTargets.CollectDuel(d, f, b.DuelA, b.DuelB, buf), t.DuelFovScale);
                else p = solver.Step(buf, FramingTargets.Collect(d, f, buf));
            }
            return p;
        }

        static void AssertInView(CameraPose p, CameraTuning t, Vec3 pos, string what)
        {
            var v = CameraMath.Viewport(p, BroadcastTestKit.Aspect, t.FovMinDeg, t.FovMaxDeg, pos);
            Assert.That(v.X, Is.InRange(0f, 1f), what);
            Assert.That(v.Y, Is.InRange(0f, 1f), what);
            Assert.Greater(v.Z, 0f, what + " (카메라 앞)");
        }

        [Test]
        public void DuelFraming_TakeOn_BothInView_FovNarrower()
        {
            var t = new CameraTuning();
            var d = Scene();
            var f = (int)(EventTime * HighlightData.FrameRate);
            var basic = Run(d, Brief(), t, f, duel: false);
            var duel = Run(d, Brief(), t, f, duel: true);
            var fb = basic.FovDeg(BroadcastTestKit.Aspect, t.FovMinDeg, t.FovMaxDeg);
            var fd = duel.FovDeg(BroadcastTestKit.Aspect, t.FovMinDeg, t.FovMaxDeg);
            TestContext.WriteLine($"화각 기본 {fb:F1}° 결투 {fd:F1}°");
            Assert.Less(fd, fb * 0.95f, "결투 줌이 기본 프레이밍보다 당겨져 있다");
            AssertInView(duel, t, FramingTargets.PosAt(d, f, A), "공격수");
            AssertInView(duel, t, FramingTargets.PosAt(d, f, B), "수비수");
        }

        [Test]
        public void DuelFraming_Tackle_BothInView()
        {
            // 태클: 수비수가 사건 주체, 볼 캐리어가 상대 — 같은 규칙 (DuelA·B 순서 무관)
            var t = new CameraTuning();
            var d = Scene();
            var b = new SceneBrief { EventTime = EventTime, DuelA = B, DuelB = A };
            var f = (int)(EventTime * HighlightData.FrameRate);
            Assert.IsTrue(FramingTargets.Duel(d, f, EventTime, b, t));
            var p = Run(d, b, t, f, duel: true);
            AssertInView(p, t, FramingTargets.PosAt(d, f, A), "볼 캐리어");
            AssertInView(p, t, FramingTargets.PosAt(d, f, B), "태클한 수비수");
        }

        [Test]
        public void DuelFraming_FarApart_ReleasesToDefault()
        {
            var t = new CameraTuning();
            var d = Scene(farFrom: 110);
            var f = (int)(EventTime * HighlightData.FrameRate);
            Assert.IsFalse(FramingTargets.Duel(d, f, EventTime, Brief(), t), "두 선수가 해제 거리보다 멀면 결투 줌 아님");
            Assert.IsFalse(FramingTargets.Duel(Scene(), 0, 0d, Brief(), t), "사건 lead 이전엔 결투 줌 아님");
            Assert.IsFalse(FramingTargets.Duel(Scene(), f, EventTime, new SceneBrief { EventTime = EventTime }, t), "결투 장면이 아니면 아님");
        }

        [Test]
        public void DuelFraming_BallStaysInView()
        {
            var t = new CameraTuning();
            var d = Scene();
            var solver = new FramingSolver(Rig(), t);
            var buf = new FramingTarget[FramingTargets.Capacity];
            var b = Brief();
            for (int f = 0; f < Frames; f++)
            {
                var time = f / (double)HighlightData.FrameRate;
                var p = FramingTargets.Duel(d, f, time, b, t)
                    ? solver.Step(buf, FramingTargets.CollectDuel(d, f, A, B, buf), t.DuelFovScale)
                    : solver.Step(buf, FramingTargets.Collect(d, f, buf));
                var v = CameraMath.Viewport(p, BroadcastTestKit.Aspect, t.FovMinDeg, t.FovMaxDeg, d.Ball[f]);
                Assert.That(v.X, Is.InRange(0.049f, 0.951f), $"프레임 {f}");
                Assert.That(v.Y, Is.InRange(0.049f, 0.951f), $"프레임 {f}");
            }
        }

        [Test]
        public void DuelFraming_NonRepresentativeDuelInScene_Zooms()
        {
            // 대표 사건이 결투가 아니어도 장면 안 테이크온·태클 순간이면 결투 줌 (2026-11-16 FR-31 개정, AC-07)
            var t = new CameraTuning();
            var d = Scene();
            var f = (int)(EventTime * HighlightData.FrameRate);
            var b = new SceneBrief { EventTime = 0d, Duels = new[] { new DuelMoment(EventTime, A, B) } };
            Assert.IsTrue(FramingTargets.Duel(d, f, EventTime, b, t, out var a, out var bb));
            Assert.AreEqual(A, a);
            Assert.AreEqual(B, bb);
            Assert.IsFalse(FramingTargets.Duel(d, 0, 0d, b, t), "결투 순간 밖");
        }
    }
}
