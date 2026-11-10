using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Rendering.Logic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadToPro.Presentation.Rendering
{
    /// <summary>
    /// 하이라이트 프레임 정책 (specs/048 FR-12·13·14·17). 041 <see cref="IFramePolicy"/>의 실제 구현.
    /// 하이라이트 60 / 배터리 절약 30 / 메뉴 30. 타임라인 구간은 방송 카메라를 끄고 마지막 프레임을 UI 뒤에 고정한다.
    /// 041 시퀀서가 다음 장면 1프레임 전에 <see cref="ExitTimeline"/>을 부르므로 그때 카메라를 켠다.
    /// 재생 시각은 실시간 시계라 fps와 무관하다 (AC-04).
    /// </summary>
    public sealed class HighlightFramePolicy : IFramePolicy
    {
        readonly FrozenFrame _frozen;
        Camera _camera;
        bool _batterySaver;

        public RenderState State { get; private set; } = RenderState.Menu;
        public int TargetFps { get; private set; } = FrameCap.MenuFps;
        public bool BatterySaver => _batterySaver;
        /// <summary>타임라인 구간 동안 방송 카메라가 그린 프레임 수 (고정 이미지 렌더 제외, AC-17).</summary>
        public int TimelineCameraFrames { get; private set; }
        /// <summary>이번 재생에서 지난 타임라인 구간 수.</summary>
        public int TimelineCount { get; private set; }
        public bool Frozen => _frozen != null && _frozen.Visible;

        /// <param name="frozen">null이면 고정 없이 카메라만 끈다 (UI가 화면을 덮는 경우).</param>
        public HighlightFramePolicy(FrozenFrame frozen, bool batterySaver)
        {
            _frozen = frozen;
            _batterySaver = batterySaver;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        /// <summary>하이라이트 재생 카메라. 재생마다 다시 넣는다.</summary>
        public void SetCamera(Camera camera) => _camera = camera;

        /// <summary>사용자 토글 (FR-13). 다음 프레임부터 새 상한, 재생은 끊기지 않는다.</summary>
        public void SetBatterySaver(bool on)
        {
            _batterySaver = on;
            Apply(false);
        }

        public void EnterHighlight()
        {
            State = RenderState.Highlight;
            TimelineCount = 0;
            Apply(false);
        }

        public void EnterTimeline()
        {
            State = RenderState.Timeline;
            TimelineCount++;
            Apply(true);
        }

        public void ExitTimeline()
        {
            State = RenderState.Highlight;
            Apply(false);
        }

        public void EnterMenu()
        {
            State = RenderState.Menu;
            Apply(false);
            _frozen?.Release();
        }

        void Apply(bool enteringTimeline)
        {
            var d = FrameCap.Decide(State, _batterySaver);
            TargetFps = d.TargetFps;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = d.TargetFps;
            OnDemandRendering.renderFrameInterval = 1;

            if (d.FreezeLastFrame && enteringTimeline)
            {
                bool frozen = _frozen != null && _camera != null && _frozen.Show(_camera);
                if (_camera != null && (frozen || _frozen == null)) _camera.enabled = false;
            }
            else if (!d.FreezeLastFrame)
            {
                // 상태가 바뀔 때마다 카메라 켜짐을 다시 맞춘다 — 건너뛰기·취소로 구간이 끊겨도 꺼진 채 남지 않게
                if (_camera != null) _camera.enabled = true;
                _frozen?.Hide();
            }
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (State == RenderState.Timeline && cam == _camera && (_frozen == null || !_frozen.Capturing))
                TimelineCameraFrames++;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            _frozen?.Release();
        }
    }
}
