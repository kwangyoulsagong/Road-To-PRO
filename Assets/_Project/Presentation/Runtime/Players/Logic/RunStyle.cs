using System;
using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>달리기 스타일 원형 (specs/055 FR-50). 이름 없음 — 번호만.</summary>
    public enum RunArchetype : byte { None = 0, R1, R2, R3, R4, R5, R6, R7, R8, GK }

    /// <summary>킥 스타일 원형 (specs/055 FR-61).</summary>
    public enum KickArchetype : byte { None = 0, K1, K2, K3, K4, K5, K6, K7, K8 }

    /// <summary>
    /// 원형 매핑 입력 (FR-51). 키·체중·포지션은 필수, 역할(003 FR-01 이름의 camelCase 키)·성향(0~100, −1 = 없음)·버릇은 선택 —
    /// 002·003 구현 전에는 없어도 된다(체형·포지션·id 시드로 정한다).
    /// </summary>
    public readonly struct RunStyleInput
    {
        public readonly int Id;
        public readonly float HeightCm, WeightKg;
        public readonly BodyRole Role;
        public readonly string RoleKey;
        public readonly int Dribble, Tempo, Runs, Press, LongShots;
        public readonly IReadOnlyList<string> Habits;

        public RunStyleInput(int id, float heightCm, float weightKg, BodyRole role, string roleKey = null, int dribble = -1, int tempo = -1,
            int runs = -1, int press = -1, int longShots = -1, IReadOnlyList<string> habits = null)
        {
            Id = id;
            HeightCm = heightCm;
            WeightKg = weightKg;
            Role = role;
            RoleKey = roleKey;
            Dribble = dribble;
            Tempo = tempo;
            Runs = runs;
            Press = press;
            LongShots = longShots;
            Habits = habits;
        }
    }

    /// <summary>선수 원형 선택 결과. Jitter = 원형 sprint 극단(lo·hi) 섞기 가중치 0~1 (FR-51 흔들기, 선수 id 시드).</summary>
    public readonly struct RunStyleChoice : IEquatable<RunStyleChoice>
    {
        public readonly RunArchetype Run;
        public readonly KickArchetype Kick;
        public readonly float Jitter;

        public RunStyleChoice(RunArchetype run, KickArchetype kick, float jitter)
        {
            Run = run;
            Kick = kick;
            Jitter = jitter;
        }

        /// <summary>클립 태그 style 값 ("r1"…"r8", "gk"). 없으면 null.</summary>
        public string RunKey => Run == RunArchetype.None ? null : Run == RunArchetype.GK ? "gk" : "r" + (int)Run;

        public bool Equals(RunStyleChoice o) => Run == o.Run && Kick == o.Kick && Jitter.Equals(o.Jitter);
        public override bool Equals(object obj) => obj is RunStyleChoice o && Equals(o);
        public override int GetHashCode() => ((int)Run * 31 + (int)Kick) * 397 ^ Jitter.GetHashCode();
        public override string ToString() => $"{Run}/{Kick}/{Jitter:0.###}";
    }

    /// <summary>
    /// 선수 → 달리기·킥 원형 (specs/055 FR-51·61, AC-08·14). 규칙은 055-run-styles.json mapping — 조건이 맞으면 점수를 더해 최댓값,
    /// 동점은 id 시드. 정수 점수·정수 비교 + 키·BMI 문턱 비교만 → 입력이 같으면 결과가 같다.
    /// </summary>
    public sealed class RunStyleRules
    {
        /// <summary>003 FR-01 역할 27개 (camelCase 키).</summary>
        public static readonly IReadOnlyList<string> RoleKeys = new[]
        {
            "sweeperKeeper", "traditionalKeeper",
            "ballPlayingDefender", "stopper", "coverLibero",
            "fullBack", "invertedFullBack", "wingBack",
            "anchor", "regista", "ballWinningMidfielder",
            "boxToBox", "mezzala", "carrilero", "deepLyingPlaymaker",
            "trequartista", "enganche", "shadowStriker",
            "traditionalWinger", "insideForward", "widePlaymaker", "raumdeuter",
            "poacher", "targetMan", "falseNine", "pressingForward", "completeForward",
        };

        /// <summary>역할 키 → 포지션 묶음 (003 FR-01 라인).</summary>
        public static BodyRole BodyRoleOf(string roleKey)
        {
            int i = IndexOfRole(roleKey);
            if (i < 0) throw new ArgumentException("모르는 역할 키: " + roleKey);
            if (i < 2) return BodyRole.Goalkeeper;
            if (i < 8) return BodyRole.Defender;
            if (i < 18) return BodyRole.Midfielder;
            return BodyRole.Forward;
        }

        static int IndexOfRole(string k)
        {
            for (int i = 0; i < RoleKeys.Count; i++)
                if (RoleKeys[i] == k) return i;
            return -1;
        }

        sealed class Rule
        {
            public int Style, Points;
            public float HeightMin = float.NegativeInfinity, HeightMax = float.PositiveInfinity, BmiMin = float.NegativeInfinity;
            public int DribbleMin = -1, TempoMin = -1, TempoMax = -1, RunsMin = -1, LongShotsMin = -1;
            public string Habit;
            public HashSet<string> Roles;
            public HashSet<BodyRole> BodyRoles;
        }

        readonly List<Rule> _run = new List<Rule>(), _kick = new List<Rule>();
        readonly Dictionary<BodyRole, (RunArchetype run, KickArchetype kick)> _forced = new Dictionary<BodyRole, (RunArchetype, KickArchetype)>();

        static RunArchetype ParseRun(string s) => s == "gk" ? RunArchetype.GK : (RunArchetype)int.Parse(s.Substring(1));
        static KickArchetype ParseKick(string s) => (KickArchetype)int.Parse(s.Substring(1));

        public static RunStyleRules Parse(string json)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            if (!root.TryGet("schemaVersion", out var sv) || (double)sv != 1) throw new FormatException("055-run-styles.json schemaVersion 1 아님");
            root.TryGet("mapping", out var mo);
            var map = (JsonObject)mo;
            var r = new RunStyleRules();
            map.TryGet("forced", out var fo);
            foreach (var kv in ((JsonObject)fo).Members)
            {
                string k = kv.Key;
                var vo = (JsonObject)kv.Value;
                vo.TryGet("run", out var rr);
                vo.TryGet("kick", out var kk);
                r._forced[(BodyRole)Enum.Parse(typeof(BodyRole), k)] = (ParseRun((string)rr), ParseKick((string)kk));
            }
            map.TryGet("run", out var ra);
            foreach (var o in (List<object>)ra) r._run.Add(ReadRule((JsonObject)o, true));
            map.TryGet("kick", out var ka);
            foreach (var o in (List<object>)ka) r._kick.Add(ReadRule((JsonObject)o, false));
            return r;
        }

        static Rule ReadRule(JsonObject o, bool run)
        {
            o.TryGet("style", out var st);
            o.TryGet("points", out var pt);
            var rule = new Rule { Style = run ? (int)ParseRun((string)st) : (int)ParseKick((string)st), Points = (int)(double)pt };
            o.TryGet("if", out var cf);
            var c = (JsonObject)cf;
            foreach (var kv in c.Members)
            {
                string key = kv.Key;
                object v = kv.Value;
                switch (key)
                {
                    case "heightMin": rule.HeightMin = (float)(double)v; break;
                    case "heightMax": rule.HeightMax = (float)(double)v; break;
                    case "bmiMin": rule.BmiMin = (float)(double)v; break;
                    case "dribbleMin": rule.DribbleMin = (int)(double)v; break;
                    case "tempoMin": rule.TempoMin = (int)(double)v; break;
                    case "tempoMax": rule.TempoMax = (int)(double)v; break;
                    case "runsMin": rule.RunsMin = (int)(double)v; break;
                    case "longShotsMin": rule.LongShotsMin = (int)(double)v; break;
                    case "habit": rule.Habit = (string)v; break;
                    case "roles":
                        rule.Roles = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var x in (List<object>)v)
                        {
                            if (IndexOfRole((string)x) < 0) throw new FormatException("모르는 역할 키: " + x);
                            rule.Roles.Add((string)x);
                        }
                        break;
                    case "bodyRoles":
                        rule.BodyRoles = new HashSet<BodyRole>();
                        foreach (var x in (List<object>)v) rule.BodyRoles.Add((BodyRole)Enum.Parse(typeof(BodyRole), (string)x));
                        break;
                    default: throw new FormatException("모르는 조건: " + key);
                }
            }
            return rule;
        }

        static bool Match(Rule r, in RunStyleInput q, float bmi)
        {
            if (q.HeightCm < r.HeightMin || q.HeightCm > r.HeightMax) return false;
            if (bmi < r.BmiMin) return false;
            if (r.DribbleMin >= 0 && (q.Dribble < 0 || q.Dribble < r.DribbleMin)) return false;
            if (r.TempoMin >= 0 && (q.Tempo < 0 || q.Tempo < r.TempoMin)) return false;
            if (r.TempoMax >= 0 && (q.Tempo < 0 || q.Tempo > r.TempoMax)) return false;
            if (r.RunsMin >= 0 && (q.Runs < 0 || q.Runs < r.RunsMin)) return false;
            if (r.LongShotsMin >= 0 && (q.LongShots < 0 || q.LongShots < r.LongShotsMin)) return false;
            if (r.Roles != null && (q.RoleKey == null || !r.Roles.Contains(q.RoleKey))) return false;
            if (r.BodyRoles != null && !r.BodyRoles.Contains(q.Role)) return false;
            if (r.Habit != null)
            {
                bool has = false;
                if (q.Habits != null)
                    for (int i = 0; i < q.Habits.Count; i++)
                        if (q.Habits[i] == r.Habit) { has = true; break; }
                if (!has) return false;
            }
            return true;
        }

        static int Best(List<Rule> rules, in RunStyleInput q, float bmi, int count, ulong seed)
        {
            Span<int> score = stackalloc int[16];
            for (int i = 0; i < rules.Count; i++)
                if (Match(rules[i], q, bmi)) score[rules[i].Style] += rules[i].Points;
            int best = -1, ties = 0;
            for (int s = 1; s <= count; s++)
            {
                if (best < 0 || score[s] > score[best]) { best = s; ties = 1; }
                else if (score[s] == score[best]) ties++;
            }
            if (ties == 1) return best;
            int pick = (int)(seed % (ulong)ties), n = 0;
            for (int s = 1; s <= count; s++)
                if (score[s] == score[best] && n++ == pick) return s;
            return best;
        }

        public RunStyleChoice Choose(in RunStyleInput q)
        {
            if (q.RoleKey != null && IndexOfRole(q.RoleKey) < 0) throw new ArgumentException("모르는 역할 키: " + q.RoleKey);
            var h = StableHasher.Create();
            h.Add(q.Id);
            h.Add(0x055);
            ulong st = h.Value;
            ulong a = SplitMix64.Next(ref st), b = SplitMix64.Next(ref st), c = SplitMix64.Next(ref st);
            float jitter = (float)(a >> 40) / (1 << 24);
            if (_forced.TryGetValue(q.Role, out var f)) return new RunStyleChoice(f.run, f.kick, jitter);
            float m = q.HeightCm / 100f;
            float bmi = m > 0 ? q.WeightKg / (m * m) : 0f;
            var run = (RunArchetype)Best(_run, q, bmi, 8, b);
            var kick = (KickArchetype)Best(_kick, q, bmi, 7, c);
            return new RunStyleChoice(run, kick, jitter);
        }
    }
}
