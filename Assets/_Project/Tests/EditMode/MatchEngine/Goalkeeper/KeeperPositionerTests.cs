using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Goalkeeper;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/011 위치 선정 FR-01~07 (AC-01·02·22).</summary>
    public class KeeperPositionerTests
    {
        static readonly Vec2 Goal = new Vec2(0f, 34f);
        static GoalkeeperParams P => GoalkeeperParams.Defaults;
        static KeeperProfile Avg => KeeperProfile.From(P, KeeperAttributes.Uniform(10.5f));
        static KeeperPositioner New() => new KeeperPositioner(P, new GoalGrid(P));

        static PositionInput In(Vec2 ball, KeeperRole role = KeeperRole.Standard, float line = 30f, bool high = false) => new PositionInput
        {
            Goal = Goal, DirIn = 1f, Ball = ball, LineHeight = line, HighBlock = high, Role = role,
        };

        [Test]
        public void Target_PenaltySpotFront_OnBisectorDepthInRange()
        {
            var t = New().Target(In(new Vec2(11f, 34f)), Avg);
            Assert.AreEqual(34f, t.Y, 0.05f, "이등분선(정면) 위");
            Assert.That(t.X, Is.InRange(0.5f, 6f), "깊이 0.5~6 m");
        }

        [Test]
        public void Target_OffCenter_OnBisectorLine()
        {
            var pos = New();
            var ball = new Vec2(16f, 24f);
            var t = pos.Target(In(ball), Avg);
            var foot = KeeperPositioner.BisectorFoot(Goal, 1f, ball, out _);
            var dir = (ball - foot).Normalized();
            var off = Vec2.Cross(dir, t - foot);
            Assert.Less(System.Math.Abs(off), 0.35f, "좌우 후보(±0.5 m) 섞기 뒤에도 이등분선 근처");
        }

        [Test]
        public void Target_NearSideGoalLine_CoversNearPost()
        {
            var pos = New();
            var t = pos.Target(In(new Vec2(1f, 50f)), Avg);
            Assert.Greater(t.Y, 34f + 2.5f, "니어포스트(위쪽) 쪽");
            Assert.GreaterOrEqual(t.X, 0.25f);
            var behind = pos.Target(In(new Vec2(-0.8f, 52f)), Avg);
            Assert.Greater(behind.Y, 34f + 2.5f, "골라인 뒤 공도 니어포스트");
            Assert.GreaterOrEqual(behind.X, 0.25f);
        }

        [Test]
        public void Target_RandomBalls_NoNaN_NeverInGoal()
        {
            var pos = New();
            var rng = new SeedRng(1101UL);
            for (int i = 0; i < 1000; i++)
            {
                var ball = new Vec2(rng.Range(-2f, 52f), rng.Range(0f, 68f));
                var t = pos.Target(In(ball), Avg);
                Assert.IsFalse(float.IsNaN(t.X) || float.IsNaN(t.Y) || float.IsInfinity(t.X) || float.IsInfinity(t.Y), $"NaN @ {ball}");
                Assert.GreaterOrEqual(t.X, 0.25f - 1e-4f, $"골라인 뒤·골문 안 금지 @ {ball}");
                Assert.That(t.Y, Is.InRange(34f - 4.66f - 1e-3f, 34f + 4.66f + 1e-3f), $"피치 밖·포스트 밖 과도 @ {ball}");
            }
        }

        [Test]
        public void Target_ShootRangeBoundarySweep_MaxChange02()
        {
            var pos = New();
            Vec2 prev = default;
            var has = false;
            float maxDelta = 0f;
            // 슈팅 가능 거리 35 m 경계(섞는 구간 33.5~36.5 m)를 넉넉히 덮는 구간
            for (float x = 30f; x <= 40f; x += 0.1f)
            {
                var t = pos.Target(In(new Vec2(x, 30f)), Avg);
                if (has) maxDelta = System.Math.Max(maxDelta, (t - prev).Length());
                prev = t;
                has = true;
            }
            Assert.LessOrEqual(maxDelta, 0.2f);
        }

        [Test]
        public void Target_LateralSweep_Monotonic()
        {
            var pos = New();
            float prevY = float.MinValue;
            // 페널티 박스 폭(골문 중앙 ± 20.16 m) 안에서 좌우로 쓴다
            for (float y = 14f; y <= 54f; y += 0.5f)
            {
                var t = pos.Target(In(new Vec2(18f, y)), Avg);
                Assert.GreaterOrEqual(t.Y, prevY - 0.05f, $"y={y}");
                prevY = System.Math.Max(prevY, t.Y);
            }
        }

        [Test]
        public void Target_MirroredBall_MirroredTarget()
        {
            var pos = New();
            for (float y = 6f; y <= 30f; y += 3f)
            {
                var a = pos.Target(In(new Vec2(18f, y)), Avg);
                var b = pos.Target(In(new Vec2(18f, 68f - y)), Avg);
                Assert.AreEqual(a.X, b.X, 1e-3f, $"y={y}");
                Assert.AreEqual(34f - a.Y, b.Y - 34f, 1e-3f, $"y={y}");
            }
        }

        [Test]
        public void WaitDepth_RolesAndClamp()
        {
            var pos = New();
            Assert.AreEqual(12f, pos.WaitDepth(40f, false, KeeperRole.Standard), 1e-4f);
            Assert.AreEqual(12f * 0.6f, pos.WaitDepth(40f, false, KeeperRole.Traditional), 1e-4f);
            Assert.AreEqual(4f, pos.WaitDepth(5f, false, KeeperRole.Standard), 1e-4f, "하한 4 m");
            Assert.AreEqual(20f, pos.WaitDepth(50f, true, KeeperRole.Sweeper), 1e-4f, "스위퍼 라인 − 18 = 32 → 상한 20 우선");
            Assert.AreEqual(17f, pos.WaitDepth(35f, true, KeeperRole.Sweeper), 1e-4f, "라인 35 → max(10.5, 17)");
            Assert.AreEqual(10.5f, pos.WaitDepth(35f, false, KeeperRole.Sweeper), 1e-4f, "DefHigh 아니면 기본식");
            Assert.AreEqual(9f, pos.WaitDepth(-1f, false, KeeperRole.Standard), 1e-4f, "라인 모름 → 기본 30 m");
        }

        [Test]
        public void Target_FarBall_UsesWaitDepth()
        {
            var t = New().Target(In(new Vec2(70f, 34f), line: 40f), Avg);
            Assert.AreEqual(12f, t.X, 0.01f);
            Assert.AreEqual(34f, t.Y, 0.01f);
        }

        [Test]
        public void Target_PrevFar_StepLimited()
        {
            var inp = In(new Vec2(11f, 34f));
            inp.HasPrev = true;
            inp.Prev = new Vec2(10f, 34f);
            var t = New().Target(inp, Avg);
            Assert.AreEqual(1.5f, (t - inp.Prev).Length(), 1e-3f);
        }

        [Test]
        public void Target_DepthNeverBeyondMax_ChipTermPullsBack()
        {
            var pos = New();
            for (float x = 6f; x <= 34f; x += 2f)
            {
                var t = pos.Target(In(new Vec2(x, 34f)), Avg);
                Assert.LessOrEqual(t.X, P.PosDepthMax + 1e-3f, $"x={x}");
            }
        }

        [Test]
        public void Target_NoiseIsAdded()
        {
            var a = New().Target(In(new Vec2(11f, 34f)), Avg);
            var inp = In(new Vec2(11f, 34f));
            inp.Noise = new Vec2(0f, 0.3f);
            var b = New().Target(inp, Avg);
            Assert.AreEqual(0.3f, b.Y - a.Y, 1e-4f);
        }
    }
}
