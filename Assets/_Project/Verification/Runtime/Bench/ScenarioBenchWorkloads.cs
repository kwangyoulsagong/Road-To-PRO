using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.Scenarios;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Verification.Bench
{
    /// <summary>specs/013 성능·대량 워크로드 (AC-07·10·16·17·21). 시계·스레드는 코어 밖에만 둔다. 경기 단위 병렬은 006 FR-71이 허용한다.</summary>
    public sealed class ScenarioBenchWorkloads
    {
        public const int MassMatches = 1000;
        const int MaxTicks = 140000;

        readonly string _data;
        readonly MatchTuning _base;
        readonly TuningBundle _scenarioTuning;

        public ScenarioBenchWorkloads(string dataDir)
        {
            _data = dataDir;
            string P(string f) => Path.Combine(dataDir, "tuning", f);
            var shape = new ShapeData(
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("shape.json"))),
                FormationCatalog.Parse(File.ReadAllText(Path.Combine(dataDir, "tactics", "formations.json"))));
            _base = new MatchTuning(
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("ball.json"))),
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("kinematics.json"))),
                TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("match.json"))),
                shape);
            _scenarioTuning = TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("scenario.json")));
        }

        string Shared(string f) => File.ReadAllText(Path.Combine(_data, "scenarios", "_shared", f));

        ScenarioSources Sources(bool samples)
        {
            var s = new ScenarioSources { RegionsJson = Shared("regions.json"), CatalogIdsJson = Shared("catalog-ids.json") };
            if (!samples) return s;
            s.FinishingJson = Shared("finishing.json");
            s.ModifiersJson = Shared("modifiers.json");
            foreach (var f in Directory.GetFiles(Path.Combine(_data, "scenarios"), "*.json")) s.Add(Path.GetFileName(f), File.ReadAllText(f));
            return s;
        }

        static readonly string[] Categories = { "BU", "PR", "FT", "DEF" };

        /// <summary>합성 시나리오 (014 전). 분류·구역을 돌려 실제 후보 분포를 흉내 낸다.</summary>
        static string Synth(int i)
        {
            var cat = Categories[i % Categories.Length];
            var col = i % 6 + 1;
            var lane = "LCR"[i % 3];
            var id = "SYN-" + i.ToString("D6");
            var trigger = "{\"zones\":{\"col\":[" + col + "," + (col % 6 + 1) + "],\"lane\":[\"" + lane + "\",\"C\"]}}";
            string cast;
            if (cat == "DEF")
                cast = "[{\"role\":\"p\",\"steps\":[{\"t\":0,\"action\":\"engage\"}]},{\"role\":\"c\",\"required\":false,\"steps\":[{\"t\":0,\"action\":\"cover_shadow\"}]}]";
            else if (i % 2 == 0)
                cast = "[{\"role\":\"a\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"pass_short\",\"to\":\"b\"}]},{\"role\":\"b\",\"steps\":[{\"do\":\"receive\"}]},{\"role\":\"o\",\"required\":false,\"steps\":[{\"do\":\"overlap\"}]}]";
            else
                cast = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\",\"distanceM\":12}]},{\"role\":\"r\",\"steps\":[{\"do\":\"run_in_behind\"}]}]";
            return "{\"id\":\"" + id + "\",\"name\":{\"ko\":\"합성\",\"en\":\"synthetic\"},\"category\":\"" + cat + "\",\"trigger\":" + trigger +
                   ",\"cast\":" + cast + ",\"branches\":[],\"kpi\":[\"x\"],\"basePattern\":\"BASE-" + (i % 268) + "\"}";
        }

        /// <summary>샘플 + 합성 시나리오 라이브러리.</summary>
        public ScenarioLibrary SynthLibrary(int count)
        {
            var s = Sources(true);
            var sb = new System.Text.StringBuilder("{\"scenarios\":[");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Synth(i));
            }
            sb.Append("]}");
            s.Add("synthetic.json", sb.ToString());
            return ScenarioLibrary.Load(s);
        }

        MatchTuning Tuning(ScenarioLibrary lib) => new MatchTuning(_base.Ball, _base.Kinematics, _base.Match, _base.Shape, new ScenarioData(_scenarioTuning, lib));

        public MatchSim NewMatch(ScenarioLibrary lib, ulong seed = MatchBenchWorkloads.Seed)
        {
            var s = MatchBenchWorkloads.Setup(seed);
            s.Home.Tactics = new TeamTactics { FormationId = "4-3-3" };
            s.Away.Tactics = new TeamTactics { FormationId = "4-4-2" };
            return new MatchSim(s, Tuning(lib), new MatchSimOptions { Checkpoints = RoadToPro.MatchEngine.Match.Replay.CheckpointMode.None });
        }

        public static ScenarioRuntime RuntimeOf(MatchSim sim) => ((ScenarioStage)sim.Stage(StageId.ScenarioOrUtility)).Runtime;

        /// <summary>Advance + Select를 틱 단위로 합친다.</summary>
        sealed class TickTimer : IScenarioTimer
        {
            public readonly long[] Samples = new long[MaxTicks];
            public int Count;
            long _start, _sum;
            int _calls;

            public void Begin() => _start = Stopwatch.GetTimestamp();

            public void End()
            {
                _sum += Stopwatch.GetTimestamp() - _start;
                if (++_calls < 2) return;
                _calls = 0;
                if (Count < Samples.Length) Samples[Count++] = _sum;
                _sum = 0;
            }
        }

        public Dictionary<string, double> Tick(ScenarioLibrary lib)
        {
            NewMatch(lib).RunUntilTick(6000);
            var sim = NewMatch(lib);
            var t = new TickTimer();
            RuntimeOf(sim).Timer = t;
            GC.Collect();
            sim.RunToEnd();

            var sorted = new long[t.Count];
            Array.Copy(t.Samples, sorted, t.Count);
            Array.Sort(sorted);
            double sum = 0;
            for (int i = 0; i < t.Count; i++) sum += sorted[i];
            var toUs = 1e6 / Stopwatch.Frequency;
            return new Dictionary<string, double>
            {
                ["scenario_tick_avg_us"] = sum / t.Count * toUs,
                ["scenario_tick_p95_us"] = sorted[(int)(t.Count * 0.95)] * toUs,
                ["scenario_match_s"] = sum * toUs * 1e-6,
                ["scenario_ticks"] = t.Count,
            };
        }

        public double AllocPerTickBytes(ScenarioLibrary lib)
        {
            NewMatch(lib).RunUntilTick(9000);
            var sim = NewMatch(lib);
            sim.RunUntilTick(3000);
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            sim.RunUntilTick(9000);
            var a1 = GC.GetAllocatedBytesForCurrentThread();
            if (a0 == 0 && a1 == 0) return -1;
            return (a1 - a0) / 6000.0;
        }

        /// <summary>라이브러리 로딩 시간(ms)과 살아남은 메모리(MB).</summary>
        public void Load(int count, out double ms, out double mb)
        {
            var before = GC.GetTotalMemory(true);
            var w = Stopwatch.StartNew();
            var lib = SynthLibrary(count);
            w.Stop();
            ms = w.Elapsed.TotalMilliseconds;
            mb = (GC.GetTotalMemory(true) - before) / (1024.0 * 1024.0);
            GC.KeepAlive(lib);
        }

        struct MatchCheck
        {
            public int Double, CapOver, AggMismatch, ReasonMismatch, Failed, Starts, Fallbacks;
            public double Goals;
        }

        static MatchCheck RunChecked(MatchSim sim, bool steps)
        {
            var c = new MatchCheck();
            var rt = RuntimeOf(sim);
            var seen = new bool[Squad.Slots];
            var count = new int[2];
            while (!sim.Context.Flow.IsFinished)
            {
                sim.StepTicks(1);
                if (!steps) continue;
                Array.Clear(seen, 0, seen.Length);
                count[0] = count[1] = 0;
                for (int team = 0; team < 2; team++)
                {
                    var run = rt.Run(team);
                    if (!run.Active) continue;
                    for (int r = 0; r < run.Def.Cast.Length; r++)
                    {
                        var s = run.Slot[r];
                        if (s < 0) continue;
                        if (seen[s]) c.Double++;
                        seen[s] = true;
                        count[Squad.TeamOfSlot(s)]++;
                    }
                }
                for (int team = 0; team < 2; team++)
                {
                    var run = rt.Run(team);
                    var cap = run.Active && run.Def.Side == ScenarioSide.Attack ? ScenarioValidator.MaxAttackCast : ScenarioValidator.MaxDefenseCast;
                    if (count[team] > cap) c.CapOver++;
                }
            }

            var result = sim.RunToEnd();
            if (result.Reason == RoadToPro.MatchEngine.Match.Flow.EndReason.Failed) c.Failed++;
            c.Goals = sim.Context.Flow.Goals(0) + sim.Context.Flow.Goals(1);

            var st = rt.Stats;
            int starts = 0, completes = 0, fallbacks = 0, reasons = 0;
            for (int o = 0; o < st.Size; o++)
            {
                starts += st.Starts(o);
                completes += st.Completes(o);
                fallbacks += st.Fallbacks(o);
                for (int r = 0; r < ScenarioEndReasons.Count; r++)
                    if (ScenarioEndReasons.IsFallback((ScenarioEndReason)r)) reasons += st.Reason(o, (ScenarioEndReason)r);
            }
            int logStarts = 0, logCompletes = 0, logFallbacks = 0;
            for (int i = 0; i < sim.Log.Count; i++)
            {
                ref readonly var e = ref sim.Log[i];
                if (e.Kind == SimEventKind.ScenarioStart) logStarts++;
                else if (e.Kind == SimEventKind.ScenarioEnd)
                {
                    if (ScenarioEndReasons.IsFallback((ScenarioEndReason)e.B)) logFallbacks++;
                    else logCompletes++;
                }
            }
            if (starts != logStarts || completes != logCompletes || fallbacks != logFallbacks) c.AggMismatch++;
            if (reasons != fallbacks) c.ReasonMismatch++;
            c.Starts = starts;
            c.Fallbacks = fallbacks;
            return c;
        }

        Dictionary<string, double> Mass(ScenarioLibrary lib, string prefix, bool steps)
        {
            var total = new MatchCheck();
            int done = 0;
            var gate = new object();
            var workers = Math.Max(1, Environment.ProcessorCount - 2);
            var po = new ParallelOptions { MaxDegreeOfParallelism = workers };
            var w = Stopwatch.StartNew();
            Parallel.For(0, MassMatches, po, () => new MatchCheck(), (i, _, local) =>
            {
                var c = RunChecked(NewMatch(lib, (ulong)(50000 + i)), steps);
                local.Double += c.Double; local.CapOver += c.CapOver; local.AggMismatch += c.AggMismatch;
                local.ReasonMismatch += c.ReasonMismatch; local.Failed += c.Failed; local.Starts += c.Starts;
                local.Fallbacks += c.Fallbacks; local.Goals += c.Goals;
                Interlocked.Increment(ref done);
                return local;
            }, local =>
            {
                lock (gate)
                {
                    total.Double += local.Double; total.CapOver += local.CapOver; total.AggMismatch += local.AggMismatch;
                    total.ReasonMismatch += local.ReasonMismatch; total.Failed += local.Failed; total.Starts += local.Starts;
                    total.Fallbacks += local.Fallbacks; total.Goals += local.Goals;
                }
            });
            w.Stop();
            return new Dictionary<string, double>
            {
                [prefix + "_missing"] = MassMatches - done,
                [prefix + "_double_cast"] = total.Double,
                [prefix + "_cap_over"] = total.CapOver,
                [prefix + "_agg_mismatch"] = total.AggMismatch,
                [prefix + "_reason_mismatch"] = total.ReasonMismatch,
                [prefix + "_failed"] = total.Failed,
                [prefix + "_starts_per_match"] = (double)total.Starts / MassMatches,
                [prefix + "_goals_per_match"] = total.Goals / MassMatches,
                [prefix + "_minutes"] = w.Elapsed.TotalMinutes,
            };
        }

        /// <summary>전체 측정 (벤치 플레이어용).</summary>
        public Dictionary<string, double> RunAll()
        {
            var lib = SynthLibrary(262);
            Dictionary<string, double> best = null;
            for (int i = 0; i < 3; i++)
            {
                var m = Tick(lib);
                if (best == null || m["scenario_tick_avg_us"] < best["scenario_tick_avg_us"]) best = m;
            }
            best["alloc_per_tick_b"] = AllocPerTickBytes(lib);
            Load(262, out var ms270, out _);
            Load(40000, out var ms40k, out var mb40k);
            best["load_270_ms"] = ms270;
            best["load_40k_ms"] = ms40k;
            best["load_40k_mb"] = mb40k;
            foreach (var kv in Mass(lib, "mass", true)) best[kv.Key] = kv.Value;
            foreach (var kv in Mass(ScenarioLibrary.Load(Sources(false)), "empty", false)) best[kv.Key] = kv.Value;
            return best;
        }
    }
}
