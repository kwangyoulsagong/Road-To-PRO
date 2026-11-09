namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>HUD에 넘기는 재생 상태 (specs/041 FR-40·41).</summary>
    public readonly struct PlaybackStatus
    {
        public readonly int SceneIndex, SceneCount;
        public readonly bool IsReplay, IsReverseAngle, Paused;
        public readonly float UserRate, EffectiveRate;
        public readonly double SimTime;

        public PlaybackStatus(int sceneIndex, int sceneCount, bool isReplay, bool isReverseAngle, bool paused,
            float userRate, float effectiveRate, double simTime)
        {
            SceneIndex = sceneIndex;
            SceneCount = sceneCount;
            IsReplay = isReplay;
            IsReverseAngle = isReverseAngle;
            Paused = paused;
            UserRate = userRate;
            EffectiveRate = effectiveRate;
            SimTime = simTime;
        }
    }

    /// <summary>장면 목록 공급 (018이 구현).</summary>
    public interface ISceneListSource
    {
        HighlightScene[] GetScenes();
    }

    /// <summary>키 모먼트 선택 요청 (019가 구현). 답은 <see cref="HighlightSequencer.ResolveKeyMoment"/>로 돌려준다.</summary>
    public interface IKeyMomentHandler
    {
        /// <param name="sceneId">장면 id. FR-57 복구 위치로도 쓴다.</param>
        /// <param name="simTime">정지한 시뮬 시각.</param>
        /// <param name="textOnly">모두 건너뛰기 뒤라 3D 없이 텍스트 + 미니맵으로 보이는 경우 (FR-21).</param>
        void RequestChoice(int sceneId, double simTime, bool textOnly);
    }

    /// <summary>재생 상태 표시 (039가 구현).</summary>
    public interface IHighlightHud
    {
        void OnStatus(in PlaybackStatus status);
        /// <summary>로딩 진행률 0~1 (FR-30).</summary>
        void OnLoadingProgress(float progress);
    }

    /// <summary>장면 시작 통지와 리버스 앵글 플래그 (042가 구현).</summary>
    public interface IHighlightCamera
    {
        bool IsReverseAngle { get; }
        void OnSceneBegin(in HighlightScene scene);
    }

    /// <summary>프레임·렌더 정책 (048이 구현, FR-32·33·34).</summary>
    public interface IFramePolicy
    {
        void EnterHighlight();
        void EnterTimeline();
        /// <summary>다음 장면 첫 프레임 1프레임 전에 부른다 (048 FR-17).</summary>
        void ExitTimeline();
        void EnterMenu();
    }

    /// <summary>재생 상태와 큐 전달 (046이 구현, FR-15·55).</summary>
    public interface IHighlightAudio
    {
        void OnPlaybackState(float rate, bool paused);
        void OnCue(in PlaybackCue cue);
    }

    /// <summary>진단 (FR-53). 검증에서는 호출되면 실패로 본다.</summary>
    public interface IHighlightDiagnostics
    {
        void OnSceneUnavailable(int sceneId);
    }
}
