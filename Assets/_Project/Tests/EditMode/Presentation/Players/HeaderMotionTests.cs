using System;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>specs/082 FR-01·02·03 ②·③ · AC-01·03: 헤더 점프/서서 선택, 수직 리프트, 헤더 루트 맞춤 상한.</summary>
    public class HeaderMotionTests
    {
        static ClipLibrary Lib => AnimTestKit.Lib;
        static float StandY => Lib.Get(Lib.IndexOf(HeaderMotion.StandClip)).ContactPoint.Y;
        static float JumpY => Lib.Get(Lib.IndexOf(HeaderMotion.JumpClip)).ContactPoint.Y;
        static float ContactFwd(string id) => Lib.Get(Lib.IndexOf(id)).ContactPoint.Z;

        const float Contact = SyntheticScenes.Start + 2f;

        /// <summary>엔티티 0이 +Z를 보고 서 있고, Contact에 머리로 공을 친다. 공 = 클립 이마 앞(fwd) + 옆 side, 높이 ballY.</summary>
        static HighlightData Scene(float ballY, float scale, float side = 0f, SimEventKind kind = SimEventKind.Clearance, BodyPart part = BodyPart.Head,
            ActionOutcome outcome = ActionOutcome.Success, float extraFwd = 0f)
        {
            var d = SyntheticScenes.Empty(1, out _);
            for (int f = 0; f < d.Frames; f++) d.Set(f, 0, 0f, 0f, 0f);
            var ball = new Vec3(side, ballY, ContactFwd(HeaderMotion.StandClip) * scale + extraFwd);
            for (int f = 0; f < d.Frames; f++) d.Ball[f] = ball;
            d.Events.Add(new AnimEvent(Contact, 1, kind, 0, -1, part, outcome, ball, new Vec3(0, 0, 8f), AnimEvent.FromSim));
            return d;
        }

        static AnimPlan Plan(HighlightData d, float scale) =>
            AnimPlan.Build(Lib, AnimTestKit.Map, d, NullLogger.Instance, null, Scales(scale));

        static float[] Scales(float s)
        {
            var a = new float[HighlightData.Entities];
            for (int i = 0; i < a.Length; i++) a[i] = 1f;
            a[0] = s;
            return a;
        }

        static string ClipOf(AnimPlan p) => Lib.Get(p.Timeline(0).Get(0).Clip).Id;

        [Test]
        public void TuningValues_MatchHighlightJson()
        {
            var reg = TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning", "highlight.json"));
            var b = TuningBundle.Defaults(reg);
            Assert.AreEqual(HeaderMotion.JumpMin, b.Get("highlight.headerJumpMin"), 1e-6f);
            Assert.AreEqual(HeaderMotion.LiftMin, b.Get("highlight.headerLiftMin"), 1e-6f);
            Assert.AreEqual(HeaderMotion.LiftMax, b.Get("highlight.headerLiftMax"), 1e-6f);
            Assert.AreEqual(HeaderMotion.RootMax, b.Get("highlight.headerRootMax"), 1e-6f);
        }

        [Test]
        public void RequiredJump_Boundary_0_10()
        {
            Assert.IsFalse(HeaderMotion.UseJump(HeaderMotion.RequiredJump(StandY + 0.09f, StandY, 1f)));
            Assert.IsTrue(HeaderMotion.UseJump(HeaderMotion.RequiredJump(StandY + 0.11f, StandY, 1f)));
            Assert.AreEqual(HeaderMotion.StandClip, ClipOf(Plan(Scene(StandY + 0.09f, 1f), 1f)), "0.09 m → 서서");
            Assert.AreEqual(HeaderMotion.JumpClip, ClipOf(Plan(Scene(StandY + 0.11f, 1f), 1f)), "0.11 m → 점프");
        }

        [Test]
        public void Lift_ClampedToRange()
        {
            Assert.AreEqual(HeaderMotion.LiftMax, HeaderMotion.Lift(JumpY + 1f, JumpY, 1f), 1e-6f);
            Assert.AreEqual(HeaderMotion.LiftMin, HeaderMotion.Lift(StandY - 1f, StandY, 1f), 1e-6f);
            Assert.AreEqual(0.12f, HeaderMotion.Lift(JumpY + 0.12f, JumpY, 1f), 1e-5f);
            Assert.AreEqual(JumpY * 1.1f + 0.05f - JumpY * 1.1f, HeaderMotion.Lift(JumpY * 1.1f + 0.05f, JumpY, 1.1f), 1e-5f, "키 배율");
            // 점프 헤딩: 필요한 점프 0.15 m(서서 이마 + 0.15) → 클립 점프를 낮춰 정확히 맞춘다 (하한 = 점프 0.10 m)
            Assert.AreEqual(StandY + 0.15f - JumpY, HeaderMotion.JumpLift(StandY + 0.15f, JumpY, StandY, 1f), 1e-5f);
            Assert.AreEqual(StandY + 0.10f - JumpY, HeaderMotion.JumpLift(StandY, JumpY, StandY, 1f), 1e-5f, "점프 0.10 m 아래로는 안 낮춘다");
            Assert.AreEqual(HeaderMotion.LiftMax, HeaderMotion.JumpLift(JumpY + 1f, JumpY, StandY, 1f), 1e-6f);
        }

        [Test]
        public void Lift_PeaksAtContact_ZeroOutside()
        {
            Assert.AreEqual(1f, HeaderMotion.LiftWeight(1f, 1.5f, 1.5f), 1e-6f);
            Assert.AreEqual(0f, HeaderMotion.LiftWeight(1f, 1.5f, 1f), 1e-6f);
            Assert.AreEqual(0f, HeaderMotion.LiftWeight(1f, 1.5f, 2f), 1e-6f);
            Assert.AreEqual(0.5f, HeaderMotion.LiftWeight(1f, 1.5f, 1.25f), 1e-6f);
            // 계획: 점프 헤딩 공이 클립보다 0.2 m 높으면 접촉 순간 루트 Y = 0.2, 액션 밖은 0
            var p = Plan(Scene(JumpY + 0.2f, 1f), 1f);
            Assert.AreEqual(HeaderMotion.JumpClip, ClipOf(p));
            var s = new PoseSample();
            p.Evaluate(0, p.Timeline(0).Get(0).ContactAt, ref s);
            Assert.AreEqual(0.2f, s.RootOffset.Y, 1e-3f, "접촉 순간 리프트");
            p.Evaluate(0, SyntheticScenes.Start + 0.2f, ref s);
            Assert.AreEqual(0f, s.RootOffset.Y, 1e-6f, "액션 전");
        }

        [TestCase(150f / 180f)]
        [TestCase(205f / 180f)]
        public void ExtremeHeights_150_205cm(float scale)
        {
            // 머리 높이는 키에 비례: 같은 상대 높이(이마 + 0.05)면 키와 상관없이 서서 헤딩, 리프트 0.05
            var ballY = StandY * scale + 0.05f;
            var p = Plan(Scene(ballY, scale), scale);
            Assert.AreEqual(HeaderMotion.StandClip, ClipOf(p));
            Assert.AreEqual(0.05f, p.Timeline(0).Get(0).Lift, 1e-4f);
            var s = new PoseSample();
            for (float t = SyntheticScenes.Start; t < SyntheticScenes.Start + SyntheticScenes.Length; t += 0.05f)
            {
                p.Evaluate(0, t, ref s);
                Assert.IsFalse(float.IsNaN(s.Root.X) || float.IsNaN(s.Root.Y) || float.IsNaN(s.Root.Z), "NaN");
            }
        }

        /// <summary>082 T07b ③: 0.16 s 뒤 같은 선수의 다음 동작 루트 보정이 헤더 접촉 순간 위치를 끌지 않는다.</summary>
        [Test]
        public void NextAction_DoesNotPullHeaderContact()
        {
            var d = Scene(StandY, 1f, side: 0.5f);
            var ball2 = new Vec3(-0.6f, 0.11f, 0.2f);
            d.Events.Add(new AnimEvent(Contact + 0.16f, 2, SimEventKind.Pass, 0, -1, BodyPart.RightFoot, ActionOutcome.Success, ball2, new Vec3(0, 0, 8f), AnimEvent.FromSim));
            var p = Plan(d, 1f);
            var head = p.Timeline(0).Get(0);
            Assert.AreEqual(HeaderMotion.StandClip, Lib.Get(head.Clip).Id);
            var s = new PoseSample();
            p.Evaluate(0, head.ContactAt, ref s);
            var h = new Vec3(s.RootOffset.X, 0, s.RootOffset.Z);
            Assert.AreEqual(0f, (h - head.RootOffset).Length(), 1e-3f, "접촉 순간 루트 보정 = 헤더 보정");
        }

        [Test]
        public void MissedContest_NoHeaderMotion()
        {
            // 경합 뒤 헤더 없이 끝남 = Header 기록(071 Missed)만 — 동작 없음 (082 경계: 놓친 점프도 이 스펙에서는 안 보임)
            var d = SyntheticScenes.Empty(1, out _);
            for (int f = 0; f < d.Frames; f++) d.Set(f, 0, 0f, 0f, 0f);
            d.Events.Add(new AnimEvent(Contact, 1, SimEventKind.Header, 0, -1, BodyPart.Head, ActionOutcome.Success, new Vec3(0, 2.5f, 0.4f), Vec3.Zero, AnimEvent.FromSim));
            var p = Plan(d, 1f);
            Assert.AreEqual(0, p.Timeline(0).Count);
        }

        [Test]
        public void HeaderRootOffset_UptoRootMax_OthersStay10cm()
        {
            var head = Plan(Scene(StandY, 1f, side: 0.5f), 1f).Timeline(0).Get(0);
            Assert.AreEqual(0.5f, head.RootOffset.Length(), 0.02f, "헤더: 0.5 m 옆 공에 루트를 맞춘다 (상한 1.0)");
            var cap = HeaderMotion.RootCap(Lib.Get(Lib.IndexOf(HeaderMotion.StandClip)).ContactPoint, 1f);
            Assert.AreEqual(cap, head.RootMax, 1e-6f);
            Assert.Greater(cap, HeaderMotion.RootMax, "상한 = headRadius + 이마 앞 거리");
            var far = Plan(Scene(StandY, 1f, side: 2.5f), 1f).Timeline(0).Get(0);
            Assert.AreEqual(cap, far.RootOffset.Length(), 1e-3f, "2.5 m 옆 → 상한으로 자름");
            var pass = Plan(Scene(0.11f, 1f, side: 0.5f, kind: SimEventKind.Pass, part: BodyPart.RightFoot), 1f).Timeline(0).Get(0);
            Assert.LessOrEqual(pass.RootOffset.Length(), 0.10f + 1e-4f, "헤더가 아니면 054 FR-02 10 cm 그대로");
            Assert.AreEqual(0f, pass.Lift);
            // 평가: 접촉 순간 화면 루트 수평 보정 = 0.5 m (기본 상한 0.3 m에 잘리지 않음)
            var p = Plan(Scene(StandY, 1f, side: 0.5f), 1f);
            var s = new PoseSample();
            p.Evaluate(0, p.Timeline(0).Get(0).ContactAt, ref s);
            Assert.AreEqual(0.5f, new Vec3(s.RootOffset.X, 0, s.RootOffset.Z).Length(), 0.02f);
        }
    }
}
