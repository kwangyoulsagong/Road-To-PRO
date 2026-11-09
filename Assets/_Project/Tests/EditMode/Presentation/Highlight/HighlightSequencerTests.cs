using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>재생 상태 기계 (specs/041 AC-01·07·09·21, FR-21·54·56·57·5A).</summary>
    public class HighlightSequencerTests
    {
        const double Snapshot = 1d / 30d;
        const double F = SequencerRig.Frame;

        static HighlightScene[] Three() => new[]
        {
            new HighlightScene(10, 100d, 103d),
            new HighlightScene(20, 200d, 203d),
            new HighlightScene(30, 300d, 303d),
        };

        [Test]
        public void Tick_ThreeScenes_PlaysInOrderWithinOneSnapshot()
        {
            var r = new SequencerRig();
            var scenes = Three();
            r.StartLoaded(scenes);
            var first = new Dictionary<int, double>();
            var last = new Dictionary<int, double>();
            var order = new List<int>();

            for (int i = 0; i < 100000 && r.Seq.State != SequencerState.Results; i++)
            {
                if (r.Seq.State == SequencerState.Playing)
                {
                    int id = scenes[r.Seq.SceneIndex].Id;
                    if (!first.ContainsKey(id)) { first[id] = r.Seq.Clock.Position; order.Add(id); }
                    last[id] = r.Seq.Clock.Position;
                }
                r.Seq.Tick(F);
            }

            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, order);
            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, r.Camera.Begun);
            foreach (var s in scenes)
            {
                Assert.AreEqual(s.Start, first[s.Id], Snapshot);
                Assert.AreEqual(s.End, last[s.Id], Snapshot);
            }
        }

        [Test]
        public void Start_NoScenes_NeverRequestsLoad()
        {
            var r = new SequencerRig();
            r.Seq.Start(new HighlightScene[0]);
            Assert.AreEqual(0, r.Seq.LoadRequests);
            Assert.AreEqual(SequencerState.Results, r.Seq.State);
            Assert.IsFalse(r.Seq.Wants3D);
        }

        [Test]
        public void Tick_KeyMoment_StopsOnceAndContinuesWithNewList()
        {
            var r = new SequencerRig();
            r.StartLoaded(new[]
            {
                new HighlightScene(1, 100d, 110d, isKeyMoment: true, keyMomentTime: 104d),
                new HighlightScene(2, 200d, 203d),
                new HighlightScene(3, 300d, 303d),
            });

            r.RunUntil(() => r.Seq.State == SequencerState.KeyMomentWait);
            Assert.AreEqual(104d, r.Seq.Clock.Position, F * 1.5d, "지정 시각 ±1 렌더 프레임");
            r.Run(30);
            Assert.AreEqual(1, r.Moments.Requests.Count, "요청은 1번");
            Assert.AreEqual(1, r.Moments.Requests[0].id);
            Assert.IsFalse(r.Moments.Requests[0].textOnly);

            r.Seq.ResolveKeyMoment(new[] { new HighlightScene(9, 150d, 153d) }, new[] { new PlaybackCue[0] });
            r.RunUntil(() => r.Camera.Begun.Count == 2);
            Assert.AreEqual(9, r.Camera.Begun[1], "바뀐 목록의 다음 장면");
            Assert.AreEqual(2, r.Seq.SceneCount);
        }

        [Test]
        public void ResolveKeyMoment_Unchanged_ResumesSameScene()
        {
            var r = new SequencerRig();
            r.StartLoaded(new[] { new HighlightScene(1, 100d, 110d, isKeyMoment: true, keyMomentTime: 104d) });
            r.RunUntil(() => r.Seq.State == SequencerState.KeyMomentWait);
            double at = r.Seq.Clock.Position;

            r.Seq.ResolveKeyMoment(null, null);
            r.Run(1);

            Assert.AreEqual(SequencerState.Playing, r.Seq.State);
            Assert.Greater(r.Seq.Clock.Position, at);
            Assert.AreEqual(1, r.Camera.Begun.Count, "같은 장면을 다시 시작하지 않는다");
        }

        [Test]
        public void RequestSkipScene_KeyMomentAhead_StopsAtKeyMoment()
        {
            var r = new SequencerRig();
            r.StartLoaded(new[] { new HighlightScene(1, 100d, 110d, isKeyMoment: true, keyMomentTime: 104d) });
            r.Run(2);
            r.Seq.RequestSkipScene();
            r.Run(1);
            Assert.AreEqual(SequencerState.KeyMomentWait, r.Seq.State);
            Assert.AreEqual(104d, r.Seq.Clock.Position, 1e-9);
        }

        [Test]
        public void RequestSkipAll_RemainingKeyMoments_VisitsAsTextThenResults()
        {
            var r = new SequencerRig();
            r.StartLoaded(new[]
            {
                new HighlightScene(1, 100d, 103d),
                new HighlightScene(2, 200d, 210d, isKeyMoment: true, keyMomentTime: 205d),
                new HighlightScene(3, 300d, 303d),
                new HighlightScene(4, 400d, 410d, isKeyMoment: true, keyMomentTime: 405d),
            });
            r.Run(2);
            r.Seq.RequestSkipAll();
            r.Run(1);

            Assert.IsFalse(r.Seq.Wants3D);
            Assert.AreEqual(SequencerState.KeyMomentWait, r.Seq.State);
            Assert.AreEqual((2, 205d, true), r.Moments.Requests[0]);
            r.Seq.ResolveKeyMoment(null, null);
            Assert.AreEqual((4, 405d, true), r.Moments.Requests[1]);
            r.Seq.ResolveKeyMoment(null, null);
            Assert.AreEqual(SequencerState.Results, r.Seq.State);
            Assert.AreEqual(2, r.Moments.Requests.Count);
        }

        [Test]
        public void RequestSkipAll_DuringTimeline_KeepsNextKeyMoment()
        {
            var r = new SequencerRig();
            r.StartLoaded(new[]
            {
                new HighlightScene(1, 100d, 101d),
                new HighlightScene(2, 200d, 210d, isKeyMoment: true, keyMomentTime: 205d),
            });
            r.RunUntil(() => r.Seq.State == SequencerState.Timeline);
            r.Seq.RequestSkipAll();
            r.Run(1);
            Assert.AreEqual(1, r.Moments.Requests.Count);
            Assert.AreEqual(2, r.Moments.Requests[0].id);
        }

        [Test]
        public void RequestRate_OnTransition_AppliesNextScene()
        {
            var r = new SequencerRig(gap: 0d);
            r.StartLoaded(new[] { new HighlightScene(1, 100d, 101d), new HighlightScene(2, 200d, 203d) });
            r.RunUntil(() => r.Seq.Clock.Peek(F) >= 101d);

            r.Seq.RequestRate(3f);
            r.Run(1);
            Assert.AreEqual(1.5f, r.Seq.Clock.UserRate, "전환 프레임에서는 앞 장면 속도를 유지한다");
            Assert.AreEqual(3f, r.Settings.Rate);
            r.Run(1);
            Assert.AreEqual(2, r.Camera.Begun[1]);
            Assert.AreEqual(3f, r.Seq.Clock.UserRate, "다음 장면 첫 프레임부터 새 속도");
        }

        [Test]
        public void RequestRate_OnTransition_KeepsCueOrderAndCount()
        {
            PlaybackCue[][] Cues() => new[]
            {
                new[] { new PlaybackCue(100.5d, 1, 0, 0), new PlaybackCue(101d, 2, 0, 0) },
                new[] { new PlaybackCue(200d, 3, 0, 0), new PlaybackCue(201d, 4, 0, 0) },
            };
            var scenes = new[] { new HighlightScene(1, 100d, 101d), new HighlightScene(2, 200d, 203d) };

            var plain = new SequencerRig(gap: 0d);
            plain.StartLoaded(scenes, Cues());
            plain.RunUntil(() => plain.Seq.State == SequencerState.Results);

            var changed = new SequencerRig(gap: 0d);
            changed.StartLoaded(scenes, Cues());
            changed.RunUntil(() => changed.Seq.Clock.Peek(F) >= 101d);
            changed.Seq.RequestRate(3f);
            changed.RunUntil(() => changed.Seq.State == SequencerState.Results);

            CollectionAssert.AreEqual(plain.Audio.Cues, changed.Audio.Cues);
            Assert.AreEqual(4, changed.Audio.Cues.Count);
        }

        [Test]
        public void Tick_SkipAndRateSameFrame_SkipsFirst()
        {
            var r = new SequencerRig(gap: 0d);
            r.StartLoaded(new[] { new HighlightScene(1, 100d, 110d), new HighlightScene(2, 200d, 203d) });
            r.Run(2);
            r.Seq.RequestRate(2f);
            r.Seq.RequestSkipScene();
            r.Run(1);
            CollectionAssert.AreEqual(new[] { 1, 2 }, r.Camera.Begun, "건너뛰기가 먼저 처리된다");
            Assert.AreEqual(200d, r.Seq.Clock.Position);
            Assert.AreEqual(2f, r.Seq.Clock.UserRate, "속도는 다음 장면 첫 프레임에 들어간다");
        }

        [Test]
        public void Tick_InputWhileLoading_Ignored()
        {
            var r = new SequencerRig();
            r.Seq.Start(Three());
            r.Seq.RequestRate(3f);
            r.Seq.RequestPause();
            r.Seq.RequestSkipScene();
            r.Seq.RequestSkipAll();
            r.Run(1);
            Assert.AreEqual(SequencerState.Loading, r.Seq.State);
            Assert.AreEqual(1.5f, r.Settings.Rate);

            r.Seq.OnLoaded(new[] { new PlaybackCue[0], new PlaybackCue[0], new PlaybackCue[0] });
            r.Run(1);
            Assert.IsFalse(r.Seq.Paused, "로딩 중 입력이 재생으로 넘어오지 않는다");
        }

        [Test]
        public void Tick_CancelWhileLoading_GoesToCancelled()
        {
            var r = new SequencerRig();
            r.Seq.Start(Three());
            r.Seq.RequestCancel();
            r.Run(1);
            Assert.AreEqual(SequencerState.Cancelled, r.Seq.State);
            Assert.IsFalse(r.Seq.Wants3D);
        }

        [Test]
        public void OnApplicationBackground_Playing_PausesAndStays()
        {
            var r = new SequencerRig();
            r.StartLoaded(Three());
            r.Run(2);
            r.Seq.OnApplicationBackground();
            r.Run(1);
            double at = r.Seq.Clock.Position;
            r.Run(60);
            Assert.IsTrue(r.Seq.Paused);
            Assert.AreEqual(at, r.Seq.Clock.Position);
            r.Seq.RequestResume();
            r.Run(1);
            Assert.Greater(r.Seq.Clock.Position, at);
        }

        [Test]
        public void RequestRate_WhilePaused_AppliesOnResume()
        {
            var r = new SequencerRig();
            r.StartLoaded(Three());
            r.Seq.RequestPause();
            r.Run(1);
            r.Seq.RequestRate(2f);
            r.Run(1);
            r.Seq.RequestResume();
            double at = r.Seq.Clock.Position;
            r.Run(1);
            Assert.AreEqual(at + F * 2d, r.Seq.Clock.Position, 1e-9);
        }

        [Test]
        public void RequestRate_DuringReplay_KeepsHalfSpeedThenApplies()
        {
            var r = new SequencerRig(gap: 0d);
            r.StartLoaded(new[] { new HighlightScene(1, 100d, 102d, isReplay: true), new HighlightScene(2, 200d, 203d) });
            r.Seq.RequestRate(3f);
            r.Run(1);
            Assert.AreEqual(0.5f, r.Seq.Clock.EffectiveRate);
            r.RunUntil(() => r.Camera.Begun.Count == 2);
            Assert.AreEqual(3f, r.Seq.Clock.EffectiveRate);
        }

        [Test]
        public void RequestPause_DuringTimeline_AppliesOnNextScene()
        {
            var r = new SequencerRig();
            r.StartLoaded(new[] { new HighlightScene(1, 100d, 101d), new HighlightScene(2, 200d, 203d) });
            r.RunUntil(() => r.Seq.State == SequencerState.Timeline);
            r.Seq.RequestPause();
            r.Run(1);
            Assert.IsFalse(r.Seq.Paused, "전환 도중에는 멈추지 않는다");
            r.RunUntil(() => r.Camera.Begun.Count == 2);
            Assert.IsTrue(r.Seq.Paused);
            Assert.AreEqual(200d, r.Seq.Clock.Position);
        }

        [Test]
        public void Tick_Timeline_HasConfiguredLengthAndPolicyOrder()
        {
            var r = new SequencerRig(gap: 1d);
            r.StartLoaded(new[] { new HighlightScene(1, 100d, 101d), new HighlightScene(2, 200d, 203d) });
            r.RunUntil(() => r.Seq.State == SequencerState.Timeline);
            int frames = 0;
            while (r.Seq.State == SequencerState.Timeline && frames < 1000) { r.Seq.Tick(F); frames++; }

            Assert.AreEqual(60, frames, 1);
            CollectionAssert.AreEqual(new[] { "highlight", "timeline", "exit" }, r.Frames.Calls);
        }

        [Test]
        public void Tick_NextIsReplay_SkipsTimeline()
        {
            var r = new SequencerRig(gap: 1d);
            r.StartLoaded(new[] { new HighlightScene(1, 100d, 101d), new HighlightScene(2, 100d, 101d, isReplay: true) });
            r.RunUntil(() => r.Camera.Begun.Count == 2);
            CollectionAssert.DoesNotContain(r.Frames.Calls, "timeline");
        }

        [Test]
        public void Tick_LargeFrame_KeepsCuesAndScenes()
        {
            var r = new SequencerRig(gap: 0d);
            var cues = new[]
            {
                new[] { new PlaybackCue(100.2d, 1, 0, 0), new PlaybackCue(100.8d, 2, 0, 0) },
                new[] { new PlaybackCue(200.1d, 3, 0, 0) },
            };
            r.StartLoaded(new[] { new HighlightScene(1, 100d, 101d), new HighlightScene(2, 200d, 201d) }, cues);
            r.RunUntil(() => r.Seq.State == SequencerState.Results, dt: 5d);
            Assert.AreEqual(3, r.Audio.Cues.Count);
            CollectionAssert.AreEqual(new[] { "highlight", "menu" }, r.Frames.Calls);
        }
    }
}
