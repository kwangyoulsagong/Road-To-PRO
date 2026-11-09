using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Hashing;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Logging;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Verification.Bench;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadToPro.Verification.Samples.Highlight
{
    /// <summary>
    /// specs/041 E2E 호스트. 메뉴와 카메라·HUD를 소유하고 3D 무대 씬을 넣고 뺀다.
    /// 화면: highlightPlay, highlightKeyMoment, freeze.(first|goal|replayMid|afterTransition), highlightSpeed.X, highlightHud.off,
    /// highlightSkipAll, highlightWait, highlightLoop.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class HighlightScreen : MonoBehaviour, IFarmPlayback, IDeterminismProbe
    {
        const string StageScene = "Highlight3D";
        const ulong Seed = 20261109UL;
        const int LoopCount = 10, WaitFrames = 60, FramingPlayers = 10;
        const float FitMargin = 1.12f, ViewportMargin = 0.02f;
        const double TransitionWindow = 1d;
        static readonly Vector3 CameraPosition = new Vector3(0f, 24f, -72f);
        static readonly Color MenuBackground = new Color(0.95f, 0.95f, 0.96f);

        [SerializeField] Camera cam;
        [SerializeField] UIDocument doc;
        [SerializeField] HighlightPlayer player;

        static MatchSim _match;
        static MatchTuning _tuning;

        readonly PlaybackSettings _settings = new PlaybackSettings();
        StandInHud _hud;
        readonly StandInCamera _camera = new StandInCamera();
        readonly StandInFramePolicy _frames = new StandInFramePolicy();
        readonly StandInAudio _audio = new StandInAudio();
        StandInKeyMoments _moments;
        Label _menu, _fps, _framing, _keyLabel, _waitLabel, _memory, _handles;
        readonly List<Vector3> _targets = new List<Vector3>();
        int _waitLeft = -1;
        ProfilerRecorder _shaders;
        double _lastFrame;
        double _frameMs;
        bool _inTransition;
        int _shaderFrame;
        int _shownFps = -1, _shownHandles = -1;
        bool _shownFramed;
        Vector3 _sortBall;
        System.Comparison<Vector3> _byBallDistance;

        public void Configure(Camera c, UIDocument d, HighlightPlayer p)
        {
            cam = c;
            doc = d;
            player = p;
        }

        void OnEnable()
        {
            FarmContext.RegisterCamera(cam);
            FarmContext.RegisterDocument(doc);
            FarmContext.Playback = this;
            FarmContext.DeterminismProbe = this;
            var root = doc.rootVisualElement;
            _hud = new StandInHud(root);
            _menu = StandInHud.Add(root, "menuLabel", 16f, 100f);
            _fps = StandInHud.Add(root, "fpsLabel", 16f, 128f);
            _framing = StandInHud.Add(root, "framingLabel", 16f, 156f);
            _keyLabel = StandInHud.Add(root, "keyMomentLabel", 16f, 184f);
            _waitLabel = StandInHud.Add(root, "waitLabel", 16f, 212f);
            _memory = StandInHud.Add(root, "memoryLabel", 16f, 240f);
            _handles = StandInHud.Add(root, "handlesLabel", 16f, 268f);
            foreach (var l in new[] { _keyLabel, _waitLabel, _menu }) l.style.display = DisplayStyle.None;
            _waitLabel.style.opacity = 0f; // 대기 신호일 뿐 캡처 비교에 들어가지 않게

            player.PauseOnBackground = false;
            FarmContext.Screens["highlightMenu"] = ShowMenu;
            FarmContext.Screens["highlightEmpty"] = () => _frames.EnterHighlight(); // 프레임 판정 기준: 3D 없이 재생과 같은 목표 fps
            FarmContext.Screens["highlightPlay"] = () => Play(false);
            FarmContext.Screens["highlightKeyMoment"] = () => Play(true);
            FarmContext.Screens["freeze.first"] = () => FreezeScene(0, 0.5d);
            FarmContext.Screens["freeze.goal"] = FreezeGoal;
            FarmContext.Screens["freeze.replayMid"] = FreezeReplay;
            FarmContext.Screens["freeze.afterTransition"] = () => FreezeScene(1, 0d);
            foreach (var r in new[] { 1f, 1.5f, 2f, 3f })
            {
                float rate = r;
                FarmContext.Screens["highlightSpeed." + rate.ToString("0.0", CultureInfo.InvariantCulture)] = () => SetSpeed(rate);
            }
            FarmContext.Screens["highlightHud.off"] = () => _hud.SetVisible(false);
            FarmContext.Screens["highlightSkipAll"] = () => player.Sequencer.RequestSkipAll();
            FarmContext.Screens["highlightResume"] = () => player.Sequencer.RequestResume();
            FarmContext.Screens["highlightWait"] = () => { _waitLeft = WaitFrames; _waitLabel.style.display = DisplayStyle.None; };
            FarmContext.Screens["highlightLoop"] = () => StartCoroutine(EnterExitLoop());
            FarmContext.CaptureRegions = null;
            FarmContext.PerfCounters["transitionFrameMs"] = () => _inTransition ? _frameMs : (double?)null;
            FarmContext.PerfCounters["shaderCompiles"] = () => player.IsLoaded ? _shaderFrame : (double?)null;
            FarmContext.PerfCounters["syncLoads"] = () => player.IsLoaded ? 0d : (double?)null;
            _shaders = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shader.CreateGPUProgram", 1);
            _lastFrame = Time.realtimeSinceStartupAsDouble;
            _byBallDistance = (a, b) => (a - _sortBall).sqrMagnitude.CompareTo((b - _sortBall).sqrMagnitude);
            ShowMenu();
        }

        void OnDisable()
        {
            FarmContext.Cameras.Remove(cam);
            FarmContext.UnregisterDocument(doc);
            FarmContext.Playback = null;
            FarmContext.DeterminismProbe = null;
            FarmContext.PerfCounters.Remove("transitionFrameMs");
            FarmContext.PerfCounters.Remove("shaderCompiles");
            FarmContext.PerfCounters.Remove("syncLoads");
            _shaders.Dispose();
        }

        void ShowMenu()
        {
            _menu.text = "menu";
            _menu.style.display = DisplayStyle.Flex;
            _hud.SetVisible(false);
            _framing.text = "";
            _keyLabel.style.display = DisplayStyle.None;
            ShowCounter(_fps, ref _shownFps, _frames.TargetFps);
        }

        static string DataDir => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(FarmContext.Args.ScenarioPath)), ".."));

        static MatchSim Match()
        {
            if (_match != null) return _match;
            var work = new MatchBenchWorkloads(DataDir);
            _tuning = work.Tuning;
            _match = new MatchSim(MatchBenchWorkloads.Setup(Seed), _tuning, new MatchSimOptions { Checkpoints = CheckpointMode.All });
            _match.RunToEnd();
            return _match;
        }

        void Play(bool keyMoment)
        {
            _menu.style.display = DisplayStyle.None;
            _keyLabel.style.display = DisplayStyle.None;
            _hud.SetVisible(true);
            var sim = Match();
            var scenes = StandInSceneSelector.Select(sim, keyMoment);
            Begin(scenes);
        }

        void Begin(HighlightScene[] scenes)
        {
            var sim = Match();
            var stageHolder = new StageBinding();
            AnimPlan BuildPlan(HighlightData d)
            {
                stageHolder.Resolve(player);
                return AnimPlan.Build(stageHolder.Stage.Library, stageHolder.Stage.Map, d, new UnityLogger(RoadToPro.Core.Logging.LogLevel.Warning), null, stageHolder.Stage.Scales);
            }
            var builder = new SceneClipBuilder(scenes, sim.ExtractClip, BuildPlan, PitchSpec.Default, new StandInDiagnostics());
            _moments = new StandInKeyMoments();
            var ball = BallParams.Create(_tuning.Ball, SurfaceCondition.Dry, PitchSpec.Default);
            var goal = new GoalBox(PitchSpec.Default, BallParams.GoalWidth, BallParams.GoalHeight, ball.NetDepth, ball.Radius);
            var ports = new HighlightPlayer.Ports { KeyMoments = _moments, Hud = _hud, Camera = _camera, Frames = _frames, Audio = _audio };
            player.Begin(scenes, builder, new HighlightAssets(StageScene), cam, goal, _settings, TimelineGap(), ports);
        }

        static double TimelineGap()
        {
            var registry = RoadToPro.Core.Tuning.TuningLoader.LoadDefinitionsFile(Path.Combine(DataDir, "tuning", "highlight.json"));
            return RoadToPro.Core.Tuning.TuningBundle.Defaults(registry).Get("highlight.timelineGapSeconds");
        }

        sealed class StageBinding
        {
            public HighlightCastStage Stage;
            public void Resolve(HighlightPlayer p)
            {
                if (Stage == null) Stage = (HighlightCastStage)p.Assets.Stage;
            }
        }

        void FreezeScene(int index, double offset)
        {
            var s = player.Scenes[index].Scene;
            player.Sequencer.SeekAndPause(index, s.Start + offset);
        }

        void FreezeGoal()
        {
            var scenes = player.Scenes;
            for (int i = 0; i < scenes.Count; i++)
                if (scenes[i].Data.Events.Exists(e => e.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Goal) && !scenes[i].Scene.IsReplay)
                {
                    var goal = scenes[i].Data.Events.Find(e => e.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Goal);
                    player.Sequencer.SeekAndPause(i, System.Math.Min(goal.Time, scenes[i].Scene.End));
                    return;
                }
            Debug.LogError("[highlight] 골 장면이 없다");
        }

        void FreezeReplay()
        {
            var scenes = player.Scenes;
            for (int i = 0; i < scenes.Count; i++)
                if (scenes[i].Scene.IsReplay)
                {
                    player.Sequencer.SeekAndPause(i, (scenes[i].Scene.Start + scenes[i].Scene.End) * 0.5d);
                    return;
                }
            Debug.LogError("[highlight] 리플레이 장면이 없다");
        }

        public void SetSpeed(float speed) => player.Sequencer.RequestRate(speed);

        public double SimTimeSeconds => player.Sequencer != null ? player.Sequencer.Clock.Position : 0d;
        public int CurrentScene => player.Sequencer != null && player.IsLoaded ? player.Sequencer.SceneIndex : -1;

        public bool SeekAndFreeze(double? simTimeSeconds, int? scene)
        {
            if (!player.IsLoaded) return false;
            int index = scene ?? player.Sequencer.SceneIndex;
            player.Sequencer.SeekAndPause(index, simTimeSeconds ?? player.Scenes[index].Scene.Start);
            return true;
        }

        public IReadOnlyList<FarmCheckpoint> Checkpoints
        {
            get
            {
                var list = new List<FarmCheckpoint>();
                for (int i = 0; i < player.Scenes.Count; i++)
                {
                    var built = player.Scenes[i];
                    var h = StableHasher.Create();
                    h.Add(built.Data.Hash());
                    for (double t = built.Scene.Start; t < built.Scene.End; t += 1d / 60d)
                    {
                        var b = built.Plan.BallVisual((float)t);
                        h.Add(b.X);
                        h.Add(b.Y);
                        h.Add(b.Z);
                    }
                    list.Add(new FarmCheckpoint("scene" + i, (long)(built.Scene.Start * 10d), h.Hex));
                }
                return list;
            }
        }

        IEnumerator EnterExitLoop()
        {
            Match();
            yield return Resources.UnloadUnusedAssets();
            System.GC.Collect();
            yield return null;
            long before = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            for (int i = 0; i < LoopCount; i++)
            {
                Play(false);
                while (!player.IsLoaded) yield return null;
                player.Sequencer.RequestSkipAll();
                yield return null;
                while (!player.IsUnloaded) yield return null;
            }
            System.GC.Collect();
            yield return Resources.UnloadUnusedAssets();
            long grown = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - before;
            _memory.text = (grown / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture);
            Debug.Log($"[highlight] 진입·언로드 {LoopCount}회 뒤 메모리 증가 {_memory.text} MB");
            _waitLabel.text = "looped";
            _waitLabel.style.display = DisplayStyle.Flex;
        }

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            _frameMs = (now - _lastFrame) * 1000d;
            _lastFrame = now;
            _shaderFrame = _shaders.Valid && _shaders.LastValue > 0 ? 1 : 0;

            var seq = player.Sequencer;
            ShowCounter(_fps, ref _shownFps, _frames.TargetFps);
            ShowCounter(_handles, ref _shownHandles, player.Assets?.OutstandingHandles ?? 0);
            if (_waitLeft >= 0 && --_waitLeft < 0)
            {
                _waitLabel.text = "waited";
                _waitLabel.style.display = DisplayStyle.Flex;
            }
            if (seq == null) return;
            if (seq.State == SequencerState.Results && _menu.style.display != DisplayStyle.Flex) ShowMenu();
            if (_moments != null && _moments.Waiting)
            {
                _keyLabel.text = "key";
                _keyLabel.style.display = DisplayStyle.Flex;
            }
            _inTransition = player.IsLoaded && IsNearTransition(seq);
        }

        /// <summary>값이 바뀔 때만 글자를 만든다 (측정 구간 GC를 늘리지 않게).</summary>
        static void ShowCounter(Label label, ref int shown, int value)
        {
            if (value == shown) return;
            shown = value;
            label.text = value.ToString(CultureInfo.InvariantCulture);
        }

        bool IsNearTransition(HighlightSequencer seq)
        {
            if (seq.State == SequencerState.Timeline) return true;
            if (seq.State != SequencerState.Playing || player.Current == null) return false;
            var s = player.Current.Scene;
            double rate = seq.Clock.EffectiveRate;
            return (seq.Clock.Position - s.Start) / rate <= TransitionWindow || (s.End - seq.Clock.Position) / rate <= TransitionWindow;
        }

        void LateUpdate()
        {
            if (player.Current == null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = MenuBackground; // 경기장 조명이 바꾼 배경을 되돌린다
                return;
            }
            FrameTargets();
        }

        /// <summary>042 대체: 공과 가장 가까운 필드 선수를 화면에 넣는 화각. 이 프레임의 포즈 뒤에 돈다.</summary>
        void FrameTargets()
        {
            var stage = (HighlightCastStage)player.Assets.Stage;
            var ball = stage.Ball.position;
            _targets.Clear();
            for (int e = 0; e < HighlightData.Players; e++)
            {
                if (e % 11 == 0) continue;
                var rig = stage.Rigs[e];
                if (rig != null && rig.gameObject.activeSelf) _targets.Add(rig.transform.position);
            }
            _sortBall = ball;
            _targets.Sort(_byBallDistance);
            int n = Mathf.Min(FramingPlayers, _targets.Count);

            var center = ball;
            for (int i = 0; i < n; i++) center += _targets[i];
            center /= n + 1;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.transform.SetPositionAndRotation(CameraPosition, Quaternion.LookRotation(center - CameraPosition, Vector3.up));

            float tanX = 0f, tanY = 0f;
            Fit(ball, ref tanX, ref tanY);
            for (int i = 0; i < n; i++)
            {
                Fit(_targets[i], ref tanX, ref tanY);
                Fit(_targets[i] + Vector3.up * 1.9f, ref tanX, ref tanY);
            }
            float half = Mathf.Max(tanY, tanX / cam.aspect) * FitMargin;
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(half) * Mathf.Rad2Deg, 8f, 90f);
            bool framed = InView(ball, n);
            if (framed != _shownFramed || _framing.text.Length == 0)
            {
                _shownFramed = framed;
                _framing.text = framed ? "ok" : "bad";
            }
        }

        void Fit(Vector3 p, ref float tanX, ref float tanY)
        {
            var v = cam.transform.InverseTransformPoint(p);
            if (v.z <= 0.01f) return;
            tanX = Mathf.Max(tanX, Mathf.Abs(v.x / v.z));
            tanY = Mathf.Max(tanY, Mathf.Abs(v.y / v.z));
        }

        bool InView(Vector3 ball, int n)
        {
            if (n < FramingPlayers || !Inside(ball)) return false;
            for (int i = 0; i < n; i++)
                if (!Inside(_targets[i])) return false;
            return true;
        }

        bool Inside(Vector3 p)
        {
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x >= ViewportMargin && v.x <= 1f - ViewportMargin && v.y >= ViewportMargin && v.y <= 1f - ViewportMargin;
        }
    }
}
