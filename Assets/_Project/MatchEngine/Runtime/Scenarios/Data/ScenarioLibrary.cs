using System;
using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>시나리오 데이터 입력. 파일 읽기는 호출자가 한다 (FormationCatalog와 같은 방식).</summary>
    public sealed class ScenarioSources
    {
        public readonly List<KeyValuePair<string, string>> Files = new List<KeyValuePair<string, string>>();
        public string RegionsJson, FinishingJson, ModifiersJson, CatalogIdsJson;

        public ScenarioSources Add(string file, string text)
        {
            Files.Add(new KeyValuePair<string, string>(file, text));
            return this;
        }
    }

    /// <summary>
    /// 로드된 시나리오 전체 (FR-01~05·55). 파일 순서와 무관하게 id 서수로 정렬한다 (AC-15). 로딩 뒤 불변이라 여러 경기가 공유한다.
    /// `후보`·`폐기`는 일반 선택에서 빼고 강제 선택 전용으로 보관한다.
    /// </summary>
    public sealed class ScenarioLibrary
    {
        const int Sides = 2;
        const int EventBits = 14;

        readonly ScenarioDefinition[] _active;
        readonly ScenarioDefinition[] _all;
        readonly string[] _ids;
        readonly int[][][] _byCell;
        readonly int[][][] _byEvent;
        readonly ScenarioModifier[] _modifiers;
        readonly int _basePatterns;

        public ZoneMap Zones { get; }
        public ScenarioLoadReport Report { get; }
        public ulong Hash { get; }

        /// <summary>정상 라이브러리 수 (후보·폐기 제외).</summary>
        public int Count => _active.Length;
        /// <summary>후보·폐기를 포함한 정의 수. 정의 번호의 범위.</summary>
        public int TotalCount => _all.Length;
        public int BasePatternCount => _basePatterns;
        public ScenarioDefinition this[int ordinal] => _all[ordinal];
        public IReadOnlyList<ScenarioModifier> Modifiers => _modifiers;
        /// <summary>정의 번호 → id. 경기 결과에 그대로 남긴다 (리플레이가 라이브러리 변경에 영향받지 않게).</summary>
        public string[] Ids => _ids;

        ScenarioLibrary(ScenarioDefinition[] active, ScenarioDefinition[] all, ScenarioModifier[] modifiers, ZoneMap zones, ScenarioLoadReport report, ulong hash)
        {
            _active = active;
            _all = all;
            _modifiers = modifiers;
            Zones = zones;
            Report = report;
            Hash = hash;

            _ids = new string[all.Length];
            var bases = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < all.Length; i++)
            {
                _ids[i] = all[i].Id;
                all[i].Ordinal = i;
                bases[all[i].BasePattern] = 0;
            }
            int n = 0;
            var keys = new List<string>(bases.Keys);
            foreach (var k in keys) bases[k] = n++;
            _basePatterns = n;
            foreach (var d in all) d.BasePatternIndex = bases[d.BasePattern];

            foreach (var d in all) ResolveBranches(d);

            _byCell = new int[Sides][][];
            _byEvent = new int[Sides][][];
            for (int side = 0; side < Sides; side++)
            {
                var cells = new List<int>[ZoneMap.Cells];
                var events = new List<int>[EventBits];
                for (int c = 0; c < cells.Length; c++) cells[c] = new List<int>();
                for (int e = 0; e < events.Length; e++) events[e] = new List<int>();
                foreach (var d in active)
                {
                    if (d.Finishing || (int)d.Side != side) continue;
                    if (d.Trigger.Event != TriggerEventMask.None)
                    {
                        for (int b = 0; b < EventBits; b++)
                            if (((uint)d.Trigger.Event & (1u << b)) != 0) events[b].Add(d.Ordinal);
                        continue;
                    }
                    for (int c = 0; c < ZoneMap.Cells; c++)
                        if (d.Trigger.CellMask == 0 || (d.Trigger.CellMask & (1u << c)) != 0) cells[c].Add(d.Ordinal);
                }
                _byCell[side] = new int[ZoneMap.Cells][];
                _byEvent[side] = new int[EventBits][];
                for (int c = 0; c < cells.Length; c++) _byCell[side][c] = cells[c].ToArray();
                for (int e = 0; e < events.Length; e++) _byEvent[side][e] = events[e].ToArray();
            }
        }

        void ResolveBranches(ScenarioDefinition d)
        {
            foreach (var b in d.Branches)
            {
                Resolve(d, b.Success);
                Resolve(d, b.Fail);
                Resolve(d, b.SituationTarget);
                foreach (var a in b.Alts) Resolve(d, a.Target);
            }
        }

        void Resolve(ScenarioDefinition d, BranchTarget t)
        {
            if (t == null || t.Kind != BranchTargetKind.Chain) return;
            t.Ordinals = new int[t.Ids.Length];
            for (int i = 0; i < t.Ids.Length; i++)
            {
                var o = Array.BinarySearch(_ids, t.Ids[i], StringComparer.Ordinal);
                t.Ordinals[i] = o >= 0 ? o : -1;
                if (o < 0) Report.Warnings.Add(new ScenarioIssue(d.SourceFile, d.Line, "branches", $"분기 대상 '{t.Ids[i]}'가 로드되지 않아 폴백으로 대체"));
            }
        }

        /// <summary>구역 칸의 후보 (id 서수 오름차순, 트리거 이벤트 없는 시나리오만).</summary>
        public int[] ByCell(ScenarioSide side, int cell) => _byCell[(int)side][cell];

        /// <summary>트리거 이벤트 비트의 후보 (id 서수 오름차순).</summary>
        public int[] ByEventBit(ScenarioSide side, int bit) => _byEvent[(int)side][bit];
        public static int EventBitCount => EventBits;

        public int OrdinalOf(string id)
        {
            var i = Array.BinarySearch(_ids, id, StringComparer.Ordinal);
            return i >= 0 ? i : -1;
        }

        public static ScenarioLibrary Load(ScenarioSources src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            var report = new ScenarioLoadReport();
            var zones = ZoneMap.Parse(src.RegionsJson ?? throw new ArgumentException("구역 파일이 필요하다"));
            var catalog = ParseCatalog(src.CatalogIdsJson);

            var files = new List<KeyValuePair<string, string>>(src.Files);
            files.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            var parser = new ScenarioParser(zones, report);
            var parsed = new List<ScenarioDefinition>();
            foreach (var f in files) parsed.AddRange(parser.ParseFile(f.Key, f.Value));
            if (!string.IsNullOrEmpty(src.FinishingJson))
                foreach (var d in parser.ParseFile("_shared/finishing.json", src.FinishingJson))
                {
                    d.Finishing = true;
                    parsed.Add(d);
                }

            var valid = ScenarioValidator.Validate(parsed, catalog, report);
            valid.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            var active = new List<ScenarioDefinition>(valid.Count);
            foreach (var d in valid)
            {
                if (IsExcluded(d.Status)) report.Excluded.Add(d.Id);
                else active.Add(d);
            }

            var modifiers = ParseModifiers(src.ModifiersJson, report);

            var texts = new List<string>(files.Count);
            foreach (var f in files) texts.Add(f.Value);
            texts.Sort(string.CompareOrdinal);
            var h = StableHasher.Create();
            h.Add(src.RegionsJson);
            h.Add(src.CatalogIdsJson ?? "");
            h.Add(src.FinishingJson ?? "");
            h.Add(src.ModifiersJson ?? "");
            h.Add(texts.Count);
            foreach (var t in texts) h.Add(t);

            return new ScenarioLibrary(active.ToArray(), valid.ToArray(), modifiers, zones, report, h.Value);
        }

        static bool IsExcluded(string status) => status == "후보" || status == "폐기" || status == "candidate" || status == "retired";

        static HashSet<string> ParseCatalog(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (JsonReader.Parse(json) is JsonObject o && o.TryGet("ids", out var v) && v is List<object> list)
                foreach (var x in list)
                    if (x is string s) set.Add(s);
            return set;
        }

        static ScenarioModifier[] ParseModifiers(string json, ScenarioLoadReport report)
        {
            const string File = "_shared/modifiers.json";
            if (string.IsNullOrEmpty(json)) return Array.Empty<ScenarioModifier>();
            object root;
            try { root = JsonReader.Parse(json); }
            catch (JsonException e)
            {
                report.Errors.Add(new ScenarioIssue(File, e.Line, "(JSON)", e.Message));
                return Array.Empty<ScenarioModifier>();
            }
            if (!(root is JsonObject o) || !o.TryGet("modifiers", out var v) || !(v is List<object> list))
            {
                report.Errors.Add(new ScenarioIssue(File, 1, "modifiers", "배열이 없다"));
                return Array.Empty<ScenarioModifier>();
            }
            var mods = new List<ScenarioModifier>(list.Count);
            foreach (var item in list)
            {
                if (!(item is JsonObject m) || !m.TryGet("id", out var idV) || !(idV is string id))
                {
                    report.Errors.Add(new ScenarioIssue(File, o.LineOf("modifiers"), "modifiers[]", "id가 없다"));
                    continue;
                }
                var mod = new ScenarioModifier { Id = id };
                if (m.TryGet("weight", out var w) && w is double d) mod.Weight = (float)d;
                if (m.TryGet("categories", out var cv) && cv is List<object> cats)
                    foreach (var c in cats)
                        if (c is string s && Enum.TryParse<ScenarioCategory>(s, false, out var cat)) mod.CategoryMask |= 1u << (int)cat;
                if (m.TryGet("phase", out var pv) && pv is List<object> phases)
                {
                    ushort mask = 0;
                    foreach (var p in phases)
                        if (p is string s && PhaseBits.TryParse(s, out var b, out _)) mask |= b;
                    if (mask != 0) mod.PhaseMask = mask;
                }
                mods.Add(mod);
            }
            mods.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return mods.ToArray();
        }
    }
}
