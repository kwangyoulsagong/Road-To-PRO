using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 AC-10 대체 하네스 (017 전): 고정 상황별 태클 성공률을 리포트에 기록한다. 판정은 017에서.</summary>
    public class DuelRateHarnessTests
    {
        static double Rate(bool sliding, bool fromBehind) => Rate(sliding, fromBehind, out _);

        /// <summary>판정 성공률 (표본)과 판정 확률 평균(F0).</summary>
        static double Rate(bool sliding, bool fromBehind, out double meanProb)
        {
            double sp = 0;
            var f = new DuelFixture(sliding ? 7501UL : 7500UL);
            int n = 0, won = 0;
            for (int i = 0; i < 300; i++)
            {
                f.ClearPitch();
                f.Events.Clear();
                f.Put(9, new Vec2(50f, 34f), new Vec2(1f, 0f));
                f.Put(13, fromBehind ? new Vec2(49.2f, 34.3f) : new Vec2(50.9f, 34.3f), new Vec2(fromBehind ? 1f : -1f, 0f));
                f.Own(9);
                f.Ctx.Kinematics.SetImmobile(13, 0); // 이전 다운 무시 (과거 끝 시각은 무시됨)
                f.Duels.ScheduleTackle(f.Ctx, 13, 9, sliding);
                f.Ticks(4);
                foreach (var e in f.Events)
                    if (e.Kind == SimEventKind.Tackle) { n++; sp += e.F0; if ((e.A & EventCodes.TackleWon) != 0) won++; }
                f.Ticks(15); // 다운 풀림
            }
            meanProb = n > 0 ? sp / n : -1;
            return n > 0 ? won / (double)n : -1;
        }

        [Test]
        public void FixedSituations_TackleWinRates_Reported()
        {
            var front = Rate(false, false, out var pf);
            var behind = Rate(false, true, out var pb);
            var slide = Rate(true, false, out var ps);
            TestContext.WriteLine($"성공률(판정 확률 평균): 스탠딩 정면 {front:P0}({pf:P0}), 스탠딩 뒤 {behind:P0}({pb:P0}), 슬라이딩 정면 {slide:P0}({ps:P0}) (평균 능력치, 정지 캐리어)");
            Assert.That(front, Is.InRange(0.0, 1.0));
            Assert.Greater(pf, pb, "뒤에서 들어간 태클이 더 어렵다 (FR-10)");
            Assert.Greater(pf, ps, "슬라이딩 logit −0.3 (FR-11)");
        }
    }
}
