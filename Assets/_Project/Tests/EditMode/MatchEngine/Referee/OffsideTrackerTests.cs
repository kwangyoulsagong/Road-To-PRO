using System.Linq;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Referee;
using static RoadToPro.Tests.EditMode.MatchEngine.Referee.RefTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-03·04·05 (FR-04~08, IFAB Law 11): 관여 3유형·리셋·부심 지각.</summary>
    public class OffsideTrackerTests
    {
        const float L = 105f;

        static OffsideTracker Snap(out float[] x, float attacker = 85f, float defLine = 80f)
        {
            var t = new OffsideTracker();
            x = Line(defLine); x[5] = 60f; x[9] = attacker; x[8] = 70f;
            t.Snapshot(P, 0, 0, 5, 60f, x, Zeros(), 1, L, false);
            return t;
        }

        static readonly bool[] All = Enumerable.Repeat(true, 22).ToArray();

        [Test]
        public void Interfere_SkipsInactiveCandidate()
        {
            var t = Snap(out var x);
            var active = Enumerable.Repeat(true, 22).ToArray();
            active[9] = false; // 퇴장 등으로 빠진 후보 — 지난 위치로 관여 판정하지 않는다 (결정론 리뷰 F1)
            Assert.IsFalse(t.TryInterfere(P, new Vec2(85.5f, 34f), Pos(x), active, out _));
        }

        static Vec2[] Pos(float[] x)
        {
            var p = new Vec2[22];
            for (int s = 0; s < 22; s++) p[s] = new Vec2(float.IsNaN(x[s]) ? -100f : x[s], 34f);
            return p;
        }

        [Test]
        public void Law11_Involvement_ThreeTypes()
        {
            // ① 플레이 관여: 오프사이드 위치 동료가 터치
            var t = Snap(out _);
            Assert.IsTrue(t.TryTouch(9, OffsideTouch.Deliberate, out var o1));
            Assert.AreEqual(OffsideInvolvement.Play, o1.Type);
            Assert.Greater(o1.D, 0f);
            // ② 상대 관여: 상대가 공을 플레이할 때 1 m 안
            t = Snap(out var x);
            var pos = Pos(x);
            Assert.IsTrue(t.TryInterfere(P, new Vec2(85.5f, 34f), pos, All, out var o2));
            Assert.AreEqual(OffsideInvolvement.Opponent, o2.Type);
            Assert.AreEqual(9, o2.Slot);
            // ③ 이득 취득: 상대 세이브 뒤 터치
            t = Snap(out _);
            Assert.IsFalse(t.TryTouch(11, OffsideTouch.Save, out _));
            Assert.IsTrue(t.TryTouch(9, OffsideTouch.Deliberate, out var o3));
            Assert.AreEqual(OffsideInvolvement.Gain, o3.Type);
            // ③ 골대 맞고
            t = Snap(out _);
            t.OnRebound();
            Assert.IsTrue(t.TryTouch(9, OffsideTouch.Deliberate, out var o4));
            Assert.AreEqual(OffsideInvolvement.Gain, o4.Type);
        }

        [Test]
        public void Law11_NotInvolved_NoOffence()
        {
            // 오프사이드 위치 선수가 관여 없이 서 있고 온사이드 동료(8)가 받음 → 반칙 아님, 단계 끝
            var t = Snap(out var x);
            Assert.IsFalse(t.TryTouch(8, OffsideTouch.Deliberate, out _));
            Assert.IsFalse(t.Open);
            // 상대 플레이 지점이 멀면 상대 관여 아님
            t = Snap(out x);
            Assert.IsFalse(t.TryInterfere(P, new Vec2(70f, 34f), Pos(x), All, out _));
        }

        [Test]
        public void Law11_GkSightBlock_Off09()
        {
            // 동료(8, x=70) 슛, 오프사이드 위치 9가 슛 선 위·GK(100) 3 m 안 → 상대 관여
            var t = new OffsideTracker();
            var x = Line(80f); x[5] = 60f; x[9] = 97f; x[8] = 70f;
            t.Snapshot(P, 0, 0, 5, 60f, x, Zeros(), 1, L, false);
            var pos = Pos(x);
            Assert.IsTrue(t.TrySightBlock(P, 8, new Vec2(70f, 34f), new Vec2(L, 34f), new Vec2(100f, 34f), pos, All, out var o));
            Assert.AreEqual(OffsideInvolvement.Opponent, o.Type);
            Assert.AreEqual(9, o.Slot);
            // 슛 선에서 3 m 옆이면 아님
            t.Snapshot(P, 0, 0, 5, 60f, x, Zeros(), 1, L, false);
            pos[9] = new Vec2(97f, 37f);
            Assert.IsFalse(t.TrySightBlock(P, 8, new Vec2(70f, 34f), new Vec2(L, 34f), new Vec2(100f, 34f), pos, All, out _));
        }

        [Test]
        public void Law11_DeliberatePlay_Resets()
        {
            var t = Snap(out _);
            Assert.IsFalse(t.TryTouch(12, OffsideTouch.Deliberate, out _), "수비수 의도적 플레이");
            Assert.IsFalse(t.Open, "리셋");
            Assert.IsFalse(t.TryTouch(9, OffsideTouch.Deliberate, out _), "리셋 뒤 받은 공은 반칙 아님");
        }

        [Test]
        public void Law11_DeflectionSave_Keeps()
        {
            var t = Snap(out _);
            Assert.IsFalse(t.TryTouch(12, OffsideTouch.Deflection, out _));
            Assert.IsTrue(t.Open, "굴절은 유지");
            Assert.IsTrue(t.TryTouch(9, OffsideTouch.Deliberate, out _));
            t = Snap(out _);
            t.TryTouch(11, OffsideTouch.Save, out _);
            Assert.IsTrue(t.Open, "세이브는 유지");
            // 굴절 → 의도적 → 굴절: 마지막 의도적 플레이 기준 리셋
            t = Snap(out _);
            t.TryTouch(12, OffsideTouch.Deflection, out _);
            t.TryTouch(13, OffsideTouch.Deliberate, out _);
            Assert.IsFalse(t.Open);
        }

        [Test]
        public void Law11_Rebound_JudgedAtShot_Off07()
        {
            // 슛 순간 스냅샷(9는 x 85 → 오프사이드 위치), 슛 뒤 9가 온사이드로 돌아와도 GK 세이브 리바운드를 받으면 반칙
            var t = new OffsideTracker();
            var x = Line(80f); x[8] = 70f; x[9] = 85f;
            t.Snapshot(P, 0, 0, 8, 70f, x, Zeros(), 1, L, false); // 슛 = 스냅샷
            t.TryTouch(11, OffsideTouch.Save, out _);
            Assert.IsTrue(t.TryTouch(9, OffsideTouch.Deliberate, out var o));
            Assert.AreEqual(OffsideInvolvement.Gain, o.Type);
            Assert.AreEqual(85f - 80f, o.D, 1e-4f, "슛 순간 위치 기준 d");
        }

        [Test]
        public void AssistantPerception_10000_AlignedBetter_FlashLagBias()
        {
            var rng = new SeedRng(1201);
            int errAligned = 0, errOff = 0, falseFlag = 0, missed = 0;
            const int N = 10000;
            for (int i = 0; i < N; i++)
            {
                var d = (i & 1) == 0 ? 0.2f : -0.2f;
                var a = Perception.AssistantPerceive(P, d, 1f, 4f, rng);
                var b = Perception.AssistantPerceive(P, d, 5f, 4f, rng);
                var wrongA = (a > 0f) != (d > 0f);
                var wrongB = (b > 0f) != (d > 0f);
                if (wrongA) errAligned++;
                if (wrongB) errOff++;
                if (wrongA || wrongB) { if (d > 0f) missed++; else falseFlag++; }
            }
            TestContext.WriteLine($"오판율 정렬 {errAligned / (double)N:P1} · 어긋남 {errOff / (double)N:P1} · 깃발 오판 {falseFlag} · 놓침 {missed}");
            Assert.Less(errAligned, errOff, "정렬(1 m) 오판율 < 어긋남(5 m)");
            Assert.Greater(RefTestKit.Z(errOff, N, errAligned, N), 2.58, "p < 0.01");
            Assert.Greater(falseFlag, missed, "오판은 공격수 쪽(flash-lag) — 온사이드를 오프사이드로 더 많이");
            Assert.AreEqual(0.25f + 0.04f, Perception.AssistantSigma(P, 1f), 1e-5f, "정렬 σ ≈ 0.3 m");
        }

        [Test]
        public void LateFlag_Margin_Rule()
        {
            Assert.LessOrEqual(0.4f, P.LateFlagMargin, "근소(|d| ≤ 0.5 m)는 늦은 깃발 대상 (P4)");
            Assert.AreEqual(0.10f, P.TightMargin, 1e-6f, "근소 표시 0.10 m (FR-10)");
        }
    }
}
