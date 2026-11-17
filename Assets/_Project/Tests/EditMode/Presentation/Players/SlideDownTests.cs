using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// specs/082 FR-04 · AC-02·03: 실경기 슬라이딩 태클은 결과와 상관없이 슬라이딩 동작(T03 원인 ④), 058 FR-52 거리 대체 제외(원인 ①),
    /// 빗나간 슬라이딩은 070 다운 1.2 s 동안 일어서지 않는다.
    /// </summary>
    public class SlideDownTests
    {
        static ClipLibrary Lib => AnimTestKit.Lib;
        const float Contact = SyntheticScenes.Start + 2f;
        const int Victim = 0, Tackler = 1;

        /// <summary>피해자 0이 원점에서 +Z를 보고, 태클러 1이 뒤 gap m에 서 있다. Contact에 1이 0에게 태클.</summary>
        static HighlightData Scene(ActionOutcome outcome, bool sliding = true, float gap = 1.0f, bool fromSim = true)
        {
            var d = SyntheticScenes.Empty(1, out _);
            for (int f = 0; f < d.Frames; f++)
            {
                d.Set(f, Victim, 0f, 0f, 0f);
                d.Set(f, Tackler, 0f, -gap, 0f);
            }
            var ball = new Vec3(0f, 0.11f, 0.3f);
            for (int f = 0; f < d.Frames; f++) d.Ball[f] = ball;
            int flags = (fromSim ? AnimEvent.FromSim : 0) | (sliding ? EventCodes.TackleSliding : 0) | (outcome == ActionOutcome.Win ? EventCodes.TackleWon : 0);
            d.Events.Add(new AnimEvent(Contact, 1, SimEventKind.Tackle, Tackler, Victim, BodyPart.RightFoot, outcome, ball, Vec3.Zero, flags));
            return d;
        }

        static AnimPlan Plan(HighlightData d) => AnimPlan.Build(Lib, AnimTestKit.Map, d, NullLogger.Instance);
        static string Clip(AnimPlan p, int k = 0) => Lib.Get(p.Timeline(Tackler).Get(k).Clip).Id;

        [Test]
        public void SlideDown_MatchesActionTuning()
        {
            var reg = TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning", "action.json"));
            Assert.AreEqual(AnimPlan.SlideDown, TuningBundle.Defaults(reg).Get("act.tackle.slideDown"), 1e-6f);
        }

        [TestCase(ActionOutcome.Win)]
        [TestCase(ActionOutcome.Fail)]
        public void SlidingWin_StillSlideClip(ActionOutcome outcome)
        {
            Assert.AreEqual("tackle_slide_r", Clip(Plan(Scene(outcome))), "이긴 슬라이딩도 슬라이딩 동작 (결과별 후보 tackle_win_r보다 슬라이딩 비트가 먼저)");
        }

        [Test]
        public void StandingWin_KeepsWinClip()
        {
            Assert.AreEqual("tackle_win_r", Clip(Plan(Scene(ActionOutcome.Win, sliding: false))), "서서 이긴 태클은 058 결과별 후보 그대로");
        }

        [Test]
        public void SlidingFromSim_NotGated()
        {
            Assert.AreEqual("tackle_slide_r", Clip(Plan(Scene(ActionOutcome.Fail, gap: 5f))), "실경기 슬라이딩은 상대 2 m 밖이어도 대체하지 않는다 (시뮬이 판정 거리 확인)");
            Assert.AreNotEqual("tackle_stand_r", Clip(Plan(Scene(ActionOutcome.Fail, sliding: false, gap: 5f))), "서서 태클은 058 FR-52 대체 그대로");
            Assert.AreEqual("trap_foot_r", Clip(Plan(Scene(ActionOutcome.Fail, sliding: false, gap: 5f))));
        }

        [Test]
        public void HoldsDownPose_UntilSlideDownEnds()
        {
            var d = Scene(ActionOutcome.Fail);
            using var kit = new AnimRigKit(d, Tackler);
            var a = kit.Plan.Timeline(Tackler).Get(0);
            Assert.AreEqual("tackle_slide_r", Lib.Get(a.Clip).Id);
            Assert.GreaterOrEqual(a.End, a.ContactAt + AnimPlan.SlideDown - 1e-4f, "다운 끝까지 액션 유지");
            Assert.AreEqual(AnimPlan.SlideHoldClipTime, a.HoldAt, 1e-6f);
            kit.System.ApplyAt(SyntheticScenes.Start + 0.2f);
            float standHips = kit.Rigs[Tackler].Bone("Hips").position.y;
            for (float t = a.ContactAt; t <= a.ContactAt + AnimPlan.SlideDown; t += 0.05f)
            {
                kit.System.ApplyAt(t);
                float hips = kit.Rigs[Tackler].Bone("Hips").position.y;
                Assert.Less(hips, standHips * 0.7f, $"t = 접촉 + {t - a.ContactAt:0.00} s 골반 {hips:0.00} m (선 자세 {standHips:0.00} m) — 일어서지 않는다");
            }
            // 다운 뒤에는 일어난다 (클립 끝 자세 = 선 자세)
            kit.System.ApplyAt(a.End - 0.02f);
            Assert.Greater(kit.Rigs[Tackler].Bone("Hips").position.y, standHips * 0.8f, "다운 뒤 일어남");
        }

        [Test]
        public void WonSlide_NoForcedDown()
        {
            // 070: 이긴 슬라이딩은 다운 없음(시뮬 SetImmobile은 빗나감만) — 클립 길이 그대로
            var a = Plan(Scene(ActionOutcome.Win)).Timeline(Tackler).Get(0);
            var c = Lib.Get(a.Clip);
            Assert.AreEqual(a.Start + c.Length / a.Rate, a.End, 1e-4f);
        }

        [Test]
        public void DeadBallAfter_DownPlaysToEnd()
        {
            // 슬라이딩 직후 공이 라인 밖(다른 선수의 다음 이벤트·데드볼) → 다운은 끝까지
            var d = Scene(ActionOutcome.Fail);
            d.Events.Add(new AnimEvent(Contact + 0.3f, 2, SimEventKind.ThrowIn, Victim, -1, BodyPart.None, ActionOutcome.Success, new Vec3(0, 0.11f, 3f), Vec3.Zero, AnimEvent.FromSim));
            var a = Plan(d).Timeline(Tackler).Get(0);
            Assert.GreaterOrEqual(a.End, a.ContactAt + AnimPlan.SlideDown - 1e-4f);
        }

        [Test]
        public void NextEventSameEntity_StartsAfterDown()
        {
            // 다운 중 같은 선수의 다음 동작은 다운이 끝난 뒤 (시뮬도 이동 불가)
            var d = Scene(ActionOutcome.Fail);
            d.Events.Add(new AnimEvent(Contact + 0.6f, 2, SimEventKind.BallRecovered, Tackler, -1, BodyPart.RightFoot, ActionOutcome.Success, new Vec3(0, 0.11f, -0.6f), Vec3.Zero, AnimEvent.FromSim));
            var tl = Plan(d).Timeline(Tackler);
            Assert.AreEqual(2, tl.Count);
            var slide = tl.Get(0);
            var next = tl.Get(1);
            Assert.GreaterOrEqual(next.Start + 1e-4f, slide.ContactAt, "앞 접촉 뒤 시작");
            Assert.LessOrEqual(slide.End, next.Start + AnimPlan.ActionFadeOut + 1e-4f, "기존 EndBefore 규칙: 다음 동작이 시작하면 페이드로 넘긴다");
        }
    }
}
