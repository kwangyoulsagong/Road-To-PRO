using System;
using System.Collections;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Motion;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace RoadToPro.Presentation.Highlight
{
    /// <summary>
    /// 하이라이트 재생 연결부 (specs/041). 재생 규칙은 <see cref="HighlightSequencer"/>가 정하고 여기서는 3D 씬·포즈·입력만 잇는다.
    /// </summary>
    public sealed class HighlightPlayer : MonoBehaviour, IBallVisualFilter
    {
        /// <summary>로딩 중 장면 구축에 프레임마다 쓰는 시간.</summary>
        const long BuildSliceMs = 12;

        public struct Ports
        {
            public IKeyMomentHandler KeyMoments;
            public IHighlightHud Hud;
            public IHighlightCamera Camera;
            public IFramePolicy Frames;
            public IHighlightAudio Audio;
        }

        HighlightSequencer _seq;
        HighlightAssets _assets;
        SceneClipBuilder _builder;
        Camera _camera;
        GoalBox _goal;
        Ports _ports;
        PlayerAnimationSystem _system;
        readonly List<BuiltScene> _scenes = new List<BuiltScene>();
        BuiltScene _current;
        int _applied = -1;
        bool _loaded, _unloading, _loadRunning;
        IMotionDiagnostics _diagnostics;

        public HighlightSequencer Sequencer => _seq;
        public HighlightAssets Assets => _assets;
        public BuiltScene Current => _current;
        public IReadOnlyList<BuiltScene> Scenes => _scenes;
        public bool IsLoaded => _loaded;
        /// <summary>이번 재생의 3D 씬·메모리 해제가 끝났다.</summary>
        public bool IsUnloaded { get; private set; }
        /// <summary>끄면 창이 포커스를 잃어도 멈추지 않는다 (디바이스팜 숨김 창).</summary>
        public bool PauseOnBackground { get; set; } = true;

        /// <summary>진단 켜기 (FR-70, 개발·검증 빌드). 꺼져 있으면 호출 비용이 없다.</summary>
        public void SetDiagnostics(IMotionDiagnostics diagnostics)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _diagnostics = diagnostics;
            if (_system != null) _system.Diagnostics = diagnostics;
#endif
        }

        /// <summary>재생을 시작한다. 장면이 없으면 3D를 로드하지 않는다 (FR-50).</summary>
        public void Begin(HighlightScene[] normalizedScenes, SceneClipBuilder builder, HighlightAssets assets, Camera broadcastCamera,
            GoalBox goal, PlaybackSettings settings, double timelineGapSeconds, in Ports ports)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _camera = broadcastCamera != null ? broadcastCamera : throw new ArgumentNullException(nameof(broadcastCamera));
            _goal = goal;
            _ports = ports;
            _scenes.Clear();
            _current = null;
            _applied = -1;
            _loaded = _unloading = false;
            IsUnloaded = false;
            _seq = new HighlightSequencer(settings, timelineGapSeconds, ports.KeyMoments, ports.Hud, ports.Camera, ports.Frames, ports.Audio);
            _seq.Start(normalizedScenes);
            if (_seq.State == SequencerState.Loading) StartCoroutine(LoadGuarded());
        }

        /// <summary>취소가 로드 도중에 와도 언로드는 로드가 끝난 뒤에 시작한다 (FR-58).</summary>
        IEnumerator LoadGuarded()
        {
            _loadRunning = true;
            yield return Load();
            _loadRunning = false;
        }

        IEnumerator Load()
        {
            bool Cancelled() => _seq.State == SequencerState.Cancelled;

            _ports.Hud.OnLoadingProgress(0f);
            yield return _assets.Load(_camera, Cancelled);
            if (Cancelled()) yield break;
            _ports.Hud.OnLoadingProgress(0.3f);

            var watch = new Stopwatch();
            while (!_builder.IsDone)
            {
                if (Cancelled()) yield break;
                watch.Restart();
                while (watch.ElapsedMilliseconds < BuildSliceMs && _builder.Step()) { }
                _ports.Hud.OnLoadingProgress(0.3f + 0.6f * _builder.Progress);
                yield return null;
            }

            yield return _assets.WarmUp();
            if (Cancelled()) yield break;

            _scenes.AddRange(_builder.Built);
            if (_scenes.Count > 0)
            {
                var stage = _assets.Stage;
                _system = stage.gameObject.AddComponent<PlayerAnimationSystem>();
                _system.Init(_scenes[0].Plan, stage.Rigs, stage.Clips, _seq.Clock, stage.Ball, _camera);
                _system.BallFilter = this;
                if (_diagnostics != null) _system.Diagnostics = _diagnostics;
                _current = _scenes[0];
                _applied = 0;
            }
            _loaded = true;
            _ports.Hud.OnLoadingProgress(1f);
            _seq.OnLoaded(_builder.Scenes(), _builder.Cues());
        }

        /// <summary>
        /// 키 모먼트 결과가 뒤 장면을 바꿨을 때 (FR-22). 재생이 멈춘 동안 새 장면을 만들어 교체한다. 결과가 같으면 builder는 null.
        /// </summary>
        public void ResolveKeyMoment(SceneClipBuilder followingBuilder)
        {
            if (followingBuilder == null)
            {
                _seq.ResolveKeyMoment(null, null);
                return;
            }
            while (followingBuilder.Step()) { }
            int keep = _seq.SceneIndex + 1;
            _scenes.RemoveRange(keep, _scenes.Count - keep);
            _scenes.AddRange(followingBuilder.Built);
            _seq.ResolveKeyMoment(followingBuilder.Scenes(), followingBuilder.Cues());
        }

        void Update()
        {
            if (_seq == null) return;
            _seq.Tick(Time.unscaledDeltaTime);

            if (_system != null && _seq.State == SequencerState.Playing && _seq.SceneIndex != _applied)
            {
                _applied = _seq.SceneIndex;
                _current = _scenes[_applied];
                _system.SwapPlan(_current.Plan);
                Rendering.RenderQualityApplier.ResetHistory(_camera);
            }

            if (!_unloading && !_loadRunning && !_seq.Wants3D && (_loaded || _seq.State == SequencerState.Cancelled))
            {
                _unloading = true;
                if (_seq.State == SequencerState.Cancelled) _ports.Frames.EnterMenu();
                StartCoroutine(UnloadStage());
            }
        }

        IEnumerator UnloadStage()
        {
            _system = null;
            _current = null;
            _loaded = false;
            _scenes.Clear();
            _builder = null;
            yield return _assets.Unload();
            IsUnloaded = true;
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && PauseOnBackground) _seq?.OnApplicationBackground();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && PauseOnBackground) _seq?.OnApplicationBackground();
        }

        public Vec3 Filter(Vec3 visual, float time)
        {
            if (_current == null) return visual;
            var d = _current.Data;
            d.FrameAt(time, out int f, out float a);
            return BallVisualGuard.Apply(visual, d.Ball[f], d.Ball[f + 1], a, _goal);
        }
    }
}
