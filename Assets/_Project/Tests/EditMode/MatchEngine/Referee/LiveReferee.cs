using System.Collections.Generic;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.Referee;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 실경기 픽스처: 전술 경기 12경기(시드 7400+)를 한 번만 병렬로 돌려 공유한다 (테스트 시간 예산).</summary>
    static class LiveReferee
    {
        public const int Matches = 12;
        public const ulong FirstSeed = 7400UL;
        static MatchSim[] _sims;

        public static MatchSim[] Played()
        {
            if (_sims != null) return _sims;
            ShapeTestKit.Setup(seed: 1UL);
            var sims = new MatchSim[Matches];
            System.Threading.Tasks.Parallel.For(0, Matches, m =>
            {
                var sim = new MatchSim(ShapeTestKit.Setup(seed: FirstSeed + (ulong)m), ShapeTestKit.TuningWithShape());
                sim.RunToEnd();
                sims[m] = sim;
            });
            _sims = sims;
            return sims;
        }

        public static RefereeStage Stage(MatchSim sim) => (RefereeStage)sim.Stage(StageId.Referee);

        public static int Count(SimEventLog log, SimEventKind k)
        {
            var n = 0;
            for (int i = 0; i < log.Count; i++) if (log[i].Kind == k) n++;
            return n;
        }

        public static IEnumerable<int> Indices(SimEventLog log, SimEventKind k)
        {
            for (int i = 0; i < log.Count; i++) if (log[i].Kind == k) yield return i;
        }

        /// <summary>판정 이벤트(400번대 + 417~423, 카드·VAR)만의 해시.</summary>
        public static ulong RefereeHash(SimEventLog log)
        {
            var h = RoadToPro.Core.Hashing.StableHasher.Create();
            for (int i = 0; i < log.Count; i++)
            {
                ref readonly var e = ref log[i];
                var k = (int)e.Kind;
                if (k < 400 || k > 499) continue;
                h.Add(k); h.Add(e.Tick); h.Add(e.PlayerId); h.Add(e.OtherId); h.Add(e.A); h.Add(e.B); h.Add(e.F0); h.Add(e.F1);
            }
            return h.Value;
        }
    }
}
