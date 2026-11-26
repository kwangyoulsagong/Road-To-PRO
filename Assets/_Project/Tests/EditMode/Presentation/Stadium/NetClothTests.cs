using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Stadium.Net;
using RoadToPro.Tests.EditMode.MatchEngine.Ball;
using RoadToPro.Tests.EditMode.Presentation.Highlight;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>
    /// specs/059 AC-02·03·04 (그물 시뮬 + 굽기, 2026-11-26 "ㄱㄱ" 개정): 실제 공 물리(059 그물 주머니)로 만든 궤적을 굽는다.
    /// 아래쪽 골(x = 0, 하이라이트 side −1).
    /// </summary>
    public class NetClothTests
    {
        const float HalfL = 52.5f;
        static NetRippleTuning _t;
        static NetCloth _cloth;
        static NetRippleTuning T => _t ??= NetRippleTuningTests.Defaults();
        static NetCloth Cloth => _cloth ??= new NetCloth(T);
        static BallParams P => BallTestKit.Params();
        static GoalBox Box => new GoalBox(PitchSpec.Default, BallParams.GoalWidth, BallParams.GoalHeight, P.NetDepth, P.Radius, P.NetPocket);

        sealed class Shot
        {
            public HighlightData Data;
            public readonly List<NetContact> Contacts = new List<NetContact>();
            public NetBake Bake;
        }

        static Shot Make(BallState s, float seconds = 5f)
        {
            var shot = new Shot { Data = NetContactDetectorTests.Frames(s, seconds) };
            NetContactDetector.Find(shot.Data, Box, NetContactSettings.Default, shot.Contacts);
            shot.Bake = NetBake.Build(Cloth, shot.Data, shot.Contacts, -1, HalfL, Box);
            return shot;
        }

        static Shot Back(float speed) => Make(NetContactDetectorTests.InGoal(0.3f, 0.3f, 1.2f, speed, 0f, 0f));

        static float Len(Vec3 a) => (float)Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);

        /// <summary>굽기 전체에서 최대 변위·그 노드·프레임.</summary>
        static float MaxDisp(NetBake b, out int node, out int frame)
        {
            float best = 0f;
            node = frame = -1;
            for (int f = 0; f < b.Frames; f++)
                for (int i = 0; i < b.Nodes; i++)
                {
                    float d = Len(b.Offset(f, i));
                    if (d > best) { best = d; node = i; frame = f; }
                }
            return best;
        }

        [Test]
        public void Rest_OutsideBox_PinsOnFrame()
        {
            var c = Cloth;
            for (int i = 0; i < c.NodeCount; i++)
            {
                var p = c.Rest[i];
                // 정지 그물은 시뮬 공 중심 영역 + 반지름 상자 밖 (FR-02)
                bool inside = p.X > 1e-3f && p.X < T.Shape.Depth - 1e-3f && Math.Abs(p.Z) < T.Shape.HalfMouth - 1e-3f && p.Y < T.Shape.Height - 1e-3f && p.Y > 1e-3f;
                Assert.IsFalse(inside, $"노드 {i} {p} 가 그물 상자 안");
                Assert.GreaterOrEqual(p.Y, -1e-4f, "바닥 아래");
            }
            Assert.Greater(c.NodeCount, 800);
            Assert.Less(c.NodeCount, 1600);
        }

        [Test]
        public void SpeedMonotone_DepthTarget_WholeNetMoves()
        {
            float prev = 0f;
            foreach (float v in new[] { 5f, 15f, 30f })
            {
                var shot = Back(v);
                Assert.IsNotNull(shot.Bake, $"{v} m/s 굽기 없음");
                float m = MaxDisp(shot.Bake, out var node, out _);
                Assert.Greater(m, prev, $"{v} m/s: 속도 순서대로 커져야 (FR-21)");
                Assert.LessOrEqual(m, T.MaxDisp + 1e-3f, $"{v} m/s: 상한 (FR-21)");
                prev = m;
                if (v == 30f)
                {
                    Assert.That(m, Is.InRange(0.6f, 1.0f), "30 m/s 정면 최대 깊이 목표 0.6~1.0 m (FR-21 [제안])");
                    // 위치: 공이 민 자리(접촉점) 0.5 m 안 (높이·폭)
                    var c = shot.Contacts[0];
                    var cl = GoalLocal.ToLocal(c.Point, -1, HalfL);
                    var p = Cloth.Rest[node];
                    float lateral = (float)Math.Sqrt((p.Y - cl.Y) * (p.Y - cl.Y) + (p.Z - cl.Z) * (p.Z - cl.Z));
                    Assert.LessOrEqual(lateral, 0.5f, "최대 변위가 공이 민 자리 근처 (FR-20)");
                    // 방향: 뒤 면 바깥(+X)
                    MaxDisp(shot.Bake, out node, out var frame);
                    var d = shot.Bake.Offset(frame, node);
                    Assert.GreaterOrEqual(d.X / Len(d), 0.8f, "변위 방향 (FR-20)");
                    // 그물 전체가 크게 출렁임 (FR-20·24, 2026-11-26 사용자 "골망이 저렇게 다른데도 출렁여야지 크게")
                    float other = 0f, far = 0f;
                    for (int f = 0; f < shot.Bake.Frames; f++)
                        for (int i = 0; i < shot.Bake.Nodes; i++)
                        {
                            float dd = Len(shot.Bake.Offset(f, i));
                            if ((Cloth.FacesOf(i) & (1 << (int)NetFace.Back)) == 0) { other = Math.Max(other, dd); continue; }
                            var r = Cloth.Rest[i];
                            float lat2 = (float)Math.Sqrt((r.Y - cl.Y) * (r.Y - cl.Y) + (r.Z - cl.Z) * (r.Z - cl.Z));
                            if (lat2 > 1.5f) far = Math.Max(far, dd);
                        }
                    Assert.GreaterOrEqual(other, 0.2f, "옆·위 그물 최대 변위 (FR-20·24)");
                    Assert.GreaterOrEqual(far, 0.4f, "뒤 그물 접촉점 1.5 m 밖 최대 변위 (FR-20)");
                }
            }
        }

        /// <summary>FR-23: 접촉 + 4 s에 공 주변 0.8 m 밖 전 점 4 cm 미만, 0.5 s 구간 최대 변위가 직전보다 10 % 넘게 안 커짐.</summary>
        [Test]
        public void Settles_Within4s_MostlyDecreasing()
        {
            var shot = Back(25f);
            var b = shot.Bake;
            MaxDisp(b, out _, out int peak);
            int win = (int)(0.5f * NetBake.FrameRate);
            float last = float.MaxValue;
            for (int f0 = peak; f0 + win <= b.Frames; f0 += win)
            {
                float m = 0f;
                for (int f = f0; f < f0 + win; f++) for (int i = 0; i < b.Nodes; i++) m = Math.Max(m, Len(b.Offset(f, i)));
                Assert.LessOrEqual(m, last * 1.1f + 1e-3f, $"구간 {f0 / NetBake.FrameRate:F1} s 최대 변위가 다시 커짐");
                last = m;
            }
            double contact = shot.Contacts[shot.Contacts.Count - 1].Time;
            int fs = (int)Math.Ceiling((contact + 4.0 - b.StartTime) * NetBake.FrameRate);
            Assume.That(fs < b.Frames, "굽기 길이가 접촉 + 4 s를 덮어야");
            var ball = GoalLocal.ToLocal(shot.Data.Ball[shot.Data.FrameIndex((float)(b.StartTime + fs / NetBake.FrameRate))], -1, HalfL);
            for (int i = 0; i < b.Nodes; i++)
            {
                if (Len(Cloth.Rest[i] - ball) < 0.8f) continue;   // 공이 기대 있는 자리 (FR-22·23)
                Assert.Less(Len(b.Offset(fs, i)), 0.04f, $"노드 {i}: 접촉 + 4 s에 4 cm 이상");
            }
        }

        /// <summary>FR-22·26·02: 공이 그물 밖으로 안 보임(노드가 공 구 반지름 안으로 안 들어옴), 고정점 불변, 상자 안쪽·바닥 아래 0.</summary>
        [Test]
        public void BallInside_PinsFixed_NoInsideBox()
        {
            foreach (var shot in new[] { Back(30f), Make(NetContactDetectorTests.InGoal(0.6f, 0f, 1.6f, 1.5f, 0f, 12f)), Make(NetContactDetectorTests.InGoal(0.6f, 1.5f, 1.0f, 1f, 15f, 0f)) })
            {
                var b = shot.Bake;
                Assert.IsNotNull(b);
                var nodes = new Vec3[b.Nodes];
                for (int f = 0; f < b.Frames; f++)
                {
                    double t = b.StartTime + f / NetBake.FrameRate;
                    b.Sample(t, Cloth.Rest, nodes);
                    var ball = GoalLocal.ToLocal(shot.Data.Ball[shot.Data.FrameIndex((float)t)], -1, HalfL);
                    for (int i = 0; i < b.Nodes; i++)
                    {
                        var p = nodes[i];
                        if (Cloth.IsPinned(i)) Assert.Less(Len(p - Cloth.Rest[i]), 1e-3f, "고정점 움직임");
                        Assert.GreaterOrEqual(Len(p - ball), T.BallReach - 0.02f, $"프레임 {f} 노드 {i}: 공이 그물 밖으로 비침");
                        Assert.GreaterOrEqual(p.Y, -1e-3f, "바닥 아래");
                        byte fc = Cloth.FacesOf(i);
                        if ((fc & (1 << (int)NetFace.Back)) != 0) Assert.GreaterOrEqual(p.X, T.Shape.Depth - 2e-3f, "뒤 그물이 상자 안");
                        if ((fc & (1 << (int)NetFace.Top)) != 0) Assert.GreaterOrEqual(p.Y, T.Shape.Height - 2e-3f, "위 그물이 상자 안");
                    }
                }
            }
        }

        /// <summary>FR-28: 두 번 구우면 같은 해시. FR-57: 반대 골대 거울상 궤적 = 같은 로컬 움직임(±1 mm).</summary>
        [Test]
        public void TwiceSameHash_Mirror()
        {
            var a = Back(20f).Bake;
            var b = Back(20f).Bake;
            Assert.AreEqual(a.Hash(), b.Hash());

            var p = P;
            var hiData = NetContactDetectorTests.Frames(BallState.InAir(new Vec3(p.Pitch.LengthM + 0.3f, p.GoalCenterY + 0.3f, 1.2f), new Vec3(20f, 0f, 0f), Vec3.Zero), 5f);
            var cs = new List<NetContact>();
            NetContactDetector.Find(hiData, Box, NetContactSettings.Default, cs);
            var hi = NetBake.Build(Cloth, hiData, cs, 1, HalfL, Box);
            Assert.AreEqual(a.Frames, hi.Frames);
            float worst = 0f;
            for (int f = 0; f < a.Frames; f += 5)
                for (int i = 0; i < a.Nodes; i++)
                {
                    float e = Len(a.Offset(f, i) - hi.Offset(f, i));
                    worst = Math.Max(worst, e);
                }
            TestContext.WriteLine($"거울상 최대 차 {worst * 1000f:F2} mm");
            Assert.Less(worst, 0.0015f, "거울상");
        }

        /// <summary>FR-27·29: 굽기 시작 전·시작 = 정지 모양, 0.5x에서 프레임당 변화가 1x의 0.6배 이하·멈칫 없음, 끝 뒤는 마지막 프레임 유지.</summary>
        [Test]
        public void Sample_Edges_Slowmo()
        {
            var b = Back(25f).Bake;
            var rest = Cloth.Rest;
            var x = new Vec3[b.Nodes];
            var y = new Vec3[b.Nodes];
            b.Sample(b.StartTime - 1.0, rest, x);
            for (int i = 0; i < b.Nodes; i++) Assert.AreEqual(rest[i], x[i]);
            b.Sample(b.StartTime + 1e-4, rest, x);
            for (int i = 0; i < b.Nodes; i++) Assert.Less(Len(x[i] - rest[i]), 0.001f, "굽기 시작 튐");
            b.Sample(b.EndTime + 5.0, rest, x);
            b.Sample(b.EndTime, rest, y);
            for (int i = 0; i < b.Nodes; i++) Assert.Less(Len(x[i] - y[i]), 1e-5f, "끝 뒤 = 마지막 프레임");

            float Step(double t0, double dt)
            {
                b.Sample(t0, rest, x);
                b.Sample(t0 + dt, rest, y);
                float m = 0f;
                for (int i = 0; i < b.Nodes; i++) m = Math.Max(m, Len(y[i] - x[i]));
                return m;
            }
            const double Frame = 1d / 60d;
            for (double w0 = b.StartTime; w0 < b.StartTime + 1.5; w0 += 0.1)
            {
                float one = 0f, half = 0f;
                int n1 = 0, n2 = 0, same = 0;
                for (double s = w0; s < w0 + 0.1 - 1e-9; s += Frame) { one += Step(s, Frame); n1++; }
                for (double s = w0; s < w0 + 0.1 - 1e-9; s += Frame * 0.5)
                {
                    float d = Step(s, Frame * 0.5);
                    half += d; n2++;
                    // 멈칫 = 1x로는 1 mm 넘게 움직이는 순간인데 0.5x 프레임이 그대로 (구운 데이터 mm 양자화로 거의 멈춘 순간은 제외)
                    same = d < 1e-7f && Step(s, Frame) > 1e-3f ? same + 1 : 0;
                    Assert.Less(same, 2, "0.5x 멈칫");
                }
                if (one / n1 > 1e-4f) Assert.LessOrEqual(half / n2, 0.6f * one / n1, $"0.5x 구간 {w0 - b.StartTime:F1} s");
            }
        }

        [Test]
        public void NoContact_NoBake()
        {
            var weak = Make(BallState.RollingAt(new Vec2(0.5f, P.GoalCenterY), new Vec2(-1.6f, 0f), P.Radius), 4f);
            Assert.AreEqual(0, weak.Contacts.Count);
            Assert.IsNull(weak.Bake);
        }
    }
}
