using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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

        /// <summary>
        /// 시뮬 벤치 플레이어 (specs/006 T20): 릴리스 빌드, 스크립팅 백엔드는 -farmBackend il2cpp|mono.
        /// 백엔드는 빌드 동안만 바꾸고 finally에서 되돌린다 — ProjectSettings 커밋 변경 없음 (2026-11-03 플랜 승인).
        /// 배치: Unity -batchmode -quit -executeMethod RoadToPro.Verification.Editor.FarmBuild.BuildMacBench -farmBuildOut &lt;app&gt; -farmBackend il2cpp
        /// </summary>
        public static void BuildMacBench()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string k, string d) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            var outPath = Arg("-farmBuildOut", "Builds/FarmBench/RoadToProBench.app");
            var backend = Arg("-farmBackend", "il2cpp") == "mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;
            var target = NamedBuildTarget.Standalone;
            var before = PlayerSettings.GetScriptingBackend(target);
            var ok = false;
            try
            {
                if (before != backend) PlayerSettings.SetScriptingBackend(target, backend);
                var opts = new BuildPlayerOptions
                {
                    scenes = FarmScenes,
                    locationPathName = outPath,
                    target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(opts);
                ok = report.summary.result == BuildResult.Succeeded;
                Debug.Log($"[farm] 벤치 빌드({backend}) {report.summary.result}: {outPath} ({report.summary.totalTime.TotalSeconds:0}s, 오류 {report.summary.totalErrors})");
            }
            finally
            {
                if (PlayerSettings.GetScriptingBackend(target) != before) PlayerSettings.SetScriptingBackend(target, before);
                // 에디터가 종료할 때 ProjectSettings.asset을 다시 쓸 수 있다 — farm.py bench가 빌드 전 바이트로 되돌린다
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
