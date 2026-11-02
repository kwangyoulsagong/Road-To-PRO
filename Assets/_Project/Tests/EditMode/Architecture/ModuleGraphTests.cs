using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RoadToPro.Verification;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Architecture
{
    /// <summary>모듈(asmdef) 참조 방향·Unity 비참조·금지 API 검사 (specs/001 AC-01·02, FR-01~04).</summary>
    public class ModuleGraphTests
    {
        static readonly string[] Domain = { "RoadToPro.Core", "RoadToPro.MatchEngine", "RoadToPro.Career", "RoadToPro.WorldGen" };
        static readonly string[] Lower = { "RoadToPro.Core", "RoadToPro.MatchEngine", "RoadToPro.Career", "RoadToPro.WorldGen", "RoadToPro.Data" };

        /// <summary>.claude/rules/10-csharp.md 의존 방향. 키 → 참조해도 되는 RoadToPro 모듈.</summary>
        internal static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            { "RoadToPro.Core", new string[0] },
            { "RoadToPro.MatchEngine", new[] { "RoadToPro.Core" } },
            { "RoadToPro.Career", new[] { "RoadToPro.Core", "RoadToPro.MatchEngine" } },
            { "RoadToPro.WorldGen", new[] { "RoadToPro.Core", "RoadToPro.Career", "RoadToPro.MatchEngine" } },
            { "RoadToPro.Data", new[] { "RoadToPro.Core", "RoadToPro.MatchEngine", "RoadToPro.Career", "RoadToPro.WorldGen" } },
            { "RoadToPro.Presentation", Lower },
            { "RoadToPro.UI", Lower },
            { "RoadToPro.Verification", Lower.Concat(new[] { "RoadToPro.Presentation", "RoadToPro.UI" }).ToArray() },
        };

        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        static Dictionary<string, (string[] refs, bool noEngine, string path)> LoadAsmdefs()
        {
            var result = new Dictionary<string, (string[], bool, string)>();
            foreach (var path in Directory.GetFiles(Path.Combine(Application.dataPath, "_Project"), "*.asmdef", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                var d = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(path));
                var name = (string)d["name"];
                var refs = d.TryGetValue("references", out var r) && r is List<object> l ? l.Cast<string>().ToArray() : new string[0];
                var noEngine = d.TryGetValue("noEngineReferences", out var n) && n is bool b && b;
                result.Add(name, (refs, noEngine, path));
            }
            return result;
        }

        /// <summary>그래프 검사기. 위반 목록을 돌려준다 (빈 목록 = 통과).</summary>
        internal static List<string> CheckGraph(IDictionary<string, string[]> graph)
        {
            var errors = new List<string>();
            foreach (var kv in graph.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Key.StartsWith("RoadToPro.Tests", StringComparison.Ordinal)) continue; // 테스트는 전부 참조 가능
                var allowed = Allowed.TryGetValue(kv.Key, out var a) ? a : null;
                foreach (var r in kv.Value)
                {
                    if (!r.StartsWith("RoadToPro.", StringComparison.Ordinal)) continue;
                    if (r.StartsWith("RoadToPro.Verification", StringComparison.Ordinal) && kv.Key == "RoadToPro.Verification.Editor") continue;
                    if (kv.Key == "RoadToPro.Verification.Editor") continue; // 에디터 도구는 최상위
                    if (allowed == null || Array.IndexOf(allowed, r) < 0)
                        errors.Add($"{kv.Key} → {r} 참조 금지");
                }
            }
            errors.AddRange(FindCycles(graph));
            return errors;
        }

        static IEnumerable<string> FindCycles(IDictionary<string, string[]> graph)
        {
            var state = new Dictionary<string, int>();
            var cycles = new List<string>();
            foreach (var n in graph.Keys.OrderBy(k => k, StringComparer.Ordinal)) Visit(n, new List<string>());
            return cycles;

            void Visit(string n, List<string> stack)
            {
                state.TryGetValue(n, out var s);
                if (s == 2) return;
                if (s == 1)
                {
                    cycles.Add("순환 참조: " + string.Join(" → ", stack.SkipWhile(x => x != n)) + " → " + n);
                    return;
                }
                state[n] = 1;
                stack.Add(n);
                if (graph.TryGetValue(n, out var refs))
                    foreach (var r in refs.Where(graph.ContainsKey)) Visit(r, stack);
                stack.RemoveAt(stack.Count - 1);
                state[n] = 2;
            }
        }

        static Dictionary<string, string[]> CurrentGraph() => LoadAsmdefs().ToDictionary(k => k.Key, v => v.Value.refs);

        [Test]
        public void AsmdefGraph_MatchesAllowedDirections()
        {
            var g = CurrentGraph();
            foreach (var m in Allowed.Keys) Assert.That(g.ContainsKey(m), $"{m} asmdef 없음");
            var errors = CheckGraph(g).Where(e => !e.StartsWith("순환", StringComparison.Ordinal)).ToList();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void AsmdefGraph_IsAcyclic()
        {
            var errors = FindCycles(CurrentGraph()).ToList();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void Checker_RejectsCorePresentationAndMatchEngineCareer()
        {
            var g1 = CurrentGraph();
            g1["RoadToPro.Core"] = g1["RoadToPro.Core"].Concat(new[] { "RoadToPro.Presentation" }).ToArray();
            var e1 = CheckGraph(g1);
            Assert.That(e1.Any(e => e.Contains("RoadToPro.Core → RoadToPro.Presentation")), string.Join("\n", e1));
            // Presentation → … → Core 가 이미 있으므로 순환이 생긴다 = Unity 컴파일 실패
            Assert.That(e1.Any(e => e.StartsWith("순환", StringComparison.Ordinal)), "Core→Presentation은 순환이어야 한다");

            var g2 = CurrentGraph();
            g2["RoadToPro.MatchEngine"] = g2["RoadToPro.MatchEngine"].Concat(new[] { "RoadToPro.Career" }).ToArray();
            var e2 = CheckGraph(g2);
            Assert.That(e2.Any(e => e.Contains("RoadToPro.MatchEngine → RoadToPro.Career")), string.Join("\n", e2));
            Assert.That(e2.Any(e => e.StartsWith("순환", StringComparison.Ordinal)), "MatchEngine→Career는 순환이어야 한다");
        }

        [Test]
        public void CoreModules_HaveNoUnityEngineReferences()
        {
            var defs = LoadAsmdefs();
            var loaded = AppDomain.CurrentDomain.GetAssemblies().ToDictionary(a => a.GetName().Name, a => a, StringComparer.Ordinal);
            foreach (var m in Domain)
            {
                Assert.That(defs[m].noEngine, Is.True, $"{m} asmdef noEngineReferences=false");
                Assert.That(loaded.TryGetValue(m, out var asm), $"{m} 어셈블리가 로드되지 않음 (스크립트가 없나?)");
                var bad = asm.GetReferencedAssemblies().Select(n => n.Name)
                    .Where(n => n.StartsWith("UnityEngine", StringComparison.Ordinal) || n.StartsWith("UnityEditor", StringComparison.Ordinal)
                                || (n.StartsWith("RoadToPro.", StringComparison.Ordinal) && Array.IndexOf(Allowed[m], n) < 0))
                    .ToList();
                Assert.That(bad, Is.Empty, $"{m} 금지 참조: {string.Join(", ", bad)}");
            }
        }

        // FR-04: 코어 모듈 소스 금지 API. 훅(check-pure-core.sh)과 같은 목록 + MathF/Math 직접 호출 + static 가변 필드.
        static readonly (Regex re, string why)[] Forbidden =
        {
            (new Regex(@"using\s+(static\s+)?Unity(Engine|Editor)"), "Unity 참조"),
            (new Regex(@"(^|[^A-Za-z0-9_])(Mathf|Debug)\."), "엔진 수학/로그"),
            (new Regex(@"(^|[^A-Za-z0-9_.])Vector[234](Int)?([^A-Za-z0-9_]|$)"), "엔진 벡터"),
            (new Regex(@"System\.Random|new\s+Random\s*\(|UnityEngine\.Random"), "비시드 난수"),
            (new Regex(@"Guid\.NewGuid|DateTime(Offset)?\.(Now|UtcNow|Today)|Stopwatch|Environment\.TickCount"), "시스템 시계"),
            (new Regex(@"Parallel\.|Task\.Run|ThreadPool\.|new\s+Thread\s*\("), "스레드"),
            (new Regex(@"GetHashCode\(\)\s*%"), "런타임 해시코드"),
            (new Regex(@"\bstatic\s+(?!readonly\b)(?!class\b)(?!partial\b)(?!void\b)(?!extern\b)(?!implicit\b)(?!explicit\b)(?!bool\s+operator)(?!operator\b)[\w<>\[\],\s\.]+\s+_?\w+\s*(=[^=>]|;)"), "static 가변 필드"),
        };

        static readonly Regex DirectMath = new Regex(@"(^|[^A-Za-z0-9_.])(System\.)?(MathF|Math)\.(Sqrt|Sin|Cos|Tan|Atan2|Atan|Asin|Acos|Exp|Log|Log10|Pow)\b");

        internal static List<string> ScanSource(string file, string text)
        {
            var hits = new List<string>();
            var lines = text.Split('\n');
            var isDMath = Path.GetFileName(file) == "DMath.cs";
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)) continue;
                foreach (var (re, why) in Forbidden)
                    if (re.IsMatch(line) && !(why == "static 가변 필드" && (line.Contains(" const ") || line.Contains("=>") || line.Contains("("))))
                        hits.Add($"{file}:{i + 1} [{why}] {t.Trim()}");
                if (!isDMath && DirectMath.IsMatch(line))
                    hits.Add($"{file}:{i + 1} [초월함수는 DMath만] {t.Trim()}");
            }
            return hits;
        }

        [Test]
        public void CoreSources_HaveNoForbiddenApis()
        {
            var hits = new List<string>();
            foreach (var mod in new[] { "Core", "MatchEngine", "Career", "WorldGen" })
            {
                var dir = Path.Combine(Application.dataPath, "_Project", mod);
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                    hits.AddRange(ScanSource(Path.GetFileName(f), File.ReadAllText(f)));
            }
            Assert.That(hits, Is.Empty, string.Join("\n", hits));
        }

        [Test]
        public void ForbiddenScanner_DetectsViolations()
        {
            Assert.That(ScanSource("X.cs", "var r = new System.Random(1);"), Is.Not.Empty);
            Assert.That(ScanSource("X.cs", "var t = DateTime.Now;"), Is.Not.Empty);
            Assert.That(ScanSource("X.cs", "float s = MathF.Sqrt(x);"), Is.Not.Empty);
            Assert.That(ScanSource("DMath.cs", "float s = MathF.Sqrt(x);"), Is.Empty);
            Assert.That(ScanSource("X.cs", "    static int _counter;"), Is.Not.Empty);
            Assert.That(ScanSource("X.cs", "    static readonly int[] Table = { 1 };"), Is.Empty);
            Assert.That(ScanSource("X.cs", "    public static float Lerp(float a, float b, float t) => a;"), Is.Empty);
            Assert.That(ScanSource("X.cs", "    // DateTime.Now 금지"), Is.Empty);
        }
    }
}
