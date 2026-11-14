using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 FR-02~06 · AC-08: 경기 안 공중볼 경합 예약·판정·헤더·동작 정보·접촉.</summary>
    public class AerialSystemTests
    {
        const int Kicker = 7, Att = 9, Def = 14;

        /// <summary>팀 0 공격 골문 앞 depth m 지점 위로 떨어지는 뜬 공. 공격수·수비수를 착지점 근처에 둔다.</summary>
        static DuelFixture Drop(float depth, bool attacker = true, bool defender = true, float defOffset = 0.6f, ulong seed = 7181UL, bool cross = true)
        {
            var f = new DuelFixture(seed);
            var ctx = f.Ctx;
            var dir = ctx.AttackDir(0);
            var goal = ctx.OpponentGoal(0);
            var land = new Vec2(goal.X - dir * depth, goal.Y);
            f.Put(Kicker, new Vec2(land.X - dir * 25f, 8f), new Vec2(dir, 0f));
            if (attacker) f.Put(Att, land + new Vec2(-dir * 0.4f, 0f), new Vec2(dir, 0f));
            if (defender) f.Put(Def, land + new Vec2(dir * defOffset, 0.2f), new Vec2(-dir, 0f));
            f.Put(11, new Vec2(goal.X - dir * 0.5f, goal.Y + 3f), new Vec2(-dir, 0f));
            ctx.Ball = BallState.InAir(new Vec3(land.X - dir * 1.5f, land.Y, 6f), new Vec3(dir * 3f, 0f, -1f), Vec3.Zero);
            ctx.LastTouchTeam = 0;
            ctx.LastTouchSlot = Kicker;
            ctx.LastKickSlot = Kicker;
            ctx.LastKickKind = (byte)BallActionKind.Pass;
            ctx.LastKickSubstep = ctx.Kinematics.Substep;
            // OnLoftedKick은 위로 올라가는 공에서만 예약 → 직접 예약 상태로 만든다 (킥 직후와 같은 상태)
            var b = ctx.Ball;
            ctx.Ball = BallState.InAir(b.Position, new Vec3(b.Velocity.X, 0f, 1f), Vec3.Zero);
            ctx.Actions.Aerials.OnLoftedKick(ctx, Kicker, BallActionKind.Pass, cross ? (byte)((int)CrossType.Lofted + 1) : (byte)0);
            ctx.Ball = b;
            return f;
        }

        static SimEvent? Find(DuelFixture f, SimEventKind k)
        {
            foreach (var e in f.Events) if (e.Kind == k) return e;
            return null;
        }

        [Test]
        public void Contest_HeaderEvent_MotionInfo_ApexWithinLand05()
        {
            var f = Drop(8f);
            Assert.IsTrue(f.Ctx.Actions.Aerials.IsArmed);
            f.Ticks(20);
            var h = Find(f, SimEventKind.Header);
            Assert.IsTrue(h.HasValue, "헤더");
            var log = f.Ctx.Actions.Details.Aerials;
            Assert.AreEqual(2, log.Count, "참여자 2명 기록");
            var dt = f.Ctx.BallParams.Dt;
            var won = 0;
            for (int i = 0; i < log.Count; i++)
            {
                var d = log[i];
                Assert.LessOrEqual(System.Math.Abs(d.ApexSubstep - d.LandSubstep) * dt, 0.5f + 1e-4f, "정점 = t_land ± 0.5 s");
                Assert.Less(d.JumpSubstep, d.ApexSubstep);
                Assert.That(d.ApexHeight, Is.InRange(0.3f, 0.9f));
                Assert.AreEqual(0, d.GroupFirst);
                Assert.AreEqual(2, d.GroupCount);
                if (d.Won) won++;
            }
            Assert.AreEqual(1, won);
            Assert.AreEqual(1, h.Value.B, "Header B = 첫 기록 + 1");
            Assert.That(h.Value.F0, Is.InRange(0f, 1f));
        }

        [Test]
        public void AerialContact_AllContactFieldsFilled()
        {
            var f = Drop(8f);
            f.Ticks(20);
            SimEvent? c = null;
            foreach (var e in f.Events) if (e.Kind == SimEventKind.Contact && e.A == (int)ContactKind.Aerial) c = e;
            Assert.IsTrue(c.HasValue, "공중볼 접촉 이벤트");
            var d = f.Ctx.Actions.Details.Contacts[c.Value.B - 1];
            Assert.AreEqual(ContactKind.Aerial, d.Kind);
            Assert.That(d.Intensity, Is.InRange(0f, 1f));
            Assert.Greater(d.Height, 0.5f, "접촉 높이");
            Assert.IsTrue(d.DogsoValid, "득점 기회 스냅샷");
            Assert.AreEqual(Att, d.Attacker);
            Assert.AreEqual(Def, d.Defender);
        }

        [Test]
        public void AttackerAlone_InBox_HeaderShot_PartHead()
        {
            var f = Drop(7f, defender: false);
            f.Ticks(20);
            var s = Find(f, SimEventKind.Shot);
            Assert.IsTrue(s.HasValue, "박스 안 공격수 단독 → 헤딩슛");
            Assert.AreEqual(BodyPart.Head, s.Value.Part);
            Assert.AreNotEqual(0, s.Value.A & ActionSystem.ShotFlagHeader);
            Assert.Greater(s.Value.F1, 0f, "헤더 xG");
            Assert.AreEqual((int)HeaderOutcome.Shot, Find(f, SimEventKind.Header).Value.A);
        }

        [Test]
        public void DefenderAlone_Clears_PartHead()
        {
            var f = Drop(8f, attacker: false);
            f.Ticks(20);
            var c = Find(f, SimEventKind.Clearance);
            Assert.IsTrue(c.HasValue);
            Assert.AreEqual(BodyPart.Head, c.Value.Part);
            Assert.AreEqual(Def, f.Ctx.LastTouchSlot);
        }

        [Test]
        public void ZeroParticipants_LooseBall_RngStillConsumed()
        {
            var f = Drop(8f, attacker: false, defender: false);
            f.Put(11, new Vec2(f.Ctx.OpponentGoal(0).X, 60f), new Vec2(1f, 0f));
            f.Ticks(20);
            Assert.IsFalse(Find(f, SimEventKind.Header).HasValue);
            Assert.AreEqual(1, f.Ctx.Actions.Aerials.EmptyContests);
            Assert.AreEqual(0, f.Ctx.Actions.Details.Aerials.Count);
        }

        [Test]
        public void NonCross_OneTeamOnly_NoContest_NormalReception()
        {
            var f = Drop(8f, defender: false, cross: false);
            f.Ticks(25);
            Assert.IsFalse(Find(f, SimEventKind.Header).HasValue, "크로스가 아닌 뜬 공 + 상대 없음 → 헤더 없이 수신");
            Assert.AreEqual(1, f.Ctx.Actions.Aerials.Uncontested);
            Assert.AreEqual(0, f.Ctx.Actions.Details.Aerials.Count);
        }

        [Test]
        public void NonCross_BothTeams_Contest()
        {
            var f = Drop(8f, cross: false);
            f.Ticks(20);
            Assert.IsTrue(Find(f, SimEventKind.Header).HasValue);
        }

        [Test]
        public void Reservation_CancelledOnTouch()
        {
            var f = Drop(8f);
            f.Ctx.LastTouchSlot = 3; // 다른 경로로 누군가 만짐
            f.Tick();
            Assert.IsFalse(f.Ctx.Actions.Aerials.IsArmed);
            Assert.IsFalse(f.Ctx.Actions.Aerials.IsDecided);
        }

        [Test]
        public void DownedPlayer_Excluded()
        {
            var f = Drop(8f);
            f.Ctx.Kinematics.SetImmobile(Att, f.Ctx.Kinematics.Substep + 1000);
            f.Ticks(20);
            var h = Find(f, SimEventKind.Header);
            Assert.IsTrue(h.HasValue);
            Assert.AreEqual(f.Ctx.PlayerAtSlot(Def), h.Value.PlayerId, "다운된 공격수는 참여 못 함");
            Assert.AreEqual(1, f.Ctx.Actions.Details.Aerials.Count);
        }

        [Test]
        public void KeeperInOwnBox_WinsWithHands_NoHeader()
        {
            // 팀 1 GK(11)가 자기 박스 안 착지점에 혼자 있다
            var f = Drop(4f, attacker: false, defender: false);
            var dir = f.Ctx.AttackDir(0);
            var goal = f.Ctx.OpponentGoal(0);
            f.Put(11, new Vec2(goal.X - dir * 4f, goal.Y), new Vec2(-dir, 0f));
            f.Ticks(20);
            Assert.AreEqual(1, f.Ctx.Actions.Aerials.KeeperWins);
            var h = Find(f, SimEventKind.Header);
            Assert.IsTrue(h.HasValue && h.Value.A == (int)HeaderOutcome.Keeper, "GK는 헤더 없이 011 잡기 경로 — Keeper 결과 기록만 (결정론 리뷰 M2)");
            Assert.AreEqual(0, f.Count(SimEventKind.Shot) + f.Count(SimEventKind.Clearance), "헤더 동작 없음");
        }
    }
}
