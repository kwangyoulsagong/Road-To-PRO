using System;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// 결정론 (specs/043 AC-12). 같은 장면을 30 fps·60 fps로 재생하면 같은 재생 시각의 클립 id·클립 시각이 같다(렌더 fps가 선택에 영향 없음).
    /// 2회 재생 포즈 해시 동일(본 로컬 회전 1e-4 양자화). 시각은 누적하지 않고 시작 + n/fps로 계산(FixedPlaybackClock과 같은 방식).
    /// </summary>
    [Category("Determinism")]
    public class AnimDeterminismTests
    {
        static HighlightData Scene(int i) => i % 2 == 0
            ? SyntheticScenes.Kick(900 + i, i % 4 == 0 ? SimEventKind.Pass : SimEventKind.Shot, out _)
            : SyntheticScenes.Run(2f + i, 0.2f * i, 30f * i, 0.3f);

        [Test]
        public void Fps30vs60_SameClipAndTime()
        {
            for (int i = 0; i < 6; i++)
            {
                var d = Scene(i);
                var plan60 = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, RoadToPro.Core.Logging.NullLogger.Instance);
                var plan30 = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, RoadToPro.Core.Logging.NullLogger.Instance);
                var a = new PoseSample();
                var b = new PoseSample();
                var c60 = new FixedPlaybackClock { Start = d.StartTime, Fps = 60f, Rate = 1f };
                var c30 = new FixedPlaybackClock { Start = d.StartTime, Fps = 30f, Rate = 1f };
                for (long n = 0; n < (long)(SyntheticScenes.Length * 30) - 1; n++)
                {
                    c30.Frame = n;
                    c60.Frame = 2 * n;
                    Assert.AreEqual(c30.SimTime, c60.SimTime, "같은 재생 시각(비트 동일)");
                    for (int e = 0; e < HighlightData.Entities; e++)
                    {
                        plan30.Evaluate(e, c30.SimTime, ref a);
                        plan60.Evaluate(e, c60.SimTime, ref b);
                        Assert.AreEqual(a.Count, b.Count);
                        for (int k = 0; k < a.Count; k++)
                        {
                            Assert.AreEqual(a.Clip(k), b.Clip(k), $"장면 {i} e {e} n {n}");
                            Assert.AreEqual(a.Time(k), b.Time(k));
                            Assert.AreEqual(a.Weight(k), b.Weight(k));
                        }
                        Assert.AreEqual(a.Hash(), b.Hash());
                    }
                }
            }
        }

        [Test]
        public void SpeedChange_DoesNotChangePose()
        {
            // 1x·1.5x·3x 재생에서 같은 재생 시각이면 같은 포즈(FR-50~52, AC-15의 로직 쪽)
            var d = Scene(0);
            var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, RoadToPro.Core.Logging.NullLogger.Instance);
            var s = new PoseSample();
            var c1 = new FixedPlaybackClock { Start = d.StartTime, Fps = 60f, Rate = 1f, Frame = 180 };
            var c3 = new FixedPlaybackClock { Start = d.StartTime, Fps = 60f, Rate = 3f, Frame = 60 };
            Assert.AreEqual(c1.SimTime, c3.SimTime);
            plan.Evaluate(0, c1.SimTime, ref s);
            ulong h1 = s.Hash();
            plan.Evaluate(0, c3.SimTime, ref s);
            Assert.AreEqual(h1, s.Hash());
        }

        static ulong PoseHash(PlayerRig rig)
        {
            var h = StableHasher.Create();
            foreach (var t in rig.GetComponentsInChildren<Transform>())
            {
                var q = t.localRotation;
                h.Add((int)Math.Round(q.x * 1e4)); h.Add((int)Math.Round(q.y * 1e4));
                h.Add((int)Math.Round(q.z * 1e4)); h.Add((int)Math.Round(q.w * 1e4));
                var p = t.position;
                h.Add((int)Math.Round(p.x * 1e4)); h.Add((int)Math.Round(p.y * 1e4)); h.Add((int)Math.Round(p.z * 1e4));
            }
            return h.Value;
        }

        static ulong Run(HighlightData d)
        {
            using var kit = new AnimRigKit(d, 0, 1, 22);
            var h = StableHasher.Create();
            for (int k = 0; k < (int)(SyntheticScenes.Length * 60); k += 3)
            {
                kit.System.ApplyAt(SyntheticScenes.Start + k / 60f);
                foreach (var e in new[] { 0, 1, 22 }) h.Add(PoseHash(kit.Rigs[e]));
            }
            return h.Value;
        }

        [Test]
        public void TwoRuns_SamePoseHash()
        {
            var d = Scene(2);
            Assert.AreEqual(Run(d), Run(d));
        }
    }
}
