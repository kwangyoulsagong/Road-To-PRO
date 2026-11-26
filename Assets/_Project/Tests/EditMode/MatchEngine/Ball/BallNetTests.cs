using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>
    /// specs/059 FR-41 · AC-12: 골 안 공은 그물 면(뒤·옆·위)에 닿기 전까지 감속 영역 때문에 멈추지 않고,
    /// 닿으면 그물을 밀고 들어갔다가(FR-41 ⑥, 깊이 ≤ pocket) 0.3 s 안에 면 바깥쪽 속도 ≤ 0·면에서 0.5 m 안(위 면은 중력으로 떨어지므로 거리 조건 제외), 주머니 밖으로 나가지 않는다.
    /// 골문 밖 공은 바깥 면에 닿을 때만 막힌다(2026-11-26 사용자 결정) — 닿기 전에는 손대지 않는다. 바깥 면 출렁임 화면 검증은 012 (Q2 (a)).
    /// 좌표: 아래쪽 골(x = 0) — 그물 상자 x ∈ [−depth, 0], |y − 중심| &lt; 7.32/2, z &lt; 2.44.
    /// </summary>
    public class BallNetTests
    {
        const float Eps = 1e-3f;
        const int StepsPerSecond = 100;
        const float SettleSeconds = 0.3f, NearFace = 0.5f;

        enum Face { Back, SideHigh, Top }

        /// <summary>면 바깥 방향 단위 벡터와 공 중심이 넘으면 안 되는 한계(그 축 좌표).</summary>
        static (Vec3 outward, float limit) FaceOf(Face f, BallParams p)
        {
            switch (f)
            {
                case Face.Back: return (new Vec3(-1f, 0f, 0f), -p.NetDepth + p.Radius);
                case Face.SideHigh: return (new Vec3(0f, 1f, 0f), p.GoalCenterY + BallParams.GoalWidth * 0.5f - p.Radius);
                default: return (new Vec3(0f, 0f, 1f), BallParams.GoalHeight - p.Radius);
            }
        }

        /// <summary>면 바깥 방향 좌표 (클수록 바깥). 한계도 같은 부호로.</summary>
        static float Out(Vec3 v, Vec3 outward) => v.X * outward.X + v.Y * outward.Y + v.Z * outward.Z;

        static void AssertFaceContact(Face face, BallState s, BallParams p)
        {
            var (outward, limit) = FaceOf(face, p);
            float outLimit = face == Face.Back ? -limit : limit; // 바깥 방향 좌표로 바꾼 한계
            int contactStep = -1;
            bool groundBounce = false;
            var ev = new BallEventBuffer();
            for (int i = 0; i < 3 * StepsPerSecond && s.Phase != BallPhase.Rest; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                float o = Out(s.Position, outward);
                Assert.LessOrEqual(o, outLimit + p.NetPocket + Eps, $"{face}: step {i} 공 중심이 그물 주머니 밖 ({o:F4} > {outLimit:F4} + {p.NetPocket})");
                if (contactStep < 0 && o >= outLimit - 0.01f) contactStep = i;
                if (contactStep >= 0)
                    for (int k = 0; k < ev.Count; k++)
                        if (ev[k].Kind == BallEventKind.Bounce) groundBounce = true;
                if (contactStep >= 0 && i >= contactStep + (int)(SettleSeconds * StepsPerSecond))
                {
                    // 위 그물: 접촉 뒤 바닥 바운드의 위쪽 속도는 그물과 무관 — 바운드 전까지만 본다
                    if (face != Face.Top || !groundBounce)
                        Assert.LessOrEqual(Out(s.Velocity, outward), Eps, $"{face}: 접촉 +{SettleSeconds}s 뒤 면 바깥쪽 속도");
                    // 위 그물은 중력이 공을 면에서 떼어 바닥으로 떨어뜨리므로 거리 조건은 뒤·옆 면만 (T03 해석 보완)
                    if (face != Face.Top)
                        Assert.LessOrEqual(outLimit - o, NearFace, $"{face}: 접촉 +{SettleSeconds}s 뒤 면에서 {NearFace} m 안");
                }
            }
            Assert.GreaterOrEqual(contactStep, 0, $"{face}: 그물 면에 닿지 않음");
            Assert.Less(s.Position.X, -p.Radius + Eps, $"{face}: 공이 골라인 뒤(골 안)에 남아야 함");
        }

        [TestCase(10f)]
        [TestCase(20f)]
        [TestCase(30f)]
        public void BackFace_StopsWithin03s_NeverOutside(float speed)
        {
            var p = BallTestKit.Params();
            AssertFaceContact(Face.Back, BallState.InAir(new Vec3(-0.2f, p.GoalCenterY, 1.2f), new Vec3(-speed, 0f, 0f), Vec3.Zero), p);
        }

        [TestCase(25f)]
        public void BackFace_Rolling_StaysInGoal(float speed)
        {
            var p = BallTestKit.Params();
            AssertFaceContact(Face.Back, BallState.RollingAt(new Vec2(-0.2f, p.GoalCenterY), new Vec2(-speed, 0f), p.Radius), p);
        }

        [TestCase(8f)]
        [TestCase(20f)]
        public void SideFace_StopsWithin03s_NeverOutside(float speed)
        {
            var p = BallTestKit.Params();
            var s = BallState.InAir(new Vec3(-0.6f, p.GoalCenterY + 1.5f, 1.0f), new Vec3(-1f, speed, 0f), Vec3.Zero);
            AssertFaceContact(Face.SideHigh, s, p);
        }

        [TestCase(8f)]
        [TestCase(14f)]
        public void TopFace_StopsWithin03s_NeverOutside(float vz)
        {
            var p = BallTestKit.Params();
            var s = BallState.InAir(new Vec3(-0.6f, p.GoalCenterY, 1.6f), new Vec3(-1.5f, 0f, vz), Vec3.Zero);
            AssertFaceContact(Face.Top, s, p);
        }

        /// <summary>같은 공을 경계에서 먼 중앙에서 같은 스텝 수만큼 굴린 기준 상태 (공기 저항·중력만).</summary>
        static BallState Reference(BallState s, BallParams p, int steps)
        {
            var r = BallState.InAir(new Vec3(s.Position.X + 60f, s.Position.Y, s.Position.Z), s.Velocity, s.Spin);
            var ev = new BallEventBuffer();
            for (int i = 0; i < steps; i++)
            {
                ev.Clear();
                BallStepper.Step(ref r, p, ev, out _);
            }
            return r;
        }

        static int StepUntil(ref BallState s, BallParams p, float x, int max)
        {
            var ev = new BallEventBuffer();
            int i = 0;
            for (; i < max && s.Position.X > x; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
            }
            return i;
        }

        /// <summary>그물 면 전까지는 감속 영역 없음: 골라인 뒤 1 m 지점 속도가 경계 밖 기준 공과 같다(공기 저항·중력만).</summary>
        [Test]
        public void NoDecelBeforeFace()
        {
            var p = BallTestKit.Params();
            var s0 = BallState.InAir(new Vec3(0.5f, p.GoalCenterY, 1.2f), new Vec3(-20f, 0f, 0f), Vec3.Zero);
            var s = s0;
            int n = StepUntil(ref s, p, -1.0f, 200);
            Assert.LessOrEqual(s.Position.X, -1.0f, "골라인 뒤 1 m까지 못 감");
            var r = Reference(s0, p, n);
            Assert.AreEqual(r.Velocity.X, s.Velocity.X, 1e-3f, "그물 면 전에 감속됨 (x)");
            Assert.AreEqual(r.Velocity.Z, s.Velocity.Z, 1e-3f, "그물 면 전에 감속됨 (z)");
        }

        /// <summary>골문 밖(옆·위로 빗나감) 골라인 뒤 공은 그물 상자 판정을 받지 않는다 — 기준 공과 같은 궤적.</summary>
        [TestCase(5.5f, 1.0f)]
        [TestCase(0f, 4.0f)]
        public void OutsideBox_Unchanged(float dy, float z)
        {
            var p = BallTestKit.Params();
            var s0 = BallState.InAir(new Vec3(-0.3f, p.GoalCenterY + dy, z), new Vec3(-15f, 0f, 0f), Vec3.Zero);
            var s = s0;
            int n = StepUntil(ref s, p, -p.NetDepth - 1f, 100);
            Assert.Less(s.Position.X, -p.NetDepth - 1f + 1e-3f, "그물 상자 밖 공이 막힘");
            var r = Reference(s0, p, n);
            Assert.AreEqual(r.Velocity.X, s.Velocity.X, 1e-3f, "그물 상자 밖 공이 감속됨");
            Assert.AreEqual(r.Position.Z, s.Position.Z, 1e-3f, "그물 상자 밖 공 높이 다름");
        }

        /// <summary>
        /// 바깥에서 온 공(빗나가 골라인 뒤로 나간 공·크로스바 위로 넘어간 공·뒤 그물 뒤 공)은 그물 상자 안으로 들어가지 않고
        /// 바깥 면에서 반지름만큼 떨어져 막힌다 (2026-11-26 사용자 결정 "지금 바깥 면에서 막기", FR-41 ⑤).
        /// </summary>
        [TestCase(0.5f, 4.6f, 0.109f, -5f, -4f, 0f, TestName = "OutsideFace_WideRoll_BlockedBySideNet")]
        [TestCase(-1.0f, 4.7f, 0.6f, 0f, -10f, 0f, TestName = "OutsideFace_BehindLineSideways_BlockedBySideNet")]
        [TestCase(0.5f, 0f, 3.0f, -4f, 0f, 0f, TestName = "OutsideFace_OverBar_StaysOnRoof")]
        [TestCase(-2.6f, 0f, 0.5f, 8f, 0f, 0f, TestName = "OutsideFace_BehindBackNet_Blocked")]
        public void OutsideFace_NeverEntersNetBox(float x, float dy, float z, float vx, float vy, float vz)
        {
            var p = BallTestKit.Params();
            float half = BallParams.GoalWidth * 0.5f, H = BallParams.GoalHeight, D = p.NetDepth, r = p.Radius;
            var s = BallState.InAir(new Vec3(x, p.GoalCenterY + dy, z), new Vec3(vx, vy, vz), Vec3.Zero);
            var ev = new BallEventBuffer();
            bool touched = false;
            for (int i = 0; i < 300 && s.Phase != BallPhase.Rest; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                float d = -s.Position.X, w = DMath.Abs(s.Position.Y - p.GoalCenterY), h = s.Position.Z;
                if (d <= 0f) continue;
                // 그물 면 + 반지름 상자 안 = 공 구가 그물을 뚫음
                bool inExpanded = d < D + r - Eps && w < half + r - Eps && h < H + r - Eps;
                Assert.IsFalse(inExpanded, $"step {i}: 바깥 공이 그물을 뚫음 d={d:F3} w={w:F3} z={h:F3}");
                if (d < D + r + 0.01f && w < half + r + 0.01f && h < H + r + 0.01f) touched = true;
            }
            Assert.IsTrue(touched, "바깥 면에 닿지 않음 — 시나리오가 그물을 비껴감");
        }

        /// <summary>골라인에 걸친 코너 쪽 공(그물과 먼 곳)은 상태가 비트 단위로 그대로 (결정론 리뷰 1: 좌표 왕복 1 ulp).</summary>
        [TestCase(5.3f)]
        [TestCase(63.7f)]
        public void FarFromNet_StateBitExact(float y)
        {
            var p = BallTestKit.Params();
            var s = BallState.InAir(new Vec3(-0.03f, y, 0.5f), new Vec3(-3f, 0.2f, 0f), Vec3.Zero);
            var before = s;
            GoalFrame.ApplyNet(ref s, new Vec3(0.05f, y - 0.01f, 0.5f), p);
            Assert.IsTrue(before.Position.Y == s.Position.Y && before.Position.X == s.Position.X && before.Position.Z == s.Position.Z, "위치 비트 변경");
            Assert.IsTrue(before.Velocity.X == s.Velocity.X && before.Velocity.Y == s.Velocity.Y && before.Velocity.Z == s.Velocity.Z, "속도 비트 변경");
        }

        /// <summary>뒤 그물 가운데 슈팅 하나의 최대 주머니 깊이 (면 − 반지름 기준, m).</summary>
        static float MaxPocket(BallParams p, float speed, float w, float h)
        {
            var s = BallState.InAir(new Vec3(-0.3f, p.GoalCenterY + w, h), new Vec3(-speed, 0f, 0f), Vec3.Zero);
            var ev = new BallEventBuffer();
            float lim = p.NetDepth - p.Radius, max = 0f;
            for (int i = 0; i < 200; i++)
            {
                ev.Clear();
                BallStepper.Step(ref s, p, ev, out _);
                max = System.Math.Max(max, -s.Position.X - lim);
            }
            return max;
        }

        /// <summary>FR-41 ⑥: 빠를수록 깊이 밀고 들어가고(10·20·30 m/s 순), 주머니 최대 깊이를 넘지 않는다.</summary>
        [Test]
        public void Pocket_FasterGoesDeeper_WithinLimit()
        {
            var p = BallTestKit.Params();
            float a = MaxPocket(p, 10f, 0f, 1.2f), b = MaxPocket(p, 20f, 0f, 1.2f), c = MaxPocket(p, 30f, 0f, 1.2f);
            Assert.Greater(a, 0.05f, "10 m/s도 그물을 민다");
            Assert.Less(a, b);
            Assert.Less(b, c);
            Assert.LessOrEqual(c, p.NetPocket + Eps, "주머니 최대 깊이");
        }

        /// <summary>FR-41 ⑥·FR-26: 면 테두리(포스트 쪽·바닥) 가까이는 그물이 묶여 있어 거의 밀고 들어가지 못한다.</summary>
        [Test]
        public void Pocket_NearEdge_Shallow()
        {
            var p = BallTestKit.Params();
            float mid = MaxPocket(p, 25f, 0f, 1.2f);
            float post = MaxPocket(p, 25f, BallParams.GoalWidth * 0.5f - p.Radius - 0.02f, 1.2f);
            Assert.Less(post, 0.25f * mid, $"포스트 쪽 {post:F3} m vs 가운데 {mid:F3} m");
        }

        /// <summary>표시(NetSide)는 골문으로 들어온 공에만, 피치로 돌아오면 지워진다.</summary>
        [Test]
        public void NetSide_SetInGoal_ClearedOnPitch()
        {
            var p = BallTestKit.Params();
            var s = BallState.InAir(new Vec3(1.0f, p.GoalCenterY, 1.0f), new Vec3(-20f, 0f, 0f), Vec3.Zero);
            var ev = new BallEventBuffer();
            for (int i = 0; i < 20; i++) { ev.Clear(); BallStepper.Step(ref s, p, ev, out _); }
            Assert.AreEqual(-1, s.NetSide, "골 안");
            s = BallState.InAir(new Vec3(-0.5f, p.GoalCenterY, 1.0f), new Vec3(8f, 0f, 0f), Vec3.Zero);
            s.NetSide = -1;
            for (int i = 0; i < 20; i++) { ev.Clear(); BallStepper.Step(ref s, p, ev, out _); }
            Assert.Greater(s.Position.X, 0f);
            Assert.AreEqual(0, s.NetSide, "피치로 돌아오면 0");
            var wide = BallState.InAir(new Vec3(0.5f, p.GoalCenterY + 4.6f, 0.5f), new Vec3(-5f, -4f, 0f), Vec3.Zero);
            for (int i = 0; i < 100; i++) { ev.Clear(); BallStepper.Step(ref wide, p, ev, out _); }
            Assert.AreEqual(0, wide.NetSide, "빗나간 공은 표시 없음");
        }

        [Test]
        public void SameSeed_TwiceSameHash()
        {
            var p = BallTestKit.Params();
            Assert.AreEqual(HashShots(p), HashShots(p));
        }

        /// <summary>골문 안 여러 지점·속도 슈팅 묶음을 3 s씩 굴린 상태 해시.</summary>
        static ulong HashShots(BallParams p)
        {
            var h = StableHasher.Create();
            var ev = new BallEventBuffer();
            for (int iy = -3; iy <= 3; iy++)
            for (int iz = 0; iz < 3; iz++)
            for (int iv = 0; iv < 3; iv++)
            {
                float y = p.GoalCenterY + iy * 1.1f, z = 0.4f + iz * 0.8f, v = 8f + iv * 11f;
                var s = BallState.InAir(new Vec3(6f, p.GoalCenterY, 0.5f), new Vec3(-v, (y - p.GoalCenterY) * v / 6f, (z - 0.2f) * v / 6f + 1f), new Vec3(0f, 0f, iy * 2f));
                for (int i = 0; i < 300 && s.Phase != BallPhase.Rest; i++)
                {
                    ev.Clear();
                    BallStepper.Step(ref s, p, ev, out _);
                    h.Add(s.Position.X); h.Add(s.Position.Y); h.Add(s.Position.Z);
                    h.Add(s.Velocity.X); h.Add(s.Velocity.Y); h.Add(s.Velocity.Z);
                }
            }
            return h.Value;
        }
    }
}
