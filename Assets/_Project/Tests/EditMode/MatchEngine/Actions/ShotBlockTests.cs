using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 FR-15: 슛 경로 수비수 블록 (선분–몸 원기둥), 팔 영역 → Arm 접촉·핸드볼 입력.</summary>
    public class ShotBlockTests
    {
        /// <summary>슈터 9가 (x0, 34)에서 +공격 방향으로 높이 z, 속도 v로 찬 공을 직접 띄운다.</summary>
        static DuelFixture Fly(float z, float lateral, out int dir, int defender = 14, float defDist = 5f, bool putDefender = true)
        {
            var f = new DuelFixture(7172UL);
            var ctx = f.Ctx;
            dir = ctx.AttackDir(0);
            var x0 = ctx.Pitch.LengthM * 0.5f + dir * 20f;
            f.Put(9, new Vec2(x0, 34f), new Vec2(dir, 0f));
            if (putDefender) f.Put(defender, new Vec2(x0 + dir * defDist, 34f + lateral), new Vec2(-dir, 0f));
            ctx.Ball = BallState.InAir(new Vec3(x0 + dir * 0.5f, 34f, z), new Vec3(dir * 25f, 0f, 0f), Vec3.Zero);
            ctx.LastTouchTeam = 0;
            ctx.LastTouchSlot = 9;
            ctx.LastKickSlot = 9;
            ctx.LastKickKind = (byte)BallActionKind.Shot;
            ctx.LastKickSubstep = ctx.Kinematics.Substep;
            ctx.Actions.Shots.OnKicked(ctx, 9, ctx.Kinematics.Substep);
            return f;
        }

        [Test]
        public void DefenderInPath_Blocks_Deflects_TouchToDefender()
        {
            var f = Fly(0.5f, 0f, out var dir);
            f.Ticks(5);
            Assert.AreEqual(1, f.Count(SimEventKind.Block));
            Assert.AreEqual(14, f.Ctx.LastTouchSlot);
            Assert.AreEqual(1, f.Ctx.LastTouchTeam);
            Assert.IsFalse(f.Ctx.Actions.Shots.InFlight);
            Assert.Less(f.Ctx.Ball.Velocity.Length(), 25f * 0.5f, "굴절 감속");
        }

        [Test]
        public void NoDefender_NoBlock_TeammateIgnored()
        {
            var f = Fly(0.5f, 0f, out _, defender: 5);
            f.Ticks(5);
            Assert.AreEqual(0, f.Count(SimEventKind.Block), "같은 팀은 블록하지 않음");
        }

        [Test]
        public void OverHead_NoBlock()
        {
            var f = Fly(2.3f, 0f, out _);
            f.Ticks(4);
            Assert.AreEqual(0, f.Count(SimEventKind.Block), "몸 높이 1.9 m 위");
        }

        [Test]
        public void ArmHeight_ArmContactWithHandInputs()
        {
            var f = Fly(1.3f, 0.3f, out _);
            f.Ticks(5);
            Assert.AreEqual(1, f.Count(SimEventKind.Block));
            SimEvent block = default, contact = default;
            var hasContact = false;
            foreach (var e in f.Events)
            {
                if (e.Kind == SimEventKind.Block) block = e;
                if (e.Kind == SimEventKind.Contact && e.A == (int)ContactKind.Arm) { contact = e; hasContact = true; }
            }
            Assert.AreEqual(1, block.A, "팔 영역 비트");
            Assert.IsTrue(hasContact, "Arm 접촉");
            var d = f.Ctx.Actions.Details.Contacts[contact.B - 1];
            Assert.IsTrue(d.HasHand);
            Assert.That(d.HandSilhouette, Is.InRange(0f, 1f));
        }
    }
}
