using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 FR-13·21 · AC-02 (필드 형식): 접촉 상세 계산·핸드볼 입력·상세 기록 연결. 경기 안 발행은 <c>DuelStageTests</c>.</summary>
    public class ContactEventTests
    {
        static ActionParams P => ActionTestKit.P;

        static ActionWorld World()
        {
            var w = ActionTestKit.World();
            for (int s = 0; s < 22; s++) ActionTestKit.Put(w, s, new Vec2(10f + 4f * (s % 11), 5f + 5f * (s / 11)));
            ActionTestKit.Put(w, 9, new Vec2(85f, 34f), new Vec2(6f, 0f));   // 홈 공격수 (+x 공격)
            ActionTestKit.Put(w, 13, new Vec2(86f, 34.5f), new Vec2(-3f, 0f)); // 원정 수비수
            return w;
        }

        static void AssertFilled(in ContactDetail c)
        {
            foreach (var v in new[] { c.BallFirst, c.Intensity, c.FromBehind, c.Lateness, c.StudsUp, c.ControlLikely, c.HandSilhouette })
            {
                Assert.IsFalse(float.IsNaN(v));
                Assert.That(v, Is.InRange(0f, 1f));
            }
            foreach (var v in new[] { c.Height, c.X, c.Y, c.GoalDist, c.GoalAngle, c.HandBallDist, c.HandReactTime })
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
        }

        [Test]
        public void Tackle_TakeOn_Aerial_Shield_OneEachAllFields()
        {
            var w = World();
            var at = new Vec2(85.5f, 34f);
            foreach (var kind in new[] { ContactKind.Tackle, ContactKind.TakeOn, ContactKind.Aerial, ContactKind.Shield })
            {
                var c = ContactBuilder.Make(P, w, kind, 100, 3, 9, 13, at, 1f, 0.3f, 0f, kind == ContactKind.Tackle, kind == ContactKind.Aerial ? 2.1f : 0.3f, 0.8f);
                AssertFilled(in c);
                Assert.AreEqual(kind, c.Kind);
                Assert.IsTrue(c.DogsoValid);
                Assert.IsFalse(c.InBox, "x 85.5 = 골까지 19.5 m → 박스 밖");
            }
        }

        [Test]
        public void Dogso_BoxAndDefendersAndOffPitch()
        {
            var w = World();
            var inBox = ContactBuilder.Make(P, w, ContactKind.Tackle, 1, 0, 9, 13, new Vec2(95f, 34f), 0f, 0f, 0f, false, 0.3f, 1f);
            Assert.IsTrue(inBox.InBox);
            var outBox = ContactBuilder.Make(P, w, ContactKind.Tackle, 1, 0, 9, 13, new Vec2(70f, 34f), 0f, 0f, 0f, false, 0.3f, 1f);
            Assert.IsFalse(outBox.InBox);
            Assert.Greater(outBox.GoalDist, inBox.GoalDist);
            Assert.Greater(inBox.GoalAngle, outBox.GoalAngle);
            var off = ContactBuilder.Make(P, w, ContactKind.Collision, 1, 0, 9, 13, new Vec2(-2f, 70f), 0f, 0f, 0f, false, 0.3f, 1f);
            Assert.IsFalse(off.DogsoValid, "피치 밖 → 스냅샷 비움");
            Assert.AreEqual(0f, off.GoalDist);
            AssertFilled(in off);
            var nan = ContactBuilder.Make(P, w, ContactKind.Collision, 1, 0, 9, 13, new Vec2(float.NaN, 30f), float.NaN, float.NaN, float.NaN, false, float.NaN, float.NaN);
            AssertFilled(in nan);
            Assert.IsFalse(nan.DogsoValid);
        }

        [Test]
        public void FromBehind_GreaterThanFrontal()
        {
            var carrier = new Vec2(60f, 34f);
            var head = new Vec2(1f, 0f);
            Assert.Greater(TackleModel.AngleFromBehind(carrier, head, new Vec2(59f, 34f)), TackleModel.AngleFromBehind(carrier, head, new Vec2(61f, 34f)));
        }

        [Test]
        public void LateTackle_LatenessPositive()
        {
            var w = World();
            var c = ContactBuilder.Make(P, w, ContactKind.Tackle, 1, 0, 9, 13, new Vec2(85f, 34f), 0f, 0.5f, TackleModel.Lateness(P, 0.15f), true, 0.2f, 0f);
            Assert.Greater(c.Lateness, 0f);
            Assert.Greater(c.StudsUp, 0f, "슬라이딩 → 발바닥 노출");
        }

        [Test]
        public void Intensity_FasterAndHeavier_Higher()
        {
            var slow = ContactBuilder.Intensity(P, new Vec2(2f, 0f), Vec2.Zero, 0.5f, 0.5f);
            var fast = ContactBuilder.Intensity(P, new Vec2(7f, 0f), new Vec2(-2f, 0f), 0.5f, 0.5f);
            var heavy = ContactBuilder.Intensity(P, new Vec2(2f, 0f), Vec2.Zero, 1f, 1f);
            Assert.Greater(fast, slow);
            Assert.Greater(heavy, slow);
            Assert.That(ContactBuilder.Intensity(P, new Vec2(30f, 0f), new Vec2(-30f, 0f), 1f, 1f), Is.EqualTo(1f));
        }

        [Test]
        public void HandInput_OnlyInArmZone()
        {
            var w = World();
            var c = ContactBuilder.Make(P, w, ContactKind.Arm, 1, 0, 9, 13, new Vec2(90f, 34f), 0f, 0f, 0f, false, 1.2f, 0.5f);
            Assert.IsTrue(ContactBuilder.AttachHand(P, ref c, 1.2f, 2f, 20f, 3f, false));
            Assert.IsTrue(c.HasHand);
            Assert.AreEqual(0.1f, c.HandReactTime, 1e-5f);
            AssertFilled(in c);
            var low = ContactBuilder.Make(P, w, ContactKind.Arm, 1, 0, 9, 13, new Vec2(90f, 34f), 0f, 0f, 0f, false, 0.2f, 0.5f);
            Assert.IsFalse(ContactBuilder.AttachHand(P, ref low, 0.2f, 2f, 20f, 3f, false), "발 높이 공은 팔 접촉 아님");
            Assert.IsFalse(low.HasHand);
        }

        [Test]
        public void Log_Bind_HashChangesOnEdit_SnapshotRoundTrip()
        {
            var w = World();
            var log = new ActionDetailLog();
            for (int i = 0; i < 40; i++)
            {
                var c = ContactBuilder.Make(P, w, (ContactKind)(i % 5), i, i % 10, 9, 13, new Vec2(80f + i * 0.1f, 34f), 0.5f, 0.2f, 0f, false, 0.3f, 0.5f);
                var idx = log.Contacts.Add(in c);
                log.Contacts.Bind(idx, 1000 + i);
            }
            Assert.Throws<System.InvalidOperationException>(() => log.Contacts.Bind(3, 9));
            var h0 = Hash(log);
            var wr = new StateWriter();
            log.Write(wr);
            var b = new ActionDetailLog();
            b.Read(new StateReader(wr.ToArray()));
            Assert.AreEqual(h0, Hash(b), "스냅샷 왕복");
            Assert.IsFalse(b.Contacts.IsAvailable(0), "창 이전은 복원본에서 읽을 수 없음");
            Assert.IsTrue(b.Contacts.IsAvailable(39));
            log.Contacts.TamperForTest(39, 0.99f);
            Assert.AreNotEqual(h0, Hash(log), "창 안 기록 변경 → 해시 변화");
        }

        static ulong Hash(ActionDetailLog log)
        {
            var h = StableHasher.Create();
            log.AddTo(ref h);
            return h.Value;
        }
    }
}
