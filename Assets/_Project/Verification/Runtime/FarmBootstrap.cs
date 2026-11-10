using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using RoadToPro.Presentation.Rendering;
using RoadToPro.Presentation.Rendering.Logic;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 디바이스팜 실행 진입점 (specs/000 FR-22). -farmScenario가 있을 때만 동작한다.
    /// 인자 파싱 → 로그 시작 → 프로필 적용(품질·렌더 스케일·목표 fps·테마·언어·배터리 절약) → 단계 실행기 생성.
    /// </summary>
    public static class FarmBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            FarmArgs args;
            try
            {
                args = FarmArgs.Parse(Environment.GetCommandLineArgs());
            }
            catch (FormatException e)
            {
                Console.Error.WriteLine("[farm] 인자 오류: " + e.Message);
                Application.Quit(2);
                return;
            }
            if (!args.IsFarmRun) return;

            Directory.CreateDirectory(args.OutDir);
            Directory.CreateDirectory(Path.Combine(args.OutDir, "captures"));
            FarmLog.Start(Path.Combine(args.OutDir, "logs.jsonl"));
            FarmContext.Args = args;

            Apply(args);
            WriteDevice(args);

            var go = new GameObject("FarmRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ScenarioRunner>();
        }

        static void Apply(FarmArgs a)
        {
            // 048: 프리셋 적용기 경유 (품질 레벨 → 렌더 스케일). 이 플랫폼에 꺼진 레벨(Mobile)은 같은 이름의 URP 에셋으로.
            var preset = RenderPresetTable.Find(a.Quality, m => Debug.LogWarning("[farm] " + m));
            RenderQualityApplier.ApplyGlobal(preset, a.UnityQualityLevel, a.RenderScale,
                name => Resources.Load<FarmRenderAssets>("FarmRenderAssets")?.Find(name),
                m => Debug.LogError("[farm] " + m));
            // 하이라이트 밖 E2E 화면(경기장·선수)도 같은 프리셋 카메라 설정으로 찍는다
            SceneManager.sceneLoaded += (scene, _) =>
            {
                foreach (var root in scene.GetRootGameObjects())
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                    RenderQualityApplier.ApplyCamera(RenderQualityApplier.Current, cam);
            };
            // 검증 중 창이 포커스를 잃어도 멈추지 않게 (기본값은 비포커스 시 일시정지 — 성능 구간이 멈춘다).
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = a.TargetFps;

            Debug.Log($"[farm] 프로필 {a.ProfileId}: {a.Width}x{a.Height} 배율 {a.UiScale} 품질 {a.Quality}({RenderQualityApplier.CurrentLevelName}/{GraphicsSettings.currentRenderPipeline?.name}) " +
                      $"렌더스케일 {(GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset)?.renderScale} 업스케일 {(GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset)?.upscalingFilter} 목표fps {Application.targetFrameRate} " +
                      $"테마 {a.Theme} 언어 {a.Lang} 배터리절약 {a.BatterySaver} 시드 {a.Seed}");
        }

        static void WriteDevice(FarmArgs a)
        {
            var d = new Dictionary<string, object>
            {
                { "cpu", SystemInfo.processorType },
                { "cpuCount", SystemInfo.processorCount },
                { "gpu", SystemInfo.graphicsDeviceName },
                { "graphicsApi", SystemInfo.graphicsDeviceType.ToString() },
                { "os", SystemInfo.operatingSystem },
                { "memoryMb", SystemInfo.systemMemorySize },
                { "battery", SystemInfo.batteryStatus.ToString() },
                { "unity", Application.unityVersion },
                { "qualityLevel", QualitySettings.names[QualitySettings.GetQualityLevel()] },
                { "renderPipeline", GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "" },
                { "renderScale", (GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset)?.renderScale ?? -1f },
                { "targetFps", Application.targetFrameRate },
            };
            File.WriteAllText(Path.Combine(a.OutDir, "device.json"), MiniJson.Serialize(d));
        }
    }
}
