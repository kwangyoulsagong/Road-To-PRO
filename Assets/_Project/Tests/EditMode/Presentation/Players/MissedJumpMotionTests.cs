using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using AerialDetail = RoadToPro.MatchEngine.Actions.AerialDetail;
using HeaderOutcome = RoadToPro.MatchEngine.Actions.HeaderOutcome;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>specs/083 FR-01·02 · AC-01·02·04: 놓친 공중볼 경합 점프 (기록 정점 맞춤·리프트·겹침·경계) + 클립 변환.</summary>
    public class MissedJumpMotionTests
    {
        static ClipLibrary Lib => AnimTestKit.Lib;
        static float StandY => Lib.Get(Lib.IndexOf(HeaderMotion.StandClip)).ContactPoint.Y;
        static float JumpY => Lib.Get(Lib.IndexOf(HeaderMotion.JumpClip)).ContactPoint.Y;

        const float Apex = SyntheticScenes.Start + 2f;
        const long Seq = 40;
        const float OneFrame = 1f / 30f + 1e-4f;

        /// <summary>엔티티 0(승자)·1(상대)이 서 있고 공은 머리 위 3.4 m로 지나간다. 놓친 경합 점프 2개.</summary>
        static HighlightData Scene(float jumpHeight = 0.5f, float apex = Apex, float ballY = 3.4f)
        {
            var d = SyntheticScenes.Empty(1, out _);
            for (int f = 0; f < d.Frames; f++) { d.Set(f, 0, 0f, 0f, 0f); d.Set(f, 1, 0.8f, 0.3f, 0f); }
            for (int f = 0; f < d.Frames; f++) d.Ball[f] = new Vec3(0.4f, ballY, 0.6f);
            d.AerialJumps.Add(new AerialJump(0, Seq, apex - 0.3f, apex, apex + 0.5f, jumpHeight, true));
            d.AerialJumps.Add(new AerialJump(1, Seq, apex - 0.3f, apex, apex + 0.5f, jumpHeight, false));
            return d;
        }

        static AnimPlan Plan(HighlightData d, float scale = 1f)
        {
            var a = new float[HighlightData.Entities];
            for (int i = 0; i < a.Length; i++) a[i] = 1f;
            a[0] = scale;
            a[1] = scale;
            return AnimPlan.Build(Lib, AnimTestKit.Map, d, NullLogger.Instance, null, a);
        }

        static int JumpIndex(AnimPlan p, int e)
        {
            var tl = p.Timeline(e);
            for (int i = 0; i < tl.Count; i++) if (tl.SeqOf(i) == MissedJumpMotion.SeqOf(Seq)) return i;
            return -1;
        }

        [Test]
        public void ApexAligned_WithinOneFrame_HeadClearOfBall()
        {
            var d = Scene();
            var p = Plan(d);
            for (int e = 0; e < 2; e++)
            {
                int i = JumpIndex(p, e);
                Assert.GreaterOrEqual(i, 0, $"e{e} 점프");
                var a = p.Timeline(e).Get(i);
                Assert.AreEqual(HeaderMotion.JumpClip, Lib.Get(a.Clip).Id);
                Assert.AreEqual(Apex, a.ContactAt, OneFrame, "정점 시각 ±1 기록 프레임");
                Assert.AreEqual(Vec3.Zero, a.RootOffset, "공에 맞추지 않는다 (054 FR-36)");
                float dist = MissedJumpMotion.HeadDistance(Lib.Get(a.Clip).ContactPoint, 1f, d.Pos(e, Apex), d.YawAt(e, Apex), a.Lift, d.Ball[0]);
                Assert.Greater(dist, 0.05f, "머리–공 > 머리 접촉 허용치");
                Assert.IsNull(p.MissedJumpSkipReason(Seq, e));
            }
            Assert.IsFalse(p.Timeline(0).SeqOf(JumpIndex(p, 0)) >= 0, "타임라인 번호는 이벤트와 겹치지 않는 음수");
        }

        [Test]
        public void Lift_RecordHeightClampedToJumpLiftRange()
        {
            float low = (StandY - JumpY) + HeaderMotion.JumpMin;
            Assert.AreEqual(low, Plan(Scene(0.02f)).Timeline(0).Get(0).Lift, 1e-4f, "낮은 점프 → 하한(점프 0.10 m)");
            Assert.AreEqual(HeaderMotion.LiftMax, Plan(Scene(2.0f)).Timeline(0).Get(0).Lift, 1e-4f, "높은 점프 → 상한");
            float mid = Plan(Scene(0.6f)).Timeline(0).Get(0).Lift;
            Assert.AreEqual(StandY + 0.6f - JumpY, mid, 1e-4f, "기록 높이 그대로 (범위 안)");
        }

        [Test]
        public void HeadNearBall_LiftLowered()
        {
            // 공이 승자 머리 바로 위(점프 정점 머리 높이) → 리프트를 하한으로
            float headY = StandY + 0.6f;
            var d = Scene(0.6f, ballY: headY);
            var c = Lib.Get(Lib.IndexOf(HeaderMotion.JumpClip)).ContactPoint;
            for (int f = 0; f < d.Frames; f++) d.Ball[f] = new Vec3(c.X, headY, c.Z);
            var a = Plan(d).Timeline(0).Get(0);
            Assert.AreEqual((StandY - JumpY) + HeaderMotion.JumpMin, a.Lift, 1e-4f);
        }

        [Test]
        public void OtherActionBeforeApex_NoJump()
        {
            var d = Scene();
            d.Events.Add(new AnimEvent(Apex - 0.1f, 5, SimEventKind.PassReceived, 0, -1, BodyPart.RightFoot, ActionOutcome.Success,
                new Vec3(0f, 0.11f, 0.5f), Vec3.Zero, AnimEvent.FromSim));
            var p = Plan(d);
            Assert.Less(JumpIndex(p, 0), 0, "정점 전에 시작한 수신 → 점프 없음");
            Assert.AreEqual(MissedJumpMotion.ReasonOverlap, p.MissedJumpSkipReason(Seq, 0));
            Assert.GreaterOrEqual(JumpIndex(p, 1), 0, "상대는 그대로 점프");
        }

        [Test]
        public void ActionAfterApex_JumpCutAtStart()
        {
            var d = Scene();
            d.Events.Add(new AnimEvent(Apex + 0.9f, 5, SimEventKind.PassReceived, 0, -1, BodyPart.RightFoot, ActionOutcome.Success,
                new Vec3(0f, 0.11f, 0.5f), Vec3.Zero, AnimEvent.FromSim));
            var p = Plan(d);
            var tl = p.Timeline(0);
            int ji = JumpIndex(p, 0);
            Assert.GreaterOrEqual(ji, 0);
            float next = float.PositiveInfinity;
            for (int i = 0; i < tl.Count; i++) if (i != ji && tl.Get(i).Start > Apex) next = System.Math.Min(next, tl.Get(i).Start);
            Assert.Less(next, float.PositiveInfinity, "다음 동작");
            Assert.LessOrEqual(tl.Get(ji).End, next + AnimPlan.ActionFadeOut + 1e-4f, "다음 동작 시작에서 끝냄 (054 FR-82)");
            Assert.Greater(tl.Get(ji).End, Apex);
        }

        [Test]
        public void EdgeCases()
        {
            // 클립 변환이 남긴 사유(GK 자기 박스·기록 없음) → 동작 없음, 사유 그대로
            var d = SyntheticScenes.Empty(1, out _);
            for (int f = 0; f < d.Frames; f++) d.Set(f, 0, 0f, 0f, 0f);
            d.AerialJumps.Add(new AerialJump(0, Seq, Apex, Apex, Apex, 0.5f, true, MissedJumpMotion.ReasonKeeperBox));
            d.AerialJumps.Add(new AerialJump(1, Seq, Apex, Apex, Apex, 0f, false, MissedJumpMotion.ReasonNoRecord));
            var p = Plan(d);
            Assert.AreEqual(0, p.Timeline(0).Count);
            Assert.AreEqual(MissedJumpMotion.ReasonKeeperBox, p.MissedJumpSkipReason(Seq, 0));
            Assert.AreEqual(MissedJumpMotion.ReasonNoRecord, p.MissedJumpSkipReason(Seq, 1));
            // 정점이 장면 밖
            var late = Plan(Scene(apex: SyntheticScenes.Start + SyntheticScenes.Length + 0.5f));
            Assert.AreEqual(MissedJumpMotion.ReasonBoundary, late.MissedJumpSkipReason(Seq, 0));
            // 상대 없음(퇴장·교체) → 승자만
            var solo = Scene();
            solo.AerialJumps.RemoveAt(1);
            var ps = Plan(solo);
            Assert.GreaterOrEqual(JumpIndex(ps, 0), 0);
            Assert.AreEqual(0, ps.Timeline(1).Count);
            // 극단 키 150·205 cm: 리프트 범위 안, NaN 없음, 포즈 평가 NaN 없음
            foreach (var h in new[] { 150f, 205f })
            {
                float s = h / 180f;
                var pe = Plan(Scene(0.5f), s);
                var a = pe.Timeline(0).Get(JumpIndex(pe, 0));
                Assert.IsFalse(float.IsNaN(a.Lift) || float.IsNaN(a.ContactAt));
                Assert.GreaterOrEqual(a.Lift, (StandY - JumpY) * s + HeaderMotion.JumpMin - 1e-4f);
                Assert.LessOrEqual(a.Lift, HeaderMotion.LiftMax + 1e-4f);
                var ps2 = new PoseSample();
                for (float t = Apex - 0.6f; t < Apex + 0.8f; t += 1f / 60f)
                {
                    pe.Evaluate(0, t, ref ps2);
                    Assert.IsFalse(float.IsNaN(ps2.Root.X) || float.IsNaN(ps2.RootOffset.Y), $"t {t}");
                }
            }
        }

        // ── 클립 변환 (FR-01) ──

        static ReplayFrame Frame(long index, float gkX)
        {
            var f = new ReplayFrame { Index = index };
            for (int i = 0; i < f.PlayerIds.Length; i++) f.PlayerIds[i] = i + 1;
            Put(f, 3, 60f, 30f);
            Put(f, 15, 61f, 31f);
            Put(f, 5, 58f, 33f);
            Put(f, 11, gkX, 34f); // 원정 GK
            Put(f, ReplayFrame.BallEntity, 60f, 30f);
            return f;
        }

        static void Put(ReplayFrame f, int e, float x, float y)
        {
            int o = e * ReplayFrame.Stride;
            f.Values[o] = x;
            f.Values[o + 1] = y;
        }

        static SimEvent Header(HeaderOutcome o, int b, int player, int other, long tick)
        {
            var e = SimEvent.Make(SimEventKind.Header, 0, player, other);
            e.Seq = Seq;
            e.Tick = tick;
            e.Substep = 0;
            e.A = (int)o;
            e.B = b;
            return e;
        }

        static AerialDetail Rec(int slot, bool won, int first, byte n) =>
            new AerialDetail { Slot = slot, Won = won, GroupFirst = first, GroupCount = n, JumpSubstep = 1060, ApexSubstep = 1080, LandSubstep = 1090, ApexHeight = 0.45f };

        static HighlightData Convert(float gkX, SimEvent ev, params AerialDetail[] recs)
        {
            var frames = new ReplayFrame[61];
            for (int i = 0; i < frames.Length; i++) frames[i] = Frame(300 + i, gkX); // 10.0 ~ 12.0 s
            return ClipConverter.ToHighlight(new ReplayClip(100, 121, frames, new[] { ev }, recs), PitchSpec.Default);
        }

        [Test]
        public void Converter_MissedHeader_ToAerialJumps()
        {
            // 승자 슬롯 3(id 4), 가장 가까운 상대 15(id 16), 다른 참여자 5 — 점프는 승자 + 가장 가까운 상대만
            var d = Convert(90f, Header(HeaderOutcome.Missed, 1, 4, 16, 110), Rec(3, true, 0, 3), Rec(15, false, 0, 3), Rec(5, false, 0, 3));
            Assert.AreEqual(2, d.AerialJumps.Count);
            Assert.AreEqual(3, d.AerialJumps[0].Entity);
            Assert.IsTrue(d.AerialJumps[0].Won);
            Assert.AreEqual(15, d.AerialJumps[1].Entity);
            Assert.AreEqual(10.8f, d.AerialJumps[0].ApexAt, 1e-4f);
            Assert.AreEqual(10.6f, d.AerialJumps[0].JumpAt, 1e-4f);
            Assert.AreEqual(0.45f, d.AerialJumps[0].JumpHeight, 1e-6f);
            Assert.IsNull(d.AerialJumps[0].Skip);
            Assert.AreEqual(Seq, d.AerialJumps[1].Seq);
            // 헤더가 실행된 경합(Shot)·키퍼 결과는 점프 없음 (헤딩 동작·011이 맡는다)
            Assert.AreEqual(0, Convert(90f, Header(HeaderOutcome.Shot, 1, 4, 16, 110), Rec(3, true, 0, 2), Rec(15, false, 0, 2)).AerialJumps.Count);
            Assert.AreEqual(0, Convert(90f, Header(HeaderOutcome.Keeper, 1, 4, 16, 110), Rec(3, true, 0, 2), Rec(15, false, 0, 2)).AerialJumps.Count);
        }

        [Test]
        public void Converter_KeeperInBoxAndNoRecord_Skipped()
        {
            // 원정 GK(슬롯 11, id 12)가 가장 가까운 상대이고 박스 안(x 100 m) → 동작 없음 사유
            var d = Convert(100f, Header(HeaderOutcome.Missed, 1, 4, 12, 110), Rec(3, true, 0, 2), Rec(11, false, 0, 2));
            Assert.AreEqual(2, d.AerialJumps.Count);
            Assert.IsNull(d.AerialJumps[0].Skip);
            Assert.AreEqual(MissedJumpMotion.ReasonKeeperBox, d.AerialJumps[1].Skip);
            // 박스 밖 GK는 점프
            var out_ = Convert(70f, Header(HeaderOutcome.Missed, 1, 4, 12, 110), Rec(3, true, 0, 2), Rec(11, false, 0, 2));
            Assert.IsNull(out_.AerialJumps[1].Skip);
            // 기록이 클립에 없음(넘침·창 밖) → 둘 다 기록 없음 사유
            var none = Convert(90f, Header(HeaderOutcome.Missed, 7, 4, 16, 110));
            Assert.AreEqual(2, none.AerialJumps.Count);
            Assert.AreEqual(MissedJumpMotion.ReasonNoRecord, none.AerialJumps[0].Skip);
            Assert.AreEqual(MissedJumpMotion.ReasonNoRecord, none.AerialJumps[1].Skip);
        }
    }
}
