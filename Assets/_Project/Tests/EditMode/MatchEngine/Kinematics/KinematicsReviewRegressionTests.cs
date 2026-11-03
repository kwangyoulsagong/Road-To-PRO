using System;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>T16 결정론 리뷰 권장 사항 회귀 (R1~R5).</summary>
    public class KinematicsReviewRegressionTests
    {
        static ulong Hash(KinematicsSystem k)
        {
            var h = StableHasher.Create();
            k.AddTo(ref h);
            return h.Value;
        }

        [Test]
        public void R1_PendingContact_ChangesHash()
        {
            var a = One(Attr(), new Vec2(30f, 30f));
            var b = One(Attr(), new Vec2(30f, 30f));
            b.ApplyContact(1); // 정지 선수: 속도는 그대로지만 다음 서브스텝 a_cap 배율이 다르다
            Assert.That(Hash(b), Is.Not.EqualTo(Hash(a)));
        }

        [Test]
        public void R2_SeparationCandidates_CoverReachableOverlaps()
        {
            var p = Params;
            var sep = new Separation(22, p);
            var maxMove = (p.VmaxBase + p.VmaxRange) * p.Dt;
            Assert.That(sep.CandidateRadius, Is.GreaterThanOrEqualTo(2f * p.BodyRadius + Separation.ProjectionSlop + 2f * maxMove + 2f * Separation.MaxTotalProjection));
            // 튜닝으로 몸 반지름을 키워도 후보 반경이 따라 커진다
            var big = WithValues("{\"version\":1,\"values\":[{\"id\":\"kin.sep.bodyRadius\",\"value\":0.6},{\"id\":\"kin.sep.radius\",\"value\":2.0}]}");
            Assert.That(new Separation(22, big).CandidateRadius, Is.GreaterThan(sep.CandidateRadius));
        }

        [Test]
        public void R3_DefaultSetup_StillMoves_FacingFollowsVelocity()
        {
            var setups = new PlayerSetup[1];
            setups[0] = default; // Facing (0,0), id 0
            var k = new KinematicsSystem(Params, setups);
            Assert.That(k.State(0).Facing, Is.EqualTo(new Vec2(1f, 0f)));
            k.SetCommandImmediate(0, SteeringCommand.Seek(new Vec2(30f, 30f)));
            Run(k, 100);
            Assert.That(k.State(0).Velocity.Length(), Is.GreaterThan(1f), "기본값 설정에서도 정지 고착 없음");

            var m = new KinematicsSystem(Params, new[] { new PlayerSetup(1, Attr(), new Vec2(30f, 30f), new Vec2(0f, 1.5f), new Vec2(1f, 0f)) });
            Assert.That(m.State(0).Facing.Y, Is.EqualTo(1f).Within(1e-6f), "움직이는 선수의 몸 방향 = 속도 방향");
        }

        [Test]
        public void R4_NonFiniteInputs_RejectedUpFront()
        {
            var k = One(Attr(), new Vec2(30f, 30f));
            Assert.Throws<ArgumentException>(() => k.SetCommand(1, SteeringCommand.Seek(new Vec2(float.NaN, 0f))));
            Assert.Throws<ArgumentException>(() => k.SetCommandImmediate(1, SteeringCommand.Arrive(new Vec2(10f, float.PositiveInfinity))));
            Assert.Throws<ArgumentException>(() => new KinematicsSystem(Params, new[] { new PlayerSetup(1, Attr(), new Vec2(float.NaN, 0f)) }));
            Assert.Throws<ArgumentException>(() => new KinematicsSystem(Params, new[] { new PlayerSetup(1, Attr(), Vec2.Zero, w: float.NaN) }));
        }

        [Test]
        public void R5_LaterCommand_NeverActivatesBeforeEarlier()
        {
            var q = new CommandQueue(1);
            q.Enqueue(0, SteeringCommand.Arrive(new Vec2(1f, 0f)), 20);
            q.Enqueue(0, SteeringCommand.Arrive(new Vec2(2f, 0f)), 15);
            var active = SteeringCommand.Idle();
            Assert.That(q.Activate(0, 15, ref active), Is.False);
            Assert.That(q.Activate(0, 20, ref active), Is.True);
            Assert.That(active.Point.X, Is.EqualTo(2f), "같은 시각에 둘 다 활성화, 나중 명령이 남는다");
        }
    }
}
