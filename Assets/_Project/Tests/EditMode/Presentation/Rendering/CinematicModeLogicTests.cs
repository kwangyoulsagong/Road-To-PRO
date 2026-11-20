using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Rendering.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>specs/061 FR-80·83·87·경계: 연출 장면 고품질 모드 판단 (AC-01 ②, AC-10 일부).</summary>
    public class CinematicModeLogicTests
    {
        const double Fps = HighlightData.FrameRate;
        static readonly Vec3 Ball = new Vec3(0f, 0.11f, 0f);

        /// <summary>구간별 샷으로 트랙을 만든다. 카메라는 공에서 <paramref name="dist"/> m, 공을 본다.</summary>
        static CameraTrack Track(double start, params (double seconds, ShotKind shot, float dist, bool cut)[] parts)
        {
            int frames = 0;
            foreach (var p in parts) frames += (int)System.Math.Round(p.seconds * Fps);
            var t = new CameraTrack(start, frames, 8f, 30f);
            int f = 0;
            foreach (var p in parts)
            {
                int n = (int)System.Math.Round(p.seconds * Fps);
                for (int i = 0; i < n; i++, f++)
                {
                    var pos = new Vec3(0f, 1.5f, -p.dist);
                    t.Poses[f] = new CameraPose(pos, (Ball - pos).Normalized(), 0.2f, 0.36f);
                    t.Shots[f] = p.shot;
                    t.CutAt[f] = i == 0 && p.cut && f > 0;
                }
            }
            return t;
        }

        static CinematicTimeline Build(CameraTrack t) => CinematicModeLogic.Build(t, _ => Ball, null);

        [Test]
        public void ShotKinds_ModeDecision()
        {
            Assert.IsTrue(CinematicModeLogic.IsCinematicShot(ShotKind.Closeup, 40f));
            Assert.IsTrue(CinematicModeLogic.IsCinematicShot(ShotKind.Celebration, 40f));
            foreach (var r in new[] { ShotKind.ReplayShooter, ShotKind.ReplayReverse, ShotKind.ReplayHigh })
            {
                Assert.IsTrue(CinematicModeLogic.IsCinematicShot(r, 15f), r + " 20 m 안");
                Assert.IsFalse(CinematicModeLogic.IsCinematicShot(r, 25f), r + " 20 m 밖");
            }
            foreach (var n in new[] { ShotKind.Main, ShotKind.TopView, ShotKind.MyPlayer, ShotKind.ReactionWide, ShotKind.KickoffWide, ShotKind.FallbackHigh })
                Assert.IsFalse(CinematicModeLogic.IsCinematicShot(n, 5f), n.ToString());

            var tl = Build(Track(10d, (2d, ShotKind.Main, 40f, false), (2d, ShotKind.Closeup, 6f, true), (2d, ShotKind.Main, 40f, true)));
            Assert.AreEqual(0f, tl.Weight(11d), 1e-6f, "메인 중계 = 일반");
            Assert.AreEqual(1f, tl.Weight(13d), 1e-6f, "클로즈업 = 고품질");
            Assert.AreEqual(0f, tl.Weight(15d), 1e-6f, "메인 복귀 = 일반");
        }

        [Test]
        public void ShortShot_Under0_5s_Normal()
        {
            var tl = Build(Track(0d, (1d, ShotKind.Main, 40f, false), (0.4d, ShotKind.Closeup, 6f, true), (1d, ShotKind.Main, 40f, true)));
            Assert.AreEqual(0, tl.Spans.Count, "0.5 s 미만 고품질 샷은 모드에 안 들어간다");
            Assert.AreEqual(0f, tl.Weight(1.2d), 1e-6f);
        }

        [Test]
        public void ConsecutiveShots_StayOn()
        {
            var tl = Build(Track(0d, (1d, ShotKind.Closeup, 6f, false), (1d, ShotKind.Celebration, 5f, true), (1d, ShotKind.ReplayShooter, 10f, true)));
            Assert.AreEqual(1, tl.Spans.Count, "연속 고품질 샷은 구간 하나");
            for (double s = 0d; s < 2.99d; s += 1d / Fps)
                Assert.AreEqual(1f, tl.Weight(s), 1e-6f, $"t={s:0.000}에서 모드가 꺼졌다");
        }

        [Test]
        public void ReplayFarCamera_Normal()
        {
            var tl = Build(Track(0d, (2d, ShotKind.ReplayHigh, 30f, false)));
            Assert.AreEqual(0, tl.Spans.Count);
        }

        [Test]
        public void NoCutTransition_Blends0_3s()
        {
            // 같은 리플레이 샷 안에서 카메라가 20 m 안으로 들어온다 (컷 없음)
            var t = Track(0d, (1d, ShotKind.ReplayShooter, 30f, false), (2d, ShotKind.ReplayShooter, 10f, false));
            var tl = Build(t);
            Assert.AreEqual(1, tl.Spans.Count);
            Assert.IsFalse(tl.Spans[0].CutIn);
            Assert.AreEqual(0f, tl.Weight(1d), 0.05f, "진입 순간 0");
            Assert.AreEqual(0.5f, tl.Weight(1.15d), 0.1f, "0.15 s에 절반");
            Assert.AreEqual(1f, tl.Weight(1.3d), 1e-3f, "0.3 s에 완전히");
            float prev = -1f;
            for (double s = 1d; s <= 1.3d; s += 0.01d)
            {
                float w = tl.Weight(s);
                Assert.GreaterOrEqual(w, prev, "블렌드는 단조 증가");
                prev = w;
            }
        }

        [Test]
        public void WipeEntry_FinishesInsideWipe()
        {
            // 리플레이 장면 첫 프레임(장면 시작 = 와이프 진입, 042 FR-14)부터 고품질 → 0.3 s 와이프 안에서 전환 끝
            var tl = Build(Track(97.5d, (2d, ShotKind.ReplayShooter, 10f, false)));
            Assert.AreEqual(1, tl.Spans.Count);
            Assert.IsTrue(tl.Spans[0].CutIn, "장면 시작은 컷 진입");
            Assert.AreEqual(1f, tl.Weight(97.5d), 1e-6f);
            Assert.AreEqual(1f, tl.Weight(97.5d + 0.29d), 1e-6f);
        }

        [Test]
        public void KeyMomentPause_Frozen()
        {
            // 키 모먼트 정지 = 재생 시각이 멈춤 → 같은 시각이면 같은 가중치·초점 (상태 없음)
            var tl = Build(Track(0d, (1d, ShotKind.ReplayShooter, 30f, false), (2d, ShotKind.ReplayShooter, 10f, false)));
            float w = tl.Weight(1.1d);
            var f = tl.Focus(1.1d);
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(w, tl.Weight(1.1d));
                Assert.AreEqual(f.X, tl.Focus(1.1d).X);
            }
        }

        [Test]
        public void FocusOffscreen_BlendsToBall()
        {
            var subject = new Vec3(0f, 1f, 0f);
            var off = new Vec3(40f, 1f, 0f); // 카메라 화각 밖
            var t = Track(0d, (2d, ShotKind.Closeup, 6f, false));
            var tl = CinematicModeLogic.Build(t, _ => Ball, s => s < 1d ? subject : off);
            Assert.AreEqual(subject.Y, tl.Focus(0.5d).Y, 1e-4f, "화면 안이면 대상");
            var mid = tl.Focus(1.15d);
            Assert.Greater(mid.Y, Ball.Y + 0.05f, "0.15 s는 아직 섞는 중");
            Assert.Less(mid.Y, subject.Y - 0.05f);
            Assert.AreEqual(Ball.Y, tl.Focus(1.4d).Y, 1e-3f, "0.3 s 뒤 공");
        }

        [Test]
        public void SameTrack_SameSpans()
        {
            CameraTrack Make() => Track(5d, (1d, ShotKind.Main, 40f, false), (1.5d, ShotKind.Closeup, 6f, true),
                (1d, ShotKind.ReplayShooter, 30f, true), (1.5d, ShotKind.ReplayShooter, 10f, false));
            var a = Build(Make());
            var b = Build(Make());
            Assert.AreEqual(a.Hash(), b.Hash());
            Assert.AreEqual(a.Spans.Count, b.Spans.Count);
        }

        [Test]
        public void Weight_IndependentOfFrameRate()
        {
            var tl = Build(Track(0d, (1d, ShotKind.ReplayShooter, 30f, false), (2d, ShotKind.ReplayShooter, 10f, false)));
            // 30 fps·60 fps·0.5x(120 fps 상당) 렌더가 같은 시뮬 시각을 지나면 같은 값
            foreach (double step in new[] { 1d / 30d, 1d / 60d, 1d / 120d })
                for (double s = 0d; s < 3d; s += step)
                {
                    double q = System.Math.Round(s * 30d) / 30d;
                    Assert.AreEqual(Build(Track(0d, (1d, ShotKind.ReplayShooter, 30f, false), (2d, ShotKind.ReplayShooter, 10f, false))).Weight(q), tl.Weight(q));
                }
        }

        [Test]
        public void NearPlayers_TopTierSignal_Max3()
        {
            var cam = new Vec3(0f, 1.5f, 0f);
            var players = new[] { new Vec3(30f, 0f, 0f), new Vec3(2f, 0f, 0f), new Vec3(5f, 0f, 0f), new Vec3(3f, 0f, 0f), new Vec3(1f, 0f, 0f) };
            var result = new int[3];
            int n = CinematicModeLogic.TopTier(cam, players, 3, result);
            Assert.AreEqual(3, n);
            CollectionAssert.AreEqual(new[] { 4, 1, 3 }, result, "가까운 순 3명");
            Assert.AreEqual(2, CinematicModeLogic.TopTier(cam, players, 2, result));
        }
    }
}
