using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/008 AC-13 보류 대체: 017 대량 시뮬 전에는 고정 상황 하네스로 개별 비율만 기록한다(판정 없음, 단 극단값 방지 범위만).
    /// 결과: .farm/008-pass-rates.txt (리포트에 옮긴다).
    /// </summary>
    public class PassRateHarnessTests
    {
        static float Rate(Vec2 defender, bool hasDefender, float speed, int trials)
        {
            var f = new ActionFixture(9100);
            int ok = 0;
            for (int i = 0; i < trials; i++)
            {
                f.ClearPitch();
                f.Events.Clear();
                f.Put(6, new Vec2(55f, 34f), new Vec2(-1f, 0f)); // 수신자 (홈)
                if (hasDefender) f.Put(13, defender, new Vec2(-1f, 0f));
                f.Loose(new Vec2(40f, 34f), new Vec2(speed, 0f), 0, BallActionKind.Pass, 5);
                f.RunReception(250);
                foreach (var e in f.Events) if (e.Kind == SimEventKind.PassReceived) { ok++; break; }
            }
            return ok / (float)trials;
        }

        [Test]
        public void FixedSituations_RecordRates()
        {
            var open = Rate(default, false, 14f, 400);
            var nearLane = Rate(new Vec2(48f, 34.8f), true, 14f, 400);
            var inLane = Rate(new Vec2(48f, 34f), true, 14f, 400);
            var fast = Rate(default, false, 24f, 400);
            var text = $"빈 공간 15 m(14 m/s) {open:P1}\n레인 옆 0.8 m 수비 {nearLane:P1}\n레인 중간 수비 {inLane:P1}\n빠른 패스(24 m/s) {fast:P1}\n";
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), ".farm", "008-pass-rates.txt"), text);
            TestContext.WriteLine(text);
            Assert.Greater(open, 0.85f, "빈 공간 패스는 대부분 성공");
            Assert.Less(inLane, open, "레인 중간 수비가 있으면 더 낮다");
            Assert.LessOrEqual(fast, open + 0.02f, "빠른 공은 받기 어렵다");
        }
    }
}
