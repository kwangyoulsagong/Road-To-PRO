using System;
using RoadToPro.Presentation.Rendering.Logic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadToPro.Presentation.Rendering
{
    /// <summary>
    /// 품질 프리셋 적용 (specs/048 FR-01·30·31). 순서 고정: 품질 레벨 → 렌더 스케일 → (카메라) 후처리·STP 조건.
    /// 대역 프리셋(Medium/High)은 기존 에셋을 그대로 두고 레벨·렌더 스케일만 바꾼다 — 기존 기준 이미지 불변.
    /// P1은 하이라이트 진입 때 적용하고 재생 중 프리셋 변경은 후속 (FR-03).
    /// </summary>
    public static class RenderQualityApplier
    {
        static RenderPipelineAsset[] _levelAssets;

        /// <summary>실행 중 처음 한 번 각 품질 레벨의 원래 URP 에셋을 기억한다 (대역 덮어쓰기 전).</summary>
        static void RememberLevelAssets()
        {
            if (_levelAssets != null && _levelAssets.Length == QualitySettings.names.Length) return;
            _levelAssets = new RenderPipelineAsset[QualitySettings.names.Length];
            for (int i = 0; i < _levelAssets.Length; i++) _levelAssets[i] = QualitySettings.GetRenderPipelineAssetAt(i);
        }

        static readonly System.Collections.Generic.HashSet<Camera> Modified = new System.Collections.Generic.HashSet<Camera>();

        /// <summary>마지막으로 적용한 프리셋. 적용 전에는 null.</summary>
        public static RenderPreset Current { get; private set; }

        /// <summary>
        /// 품질 레벨과 렌더 스케일을 적용한다. 이 플랫폼에서 레벨이 꺼져 있으면 <paramref name="fallbackAsset"/>로 같은 이름의 URP 에셋을 찾는다.
        /// </summary>
        /// <param name="qualityLevel">null이면 프리셋의 레벨 이름.</param>
        /// <param name="renderScale">null이면 프리셋 값. 범위는 URP가 0.1~2로 자른다.</param>
        /// <returns>실제로 쓰게 된 URP 에셋 (없으면 null + 진단).</returns>
        public static UniversalRenderPipelineAsset ApplyGlobal(RenderPreset preset, string qualityLevel, float? renderScale,
            Func<string, RenderPipelineAsset> fallbackAsset, Action<string> diagnostic)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            string level = string.IsNullOrEmpty(qualityLevel) ? preset.QualityLevel : qualityLevel;
            int idx = Array.IndexOf(QualitySettings.names, level);
            RememberLevelAssets();
            if (idx >= 0)
            {
                QualitySettings.SetQualityLevel(idx, true);
                // 앞서 대역 에셋으로 덮었을 수 있으니 레벨 원래 에셋으로 되돌린다
                if (_levelAssets[idx] != null && QualitySettings.renderPipeline != _levelAssets[idx])
                    QualitySettings.renderPipeline = _levelAssets[idx];
            }
            else
            {
                // QualitySettings.renderPipeline은 현재 레벨의 에셋을 이번 실행 동안만 바꾼다 (디스크 불변).
                // 밑 레벨을 고정해야 레벨별 값(이방성 필터 등)이 실행마다 같다.
                int baseIdx = Array.IndexOf(QualitySettings.names, RenderPresetTable.FallbackBaseLevel);
                if (baseIdx >= 0) QualitySettings.SetQualityLevel(baseIdx, true);
                var rp = fallbackAsset?.Invoke(level);
                if (rp == null)
                    diagnostic?.Invoke($"품질 '{level}': 품질 레벨도 렌더 에셋도 없음 (레벨: {string.Join(", ", QualitySettings.names)})");
                else
                    QualitySettings.renderPipeline = rp;
            }

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.renderScale = renderScale ?? preset.RenderScale;
                if (!preset.IsStandIn && preset.Upscaler == Upscaler.Stp && urp.upscalingFilter != UpscalingFilterSelection.STP)
                    diagnostic?.Invoke($"프리셋 {preset.Id}: URP 에셋 {urp.name}의 업스케일러가 STP가 아님 ({urp.upscalingFilter})");
            }
            Current = preset;
            return urp;
        }

        /// <summary>
        /// 카메라 설정 (FR-31): 실제 프리셋은 후처리를 켜서 톤맵·블룸·STP가 돈다(STP는 카메라 후처리·스택 없음·동적 해상도 꺼짐이 조건).
        /// 대역 프리셋은 카메라를 건드리지 않는다.
        /// </summary>
        public static void ApplyCamera(RenderPreset preset, Camera camera)
        {
            if (preset == null || camera == null) return;
            if (preset.IsStandIn)
            {
                // 실제 프리셋이 켰던 카메라만 원래(후처리 끔)로 되돌린다 — 대역 화면을 048 전과 같게
                if (Modified.Remove(camera)) camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                return;
            }
            Modified.Add(camera);
            camera.allowDynamicResolution = false;
            camera.allowMSAA = false;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None; // STP가 요청되면 URP가 TAA 경로를 강제한다
            data.resetHistory = true;
        }

        /// <summary>3D를 내릴 때 실제 프리셋이 켰던 카메라 후처리를 끈다 (메뉴 화면 = UI만, FR-12 메뉴 상태).</summary>
        public static void RevertCamera(Camera camera)
        {
            if (camera != null && Modified.Remove(camera)) camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        }

        /// <summary>장면 컷마다 STP 히스토리를 버린다 — 이전 장면 잔상 방지 (plan 버그 방지).</summary>
        public static void ResetHistory(Camera camera)
        {
            if (camera == null || Current == null || Current.IsStandIn) return;
            camera.GetUniversalAdditionalCameraData().resetHistory = true;
        }

        /// <summary>현재 품질 레벨 이름 (로그·장치 기록용).</summary>
        public static string CurrentLevelName => QualitySettings.names[QualitySettings.GetQualityLevel()];
    }
}
