using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/074 배급 실경기 연결 (FR-10~14, AC-02 실경로): 010 파이프라인 90분 4경기를 공유한다.</summary>
    public class DistributionLiveTests
    {
        const int Matches = 4;
        static MatchSim[] _sims;

        public static MatchSim[] Played()
        {
            if (_sims != null) return _sims;
            ShapeTestKit.Setup(seed: 1UL);
            var sims = new MatchSim[Matches];
            System.Threading.Tasks.Parallel.For(0, Matches, m =>
            {
                var sim = new MatchSim(ShapeTestKit.Setup(seed: 7400UL + (ulong)m), ShapeTestKit.TuningWithShape());
                sim.RunToEnd();
                sims[m] = sim;
            });
            return _sims = sims;
        }

        static long Abs(in SimEvent e) => e.Tick * 10 + (e.Substep < 0 ? 9 : e.Substep);

        [Test]
        public void LiveMatch_KeeperUsesThrowAndLong()
        {
            var kinds = new int[DistributionModel.Kinds];
            int total = 0, paired = 0;
            foreach (var sim in Played())
            {
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    var e = log[i];
                    if (e.Kind != SimEventKind.Distribution) continue;
                    total++;
                    kinds[e.A]++;
                    if (i > 0 && log[i - 1].Kind == SimEventKind.Pass && log[i - 1].PlayerId == e.PlayerId && Abs(log[i - 1]) == Abs(in e)) paired++;
                }
            }
            TestContext.WriteLine($"배급 {total}: 굴리기 {kinds[0]} · 던지기 {kinds[1]} · 짧은 킥 {kinds[2]} · 롱 골킥 {kinds[3]} · 펀트 {kinds[4]} · 드롭킥 {kinds[5]}");
            Assert.AreEqual(total, paired, "배급 이벤트는 같은 서브스텝 GK 패스 바로 뒤");
            Assert.Greater(kinds[(int)DistributionKind.Throw], 0);
            Assert.Greater(kinds[(int)DistributionKind.LongKick] + kinds[(int)DistributionKind.Punt] + kinds[(int)DistributionKind.DropKick], 0);
            Assert.Greater(kinds[(int)DistributionKind.ShortKick], 0);
        }

        [Test]
        public void ThrowRelease_NoHandball_HoldingKeeper_StaysInBox()
        {
            int hands = 0;
            foreach (var sim in Played())
            {
                var log = sim.Log;
                var len = sim.Context.Pitch.LengthM;
                for (int i = 0; i < log.Count; i++)
                {
                    var e = log[i];
                    if (e.Kind != SimEventKind.Distribution || e.B < 0) continue;
                    hands++;
                    var x = e.X * len;
                    var depth = System.Math.Min(x, len - x); // 후반에 진영이 바뀌므로 가까운 골라인 기준
                    Assert.LessOrEqual(depth, 16.5f + 0.5f, $"틱 {e.Tick}: 손 배급이 박스 밖 ({depth:F1} m)");
                    for (int k = i + 1; k < log.Count && log[k].Tick <= e.Tick + 10; k++)
                        Assert.IsFalse(log[k].Kind == SimEventKind.HandballOffence && log[k].PlayerId == e.PlayerId, $"틱 {e.Tick}: GK 손 배급 뒤 핸드볼");
                }
            }
            Assert.Greater(hands, 0, "손 배급 0");
        }

        [Test]
        public void LongKick_GoesThroughAerialSystem()
        {
            int longs = 0, contested = 0;
            foreach (var sim in Played())
            {
                var log = sim.Log;
                for (int i = 0; i < log.Count; i++)
                {
                    var e = log[i];
                    if (e.Kind != SimEventKind.Distribution || !DistributionModel.IsLong((DistributionKind)e.A)) continue;
                    longs++;
                    for (int k = i + 1; k < log.Count && log[k].Tick <= e.Tick + 50; k++)
                    {
                        var x = log[k].Kind;
                        if (x == SimEventKind.Header) { contested++; break; }
                        if (x == SimEventKind.Pass || x == SimEventKind.Shot || x == SimEventKind.Clearance) break;
                    }
                }
            }
            TestContext.WriteLine($"롱 배급 {longs} · 헤더 경합 {contested}");
            Assert.Greater(contested, 0);
        }
    }
}
