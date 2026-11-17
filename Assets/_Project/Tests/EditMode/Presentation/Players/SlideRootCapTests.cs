using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Logic.Motion;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>specs/083 FR-03 ⓑ · AC-03·04: 이긴 실경기 슬라이딩의 루트 맞춤 상한 = 슬라이딩 도달 거리 + 클립 접촉점 수평 거리.</summary>
    public class SlideRootCapTests
    {
        static ClipLibrary Lib => AnimTestKit.Lib;
        const float Contact = SyntheticScenes.Start + 2f;
        const int Victim = 0, Tackler = 1;
        static int SlideClip => Lib.IndexOf("tackle_slide_r");

        /// <summary>태클러 1이 원점에서 +Z를 보고 서 있고, 공은 앞 ahead m·옆 side m. Contact에 1이 0에게 태클.</summary>
        static HighlightData Scene(ActionOutcome outcome, float ahead, float side = 0f, bool sliding = true)
        {
            var d = SyntheticScenes.Empty(1, out _);
            for (int f = 0; f < d.Frames; f++)
            {
                d.Set(f, Tackler, 0f, 0f, 0f);
                d.Set(f, Victim, side, ahead + 0.4f, 0f);
            }
            var ball = new Vec3(side, 0.11f, ahead);
            for (int f = 0; f < d.Frames; f++) d.Ball[f] = ball;
            int flags = AnimEvent.FromSim | (sliding ? EventCodes.TackleSliding : 0) | (outcome == ActionOutcome.Win ? EventCodes.TackleWon : 0);
            d.Events.Add(new AnimEvent(Contact, 1, SimEventKind.Tackle, Tackler, Victim, BodyPart.RightFoot, outcome, ball, Vec3.Zero, flags));
            return d;
        }

        static ActionChoice Plan(HighlightData d, out AnimPlan p)
        {
            p = AnimPlan.Build(Lib, AnimTestKit.Map, d, NullLogger.Instance);
            return p.Timeline(Tackler).Get(0);
        }

        /// <summary>클립 접촉점(발)이 공에 오려면 필요한 수평 루트 보정 — ActionClipSelector와 같은 회전 규약.</summary>
        static Vec3 Need(HighlightData d, int clip)
        {
            var cp = Lib.Get(clip).ContactPoint;
            float yaw = d.YawAt(Tackler, Contact), sy = (float)System.Math.Sin(yaw), cy = (float)System.Math.Cos(yaw);
            var world = new Vec3(cp.X * cy + cp.Z * sy, 0, -cp.X * sy + cp.Z * cy);
            var ball = d.Events[0].BallPos;
            var pos = d.Pos(Tackler, Contact);
            return new Vec3(ball.X - world.X - pos.X, 0, ball.Z - world.Z - pos.Z);
        }

        [Test]
        public void Cap_EqualsReachPlusClipContact()
        {
            var cp = Lib.Get(SlideClip).ContactPoint;
            float h = (float)System.Math.Sqrt(cp.X * cp.X + cp.Z * cp.Z);
            Assert.AreEqual(SlideMotion.RootMax + h, SlideMotion.RootCap(cp, 1f), 1e-5f);
            Assert.AreEqual(SlideMotion.RootMax + h * 1.1f, SlideMotion.RootCap(cp, 1.1f), 1e-5f, "접촉점은 키 배율");
        }

        [Test]
        public void TuningMatchesAction()
        {
            string data = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning");
            var hl = TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(data, "highlight.json")));
            var act = TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(data, "action.json")));
            Assert.AreEqual(SlideMotion.RootMax, hl.Get("highlight.slideRootMax"), 1e-6f);
            Assert.AreEqual(act.Get("act.tackle.reach") + act.Get("act.tackle.slideExtra"), SlideMotion.RootMax, 1e-6f, "= 슬라이딩 도달 거리");
        }

        [TestCase(2.2f, 0f)]
        [TestCase(1.5f, 0.8f)]
        [TestCase(0.6f, -0.3f)]
        public void WonSlide_RootFitsFootToBall(float ahead, float side)
        {
            var d = Scene(ActionOutcome.Win, ahead, side);
            var a = Plan(d, out var p);
            Assert.AreEqual(SlideClip, a.Clip);
            var need = Need(d, a.Clip);
            Assert.LessOrEqual(need.Length(), SlideMotion.RootCap(Lib.Get(a.Clip).ContactPoint, 1f), "상한 안 사건");
            Assert.AreEqual(0f, (a.RootOffset - need).Length(), 1e-3f, "루트 보정 = 발이 공에 오는 이동");
            Assert.AreEqual(SlideMotion.RootCap(Lib.Get(a.Clip).ContactPoint, 1f), a.RootMax, 1e-6f);
            var s = new PoseSample();
            p.Evaluate(Tackler, a.ContactAt, ref s);
            Assert.AreEqual(0f, (new Vec3(s.RootOffset.X, 0, s.RootOffset.Z) - need).Length(), 1e-3f, "접촉 순간 화면 루트 보정이 기본 상한(0.3 m)에 잘리지 않음");
        }

        /// <summary>리그로 잰 접촉 프레임 발–공 (054 측정기, 진단 경로): 보통 슬라이딩과 0.3 s 앞 같은 선수 서서 태클 뒤 압축 슬라이딩(083 T09 실경기 사건 20261305 seq 3079 재현).</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WonSlide_FootOnBall_Rig(bool afterStandingTackle)
        {
            var d = Scene(ActionOutcome.Win, 1.6f, 0.4f);
            if (afterStandingTackle)
            {
                var b0 = new Vec3(0.1f, 0.11f, 0.5f);
                d.Events.Insert(0, new AnimEvent(Contact - 0.3f, 0, SimEventKind.Tackle, Tackler, Victim, BodyPart.RightFoot, ActionOutcome.Win, b0, Vec3.Zero,
                    AnimEvent.FromSim | EventCodes.TackleWon));
            }
            var rows = RoadToPro.Tests.EditMode.Presentation.Highlight.LiveContactAudit.Measure(d, null);
            var row = rows.Find(r => r.Seq == 1);
            Assert.IsNotNull(row);
            Assert.AreEqual("tackle_slide_r", row.Clip);
            Assert.LessOrEqual(row.DistanceAtContact, 0.05f, RoadToPro.Tests.EditMode.Presentation.Highlight.LiveContactAudit.Summary(rows));
        }

        [Test]
        public void FailedSlide_And_Standing_Stay10cm()
        {
            var fail = Plan(Scene(ActionOutcome.Fail, 2.0f), out _);
            Assert.AreEqual(Vec3.Zero, fail.RootOffset, "빗나간 슬라이딩은 공에 맞추지 않는다 (054 FR-36)");
            var stand = Plan(Scene(ActionOutcome.Win, 1.0f, sliding: false), out _);
            Assert.LessOrEqual(stand.RootOffset.Length(), MotionDefaults.RootContactMax + 1e-4f, "서서 이긴 태클은 054 FR-02 10 cm 그대로");
            Assert.AreEqual(0f, stand.RootMax);
        }

        [Test]
        public void NextActionAfterWin_RampsFromContact()
        {
            // 이긴 슬라이딩 0.3 s 뒤 같은 선수가 공을 몬다 — 다음 동작 보정이 슬라이딩 접촉 위치를 끌지 않는다 (082 T07b ③)
            var d = Scene(ActionOutcome.Win, 2.0f);
            d.Events.Add(new AnimEvent(Contact + 0.3f, 2, SimEventKind.Dribble, Tackler, -1, BodyPart.RightFoot, ActionOutcome.Success,
                new Vec3(1.2f, 0.11f, 2.6f), new Vec3(2f, 0, 2f), AnimEvent.FromSim));
            var a = Plan(d, out var p);
            var need = Need(d, a.Clip);
            var s = new PoseSample();
            p.Evaluate(Tackler, a.ContactAt, ref s);
            Assert.AreEqual(0f, (new Vec3(s.RootOffset.X, 0, s.RootOffset.Z) - need).Length(), 1e-3f);
        }
    }
}
