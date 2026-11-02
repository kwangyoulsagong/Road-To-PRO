using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoadToPro.Verification.Editor
{
    /// <summary>
    /// 디바이스팜용 macOS 개발 빌드 (specs/000 T27). 개발 빌드여야 렌더 카운터(드로우콜·삼각형)가 잡힌다.
    /// 배치: Unity -batchmode -quit -executeMethod RoadToPro.Verification.Editor.FarmBuild.BuildMac -farmBuildOut Builds/Farm/RoadToProFarm.app
    /// 종료 코드 0 성공, 1 실패.
    /// </summary>
    public static class FarmBuild
    {
        public const string DefaultOut = "Builds/Farm/RoadToProFarm.app";

        static readonly string[] FarmScenes = { FarmSampleSetup.ScenePath };

        public static void BuildMac()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-farmBuildOut");
            string outPath = i >= 0 && i + 1 < args.Length ? args[i + 1] : DefaultOut;

            // 게임 씬(빌드 설정)도 함께 넣어 이후 스펙의 E2E가 같은 빌드로 돈다.
            var scenes = FarmScenes
                .Concat(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path))
                .Distinct()
                .ToArray();

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var ok = report.summary.result == BuildResult.Succeeded;
            Debug.Log($"[farm] 빌드 {report.summary.result}: {outPath} ({report.summary.totalTime.TotalSeconds:0}s, 오류 {report.summary.totalErrors})");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
