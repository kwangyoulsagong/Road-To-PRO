using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Match.Result;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>specs/006 T17 결정론 리뷰 지적 회귀 (B1·B2·R1·R3·R4).</summary>
    [Category("Determinism")]
    public class MatchReviewRegressionTests
    {
        sealed class Frames : IReplayFrameSink
        {
            public readonly Dictionary<long, ulong> H = new Dictionary<long, ulong>();
            public void OnFrame(ReplayFrame f) => H[f.Index] = f.Hash();
        }

        [Test]
        public void B1_B2_ClipReproduces_AddInputAndSubstitution()
        {
            var sim = Sim(71UL, WithCheckpoints(CheckpointMode.All));
            var frames = new Frames();
            sim.Recorder.Sink = frames;
            sim.AddInput(new ExternalInput(600, 10, InputKind.RunInBehind));
            sim.RunUntilTick(4_950);
            sim.RequestSubstitution(0, 2, 12);
            sim.RunUntilTick(9_000);
            Assert.That(Events(sim, SimEventKind.Substitution).Count, Is.EqualTo(1));
            foreach (var (from, to) in new[] { (590L, 700L), (4_905L, 9_000L) })
            {
                var clip = sim.ExtractClip(from, to);
                foreach (var f in clip.Frames) Assert.That(f.Hash(), Is.EqualTo(frames.H[f.Index]), $"[{from},{to}) 프레임 {f.Index}");
            }
            var inputs = sim.Context.Inputs.ToArray();
            Assert.That(inputs.Length, Is.EqualTo(2), "교체 요청도 입력 로그에");
            Assert.That(System.Array.Exists(inputs, i => i.Kind == InputKind.Substitute && i.PlayerId == 2 && (int)i.Value == 12));
        }

        [Test]
        public void B2_ResimAfterInput_KeepsLaterSubstitution()
        {
            var sim = Sim(72UL, WithCheckpoints(CheckpointMode.All));
            sim.RunUntilTick(20_000);
            sim.RequestSubstitution(1, 104, 112);
            sim.RunToEnd();
            var re = sim.Resimulate(new ExternalInput(10_000, 3, InputKind.HoldPosition));
            Assert.That(Events(re, SimEventKind.Substitution).Count, Is.EqualTo(1), "재시뮬에도 교체 요청");
            Assert.That(re.Result.Inputs.Length, Is.EqualTo(2));
        }

        [Test]
        public void R1_SharedStageInstance_ForkRejected()
        {
            var shared = MatchSim.DefaultStages();
            var sim = Sim(73UL, new MatchSimOptions { Stages = () => shared, Checkpoints = CheckpointMode.All });
            sim.RunUntilTick(500);
            Assert.Throws<System.InvalidOperationException>(() => sim.ExtractClip(100, 200));
        }

        [Test]
        public void R3_RestoreFromOwnLog_Rejected()
        {
            var sim = Sim(74UL, WithCheckpoints(CheckpointMode.All));
            sim.RunUntilTick(2_000);
            var h = sim.StateHash();
            Assert.Throws<System.ArgumentException>(() => sim.Restore(sim.Snapshots[3], sim.Log, sim.Checkpoints));
            Assert.That(sim.StateHash(), Is.EqualTo(h), "거부 시 상태 그대로");
            var other = Sim(75UL);
            other.RunUntilTick(2_000);
            var fork = sim.Fork();
            var fh = fork.StateHash();
            Assert.Throws<System.InvalidOperationException>(() => fork.Restore(sim.Snapshots[3], other.Log, other.Checkpoints));
            Assert.That(fork.StateHash(), Is.EqualTo(fh), "다른 경기 로그는 상태를 건드리기 전에 거부");
        }

        sealed class AlwaysScore : IPenaltyJudge
        {
            public PenaltyOutcome Kick(MatchContext ctx, int team, int kickerId, int keeperId) => new PenaltyOutcome { Scored = true };
        }

        [Test]
        public void R4_EndlessShootout_EndsWithinQueue_NoException()
        {
            var sim = Scripted(null, rules: CompetitionRules.Cup(), options: new MatchSimOptions { PenaltyJudge = () => new AlwaysScore() });
            MatchResult r = null;
            Assert.DoesNotThrow(() => r = sim.RunToEnd());
            Assert.That(sim.IsFinished);
            Assert.That(r.ShootoutPlayed);
            Assert.That(r.Winner, Is.InRange(0, 1), "상한 라운드 뒤 동전");
        }
    }
}
