using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Replay;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-14: 스냅샷에서 복원해 끝까지 돌린 결과 = 중단 없는 실행 (60분, 틱 0, 체크포인트 경계 밖, 마지막 틱).</summary>
    [Category("Determinism")]
    public class SnapshotTests
    {
        static void AssertSameAsReference(MatchSim restored)
        {
            var refR = Reference().Result;
            var r = restored.RunToEnd();
            Assert.That(r.EventLogHash, Is.EqualTo(refR.EventLogHash), "이벤트 로그");
            Assert.That(r.FinalStateHash, Is.EqualTo(refR.FinalStateHash), "끝 상태");
            Assert.That(CheckpointLog.FindFirstDivergence(r.Checkpoints, refR.Checkpoints), Is.Null, "체크포인트 해시");
            Assert.That(r.Score(0), Is.EqualTo(refR.Score(0)));
            Assert.That(r.Score(1), Is.EqualTo(refR.Score(1)));
        }

        static MatchSim RestoreFrom(MatchSnapshot snap)
        {
            var refSim = Reference();
            var sim = refSim.Fork();
            sim.Restore(snap, refSim.Log, refSim.Checkpoints);
            return sim;
        }

        [Test]
        public void RestoreAt60Min_EqualsUninterrupted()
        {
            var snap = Reference().NearestSnapshot(36_000);
            Assert.That(snap.Tick, Is.EqualTo(36_000L), "60분 = 체크포인트 경계");
            AssertSameAsReference(RestoreFrom(snap));
        }

        [Test]
        public void RestoreAtTickZero_EqualsUninterrupted()
        {
            var snap = Reference().NearestSnapshot(0);
            Assert.That(snap.Tick, Is.EqualTo(0L));
            AssertSameAsReference(RestoreFrom(snap));
        }

        [Test]
        public void RestoreOffBoundary_AndLastTick_EqualsUninterrupted()
        {
            var end = Reference().Result.EndTick;
            var run = Sim(ReferenceSeed);
            run.RunUntilTick(36_057);
            var mid = run.Capture();
            run.RunUntilTick(end); // 마지막 틱 직전
            var last = run.Capture();
            Assert.That(last.Tick, Is.EqualTo(end));
            AssertSameAsReference(RestoreFrom(mid));
            AssertSameAsReference(RestoreFrom(last));
        }

        [Test]
        public void Restore_ImmediateStateHashEqual_SizeWithinBudget()
        {
            var refSim = Reference();
            var snap = refSim.NearestSnapshot(20_000);
            var sim = refSim.Fork();
            sim.Restore(snap, refSim.Log, refSim.Checkpoints);
            Assert.That(sim.StateHash(), Is.EqualTo(snap.StateHash));
            Assert.That(sim.NextTick, Is.EqualTo(snap.Tick));
            foreach (var s in refSim.Snapshots) Assert.That(s.SizeBytes, Is.LessThanOrEqualTo(16 * 1024), $"체크포인트 {s.Tick} 크기");
        }

        [Test]
        public void Restore_WrongSourceLog_Rejected()
        {
            var refSim = Reference();
            var other = Sim(999UL);
            other.RunUntilTick(25_000);
            var sim = refSim.Fork();
            Assert.Throws<System.InvalidOperationException>(() => sim.Restore(refSim.NearestSnapshot(20_000), other.Log, other.Checkpoints));
        }
    }
}
