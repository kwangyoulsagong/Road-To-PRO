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
                if (spec != "006" && spec != "008" && spec != "009" && spec != "010" && spec != "011" && spec != "013" && spec != "058" && spec != "072" && spec != "070" && spec != "071" && spec != "081" && spec != "082" && spec != "083" && spec != "084" && spec != "086" && spec != "087" && spec != "007" && spec != "012" && spec != "073" && spec != "074" && spec != "059") throw new ArgumentException($"벤치 없는 스펙: {spec}");
                if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) throw new ArgumentException($"-simBenchData 폴더 없음: {data}");
                Directory.CreateDirectory(outDir);
                var metrics = spec == "013" ? new ScenarioBenchWorkloads(data).RunAll() : spec == "009" ? new ShapeBenchWorkloads(data).RunAll()
                    : spec == "011" ? new GoalkeeperBenchWorkloads(data).RunAll()
                    : spec == "008" ? new ActionBenchWorkloads(data).RunAll()
                    : spec == "010" ? new DecisionBenchWorkloads(data).RunAll()
                    : spec == "072" ? new OnBallBenchWorkloads(data).RunAll()
                    : spec == "070" ? new DuelBenchWorkloads(data).RunAll()
                    : spec == "071" ? new ShotAerialBenchWorkloads(data).RunAll()
                    : spec == "081" || spec == "082" || spec == "083" || spec == "084" || spec == "086" || spec == "087" || spec == "059" ? Merge(new ShotAerialBenchWorkloads(data).RunAll(), new OnBallBenchWorkloads(data).RunAll()) // 081: 071 실경기 틱·결정 틱 + 072 소유자 결정(박스 지점 크로스 후보 포함). 059: 그물 면 접촉(BallStepper) 뒤 같은 실경기 틱 회귀
                    : spec == "007" ? Merge(Merge(new ShotAerialBenchWorkloads(data).RunAll(), new OnBallBenchWorkloads(data).RunAll()), new PitchControlBenchWorkloads(data).RunAll()) // 007: 실경기 틱·결정 틱·소유자 결정(궤적 재평가 포함) + 피치 컨트롤 질의 단건
                    : spec == "012" ? Merge(Merge(new ShotAerialBenchWorkloads(data).RunAll(), new OnBallBenchWorkloads(data).RunAll()), new RefereeBenchWorkloads(data).RunAll()) // 012: 실경기 틱·결정 틱·소유자 결정(심판 단계 연결 뒤 회귀) + 심판 판정 단건
                    : spec == "073" ? Merge(Merge(new ShotAerialBenchWorkloads(data).RunAll(), new OnBallBenchWorkloads(data).RunAll()), new KeeperLeaveBenchWorkloads(data).RunAll()) // 073: 실경기 틱·결정 틱·소유자 결정(GK 이탈 판단 연결 뒤 회귀) + GK 판단 단건
                    : spec == "074" ? Merge(Merge(new ShotAerialBenchWorkloads(data).RunAll(), new OnBallBenchWorkloads(data).RunAll()), new PenaltyDistributionBenchWorkloads(data).RunAll()) // 074: 실경기 틱·결정 틱·소유자 결정(배급·PK 연결 뒤 회귀) + 배급·PK 판단 단건
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

        /// <summary>081: 두 워크로드 지표 합치기 (같은 키는 뒤가 이긴다).</summary>
        static Dictionary<string, double> Merge(Dictionary<string, double> a, Dictionary<string, double> b)
        {
            foreach (var kv in b) a[kv.Key] = kv.Value;
            return a;
        }
    }
}
