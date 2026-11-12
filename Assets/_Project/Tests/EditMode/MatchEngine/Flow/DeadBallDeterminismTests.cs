using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Replay;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Flow
{
    /// <summary>specs/058 AC-10 (코어 부분): 골 후 흐름 포함 같은 시드 2회 해시, 골 데드볼 중 스냅샷 복원, 체크포인트 재구성 골 구간 클립.</summary>
    [Category("Determinism")]
    public class DeadBallDeterminismTests
    {
        [Test, Category("Determinism")]
        public void SameSeed_TwoRuns_SameLogAndStateHash()
        {
            var a = Sim(ReferenceSeed);
            var b = Sim(ReferenceSeed);
            a.RunToEnd();
            b.RunToEnd();
            Assert.That(Count(a, SimEventKind.DeadBallPhase), Is.GreaterThan(0), "데드볼 단계 이벤트가 없다");
            Assert.That(a.Log.Hash, Is.EqualTo(b.Log.Hash));
            Assert.That(a.StateHash(), Is.EqualTo(b.StateHash()));
        }

        [Test, Category("Determinism")]
        public void SnapshotDuringGoalDeadBall_RestoreContinues_Identical()
        {
            var refSim = Reference();
            var p = MatchParams.Create(Tuning().Match);
            // 세리머니 단계 안과 복귀 단계 안에 각각 스냅샷 경계가 있는 첫 골 (011로 골 시각이 바뀌어 첫 골 고정 대신 조건으로 고른다)
            var celeb = p.CelebrationTicks;
            var g = refSim.Result.Goals[0];
            long s1 = -1, s2 = -1;
            foreach (var cand in refSim.Result.Goals)
            {
                var a1 = refSim.NearestSnapshot(cand.Tick + celeb - 1).Tick;
                var a2 = refSim.NearestSnapshot(cand.Tick + celeb + 120).Tick;
                if (a1 > cand.Tick && a2 > cand.Tick + celeb) { g = cand; s1 = a1; s2 = a2; break; }
            }
            Assert.That(s1, Is.GreaterThan(0), "조건에 맞는 골 없음");
            foreach (var at in new[] { s1, s2 }) // 세리머니 중, 복귀 중
            {
                var snap = refSim.NearestSnapshot(at);
                Assert.That(snap.Tick, Is.GreaterThan(g.Tick), "골 뒤 스냅샷");
                var fork = refSim.Fork();
                fork.Restore(snap, refSim.Log, refSim.Checkpoints);
                var phase = fork.Context.Flow.DeadBall.Phase;
                Assert.That(phase, Is.Not.EqualTo(DeadBallPhase.None), $"스냅샷 {snap.Tick}: 데드볼 단계 복원");
                var straight = Sim(ReferenceSeed);
                straight.RunUntilTick(g.Tick + 900);
                fork.RunUntilTick(g.Tick + 900);
                Assert.That(fork.StateHash(), Is.EqualTo(straight.StateHash()), $"스냅샷 {snap.Tick} 복원 뒤 상태");
                Assert.That(fork.Log.HashOfPrefix(fork.Log.LastSeq), Is.EqualTo(straight.Log.HashOfPrefix(straight.Log.LastSeq)), $"스냅샷 {snap.Tick} 복원 뒤 로그");
            }
        }

        [Test, Category("Determinism")]
        public void CheckpointRebuild_GoalDeadBallClip_EqualsOriginalFrames()
        {
            var refSim = Reference();
            var g = refSim.Result.Goals[0];
            var clip = refSim.ExtractClip(g.Tick - 20, g.Tick + 400); // 골 → 세리머니 → 복귀 → 준비
            for (int i = 0; i < clip.Frames.Length; i++)
            {
                var f = clip.Frames[i];
                Assert.That(f.Hash(), Is.EqualTo(ReferenceFrames[f.Index]), $"프레임 {f.Index}");
            }
            Assert.That(clip.Events, Has.Some.Matches<SimEvent>(e => e.Kind == SimEventKind.DeadBallPhase), "구간 안 데드볼 단계");
        }
    }
}
