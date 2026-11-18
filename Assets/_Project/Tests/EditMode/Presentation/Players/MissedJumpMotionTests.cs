using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;
using AerialDetail = RoadToPro.MatchEngine.Actions.AerialDetail;
using HeaderOutcome = RoadToPro.MatchEngine.Actions.HeaderOutcome;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// specs/083 FR-01·02 · AC-01·02·04: 놓친 공중볼 경합 점프 (정점 맞춤·리프트·겹침·경계) + 클립 변환.
    /// specs/085 FR-01~03 · AC-01·02·03·07: 정점 = 공이 그 선수 점프 머리 높이 + 머리 여유를 내려오며 지나는 순간 (기록 정점 ± 0.5 s 안).
    /// </summary>
    public class MissedJumpMotionTests
    {
        static ClipLibrary Lib => AnimTestKit.Lib;
        static float StandY => Lib.Get(Lib.IndexOf(HeaderMotion.StandClip)).ContactPoint.Y;
        static float JumpY => Lib.Get(Lib.IndexOf(HeaderMotion.JumpClip)).ContactPoint.Y;

        const float Apex = SyntheticScenes.Start + 2f;
        const long Seq = 40;
        const float OneFrame = 1f / 30f + 1e-4f;
        /// <summary>합성 공 하강 속도(m/s).</summary>
        const float Slope = 4f;

        static float FrameTime(HighlightData d, int f) => d.StartTime + f / HighlightData.FrameRate;

        /// <summary>
        /// 엔티티 0(승자)·1(상대)이 서 있고, 공은 일정 속도로 내려오며 기록 정점 시각 <paramref name="pass"/>(기본 = 기록 정점)에
        /// 키 배율 <paramref name="scale"/> 선수의 목표 높이(점프 머리 + 여유)를 지난다. 놓친 경합 점프 2개.
        /// </summary>
        static HighlightData Scene(float jumpHeight = 0.5f, float apex = Apex, float scale = 1f, float? pass = null)
        {
            var d = SyntheticScenes.Empty(1, out _);
            for (int f = 0; f < d.Frames; f++) { d.Set(f, 0, 0f, 0f, 0f); d.Set(f, 1, 0.8f, 0.3f, 0f); }
            float target = MissedJumpMotion.TargetHeadY(Lib, jumpHeight, scale), tp = pass ?? apex;
            for (int f = 0; f < d.Frames; f++) d.Ball[f] = new Vec3(0.4f, target + Slope * (tp - FrameTime(d, f)), 0.6f);
            d.AerialJumps.Add(new AerialJump(0, Seq, apex - 0.3f, apex, apex + 0.5f, jumpHeight, true));
            d.AerialJumps.Add(new AerialJump(1, Seq, apex - 0.3f, apex, apex + 0.5f, jumpHeight, false));
            return d;
        }

        static void ConstBall(HighlightData d, float y)
        {
            for (int f = 0; f < d.Frames; f++) d.Ball[f] = new Vec3(0.4f, y, 0.6f);
        }

        static Vec3 BallAt(HighlightData d, float t)
        {
            d.FrameAt(t, out var f, out var a);
            return d.Ball[f] + (d.Ball[System.Math.Min(f + 1, d.Frames - 1)] - d.Ball[f]) * a;
        }

        static AnimPlan Plan(HighlightData d, float scale = 1f) => Plan(d, scale, scale);

        static AnimPlan Plan(HighlightData d, float scale0, float scale1)
        {
            var a = new float[HighlightData.Entities];
            for (int i = 0; i < a.Length; i++) a[i] = 1f;
            a[0] = scale0;
            a[1] = scale1;
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
                Assert.AreEqual(Apex, a.ContactAt, OneFrame, "정점 = 공 통과 시각 ±1 기록 프레임");
                Assert.AreEqual(Vec3.Zero, a.RootOffset, "공에 맞추지 않는다 (054 FR-36)");
                float dist = MissedJumpMotion.HeadDistance(Lib.Get(a.Clip).ContactPoint, 1f, d.Pos(e, a.ContactAt), d.YawAt(e, a.ContactAt), a.Lift, BallAt(d, a.ContactAt));
                Assert.Greater(dist, 0.05f, "머리–공 > 머리 접촉 허용치");
                Assert.IsNull(p.MissedJumpSkipReason(Seq, e));
            }
            Assert.IsFalse(p.Timeline(0).SeqOf(JumpIndex(p, 0)) >= 0, "타임라인 번호는 이벤트와 겹치지 않는 음수");
        }

        /// <summary>085 AC-01: 포물선 공이 기록 정점 뒤 목표 높이(점프 머리 + 여유)를 내려오며 지나면 그 순간이 정점, 머리–공 > 허용치.</summary>
        [Test]
        public void PassTime_DescendingThroughHeadPlusClearance_WithinOneFrame()
        {
            var d = Scene();
            float target = MissedJumpMotion.TargetHeadY(Lib, 0.5f, 1f);
            float peakAt = Apex - 0.2f, peakY = target + 1.0f;
            for (int f = 0; f < d.Frames; f++)
            {
                float u = FrameTime(d, f) - peakAt;
                d.Ball[f] = new Vec3(0.4f, peakY - 4.9f * u * u, 0.6f);
            }
            float expected = peakAt + (float)System.Math.Sqrt(1.0 / 4.9); // 하강 쪽 근
            Assert.Less(System.Math.Abs(expected - Apex), MissedJumpMotion.Window, "구간 안");
            var p = Plan(d);
            for (int e = 0; e < 2; e++)
            {
                var a = p.Timeline(e).Get(JumpIndex(p, e));
                Assert.AreEqual(expected, a.ContactAt, OneFrame, $"e{e} 정점 = 하강 통과");
                var ball = BallAt(d, a.ContactAt);
                Assert.GreaterOrEqual(ball.Y, StandY, "공 높이 ≥ 서서 이마 높이 (FR-04)");
                float dist = MissedJumpMotion.HeadDistance(Lib.Get(a.Clip).ContactPoint, 1f, d.Pos(e, a.ContactAt), d.YawAt(e, a.ContactAt), a.Lift, ball);
                Assert.Greater(dist, 0.05f, "머리–공 > 허용치");
            }
        }

        /// <summary>085 경계: 선수별 머리 높이가 다르면 정점도 각자 (키 큰 선수가 먼저 — 공이 내려오며 더 높은 목표를 먼저 지난다).</summary>
        [Test]
        public void PerEntityHeadHeight_SeparateApex()
        {
            var d = Scene();
            float s0 = 0.85f, s1 = 1.15f;
            var p = Plan(d, s0, s1);
            float t0 = p.Timeline(0).Get(JumpIndex(p, 0)).ContactAt, t1 = p.Timeline(1).Get(JumpIndex(p, 1)).ContactAt;
            float target = MissedJumpMotion.TargetHeadY(Lib, 0.5f, 1f);
            Assert.AreEqual(Apex + (target - MissedJumpMotion.TargetHeadY(Lib, 0.5f, s0)) / Slope, t0, OneFrame);
            Assert.AreEqual(Apex + (target - MissedJumpMotion.TargetHeadY(Lib, 0.5f, s1)) / Slope, t1, OneFrame);
            Assert.Less(t1, t0, "키 큰 선수 정점이 먼저");
        }

        /// <summary>085 AC-02 (FR-02, Q3 ①): 구간 안에서 공이 목표 높이 위에 한 번도 없으면 점프 없음 + 사유.</summary>
        [Test]
        public void BallNeverAboveHead_NoJump_Reason()
        {
            var d = Scene();
            ConstBall(d, 1.0f);
            var p = Plan(d);
            for (int e = 0; e < 2; e++)
            {
                Assert.Less(JumpIndex(p, e), 0);
                Assert.AreEqual(MissedJumpMotion.ReasonBallLow, p.MissedJumpSkipReason(Seq, e));
            }
            // 083 리프트 하한 경우(공이 머리 높이에 머묾)도 목표(머리 + 여유) 아래라 점프 없음
            var h = Scene(0.6f);
            ConstBall(h, StandY + 0.6f);
            Assert.AreEqual(MissedJumpMotion.ReasonBallLow, Plan(h).MissedJumpSkipReason(Seq, 0));
        }

        /// <summary>085 경계 (2026-11-18 스펙 보완): 구간 내내 공이 목표 높이 위(내려오지 않음) → 점프 없음 + 사유.</summary>
        [Test]
        public void BallAboveWholeWindow_NoJump_Reason()
        {
            var d = Scene();
            ConstBall(d, 5.0f);
            var p = Plan(d);
            for (int e = 0; e < 2; e++)
            {
                Assert.Less(JumpIndex(p, e), 0);
                Assert.AreEqual(MissedJumpMotion.ReasonBallHigh, p.MissedJumpSkipReason(Seq, e));
            }
        }

        [Test]
        public void Lift_RecordHeightClampedToJumpLiftRange()
        {
            float low = (StandY - JumpY) + HeaderMotion.JumpMin;
            Assert.AreEqual(low, Plan(Scene(0.02f)).Timeline(0).Get(0).Lift, 1e-4f, "낮은 점프 → 하한(점프 0.10 m)");
            Assert.AreEqual(HeaderMotion.LiftMax, Plan(Scene(2.0f)).Timeline(0).Get(0).Lift, 1e-4f, "높은 점프 → 상한");
            float mid = Plan(Scene(0.6f)).Timeline(0).Get(0).Lift;
            Assert.AreEqual(StandY + 0.6f - JumpY, mid, 1e-4f, "기록 높이 그대로 (범위 안)");
            Assert.AreEqual(JumpY + (StandY + 0.6f - JumpY) + MissedJumpMotion.HeadClearance, MissedJumpMotion.TargetHeadY(Lib, 0.6f, 1f), 1e-4f, "목표 = 점프 머리 + 여유");
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
            // 기록 정점이 장면 밖 (구간이 장면과 안 겹침)
            var late = Plan(Scene(apex: SyntheticScenes.Start + SyntheticScenes.Length + 0.6f));
            Assert.AreEqual(MissedJumpMotion.ReasonBoundary, late.MissedJumpSkipReason(Seq, 0));
            // 085: 통과가 장면 시작 전으로 옮겨짐 (구간이 장면 시작에서 잘리고 장면 안에선 공이 이미 아래) → 경계 사유
            var early = Scene(apex: SyntheticScenes.Start + 0.2f, pass: SyntheticScenes.Start - 0.2f);
            Assert.AreEqual(MissedJumpMotion.ReasonBoundary, Plan(early).MissedJumpSkipReason(Seq, 0));
            // 085: 두 번 통과(내려왔다가 바운드로 다시 떠서 또 내려옴) → 첫 번째 하강 통과
            var twice = Scene();
            float tgt = MissedJumpMotion.TargetHeadY(Lib, 0.5f, 1f), first = Apex - 0.3f, second = Apex + 0.3f;
            for (int f = 0; f < twice.Frames; f++)
            {
                float t = FrameTime(twice, f);
                // 첫 하강(first 통과) → Apex에서 바닥(tgt − 1.2) → 바운드로 Apex + 0.15에 tgt + 0.6 → 두 번째 하강(second 통과)
                float y = t <= Apex ? tgt + Slope * (first - t) : t <= Apex + 0.15f ? tgt - 1.2f + 12f * (t - Apex) : tgt + Slope * (second - t);
                twice.Ball[f] = new Vec3(0.4f, y, 0.6f);
            }
            var pt = Plan(twice);
            Assert.AreEqual(first, pt.Timeline(0).Get(JumpIndex(pt, 0)).ContactAt, OneFrame, "첫 하강 통과");
            // 085: 옮긴 정점이 같은 선수의 다음 동작과 겹침 → FR-02 사유 (기록 정점 뒤 통과, 그 전에 수신 시작)
            var moved = Scene(pass: Apex + 0.35f);
            moved.Events.Add(new AnimEvent(Apex + 0.25f, 5, SimEventKind.PassReceived, 0, -1, BodyPart.RightFoot, ActionOutcome.Success,
                new Vec3(0f, 0.11f, 0.5f), Vec3.Zero, AnimEvent.FromSim));
            Assert.AreEqual(MissedJumpMotion.ReasonOverlap, Plan(moved).MissedJumpSkipReason(Seq, 0));
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
                var pe = Plan(Scene(0.5f, scale: s), s);
                var a = pe.Timeline(0).Get(JumpIndex(pe, 0));
                Assert.IsFalse(float.IsNaN(a.Lift) || float.IsNaN(a.ContactAt));
                Assert.AreEqual(Apex, a.ContactAt, OneFrame);
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

        /// <summary>085 FR-01 직접: 구간이 장면과 안 겹치면 경계, 공 프레임 보간으로 통과 시각.</summary>
        [Test]
        public void FindPassTime_Direct()
        {
            var d = Scene();
            float tgt = MissedJumpMotion.TargetHeadY(Lib, 0.5f, 1f);
            Assert.IsTrue(MissedJumpMotion.FindPassTime(d, tgt, Apex, MissedJumpMotion.Window, out var t, out var r));
            Assert.IsNull(r);
            Assert.AreEqual(Apex, t, OneFrame);
            Assert.IsFalse(MissedJumpMotion.FindPassTime(d, tgt, d.EndTime + 2f, MissedJumpMotion.Window, out _, out r));
            Assert.AreEqual(MissedJumpMotion.ReasonBoundary, r);
            Assert.IsFalse(MissedJumpMotion.FindPassTime(d, tgt, d.StartTime - 2f, MissedJumpMotion.Window, out _, out r));
            Assert.AreEqual(MissedJumpMotion.ReasonBoundary, r);
            Assert.IsFalse(MissedJumpMotion.FindPassTime(d, float.NaN, Apex, MissedJumpMotion.Window, out _, out r), "NaN 목표");
        }

        /// <summary>085 Q4: 구간 폭 상수 = 튜닝 파일 값.</summary>
        [Test]
        public void WindowMatchesTuning()
        {
            string data = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning");
            var hl = TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(data, "highlight.json")));
            Assert.AreEqual(MissedJumpMotion.Window, hl.Get("highlight.missedJumpWindow"), 1e-6f);
            Assert.AreEqual(0.5f, MissedJumpMotion.Window, 1e-6f, "071 FR-06 자름 폭과 같음");
        }

        /// <summary>085 AC-07: 같은 장면 동작 계획 2회 같음 (비트 단위).</summary>
        [Test]
        public void PlanTwice_Same()
        {
            var d = Scene();
            var p1 = Plan(d, 0.9f, 1.1f);
            var p2 = Plan(d, 0.9f, 1.1f);
            for (int e = 0; e < 2; e++)
            {
                var a = p1.Timeline(e).Get(JumpIndex(p1, e));
                var b = p2.Timeline(e).Get(JumpIndex(p2, e));
                Assert.AreEqual(a.Start, b.Start);
                Assert.AreEqual(a.ContactAt, b.ContactAt);
                Assert.AreEqual(a.End, b.End);
                Assert.AreEqual(a.Lift, b.Lift);
                Assert.AreEqual(a.Rate, b.Rate);
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
