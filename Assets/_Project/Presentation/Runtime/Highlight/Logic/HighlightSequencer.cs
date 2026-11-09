using System;

namespace RoadToPro.Presentation.Highlight.Logic
{
    public enum SequencerState
    {
        Idle,
        Loading,
        Playing,
        Timeline,
        KeyMomentWait,
        Results,
        Cancelled,
    }

    /// <summary>
    /// 하이라이트 재생 상태 기계 (specs/041 FR-20~22·32·50·54·56·57·58·59·5A). 프레임마다 <see cref="Tick"/> 한 번.
    /// 입력은 Request*로 쌓아 두고 Tick 처음에 건너뛰기 → 속도 → 일시정지 순으로 처리한다.
    /// </summary>
    public sealed class HighlightSequencer
    {
        readonly PlaybackSettings _settings;
        readonly double _timelineGap;
        readonly IKeyMomentHandler _keyMoments;
        readonly IHighlightHud _hud;
        readonly IHighlightCamera _camera;
        readonly IFramePolicy _frames;
        readonly IHighlightAudio _audio;
        readonly CueDispatcher _dispatcher = new CueDispatcher();

        HighlightScene[] _scenes = Array.Empty<HighlightScene>();
        PlaybackCue[][] _cues = Array.Empty<PlaybackCue[]>();
        int _index = -1;
        bool _paused, _pauseOnNextScene, _keyMomentDone, _sceneFinished, _skipAllMode, _timelineExitSent;
        bool _reqSkipScene, _reqSkipAll, _reqPause, _reqResume, _reqCancel;
        float _reqRate = float.NaN;
        double _gapRemaining;
        float _lastAudioRate = float.NaN;
        bool _lastAudioPaused;

        public readonly PlaybackClock Clock = new PlaybackClock();
        public SequencerState State { get; private set; } = SequencerState.Idle;
        /// <summary>3D 씬 로드를 요청한 횟수 (FR-50: 장면 0개면 0).</summary>
        public int LoadRequests { get; private set; }
        /// <summary>모두 건너뛰기 뒤에는 false — 3D 씬을 내린다 (FR-21).</summary>
        public bool Wants3D { get; private set; }
        public int SceneIndex => _index;
        public int SceneCount => _scenes.Length;
        public bool Paused => _paused;

        public HighlightSequencer(PlaybackSettings settings, double timelineGapSeconds, IKeyMomentHandler keyMoments, IHighlightHud hud,
            IHighlightCamera camera, IFramePolicy frames, IHighlightAudio audio)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (timelineGapSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(timelineGapSeconds));
            _timelineGap = timelineGapSeconds;
            _keyMoments = keyMoments ?? throw new ArgumentNullException(nameof(keyMoments));
            _hud = hud ?? throw new ArgumentNullException(nameof(hud));
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        }

        /// <summary>정규화된 목록으로 시작한다. 장면이 없으면 3D 로드 없이 바로 결과 (FR-50).</summary>
        public void Start(HighlightScene[] scenes)
        {
            if (State != SequencerState.Idle) throw new InvalidOperationException("이미 시작했다");
            _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
            if (scenes.Length == 0)
            {
                State = SequencerState.Results;
                return;
            }
            LoadRequests++;
            Wants3D = true;
            State = SequencerState.Loading;
        }

        /// <summary>로딩이 끝났다. cues[i]는 장면 i의 정렬된 큐.</summary>
        public void OnLoaded(PlaybackCue[][] cues)
        {
            if (State != SequencerState.Loading) return;
            _cues = cues ?? throw new ArgumentNullException(nameof(cues));
            if (cues.Length != _scenes.Length) throw new ArgumentException("장면 수와 큐 배열 수가 다르다");
            _frames.EnterHighlight();
            _index = 0;
            BeginCurrentScene();
            Publish();
        }

        /// <summary>로딩 중 재구성할 수 없는 장면이 빠졌을 때의 최종 목록으로 시작한다 (FR-53). 전부 빠졌으면 결과로 간다.</summary>
        public void OnLoaded(HighlightScene[] scenes, PlaybackCue[][] cues)
        {
            if (State != SequencerState.Loading) return;
            _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
            if (scenes.Length == 0)
            {
                EnterResults();
                return;
            }
            OnLoaded(cues);
        }

        /// <summary>검증 전용: 지정 장면의 지정 시각으로 옮겨 멈춘다. 재생 경로에서는 쓰지 않는다.</summary>
        public void SeekAndPause(int sceneIndex, double time)
        {
            if (State != SequencerState.Playing && State != SequencerState.Timeline && State != SequencerState.KeyMomentWait)
                throw new InvalidOperationException("재생 중이 아니다");
            if (sceneIndex < 0 || sceneIndex >= _scenes.Length) throw new ArgumentOutOfRangeException(nameof(sceneIndex));
            _index = sceneIndex;
            _sceneFinished = false;
            BeginCurrentScene();
            Clock.JumpTo(time);
            _dispatcher.Reset(_cues[_index], time);
            _paused = true;
            _pauseOnNextScene = false;
            ClearInputs();
            Publish();
        }

        public void RequestCancel() => _reqCancel = true;
        public void RequestSkipScene() => _reqSkipScene = true;
        public void RequestSkipAll() => _reqSkipAll = true;
        public void RequestPause() => _reqPause = true;
        public void RequestResume() => _reqResume = true;

        public void RequestRate(float rate)
        {
            if (!PlaybackSettings.IsAllowed(rate)) throw new ArgumentOutOfRangeException(nameof(rate), rate, "허용되지 않은 속도");
            _reqRate = rate;
        }

        /// <summary>앱이 백그라운드로 갔다. 복귀해도 일시정지를 유지한다 (FR-54).</summary>
        public void OnApplicationBackground() => _reqPause = true;

        /// <summary>
        /// 키 모먼트 선택 결과. 결과가 같으면 둘 다 null. 결과가 바뀌면 현재 장면 뒤의 목록을 교체하고 그 첫 장면부터 재생한다 (FR-22).
        /// </summary>
        public void ResolveKeyMoment(HighlightScene[] newFollowingScenes, PlaybackCue[][] newFollowingCues)
        {
            if (State != SequencerState.KeyMomentWait) throw new InvalidOperationException("키 모먼트 대기 중이 아니다");
            if ((newFollowingScenes == null) != (newFollowingCues == null)) throw new ArgumentException("장면과 큐는 함께 넘긴다");
            _keyMomentDone = true;
            bool replaced = newFollowingScenes != null;
            if (replaced) ReplaceFollowing(newFollowingScenes, newFollowingCues);

            if (_skipAllMode)
            {
                AdvanceSkipAll();
                return;
            }
            if (replaced) _sceneFinished = true;
            Clock.SetUserRate(_settings.Rate);
            if (_pauseOnNextScene)
            {
                _pauseOnNextScene = false;
                _paused = true;
            }
            State = SequencerState.Playing;
            PublishAudio();
        }

        void ReplaceFollowing(HighlightScene[] scenes, PlaybackCue[][] cues)
        {
            int keep = _index + 1;
            var s = new HighlightScene[keep + scenes.Length];
            var c = new PlaybackCue[keep + cues.Length][];
            Array.Copy(_scenes, s, keep);
            Array.Copy(_cues, c, keep);
            Array.Copy(scenes, 0, s, keep, scenes.Length);
            Array.Copy(cues, 0, c, keep, cues.Length);
            _scenes = s;
            _cues = c;
        }

        public void Tick(double realDt)
        {
            switch (State)
            {
                case SequencerState.Loading:
                    bool cancel = _reqCancel;
                    ClearInputs();
                    if (cancel)
                    {
                        Wants3D = false;
                        State = SequencerState.Cancelled;
                    }
                    return;
                case SequencerState.Playing:
                case SequencerState.Timeline:
                    break;
                default:
                    ClearInputs();
                    return;
            }

            if (ProcessSkips())
            {
                ClearInputsExceptPause();
                Publish();
                return;
            }

            if (_sceneFinished)
            {
                _sceneFinished = false;
                ApplyRateRequest(deferred: true);
                DeferPauseAcrossTransition();
                ClearInputsExceptPause();
                FinishScene();
                Publish();
                return;
            }

            if (State == SequencerState.Timeline)
            {
                ApplyRateRequest(deferred: true);
                DeferPauseAcrossTransition();
                ClearInputsExceptPause();
                TickTimeline(realDt);
                Publish();
                return;
            }

            var scene = _scenes[_index];
            bool crossesEnd = !_paused && Clock.Peek(realDt) >= scene.End;
            ApplyRateRequest(deferred: crossesEnd);
            if (crossesEnd) DeferPauseAcrossTransition();
            else ApplyPause();
            ClearInputsExceptPause();

            if (!_paused) AdvanceScene(realDt, scene);
            Publish();
        }

        void AdvanceScene(double realDt, in HighlightScene scene)
        {
            bool stopForKeyMoment = scene.IsKeyMoment && !_keyMomentDone && Clock.Peek(realDt) >= scene.KeyMomentTime;
            double limit = stopForKeyMoment ? Math.Max(Clock.Position, Math.Min(scene.KeyMomentTime, scene.End)) : scene.End;
            Clock.Advance(realDt, limit);
            _dispatcher.FireUpTo(Clock.Position, _audio);
            if (stopForKeyMoment)
            {
                EnterKeyMoment(scene, textOnly: false);
                return;
            }
            if (Clock.Position >= scene.End) _sceneFinished = true;
        }

        /// <returns>이번 프레임 처리가 끝났으면 true.</returns>
        bool ProcessSkips()
        {
            if (_reqSkipAll)
            {
                ApplyRateRequest(deferred: true);
                DeferPauseAcrossTransition();
                BeginSkipAll();
                return true;
            }
            if (!_reqSkipScene) return false;
            if (State == SequencerState.Timeline)
            {
                _gapRemaining = 0d;
                return false;
            }

            ApplyRateRequest(deferred: true);
            DeferPauseAcrossTransition();
            var scene = _scenes[_index];
            if (scene.IsKeyMoment && !_keyMomentDone)
            {
                // 039 FR-23: 건너뛰어도 키 모먼트 시점에서 멈춘다.
                Clock.JumpTo(Math.Max(Clock.Position, Math.Min(scene.KeyMomentTime, scene.End)));
                _dispatcher.Reset(_cues[_index], Clock.Position);
                EnterKeyMoment(scene, textOnly: false);
                return true;
            }
            _sceneFinished = false;
            FinishScene();
            return true;
        }

        void BeginSkipAll()
        {
            _skipAllMode = true;
            Wants3D = false;
            _sceneFinished = false;
            AdvanceSkipAll();
        }

        void AdvanceSkipAll()
        {
            for (; _index < _scenes.Length; _index++, _keyMomentDone = false)
            {
                var s = _scenes[_index];
                if (!s.IsKeyMoment || _keyMomentDone) continue;
                EnterKeyMoment(s, textOnly: true);
                return;
            }
            _index = _scenes.Length - 1;
            EnterResults();
        }

        void EnterKeyMoment(in HighlightScene scene, bool textOnly)
        {
            State = SequencerState.KeyMomentWait;
            _keyMoments.RequestChoice(scene.Id, textOnly ? scene.KeyMomentTime : Clock.Position, textOnly);
            PublishAudio();
        }

        void FinishScene()
        {
            int next = _index + 1;
            if (next >= _scenes.Length)
            {
                EnterResults();
                return;
            }
            _index = next;
            _keyMomentDone = false;
            if (_scenes[next].IsReplay || _timelineGap <= 0d)
            {
                BeginCurrentScene();
                return;
            }
            State = SequencerState.Timeline;
            _gapRemaining = _timelineGap;
            _timelineExitSent = false;
            _frames.EnterTimeline();
        }

        void TickTimeline(double realDt)
        {
            _gapRemaining -= realDt;
            if (_gapRemaining > realDt) return;
            if (!_timelineExitSent)
            {
                _timelineExitSent = true;
                _frames.ExitTimeline();
            }
            if (_gapRemaining <= 0d) BeginCurrentScene();
        }

        void BeginCurrentScene()
        {
            var s = _scenes[_index];
            Clock.BeginScene(s.Start, s.IsReplay, _settings.Rate);
            _keyMomentDone = false;
            _dispatcher.Reset(_cues[_index], s.Start);
            _camera.OnSceneBegin(s);
            State = SequencerState.Playing;
            if (_pauseOnNextScene)
            {
                _pauseOnNextScene = false;
                _paused = true;
            }
            _dispatcher.FireUpTo(s.Start, _audio);
        }

        void EnterResults()
        {
            Wants3D = false;
            State = SequencerState.Results;
            _frames.EnterMenu();
            PublishAudio();
        }

        void ApplyRateRequest(bool deferred)
        {
            if (float.IsNaN(_reqRate)) return;
            _settings.Rate = _reqRate;
            if (!deferred) Clock.SetUserRate(_reqRate);
            _reqRate = float.NaN;
        }

        void ApplyPause()
        {
            if (_reqPause) _paused = true;
            if (_reqResume) _paused = false;
            _reqPause = _reqResume = false;
        }

        void DeferPauseAcrossTransition()
        {
            if (_reqPause) _pauseOnNextScene = true;
            if (_reqResume) _pauseOnNextScene = false;
            _reqPause = _reqResume = false;
        }

        void ClearInputs()
        {
            ClearInputsExceptPause();
            _reqPause = _reqResume = false;
            _reqRate = float.NaN;
        }

        void ClearInputsExceptPause()
        {
            _reqSkipScene = _reqSkipAll = _reqCancel = false;
        }

        void Publish()
        {
            if (State != SequencerState.Playing && State != SequencerState.Timeline) { PublishAudio(); return; }
            if (_index < 0 || _index >= _scenes.Length) return;
            _hud.OnStatus(new PlaybackStatus(_index, _scenes.Length, Clock.IsReplay, _camera.IsReverseAngle, _paused,
                _settings.Rate, Clock.EffectiveRate, Clock.Position));
            PublishAudio();
        }

        void PublishAudio()
        {
            bool paused = _paused || State != SequencerState.Playing;
            float rate = Clock.EffectiveRate;
            if (rate == _lastAudioRate && paused == _lastAudioPaused) return;
            _lastAudioRate = rate;
            _lastAudioPaused = paused;
            _audio.OnPlaybackState(rate, paused);
        }
    }
}
