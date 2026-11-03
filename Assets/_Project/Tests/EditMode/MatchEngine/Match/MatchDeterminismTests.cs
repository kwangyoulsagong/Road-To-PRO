using System.Threading.Tasks;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Replay;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-12 같은 설정 2회 동일, AC-13 실행 단위·동시 실행 무관, AC-22 보관 모드 무관.</summary>
    [Category("Determinism")]
    public class MatchDeterminismTests
    {
        static MatchSim _fresh;

        /// <summary>참조 경기와 같은 설정을 보관 모드 None으로 다시 돌린 것.</summary>
        static MatchSim Fresh()
        {
            if (_fresh != null) return _fresh;
            var s = Sim(ReferenceSeed, WithCheckpoints(CheckpointMode.None));
            s.RunToEnd();
            return _fresh = s;
        }

        [Test]
        public void SameSetup_TwoRuns_SameHashes()
        {
            var a = Reference().Result;
            var b = Fresh().Result;
            Assert.That(b.EventLogHash, Is.EqualTo(a.EventLogHash));
            Assert.That(b.FinalStateHash, Is.EqualTo(a.FinalStateHash));
            Assert.That(b.Checkpoints.Length, Is.EqualTo(a.Checkpoints.Length));
            Assert.That(CheckpointLog.FindFirstDivergence(a.Checkpoints, b.Checkpoints), Is.Null);
            Assert.That(a.Checkpoints.Length, Is.GreaterThanOrEqualTo(540), "90분 이상 = 10초 체크포인트 540개 이상");
        }

        [Test]
        public void CheckpointModeNone_EqualsAll()
        {
            // AC-22: 참조(All)와 None 결과의 이벤트 해시·체크포인트 해시 목록이 같다
            var all = Reference();
            var none = Fresh();
            Assert.That(all.Snapshots.Count, Is.GreaterThan(500));
            Assert.That(none.Snapshots.Count, Is.EqualTo(0));
            Assert.That(none.Log.Hash, Is.EqualTo(all.Log.Hash));
            for (int i = 0; i < all.Checkpoints.Count; i++) Assert.That(none.Checkpoints[i], Is.EqualTo(all.Checkpoints[i]));
        }

        [Test]
        public void StepByStep_EqualsRunInOneCall()
        {
            var a = Sim(ReferenceSeed);
            var b = Sim(ReferenceSeed);
            for (int i = 0; i < 6000; i++) a.StepTick();
            b.RunUntilTick(6000);
            Assert.That(a.Log.Hash, Is.EqualTo(b.Log.Hash));
            Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()));
            // 끝까지 한 번에 실행한 참조와도 같은 지점에서 같다
            Assert.That(a.Checkpoints[59], Is.EqualTo(Reference().Checkpoints[59]));
        }

        [Test]
        public void EightConcurrent_EqualsSequential()
        {
            const int n = 8;
            const long ticks = 3000;
            var seq = new ulong[n];
            for (int i = 0; i < n; i++)
            {
                var s = Sim(1000UL + (ulong)i);
                s.RunUntilTick(ticks);
                seq[i] = s.StateHash() ^ s.Log.Hash;
            }
            var par = new ulong[n];
            var tasks = new Task[n];
            Tuning(); // 공유 튜닝을 먼저 만든다 (읽기 전용으로만 공유)
            for (int i = 0; i < n; i++)
            {
                var k = i;
                tasks[i] = Task.Run(() =>
                {
                    var s = Sim(1000UL + (ulong)k);
                    s.RunUntilTick(ticks);
                    par[k] = s.StateHash() ^ s.Log.Hash;
                });
            }
            Task.WaitAll(tasks);
            Assert.That(par, Is.EqualTo(seq));
        }
    }
}
