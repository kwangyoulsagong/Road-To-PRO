using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// 시뮬 벤치 플레이어 진입점 (specs/006 T20). <c>-simBench 006 -simBenchOut &lt;폴더&gt; -simBenchData &lt;레포/Data&gt;</c>가 있을 때만
    /// 워크로드를 돌려 bench.json을 쓰고 종료한다 (종료 코드 0 성공, 2 오류). 판정은 farm.py bench가 Data/perf의 il2cpp 예산으로 한다.
    /// </summary>
    public static class SimBench
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var argv = Environment.GetCommandLineArgs();
            var spec = Arg(argv, "-simBench");
            if (spec == null) return;
            var outDir = Arg(argv, "-simBenchOut") ?? "bench-out";
            var data = Arg(argv, "-simBenchData");
            if (spec == "043" || spec == "056" || spec == "057" || spec == "055" || spec == "054" || spec == "041" || spec == "042") // 056·057은 같은 워크로드를 바뀐 모델로 (specs/056·057 AC-10), 055는 원형 블렌드 포함 (AC-13)
            {
                // 애니메이션 벤치: 씬을 열어야 해서 다음 프레임들에 걸쳐 돈다 (specs/043 T27)
                Directory.CreateDirectory(outDir);
                AnimBench.Start(outDir, spec, m => Write(spec, outDir, m), e => Fail(outDir, e));
                return;
            }
            try
            {
                if (spec != "006" && spec != "009" && spec != "010" && spec != "011" && spec != "013" && spec != "058") throw new ArgumentException($"벤치 없는 스펙: {spec}");
                if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) throw new ArgumentException($"-simBenchData 폴더 없음: {data}");
                Directory.CreateDirectory(outDir);
                var metrics = spec == "013" ? new ScenarioBenchWorkloads(data).RunAll() : spec == "009" ? new ShapeBenchWorkloads(data).RunAll()
                    : spec == "011" ? new GoalkeeperBenchWorkloads(data).RunAll()
                    : spec == "010" ? new DecisionBenchWorkloads(data).RunAll()
                    : spec == "058" ? new MatchBenchWorkloads(data).RunLiveHighlight() : new MatchBenchWorkloads(data).RunAll();
                var doc = new Dictionary<string, object>
                {
                    { "spec", spec },
                    { "backend", Backend() },
                    { "unity", Application.unityVersion },
                    { "cpu", SystemInfo.processorType },
                    { "os", SystemInfo.operatingSystem },
                    { "development", Debug.isDebugBuild },
                    { "metrics", ToObjects(metrics) },
                };
                File.WriteAllText(Path.Combine(outDir, "bench.json"), MiniJson.Serialize(doc));
                Console.WriteLine("[bench] 완료 " + Path.Combine(outDir, "bench.json"));
                Application.Quit(0);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("[bench] 오류: " + e);
                try { File.WriteAllText(Path.Combine(outDir, "bench-error.txt"), e.ToString()); } catch (Exception) { }
                Application.Quit(2);
            }
        }

        static void Write(string spec, string outDir, Dictionary<string, double> metrics)
        {
            var doc = new Dictionary<string, object>
            {
                { "spec", spec },
                { "backend", Backend() },
                { "unity", Application.unityVersion },
                { "cpu", SystemInfo.processorType },
                { "os", SystemInfo.operatingSystem },
                { "development", Debug.isDebugBuild },
                { "metrics", ToObjects(metrics) },
            };
            File.WriteAllText(Path.Combine(outDir, "bench.json"), MiniJson.Serialize(doc));
            Console.WriteLine("[bench] 완료 " + Path.Combine(outDir, "bench.json"));
            Application.Quit(0);
        }

        static void Fail(string outDir, Exception e)
        {
            Console.Error.WriteLine("[bench] 오류: " + e);
            try { File.WriteAllText(Path.Combine(outDir, "bench-error.txt"), e.ToString()); } catch (Exception) { }
            Application.Quit(2);
        }

        static Dictionary<string, object> ToObjects(Dictionary<string, double> m)
        {
            var d = new Dictionary<string, object>();
            foreach (var kv in m) d[kv.Key] = kv.Value;
            return d;
        }

        static string Backend()
        {
#if ENABLE_IL2CPP
            return "il2cpp";
#else
            return "mono";
#endif
        }

        static string Arg(string[] a, string key)
        {
            var i = Array.IndexOf(a, key);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }
}
