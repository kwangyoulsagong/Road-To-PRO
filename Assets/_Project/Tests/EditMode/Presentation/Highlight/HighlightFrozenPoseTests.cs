using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Tests.EditMode.Presentation.Players;
using RoadToPro.Verification.Bench;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>실제 경기 장면에서 정지 포즈가 시각만으로 정해지고 선수가 서 있다 (specs/041 FR-05·AC-12 전제).</summary>
    public class HighlightFrozenPoseTests
    {
        const ulong Seed = 20261109UL;

        [Test]
        public void ApplyAt_GoalMomentTwice_PoseIdenticalAndUpright()
        {
            var work = new MatchBenchWorkloads(Path.Combine(Directory.GetCurrentDirectory(), "Data"));
            var sim = new MatchSim(MatchBenchWorkloads.Setup(Seed), work.Tuning, new MatchSimOptions { Checkpoints = CheckpointMode.All });
            sim.RunToEnd();
            var g = sim.Result.Goals[0];
            double t = g.Tick * 0.1d;
            var clip = sim.ExtractClip(g.Tick - 80, g.Tick + 31);
            var d = ClipConverter.ToHighlight(clip, PitchSpec.Default);
            var entities = Enumerable.Range(0, HighlightData.Entities).ToArray();

            using (var kit = new AnimRigKit(d, entities))
            {
                float at = d.Events.First(e => e.Kind == SimEventKind.Goal).Time;
                kit.System.ApplyAt(at - 0.05f);
                kit.System.ApplyAt(at);
                var first = entities.Select(e => kit.Rigs[e].Bone("Head").position).ToArray();
                kit.System.ApplyAt(at);
                var problems = new System.Text.StringBuilder();
                var pose = new PoseSample();
                foreach (var e in entities)
                {
                    var head = kit.Rigs[e].Bone("Head").position;
                    var hips = kit.Rigs[e].Bone("Hips").position;
                    float moved = (head - first[e]).magnitude;
                    kit.Plan.Evaluate(e, at, ref pose);
                    // 011 다이빙 중인 GK는 누운 자세가 정상 (008 연결 뒤 이 시드 첫 골에서 GK가 다이빙 — 2026-11-13). 정지 포즈 동일성은 그대로 검사
                    var diving = IsDive(pose);
                    if (moved > 0.001f || (!diving && head.y < hips.y))
                    {
                        problems.AppendLine($"e{e} moved {moved:0.000} headY {head.y:0.00} hipsY {hips.y:0.00} roll {pose.Roll:0.00} pitch {pose.Pitch:0.00} twist {pose.Twist:0.00} yaw {pose.Yaw:0.00} clips {Clips(pose)}");
                    }
                }
                Assert.AreEqual("", problems.ToString());
            }
        }

        static bool IsDive(PoseSample s)
        {
            for (int i = 0; i < s.Count; i++)
                if (s.Weight(i) > 0.5f && AnimTestKit.Lib.Get(s.Clip(i)).Id.StartsWith("gk_dive", StringComparison.Ordinal)) return true;
            return false;
        }

        static string Clips(PoseSample s)
        {
            var b = new System.Text.StringBuilder();
            for (int i = 0; i < s.Count; i++) b.Append($"[{AnimTestKit.Lib.Get(s.Clip(i)).Id} {s.Weight(i):0.00} t{s.Time(i):0.00}]");
            return b.ToString();
        }
    }
}
