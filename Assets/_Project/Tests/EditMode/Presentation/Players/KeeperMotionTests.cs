using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>specs/080 GK 동작 재생 로직: 이벤트 해석(T02).</summary>
    public class KeeperMotionTests
    {
        static AnimEvent FromSim(SimEventKind kind, int a, int b, ActionOutcome outcome = ActionOutcome.Success)
        {
            var se = SimEvent.Make(kind);
            se.A = a;
            se.B = b;
            return new AnimEvent(1f, 1, kind, 0, -1, BodyPart.Hand, outcome, Vec3.Zero, Vec3.Zero, KeeperMotion.PackFlags(se));
        }

        [Test]
        public void Decode_CatchKindAndBand()
        {
            var i = KeeperMotion.Decode(FromSim(SimEventKind.Catch, (int)KeeperMotionKind.Dive, (int)DiveHeight.High));
            Assert.IsTrue(i.Valid);
            Assert.AreEqual(KeeperMotionKind.Dive, i.Kind);
            Assert.IsTrue(i.HasBand);
            Assert.AreEqual(DiveHeight.High, i.Band);

            var g = KeeperMotion.Decode(FromSim(SimEventKind.Catch, (int)KeeperMotionKind.CatchGround, (int)DiveHeight.Low));
            Assert.AreEqual(KeeperMotionKind.CatchGround, g.Kind);
            Assert.AreEqual(DiveHeight.Low, g.Band);

            var s = KeeperMotion.Decode(FromSim(SimEventKind.Smother, (int)KeeperMotionKind.Smother, 0));
            Assert.AreEqual(KeeperMotionKind.Smother, s.Kind);
            Assert.IsFalse(s.HasBand);
        }

        [Test]
        public void Decode_NegativeDirection()
        {
            for (int dir = -1; dir <= 1; dir++)
            for (int h = 0; h <= 2; h++)
            {
                var i = KeeperMotion.Decode(FromSim(SimEventKind.DiveStart, dir * 10 + h, 0, ActionOutcome.Fail));
                Assert.IsTrue(i.Valid, $"dir {dir} h {h}");
                Assert.AreEqual(KeeperMotionKind.Dive, i.Kind);
                Assert.AreEqual(dir, i.Direction, $"dir {dir} h {h}");
                Assert.AreEqual((DiveHeight)h, i.Band, $"dir {dir} h {h}");
            }
        }

        [Test]
        public void Decode_ParryDangerMasked()
        {
            var i = KeeperMotion.Decode(FromSim(SimEventKind.Parry, (int)KeeperMotionKind.ParrySide, (int)DiveHeight.Mid | EventCodes.ParryDanger));
            Assert.AreEqual(KeeperMotionKind.ParrySide, i.Kind);
            Assert.AreEqual(DiveHeight.Mid, i.Band);
        }

        [Test]
        public void Decode_SyntheticOrOtherKinds_Invalid()
        {
            var synth = new AnimEvent(1f, 1, SimEventKind.Catch, 0, -1, BodyPart.Hand, ActionOutcome.Success, Vec3.Zero, Vec3.Zero);
            Assert.IsFalse(KeeperMotion.Decode(synth).Valid, "합성 장면(FromSim 없음)");
            Assert.IsFalse(KeeperMotion.Decode(FromSim(SimEventKind.Pass, 3, 0)).Valid, "GK 동작 이벤트 아님");
            Assert.IsFalse(KeeperMotion.Decode(FromSim(SimEventKind.Punch, 0, 0)).Valid, "A = None");
        }

        // ---------------------------------------------------------------- T07: 동작 종류로 후보 좁히기 (FR-03·06)

        static string Id(int i) => AnimTestKit.Lib.Get(i).Id;

        static IReadOnlyList<int> Cands(AnimEvent ev, params AnimEvent[] others)
        {
            var all = new List<AnimEvent>(others) { ev };
            return KeeperMotion.Candidates(AnimTestKit.Map, all, ev, AnimTestKit.Map.Candidates(ev.Kind, ev.Outcome));
        }

        [Test]
        public void Candidates_ByKindAndBand()
        {
            foreach (var i in Cands(FromSim(SimEventKind.Catch, (int)KeeperMotionKind.Dive, (int)DiveHeight.High)))
                StringAssert.StartsWith("gk_dive_high_", Id(i));
            var g = Cands(FromSim(SimEventKind.Catch, (int)KeeperMotionKind.CatchGround, (int)DiveHeight.Low));
            CollectionAssert.Contains(g, AnimTestKit.Lib.IndexOf("gk_catch_ground"));
            foreach (var i in Cands(FromSim(SimEventKind.DiveStart, -10 + (int)DiveHeight.Low, 0, ActionOutcome.Fail)))
                StringAssert.StartsWith("gk_dive_low_", Id(i));
            foreach (var i in Cands(FromSim(SimEventKind.Smother, (int)KeeperMotionKind.Smother, 0)))
                Assert.IsTrue(Id(i).StartsWith("gk_smother_", System.StringComparison.Ordinal) || Id(i) == "gk_lunge_low", Id(i));
        }

        [Test]
        public void UnknownKind_FallsBackToEventCandidates()
        {
            var synth = new AnimEvent(1f, 1, SimEventKind.Catch, 0, -1, BodyPart.Hand, ActionOutcome.Success, Vec3.Zero, Vec3.Zero);
            Assert.AreSame(AnimTestKit.Map.Candidates(SimEventKind.Catch, ActionOutcome.Success), Cands(synth), "합성 장면");
            var pass = FromSim(SimEventKind.Pass, 3, 0);
            Assert.AreSame(AnimTestKit.Map.Candidates(SimEventKind.Pass, ActionOutcome.Success), Cands(pass), "GK 아닌 이벤트");
        }

        static AnimEvent HandPass(float t = 1f) =>
            new AnimEvent(t, 5, SimEventKind.Pass, 0, 3, BodyPart.Hand, ActionOutcome.Success, new Vec3(0, 0.11f, 0), new Vec3(0, 0, 8f),
                KeeperMotion.PackFlags(SimEvent.Make(SimEventKind.Pass)));

        static AnimEvent Distribution(DistributionKind k, float t = 1f)
        {
            var se = SimEvent.Make(SimEventKind.Distribution);
            se.A = (int)k;
            return new AnimEvent(t, 6, SimEventKind.Distribution, 0, 3, BodyPart.Hand, ActionOutcome.Success, Vec3.Zero, Vec3.Zero, KeeperMotion.PackFlags(se));
        }

        [Test]
        public void HandPass_PicksRollOrThrowByDistribution()
        {
            var roll = Cands(HandPass(), Distribution(DistributionKind.Roll));
            Assert.AreEqual(1, roll.Count);
            Assert.AreEqual("gk_roll_r", Id(roll[0]));
            var thr = Cands(HandPass(), Distribution(DistributionKind.Throw));
            Assert.AreEqual("gk_throw_r", Id(thr[0]));
            // 다른 시각·다른 선수의 배급은 무시 → 이벤트 후보
            Assert.AreSame(AnimTestKit.Map.Candidates(SimEventKind.Pass, ActionOutcome.Success), Cands(HandPass(), Distribution(DistributionKind.Throw, 3f)));
        }

        [Test]
        public void FootPass_NeverPicksThrowClip()
        {
            var lib = AnimTestKit.Lib;
            var c = AnimTestKit.Map.Candidates(SimEventKind.Pass, ActionOutcome.Success);
            var q = new ActionRequest(0f, 1f, Vec3.Zero, 0f, new Vec3(0.1f, 0.11f, 0.3f), new Vec3(0, 0, 8f), 1f, BodyPart.RightFoot);
            var ch = ActionClipSelector.Select(lib, c, q);
            Assert.IsFalse(lib.Get(ch.Clip).Id.StartsWith("gk_", System.StringComparison.Ordinal), lib.Get(ch.Clip).Id);
        }

        // ---------------------------------------------------------------- T09: 손 IK 이동량 (FR-04·05, AC-03·08)

        static readonly Vec3 ShL = new Vec3(-0.2f, 1.4f, 0f), ShR = new Vec3(0.2f, 1.4f, 0f);
        const float Arm = 0.54f;

        [Test]
        public void Reach_CappedAt30cm()
        {
            var r = KeeperMotion.HandReach(new Vec3(0.8f, 0f, 0f), new Vec3(0.4f, 1.4f, 0.3f), ShL, ShR, Arm, 1f);
            Assert.LessOrEqual(r.Length(), KeeperMotion.ReachMax + 1e-5f);
            Assert.Greater(r.X, 0.25f, "공 쪽으로");
            Assert.AreEqual(Vec3.Zero, KeeperMotion.HandReach(new Vec3(0.2f, 0, 0), Vec3.Zero, ShL, ShR, 0f, 1f), "IK 태그 없음");
        }

        [Test]
        public void Reach_ClampedToShoulderSphere()
        {
            var contact = new Vec3(0.2f + 0.80f, 1.4f, 0f); // 어깨에서 0.80 m (팔 0.54 + 여유 0.30 = 0.84 안)
            var r = KeeperMotion.HandReach(new Vec3(0.25f, 0f, 0f), contact, ShL, ShR, Arm, 1f);
            Assert.LessOrEqual((contact + r - ShR).Length(), Arm + KeeperMotion.HandBallSlack + 1e-4f);
            Assert.Less(r.Length(), 0.25f - 1e-3f, "구 밖으로는 못 간다");
            var big = KeeperMotion.HandReach(new Vec3(0.25f, 0f, 0f), contact, ShL * 1.1f, ShR * 1.1f, Arm, 1.1f);
            Assert.Greater(big.Length(), r.Length(), "키 배율이 크면 더 닿는다");
        }

        [Test]
        public void MissedDive_PushesAway15cm()
        {
            var ball = new Vec3(1.0f, 0.3f, 0.2f);
            foreach (var d in new[] { 0f, 0.05f, 0.12f, 0.2f })
            {
                var contact = ball + new Vec3(-d, 0f, 0f);
                var r = KeeperMotion.MissReach(contact, ball, fingertip: false);
                Assert.GreaterOrEqual((contact + r - ball).Length(), KeeperMotion.MissClear - 1e-4f, $"d {d}");
                Assert.LessOrEqual(r.Length(), KeeperMotion.ReachMax + 1e-5f);
                if (d >= KeeperMotion.MissClear) Assert.AreEqual(Vec3.Zero, r, "이미 떨어짐");
            }
        }

        [Test]
        public void MissedDive_Fingertip_Within10cm()
        {
            var ball = new Vec3(1.0f, 0.3f, 0.2f);
            foreach (var d in new[] { 0.05f, 0.2f, 0.35f })
            {
                var contact = ball + new Vec3(-d, 0.02f, 0f);
                var r = KeeperMotion.MissReach(contact, ball, fingertip: true);
                Assert.LessOrEqual((contact + r - ball).Length(), KeeperMotion.FingertipMax + 1e-4f, $"d {d}");
            }
            var se = SimEvent.Make(SimEventKind.DiveStart);
            se.A = 10 + (int)DiveHeight.Low;
            se.B = EventCodes.Fingertip;
            var ev = new AnimEvent(1f, 1, SimEventKind.DiveStart, 0, -1, BodyPart.Hand, ActionOutcome.Fail, Vec3.Zero, Vec3.Zero, KeeperMotion.PackFlags(se));
            Assert.IsTrue(KeeperMotion.Decode(ev).Fingertip);
            Assert.AreEqual(1, KeeperMotion.Decode(ev).Direction);
        }

        [Test]
        public void Weight_SmoothOver200ms()
        {
            var tr = new HandReachTrack();
            tr.Add(2f, new Vec3(0.2f, 0f, 0f), 3);
            tr.Finish();
            float W(float t) { tr.Sample(t, out _, out _, out var w); return w; }
            Assert.AreEqual(1f, W(2f), 1e-5f);
            Assert.AreEqual(0f, W(2f - KeeperMotion.Blend - 1e-3f), 1e-5f);
            Assert.AreEqual(0f, W(2f + KeeperMotion.Blend + 1e-3f), 1e-5f);
            float prev = 0f;
            for (float t = 1.8f; t <= 2f; t += 0.01f) { Assert.GreaterOrEqual(W(t), prev - 1e-6f); prev = W(t); }
            tr.Sample(2f, out var reach, out var hands, out _);
            Assert.AreEqual(3, hands);
            Assert.AreEqual(0.2f, reach.X, 1e-5f);
        }

        [Test]
        public void ConsecutiveContacts_WeightsDoNotOverlap()
        {
            var tr = new HandReachTrack();
            tr.Add(2.25f, new Vec3(0f, 0.1f, 0f), 2);
            tr.Add(2f, new Vec3(0.2f, 0f, 0f), 3);
            tr.Finish();
            Assert.AreEqual(2f, tr.Get(0).at, "시각 순 정렬");
            tr.Sample(2.125f, out _, out _, out var mid);
            Assert.AreEqual(0f, mid, 1e-5f, "두 접촉 사이 절반에서 0");
            tr.Sample(2.25f, out var r2, out var h2, out var w2);
            Assert.AreEqual(1f, w2, 1e-5f);
            Assert.AreEqual(2, h2);
        }

        [Test]
        public void SameLog_SameChoiceAndReach()
        {
            var d = RoadToPro.Tests.EditMode.Presentation.Players.Motion.MotionSceneKit.Scene("keeper").Data;
            var a = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            var b = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            ulong ha = 1469598103934665603UL, hb = ha;
            a.ReachTrack(0).HashInto(ref ha);
            b.ReachTrack(0).HashInto(ref hb);
            Assert.AreEqual(ha, hb);
            Assert.AreEqual(a.Timeline(0).Get(0).Clip, b.Timeline(0).Get(0).Clip);
        }
    }
}
