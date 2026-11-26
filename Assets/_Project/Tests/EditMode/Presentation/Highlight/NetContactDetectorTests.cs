using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Tests.EditMode.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>
    /// specs/059 FR-24·34·51·57 · AC-05: 클립 공 궤적(30 Hz)에서 안쪽 그물 면 접촉을 모두 찾는다.
    /// 궤적은 실제 공 물리(<see cref="BallStepper"/>, 059 그물 면 포함)로 만들어 시뮬과 같은 모양으로 검증한다.
    /// 하이라이트 좌표: X = 길이(중앙 0, 골라인 ±L/2), Y = 높이, Z = 폭(중앙 0).
    /// </summary>
    public class NetContactDetectorTests
    {
        static BallParams P => BallTestKit.Params();
        static GoalBox Box(BallParams p) => new GoalBox(PitchSpec.Default, BallParams.GoalWidth, BallParams.GoalHeight, p.NetDepth, p.Radius, p.NetPocket);

        /// <summary>시뮬 좌표 공을 100 Hz로 굴려 30 Hz 하이라이트 프레임으로 (프레임 시각 f/30 s를 앞뒤 서브스텝 사이 선형 보간).</summary>
        internal static HighlightData Frames(BallState s, float seconds, float startTime = 0f)
        {
            var p = P;
            int frames = (int)(seconds * HighlightData.FrameRate) + 1;
            var d = new HighlightData(startTime, frames);
            float halfL = p.Pitch.LengthM * 0.5f, halfW = p.Pitch.WidthM * 0.5f;
            var ev = new BallEventBuffer();
            int steps = (int)Math.Ceiling(seconds * 100f) + 2;
            var pos = new Vec3[steps + 1];
            pos[0] = s.Position;
            for (int i = 1; i <= steps; i++)
            {
                if (s.Phase != BallPhase.Rest) { ev.Clear(); BallStepper.Step(ref s, p, ev, out _); }
                pos[i] = s.Position;
            }
            for (int f = 0; f < frames; f++)
            {
                double u = f * 100.0 / HighlightData.FrameRate;
                int i0 = (int)Math.Floor(u);
                float a = (float)(u - i0);
                var q = pos[i0] + (pos[i0 + 1] - pos[i0]) * a;
                d.Ball[f] = new Vec3(q.X - halfL, q.Z, q.Y - halfW);
            }
            return d;
        }

        static List<NetContact> Find(HighlightData d)
        {
            var list = new List<NetContact>();
            NetContactDetector.Find(d, Box(P), NetContactSettings.Default, list);
            return list;
        }

        /// <summary>아래쪽 골(x = 0) 안 공: 골라인 뒤 깊이 dd, 옆 w(중심 기준), 높이 h, 속도(깊이·옆·높이).</summary>
        internal static BallState InGoal(float dd, float w, float h, float vd, float vw, float vh) =>
            BallState.InAir(new Vec3(-dd, P.GoalCenterY + w, h), new Vec3(-vd, vw, vh), Vec3.Zero);

        [Test]
        public void BackTopSide_OneContactEach()
        {
            var back = Find(Frames(InGoal(0.3f, 0f, 1.2f, 20f, 0f, 0f), 3f));
            Assert.AreEqual(1, back.Count, "뒤 그물");
            Assert.AreEqual(NetFace.Back, back[0].Face);
            Assert.AreEqual(-1, back[0].Side);
            Assert.AreEqual(20f, back[0].NormalSpeed, 2f, "수직 속도 ≈ 슈팅 속도");

            var top = Find(Frames(InGoal(0.6f, 0f, 1.6f, 1.5f, 0f, 12f), 3f));
            Assert.AreEqual(1, top.Count, "위 그물");
            Assert.AreEqual(NetFace.Top, top[0].Face);

            var side = Find(Frames(InGoal(0.6f, 1.5f, 1.0f, 1f, 15f, 0f), 3f));
            Assert.AreEqual(1, side.Count, "옆 그물");
            Assert.AreEqual(NetFace.SidePositive, side[0].Face, "폭 + 쪽 옆그물");
            // 접촉점은 그 면 위
            Assert.AreEqual(BallParams.GoalWidth * 0.5f, side[0].Point.Z, 1e-3f);
            Assert.AreEqual(-(PitchSpec.Default.LengthM * 0.5f + P.NetDepth), back[0].Point.X, 1e-3f);
        }

        [Test]
        public void SideThenBack_TwoContactsInOrder()
        {
            var c = Find(Frames(InGoal(0.2f, 2.5f, 1.0f, 6f, 8f, 0f), 3f));
            Assert.AreEqual(2, c.Count);
            Assert.AreEqual(NetFace.SidePositive, c[0].Face);
            Assert.AreEqual(NetFace.Back, c[1].Face);
            Assert.Less(c[0].Time, c[1].Time, "시각 순");
        }

        [Test]
        public void Corner_SameFrame_TwoFaces()
        {
            var c = Find(Frames(InGoal(0.3f, 0f, 1.5f, 15f, 0f, 8.36f), 3f));
            Assert.AreEqual(2, c.Count, "뒤 + 위 모서리");
            Assert.Less(Math.Abs(c[0].Time - c[1].Time), 1.0 / HighlightData.FrameRate + 1e-6, "같은 프레임 근처");
            var faces = new HashSet<NetFace> { c[0].Face, c[1].Face };
            Assert.IsTrue(faces.Contains(NetFace.Back) && faces.Contains(NetFace.Top));
            Assert.IsTrue(c[0].Time < c[1].Time || (c[0].Time == c[1].Time && c[0].Face < c[1].Face), "같은 시각이면 면 순");
        }

        /// <summary>공이 뒤 그물 아래 기대 멈춘 뒤 수 초 동안 새 접촉 없음 (FR-55).</summary>
        [Test]
        public void RestingBall_NoNewContact()
        {
            var c = Find(Frames(BallState.RollingAt(new Vec2(-0.2f, P.GoalCenterY), new Vec2(-10f, 0f), P.Radius), 6f));
            Assert.AreEqual(1, c.Count);
        }

        /// <summary>골라인 위에서 멈춘 공 (FR-51).</summary>
        [Test]
        public void BallOnLine_NoContact()
        {
            var c = Find(Frames(BallState.RollingAt(new Vec2(1.5f, P.GoalCenterY + 3.4f), new Vec2(-1.6f, 0.05f), P.Radius), 4f));
            Assert.AreEqual(0, c.Count);
        }

        /// <summary>반만 넘었다 나온 공 (FR-51) — 기록 궤적 직접 작성.</summary>
        [Test]
        public void HalfCrossedOut_NoContact()
        {
            float halfL = PitchSpec.Default.LengthM * 0.5f;
            var d = new HighlightData(0f, 31);
            for (int f = 0; f < 31; f++)
            {
                float t = f / HighlightData.FrameRate;
                float x = -halfL + 0.5f - 6f * t + 12f * t * t; // 골라인 쪽으로 가다 되돌아 나옴 (최소 −halfL − 0.25)
                d.Ball[f] = new Vec3(x, 0.109f, 3.4f);
            }
            Assert.AreEqual(0, Find(d).Count);
        }

        /// <summary>좌우 거울상 (FR-57): 반대 골대에 거울 궤적이면 같은 시각·면, 접촉점 X 부호만 반대.</summary>
        [Test]
        public void Mirror_SameContacts()
        {
            var p = P;
            var low = Find(Frames(InGoal(0.2f, 2.5f, 1.0f, 6f, 8f, 0f), 3f));
            var hi = Find(Frames(BallState.InAir(new Vec3(p.Pitch.LengthM + 0.2f, p.GoalCenterY + 2.5f, 1.0f), new Vec3(6f, 8f, 0f), Vec3.Zero), 3f));
            Assert.AreEqual(low.Count, hi.Count);
            for (int i = 0; i < low.Count; i++)
            {
                Assert.AreEqual(low[i].Time, hi[i].Time, 1e-4, "시각 (좌우 좌표 크기가 달라 float 반올림 차)");
                Assert.AreEqual(low[i].Face, hi[i].Face);
                Assert.AreEqual(-low[i].Side, hi[i].Side);
                Assert.AreEqual(-low[i].Point.X, hi[i].Point.X, 1e-3f);
                Assert.AreEqual(low[i].Point.Y, hi[i].Point.Y, 1e-3f);
                Assert.AreEqual(low[i].Point.Z, hi[i].Point.Z, 1e-3f);
                Assert.AreEqual(low[i].NormalSpeed, hi[i].NormalSpeed, 1e-2f);
            }
        }

        /// <summary>첫 접촉 시각: 골 시각 이후 첫 접촉, 없으면 NaN (058 FR-22 · 059 FR-30 입력).</summary>
        [Test]
        public void FirstAfter_ReturnsFirstContactOrNaN()
        {
            var c = Find(Frames(InGoal(0.2f, 2.5f, 1.0f, 6f, 8f, 0f), 3f, startTime: 100f));
            Assert.AreEqual(c[0].Time, NetContactDetector.FirstAfter(c, 100.0), 1e-9);
            Assert.AreEqual(c[1].Time, NetContactDetector.FirstAfter(c, c[0].Time + 1e-6), 1e-9);
            Assert.IsTrue(double.IsNaN(NetContactDetector.FirstAfter(c, 200.0)));
        }
    }
}
