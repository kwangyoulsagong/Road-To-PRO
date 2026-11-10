using System.Collections.Generic;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    sealed class FakeKeyMoments : IKeyMomentHandler
    {
        public readonly List<(int id, double time, bool textOnly)> Requests = new List<(int, double, bool)>();
        public void RequestChoice(int sceneId, double simTime, bool textOnly) => Requests.Add((sceneId, simTime, textOnly));
    }

    sealed class FakeHud : IHighlightHud
    {
        public PlaybackStatus Last;
        public void OnStatus(in PlaybackStatus status) => Last = status;
        public void OnLoadingProgress(float progress) { }
    }

    sealed class FakeCamera : IHighlightCamera
    {
        public readonly List<int> Begun = new List<int>();
        public bool IsReverseAngle => false;
        public void OnSceneBegin(in HighlightScene scene) => Begun.Add(scene.Id);
        public void OnPlayback(double simTime) => LastPlayback = simTime;
        public double LastPlayback = double.NaN;
    }

    sealed class FakeFrames : IFramePolicy
    {
        public readonly List<string> Calls = new List<string>();
        public void EnterHighlight() => Calls.Add("highlight");
        public void EnterTimeline() => Calls.Add("timeline");
        public void ExitTimeline() => Calls.Add("exit");
        public void EnterMenu() => Calls.Add("menu");
    }

    sealed class FakeAudio : IHighlightAudio
    {
        public readonly List<PlaybackCue> Cues = new List<PlaybackCue>();
        public float Rate;
        public bool Paused;
        public void OnPlaybackState(float rate, bool paused) { Rate = rate; Paused = paused; }
        public void OnCue(in PlaybackCue cue) => Cues.Add(cue);
    }

    sealed class SequencerRig
    {
        public const double Frame = 1d / 60d;
        public readonly PlaybackSettings Settings = new PlaybackSettings();
        public readonly FakeKeyMoments Moments = new FakeKeyMoments();
        public readonly FakeHud Hud = new FakeHud();
        public readonly FakeCamera Camera = new FakeCamera();
        public readonly FakeFrames Frames = new FakeFrames();
        public readonly FakeAudio Audio = new FakeAudio();
        public readonly HighlightSequencer Seq;

        public SequencerRig(double gap = 1d) =>
            Seq = new HighlightSequencer(Settings, gap, Moments, Hud, Camera, Frames, Audio);

        public void StartLoaded(HighlightScene[] scenes, PlaybackCue[][] cues = null)
        {
            Seq.Start(scenes);
            if (cues == null)
            {
                cues = new PlaybackCue[scenes.Length][];
                for (int i = 0; i < cues.Length; i++) cues[i] = new PlaybackCue[0];
            }
            Seq.OnLoaded(cues);
        }

        public void Run(int frames, double dt = Frame)
        {
            for (int i = 0; i < frames; i++) Seq.Tick(dt);
        }

        /// <summary>조건이 될 때까지 진행한다. 끝없는 루프를 막는 상한이 있다.</summary>
        public void RunUntil(System.Func<bool> done, double dt = Frame, int max = 100000)
        {
            for (int i = 0; i < max && !done(); i++) Seq.Tick(dt);
            NUnit.Framework.Assert.IsTrue(done(), "시퀀서가 기대 상태에 도달하지 못했다");
        }
    }
}
