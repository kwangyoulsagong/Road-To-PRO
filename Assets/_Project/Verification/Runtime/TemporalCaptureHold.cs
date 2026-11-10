using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 캡처 안정화 동안 시간적 업스케일(STP·TAA)의 지터 위상을 고정하고 히스토리를 버린다 (specs/048 T06).
    /// 지터가 돌면 정지 장면도 매 프레임 서브픽셀만큼 달라져 바이트 비교가 안 된다. STP는 jitterScale을 무시하므로
    /// URP 내부 `jitterFrameCountOffset`(결정론 테스트용 시드 오프셋)을 리플렉션으로 맞춰 지터 프레임 번호를 상수로 둔다.
    /// 검증 전용 — 게임 코드는 쓰지 않는다. 캡처가 끝나면 원래 값으로 돌린다.
    /// </summary>
    public sealed class TemporalCaptureHold
    {
        const int PhaseFrame = 0;
        static readonly FieldInfo TaaField = typeof(UniversalAdditionalCameraData).GetField("m_TaaSettings", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo OffsetField = typeof(TemporalAA.Settings).GetField("jitterFrameCountOffset", BindingFlags.Instance | BindingFlags.NonPublic);

        readonly List<(UniversalAdditionalCameraData data, int offset)> _held = new List<(UniversalAdditionalCameraData, int)>();

        public static bool Supported => TaaField != null && OffsetField != null;

        /// <summary>URP STP 블루 노이즈 텍스처 수 (PostProcessData.blueNoise16LTex, Time.frameCount로 돈다).</summary>
        public const int NoisePeriod = 32;

        /// <summary>현재 렌더 에셋이 STP면 노이즈 위상이 같은 프레임(32 간격)끼리 비교해야 한다.</summary>
        public static bool UsesStp =>
            UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp &&
            urp.upscalingFilter == UpscalingFilterSelection.STP;

        /// <summary>매 안정화 프레임마다 부른다: 다음 렌더 프레임의 지터 번호를 고정하고 히스토리를 버린다.</summary>
        public void Tick()
        {
            if (!Supported) return;
            foreach (var cam in Camera.allCameras)
            {
                if (!cam.TryGetComponent(out UniversalAdditionalCameraData data)) continue;
                bool known = false;
                foreach (var h in _held)
                    if (h.data == data) { known = true; break; }
                if (!known) _held.Add((data, GetOffset(data)));
                SetOffset(data, PhaseFrame - Time.frameCount);
                data.resetHistory = true;
            }
        }

        public void Release()
        {
            foreach (var (data, offset) in _held)
                if (data != null) SetOffset(data, offset);
            _held.Clear();
        }

        static int GetOffset(UniversalAdditionalCameraData data) => (int)OffsetField.GetValue(TaaField.GetValue(data));

        static void SetOffset(UniversalAdditionalCameraData data, int offset)
        {
            object boxed = TaaField.GetValue(data);
            OffsetField.SetValue(boxed, offset);
            TaaField.SetValue(data, boxed);
        }
    }
}
