using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Replay;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-15 (FR-62·63): 60분 입력 주입 재시뮬 — 이전 로그·체크포인트 그대로, 이후 달라질 수 있음, 같은 입력 2회 동일.</summary>
    [Category("Determinism")]
    public class KeyMomentResimTests
    {
        const long At = 36_000;

        [Test]
        public void Before60Unchanged_AfterMayDiffer_SameInputTwiceSame()
        {
            var refSim = Reference();
            var input = new ExternalInput(At, 10, InputKind.RunInBehind);
            var a = refSim.Resimulate(input);
            var b = refSim.Resimulate(input);
            var snap = refSim.NearestSnapshot(At);
            // 이전: 일련번호 snap.Seq까지 같은 이벤트
            for (int i = 0; i < snap.Seq; i++)
                Assert.That(a.Log[i].Seq == refSim.Log[i].Seq && SimEvent.CompareKey(a.Log[i], refSim.Log[i]) == 0 && a.Log[i].Kind == refSim.Log[i].Kind, $"#{i}");
            for (int i = 0; i < refSim.Checkpoints.Count && refSim.Checkpoints[i].Tick <= At; i++)
                Assert.That(a.Checkpoints[i], Is.EqualTo(refSim.Checkpoints[i]));
            // 입력이 적용됐다
            var applied = Events(a, SimEventKind.InputApplied);
            Assert.That(applied.Count, Is.EqualTo(1));
            Assert.That(applied[0].Tick, Is.EqualTo(At));
            Assert.That(applied[0].PlayerId, Is.EqualTo(10));
            Assert.That(a.Result.EventLogHash, Is.Not.EqualTo(refSim.Result.EventLogHash), "이후는 달라졌다");
            // 같은 입력 2회 → 같은 결과
            Assert.That(b.Result.EventLogHash, Is.EqualTo(a.Result.EventLogHash));
            Assert.That(b.Result.FinalStateHash, Is.EqualTo(a.Result.FinalStateHash));
            Assert.That(a.Result.Inputs.Length, Is.EqualTo(1));
        }

        [Test]
        public void Resim_FromScratchEqualsFromSnapshot()
        {
            // 보관 스냅샷 없이(처음부터) 다시 돌린 결과도 같다 (결정론)
            var input = new ExternalInput(At + 37, 105, InputKind.ShootNow);
            var withSnap = Reference().Resimulate(input);
            var none = Sim(ReferenceSeed);
            none.AddInput(input);
            var r = none.RunToEnd();
            Assert.That(r.EventLogHash, Is.EqualTo(withSnap.Result.EventLogHash));
        }

        [Test]
        public void InjectAtRestoreTick_KeyMomentsOnlyMode()
        {
            var sim = Sim(ReferenceSeed, WithCheckpoints(CheckpointMode.KeyMomentsOnly));
            sim.KeepSnapshotAt(30_123);
            sim.RunToEnd();
            Assert.That(sim.Snapshots.Count, Is.EqualTo(1));
            Assert.That(sim.Snapshots[0].Tick, Is.EqualTo(30_123L));
            var input = new ExternalInput(30_123, 3, InputKind.HoldPosition);
            var a = sim.Resimulate(input);
            var b = Sim(ReferenceSeed);
            b.AddInput(input);
            Assert.That(a.Result.EventLogHash, Is.EqualTo(b.RunToEnd().EventLogHash), "복원 직후 같은 틱 입력 = 처음부터 입력");
        }

        [Test]
        public void AddInput_PastTick_Rejected()
        {
            var sim = Sim(5UL);
            sim.StepTicks(10);
            Assert.Throws<System.ArgumentException>(() => sim.AddInput(new ExternalInput(5, 1, InputKind.ShootNow)));
        }
    }
}
