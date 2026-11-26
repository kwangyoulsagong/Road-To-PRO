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
            /// <summary>specs/059 골망 튜닝 (goalnet.json + ball.json). 없으면 기본값 + 골 상자 치수.</summary>
            public Stadium.Net.NetRippleTuning NetTuning;
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
        Rendering.CinematicModeController _cinematic;
        Players.CloseupHeads _closeups;
        Stadium.Net.GoalNetView _nets;

        /// <summary>specs/061 연출 장면 고품질 모드 (실제 프리셋에서만, 로딩 뒤). 검증용.</summary>
        public Rendering.CinematicModeController Cinematic => _cinematic;

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
            CreateCinematic();
            if (_cinematic != null)
            {
                // 061 FR-85: 고품질 모드 셰이더(DoF·SSAO·셸 잔디)를 로딩 화면 뒤에서 한 프레임 그려 준비 — 전환 순간 컴파일 0
                _cinematic.BeginWarmUp();
                yield return null;
                _cinematic.EndWarmUp();
                if (Cancelled()) yield break;
            }
            if (_scenes.Count > 0)
            {
                var stage = _assets.Stage;
                _system = stage.gameObject.AddComponent<PlayerAnimationSystem>();
                _system.Init(_scenes[0].Plan, stage.Rigs, stage.Clips, _seq.Clock, stage.Ball, _camera);
                _system.BallFilter = this;
                _system.EnableMotionExtras(stage.KitMotion); // 062 FR-10~13·21
                // 062 T35a FR-85~87: 클로즈업 머리(056 FR-61)·품질 등급 — 061 고품질 모드의 TopTier를 받는다(일반 중계는 클로즈업 선택만)
                _closeups = stage.gameObject.AddComponent<Players.CloseupHeads>();
                _closeups.Init(_camera, stage.Rigs);
                _closeups.Cinematic = _cinematic;
                // 059: 골망 출렁임 — 무대 씬의 glb 그물을 동적 그물로 바꾼다 (못 하면 정적 그물 유지)
                _nets = stage.gameObject.AddComponent<Stadium.Net.GoalNetView>();
                var netTuning = _ports.NetTuning ?? Stadium.Net.NetRippleTuning.ForGoal(_goal);
                if (!_nets.Init(netTuning, _goal.HalfLength, stage.gameObject.scene.GetRootGameObjects(), new Logging.UnityLogger(RoadToPro.Core.Logging.LogLevel.Warning)))
                {
                    Destroy(_nets);
                    _nets = null;
                }
                if (_diagnostics != null) _system.Diagnostics = _diagnostics;
                _current = _scenes[0];
                _applied = 0;
            }
            _loaded = true;
            _ports.Hud.OnLoadingProgress(1f);
            _seq.OnLoaded(_builder.Scenes(), _builder.Cues());
        }

        /// <summary>specs/061: 실제 프리셋이면 고품질 모드 컨트롤러를 만들고 장면마다 구간을 계산한다 (로딩 중 — 재생 중 생성 없음, FR-85).</summary>
        void CreateCinematic()
        {
            _cinematic?.Dispose();
            _cinematic = null;
            var preset = Rendering.RenderQualityApplier.Current;
            if (preset == null || preset.IsStandIn || !(_ports.Camera is Broadcast.BroadcastCamera)) return;
            var stage = _assets.Stage;
            _cinematic = new Rendering.CinematicModeController(_camera, preset.Id, stage != null ? stage.Lighting : null, stage != null ? stage.PitchRenderer : null);
            foreach (var s in _scenes) BuildCinematic(s);
        }

        void BuildCinematic(BuiltScene s)
        {
            if (_cinematic == null || !(_ports.Camera is Broadcast.BroadcastCamera bc)) return;
            var brief = bc.BriefOf(s.Scene.Id);
            _cinematic.OnSceneBuilt(s.Scene.Id, bc.TrackOf(s.Scene.Id), s.Data, brief != null ? brief.Scorer : -1);
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
            for (int i = keep; i < _scenes.Count; i++) BuildCinematic(_scenes[i]);
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
            if (_cinematic != null && _current != null && _seq.State == SequencerState.Playing)
                _cinematic.Apply(_current.Scene.Id, _seq.Clock.Position, _current.Data);
            if (_nets != null && _current != null && _loaded)
                _nets.Apply(_current, _seq.Clock.Position);   // 059: 정지 중엔 같은 시각이라 쓰기 없음 (FR-53)

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
            _closeups = null;   // 무대와 함께 해제된다
            _nets = null;
            _current = null;
            _loaded = false;
            _cinematic?.Dispose();
            _cinematic = null;
            _scenes.Clear();
            _builder = null;
            yield return _assets.Unload();
            // 메뉴는 3D가 없으니 후처리(톤맵)도 끈다 — 켜 두면 UI 바탕색까지 톤맵된다 (048 T16)
            Rendering.RenderQualityApplier.RevertCamera(_camera);
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
