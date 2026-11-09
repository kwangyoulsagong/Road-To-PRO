using System;
using System.Collections.Generic;
using System.Globalization;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>로딩 문제 하나 (FR-04): 파일·줄·필드를 함께 보고한다.</summary>
    public readonly struct ScenarioIssue
    {
        public readonly string File, Field, Message;
        public readonly int Line;

        public ScenarioIssue(string file, int line, string field, string message)
        {
            File = file; Line = line; Field = field; Message = message;
        }

        public override string ToString() => $"{File}:{Line} [{Field}] {Message}";
    }

    /// <summary>로딩 리포트 (FR-04·55).</summary>
    public sealed class ScenarioLoadReport
    {
        public readonly List<ScenarioIssue> Errors = new List<ScenarioIssue>();
        public readonly List<ScenarioIssue> Warnings = new List<ScenarioIssue>();
        /// <summary>정상 라이브러리에서 제외한 `후보`·`폐기` 시나리오 id.</summary>
        public readonly List<string> Excluded = new List<string>();
    }

    /// <summary>
    /// JSON → <see cref="ScenarioDefinition"/>. 공격 문서의 순서 표기(`do` + `at/success/fail`)와 수비·세트피스 문서의 시각 표기(`t/action` + `when/goto`)를
    /// 같은 구조로 읽는다 (FR-02). 파일 읽기는 호출자가 한다. 오류가 하나라도 있는 시나리오는 돌려주지 않는다 (FR-04).
    /// </summary>
    public sealed class ScenarioParser
    {
        readonly ZoneMap _zones;
        readonly ScenarioLoadReport _report;
        string _file;
        bool _failed;

        public ScenarioParser(ZoneMap zones, ScenarioLoadReport report)
        {
            _zones = zones ?? throw new ArgumentNullException(nameof(zones));
            _report = report ?? throw new ArgumentNullException(nameof(report));
        }

        /// <summary>파일 하나를 읽는다. 최상위는 시나리오 객체 또는 <c>{"scenarios":[...]}</c>.</summary>
        public List<ScenarioDefinition> ParseFile(string file, string text)
        {
            _file = file;
            var result = new List<ScenarioDefinition>();
            object root;
            try { root = JsonReader.Parse(text); }
            catch (JsonException e)
            {
                _report.Errors.Add(new ScenarioIssue(file, e.Line, "(JSON)", e.Message));
                return result;
            }
            if (!(root is JsonObject o))
            {
                _report.Errors.Add(new ScenarioIssue(file, 1, "(root)", "최상위가 객체가 아니다"));
                return result;
            }
            if (o.TryGet("scenarios", out var listV))
            {
                if (!(listV is List<object> list))
                {
                    _report.Errors.Add(new ScenarioIssue(file, o.LineOf("scenarios"), "scenarios", "배열이 아니다"));
                    return result;
                }
                foreach (var item in list)
                {
                    if (item is JsonObject so) ParseOne(so, result);
                    else _report.Errors.Add(new ScenarioIssue(file, o.LineOf("scenarios"), "scenarios[]", "객체가 아니다"));
                }
                return result;
            }
            ParseOne(o, result);
            return result;
        }

        void ParseOne(JsonObject o, List<ScenarioDefinition> sink)
        {
            _failed = false;
            var def = Parse(o);
            if (!_failed && def != null) sink.Add(def);
        }

        void Err(JsonObject o, string field, string msg)
        {
            _failed = true;
            _report.Errors.Add(new ScenarioIssue(_file, o.LineOf(LastKey(field)), field, msg));
        }

        static string LastKey(string field)
        {
            var i = field.LastIndexOf('.');
            var k = i >= 0 ? field.Substring(i + 1) : field;
            var b = k.IndexOf('[');
            return b >= 0 ? k.Substring(0, b) : k;
        }

        static float F(object v) => (float)(double)v;

        string Str(JsonObject o, string key, string ctx, bool required)
        {
            if (o.TryGet(key, out var v))
            {
                if (v is string s) return s;
                Err(o, ctx + key, "문자열이 아니다");
                return null;
            }
            if (required) Err(o, ctx + key, "필수 필드 누락");
            return null;
        }

        List<object> Arr(JsonObject o, string key, string ctx, bool required)
        {
            if (o.TryGet(key, out var v))
            {
                if (v is List<object> l) return l;
                Err(o, ctx + key, "배열이 아니다");
                return null;
            }
            if (required) Err(o, ctx + key, "필수 필드 누락");
            return null;
        }

        JsonObject Obj(JsonObject o, string key, string ctx, bool required)
        {
            if (o.TryGet(key, out var v))
            {
                if (v is JsonObject j) return j;
                Err(o, ctx + key, "객체가 아니다");
                return null;
            }
            if (required) Err(o, ctx + key, "필수 필드 누락");
            return null;
        }

        /// <summary>문자열 하나 또는 문자열 배열.</summary>
        List<string> Strs(JsonObject o, string key, string ctx)
        {
            if (!o.TryGet(key, out var v)) return null;
            var r = new List<string>();
            if (v is string s) { r.Add(s); return r; }
            if (v is List<object> l)
            {
                foreach (var x in l)
                {
                    if (x is string t) r.Add(t);
                    else Err(o, ctx + key, "문자열 배열이어야 한다");
                }
                return r;
            }
            Err(o, ctx + key, "문자열 또는 배열이어야 한다");
            return null;
        }

        bool Num(JsonObject o, string key, string ctx, float min, float max, out float value)
        {
            value = 0f;
            if (!o.TryGet(key, out var v)) return false;
            if (!(v is double d)) { Err(o, ctx + key, "숫자가 아니다"); return false; }
            value = (float)d;
            if (!(value >= min && value <= max))
            {
                Err(o, ctx + key, FormattableString.Invariant($"범위 밖 값 {value} ({min}~{max})"));
                return false;
            }
            return true;
        }

        static bool Bool(JsonObject o, string key, bool dflt) => o.TryGet(key, out var v) && v is bool b ? b : dflt;

        ScenarioDefinition Parse(JsonObject o)
        {
            var def = new ScenarioDefinition { SourceFile = _file, Line = o.Line };
            def.Id = Str(o, "id", "", true);
            if (def.Id != null && def.Id.Length == 0) Err(o, "id", "빈 id");

            var name = Obj(o, "name", "", true);
            if (name != null)
            {
                def.NameKo = Str(name, "ko", "name.", true);
                def.NameEn = Str(name, "en", "name.", true);
            }

            var cat = Str(o, "category", "", true);
            if (cat != null)
            {
                if (Enum.TryParse<ScenarioCategory>(cat, false, out var c) && Enum.IsDefined(typeof(ScenarioCategory), c)) def.Category = c;
                else Err(o, "category", $"알 수 없는 분류 '{cat}'");
            }
            def.Group = Str(o, "group", "", false);
            def.Finishing = Bool(o, "finishing", false) || def.Category == ScenarioCategory.FN;

            def.Side = def.Category == ScenarioCategory.DEF ? ScenarioSide.Defense : ScenarioSide.Attack;
            var side = Str(o, "side", "", false);
            if (side == "attack") def.Side = ScenarioSide.Attack;
            else if (side == "defense") def.Side = ScenarioSide.Defense;
            else if (side != null) Err(o, "side", $"알 수 없는 입장 '{side}'");

            def.Mirror = Bool(o, "mirror", false);
            def.BasePattern = Str(o, "basePattern", "", false) ?? def.Id;
            def.Status = Str(o, "상태", "", false) ?? Str(o, "status", "", false);

            var trig = Obj(o, "trigger", "", !def.Finishing);
            if (trig != null) ParseTrigger(trig, def);

            var gate = Obj(o, "gate", "", false);
            if (gate != null)
            {
                def.HasGate = true;
                if (Num(gate, "lambda", "gate.", 0f, 1000f, out var l)) def.Lambda = l;
                if (Num(gate, "maxPerMatch", "gate.", 0f, 1000f, out var m)) def.MaxPerMatch = (int)m;
                if (Num(gate, "cooldownS", "gate.", 0f, 7200f, out var c)) def.CooldownS = c;
            }
            else if (def.Category == ScenarioCategory.SPC) Err(o, "gate", "특수 시나리오는 희소성 게이트가 필수다 (FR-12)");

            if (Num(o, "timeLimitS", "", 0.1f, 600f, out var limit)) def.TimeLimitS = limit;

            var cast = Arr(o, "cast", "", true);
            if (cast != null)
            {
                if (cast.Count == 0) Err(o, "cast", "배역이 없다");
                ParseCast(o, cast, def);
            }

            var branches = Arr(o, "branches", "", true);
            if (branches != null && !_failed) ParseBranches(o, branches, def);

            def.Kpi = ToArray(Strs(o, "kpi", ""));
            if (def.Kpi.Length == 0)
            {
                var success = Obj(o, "success", "", false);
                var metric = success != null ? Str(success, "metric", "success.", false) : null;
                if (metric != null) def.Kpi = new[] { metric };
                else if (!def.Finishing) Err(o, "kpi", "성공 지표 누락");
            }
            def.VariationAxes = ToArray(Strs(o, "variationAxes", ""));
            def.CarrierTraitsBonus = ToArray(Strs(o, "carrierTraitsBonus", ""));

            var fav = Obj(o, "favoredTraits", "", false);
            if (fav != null)
            {
                def.FavoredRoles = new string[fav.Members.Count];
                def.FavoredTraits = new string[fav.Members.Count][];
                for (int i = 0; i < fav.Members.Count; i++)
                {
                    def.FavoredRoles[i] = fav.Members[i].Key;
                    def.FavoredTraits[i] = ToArray(Strs(fav, fav.Members[i].Key, "favoredTraits."));
                }
            }

            var tags = Obj(o, "tags", "", false);
            if (tags != null)
            {
                if (Num(tags, "successRate", "tags.", 0f, 1f, out var s)) { def.HasSuccessRate = true; def.SuccessRate = s; }
                if (Num(tags, "xg", "tags.", 0f, 1f, out var x)) { def.HasXg = true; def.Xg = x; }
                if (Num(tags, "durationS", "tags.", 0f, 600f, out var d)) { def.HasDuration = true; def.DurationS = d; }
            }
            return def;
        }

        static string[] ToArray(List<string> l) => l == null ? Array.Empty<string>() : l.ToArray();

        void ParseTrigger(JsonObject t, ScenarioDefinition def)
        {
            var tr = def.Trigger;
            const string C = "trigger.";

            var zones = Obj(t, "zones", C, false);
            if (zones != null) ParseZones(zones, def);

            var phases = Strs(t, "phase", C);
            if (phases != null)
            {
                ushort mask = 0;
                foreach (var p in phases)
                {
                    if (PhaseBits.TryParse(p, out var m, out var press)) { mask |= m; tr.RequirePress |= press; }
                    else Err(t, C + "phase", $"매핑에 없는 국면 표기 '{p}'");
                }
                if (mask != 0) tr.PhaseMask = mask;
            }

            var ball = Strs(t, "ballState", C);
            if (ball != null)
            {
                BallStateMask m = BallStateMask.None;
                foreach (var b in ball)
                {
                    switch (b)
                    {
                        case "control": m |= BallStateMask.Control; break;
                        case "bouncing": m |= BallStateMask.Bouncing; break;
                        case "aerial": m |= BallStateMask.Aerial; break;
                        case "dead": m |= BallStateMask.Dead; break;
                        default: Err(t, C + "ballState", $"알 수 없는 공 상태 '{b}'"); break;
                    }
                }
                if (m != BallStateMask.None) tr.BallState = m;
            }

            tr.CarrierRole = ParsePositions(t, "carrierRole", C);
            tr.ReceiverRole = ParsePositions(t, "receiverRole", C);

            var blocks = Strs(t, "oppBlock", C);
            if (blocks != null)
                foreach (var b in blocks)
                {
                    if (Enum.TryParse<OppBlockMask>(b, false, out var m) && m != OppBlockMask.None && Enum.IsDefined(typeof(OppBlockMask), m)) tr.OppBlock |= m;
                    else Err(t, C + "oppBlock", $"알 수 없는 블록 '{b}'");
                }

            var evt = Str(t, "event", C, false);
            if (evt != null)
            {
                if (TriggerEvents.TryParse(evt, out var m)) tr.Event = m;
                else Err(t, C + "event", $"알 수 없는 트리거 이벤트 '{evt}'");
            }

            var body = Str(t, "receiverBody", C, false);
            if (body == "facing_sideline") tr.ReceiverFacingSideline = true;
            else if (body != null) Err(t, C + "receiverBody", $"알 수 없는 몸 방향 '{body}'");

            var marking = Strs(t, "oppMarking", C);
            if (marking != null)
                foreach (var m in marking)
                {
                    switch (m)
                    {
                        case "zonal": tr.OppMarkingMask |= 1 << (int)Tactics.MarkingScheme.Zonal; break;
                        case "man": tr.OppMarkingMask |= 1 << (int)Tactics.MarkingScheme.Man; break;
                        case "hybrid": tr.OppMarkingMask |= 1 << (int)Tactics.MarkingScheme.Hybrid; break;
                        default: Err(t, C + "oppMarking", $"알 수 없는 마킹 '{m}'"); break;
                    }
                }

            var space = Obj(t, "space", C, false);
            if (space != null)
            {
                var list = new List<SpaceCondition>();
                for (int i = 0; i < space.Members.Count; i++)
                {
                    var key = space.Members[i].Key;
                    if (!SpaceFields.TryParse(key, out var f)) { Err(space, C + "space." + key, $"알 수 없는 공간 필드 '{key}'"); continue; }
                    if (TryCompare(space.Members[i].Value, out var op, out var val))
                        list.Add(new SpaceCondition { Field = f, Op = op, Value = val });
                    else Err(space, C + "space." + key, "비교식이 잘못됐다 (예: \">=8\", 숫자, true)");
                }
                tr.Space = list.ToArray();
            }

            var score = Arr(t, "scoreDiff", C, false);
            if (score != null)
            {
                if (score.Count == 2 && score[0] is double a && score[1] is double b && a <= b) { tr.ScoreDiffMin = (int)a; tr.ScoreDiffMax = (int)b; }
                else Err(t, C + "scoreDiff", "[최소, 최대]여야 한다");
            }
            var minute = Arr(t, "minute", C, false);
            if (minute != null)
            {
                if (minute.Count == 2 && minute[0] is double a && minute[1] is double b && a <= b) { tr.MinuteMin = (float)a; tr.MinuteMax = (float)b; }
                else Err(t, C + "minute", "[최소, 최대]여야 한다");
            }
        }

        static bool TryCompare(object v, out CompareOp op, out float value)
        {
            op = CompareOp.Eq;
            value = 0f;
            if (v is bool b) { value = b ? 1f : 0f; return true; }
            if (v is double d) { value = (float)d; return true; }
            if (!(v is string s)) return false;
            s = s.Trim();
            int skip;
            if (s.StartsWith(">=", StringComparison.Ordinal)) { op = CompareOp.Ge; skip = 2; }
            else if (s.StartsWith("<=", StringComparison.Ordinal)) { op = CompareOp.Le; skip = 2; }
            else if (s.StartsWith(">", StringComparison.Ordinal)) { op = CompareOp.Gt; skip = 1; }
            else if (s.StartsWith("<", StringComparison.Ordinal)) { op = CompareOp.Lt; skip = 1; }
            else if (s.StartsWith("==", StringComparison.Ordinal)) { op = CompareOp.Eq; skip = 2; }
            else skip = 0;
            return float.TryParse(s.Substring(skip).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        PositionMask ParsePositions(JsonObject o, string key, string ctx)
        {
            var list = Strs(o, key, ctx);
            if (list == null) return PositionMask.None;
            var m = PositionMask.None;
            foreach (var s in list)
            {
                if (Positions.TryParse(s, out var p)) m |= p;
                else Err(o, ctx + key, $"알 수 없는 역할 '{s}'");
            }
            return m;
        }

        void ParseZones(JsonObject z, ScenarioDefinition def)
        {
            var tr = def.Trigger;
            const string C = "trigger.zones.";
            uint mask = ZoneMap.AllCells;
            var groups = new List<int[]>();
            foreach (var key in new[] { "regions", "lanes5" })
            {
                var names = Strs(z, key, C);
                if (names == null) continue;
                var g = new int[names.Count];
                uint gm = 0;
                for (int i = 0; i < g.Length; i++)
                {
                    g[i] = _zones.IndexOf(names[i]);
                    if (g[i] < 0) Err(z, C + key, $"알 수 없는 구역 '{names[i]}'");
                    else gm |= _zones.CellMask(g[i]);
                }
                groups.Add(g);
                mask &= gm;
            }
            tr.RegionGroups = groups.ToArray();

            var cols = Arr(z, "col", C, false);
            var lanes = Strs(z, "lane", C);
            if (cols != null || lanes != null)
            {
                uint gm = 0;
                var colList = new List<int>();
                if (cols != null)
                    foreach (var c in cols)
                    {
                        if (c is double d && d >= 1 && d <= ZoneMap.Columns) colList.Add((int)d);
                        else Err(z, C + "col", "열은 1~6이어야 한다");
                    }
                else for (int c = 1; c <= ZoneMap.Columns; c++) colList.Add(c);
                var laneList = new List<int>();
                if (lanes != null)
                    foreach (var l in lanes)
                    {
                        if (l == "L") laneList.Add(0);
                        else if (l == "C") laneList.Add(1);
                        else if (l == "R") laneList.Add(2);
                        else Err(z, C + "lane", $"레인은 L/C/R이어야 한다 ('{l}')");
                    }
                else { laneList.Add(0); laneList.Add(1); laneList.Add(2); }
                foreach (var c in colList) foreach (var l in laneList) gm |= ZoneMap.CellBit(c, l);
                mask &= gm;
            }
            if (def.Mirror) mask = ZoneMap.MirrorMask(mask);
            tr.CellMask = mask;
        }

        void ParseCast(JsonObject o, List<object> cast, ScenarioDefinition def)
        {
            var roles = new ScenarioRole[cast.Count];
            var sources = new JsonObject[cast.Count];
            for (int i = 0; i < roles.Length; i++)
            {
                var ctx = $"cast[{i}].";
                if (!(cast[i] is JsonObject ro)) { Err(o, "cast", "배역이 객체가 아니다"); roles[i] = new ScenarioRole(); continue; }
                sources[i] = ro;
                var opponent = Bool(ro, "opponent", false);
                var r = new ScenarioRole
                {
                    Name = Str(ro, "role", ctx, true),
                    Opponent = opponent,
                    Required = Bool(ro, "required", !opponent),
                    IsCarrier = Bool(ro, "carrier", false),
                    IsTaker = Bool(ro, "taker", false),
                };
                var pos = ParsePositions(ro, "positions", ctx);
                r.Positions = pos != PositionMask.None ? pos : PositionMask.AnyField;
                var side = Str(ro, "side", ctx, false);
                switch (side)
                {
                    case null: break;
                    case "L": r.Side = def.Mirror ? RoleSideFilter.Right : RoleSideFilter.Left; break;
                    case "R": r.Side = def.Mirror ? RoleSideFilter.Left : RoleSideFilter.Right; break;
                    case "ball": r.Side = RoleSideFilter.BallSide; break;
                    case "far": r.Side = RoleSideFilter.FarSide; break;
                    default: Err(ro, ctx + "side", $"알 수 없는 측면 '{side}'"); break;
                }
                roles[i] = r;
            }
            for (int i = 0; i < roles.Length; i++)
                for (int k = 0; k < i; k++)
                    if (roles[i].Name != null && roles[i].Name == roles[k].Name) Err(sources[i] ?? o, $"cast[{i}].role", $"배역 이름 중복 '{roles[i].Name}'");
            def.Cast = roles;

            for (int i = 0; i < roles.Length; i++)
            {
                if (sources[i] == null) continue;
                var ctx = $"cast[{i}].";
                var steps = Arr(sources[i], "steps", ctx, true);
                if (steps == null) continue;
                if (steps.Count == 0) { Err(sources[i], ctx + "steps", "단계가 없다"); continue; }
                var parsed = new ScenarioStep[steps.Count];
                for (int s = 0; s < parsed.Length; s++)
                {
                    if (steps[s] is JsonObject so) parsed[s] = ParseStep(so, $"{ctx}steps[{s}].", def);
                    else { Err(sources[i], ctx + "steps", "단계가 객체가 아니다"); parsed[s] = new ScenarioStep(); }
                }
                roles[i].Steps = parsed;
            }
        }

        ScenarioStep ParseStep(JsonObject s, string ctx, ScenarioDefinition def)
        {
            var st = new ScenarioStep();
            var verb = Str(s, "do", ctx, false) ?? Str(s, "action", ctx, false);
            if (verb == null) Err(s, ctx + "do", "동사 누락");
            else if (ScenarioVerbs.TryParse(verb, out var v)) { st.Verb = v; st.Class = ScenarioVerbs.ClassOf(v); }
            else Err(s, ctx + (s.TryGet("do", out _) ? "do" : "action"), $"알 수 없는 동사 '{verb}'");

            var alt = Str(s, "alt", ctx, false);
            if (alt != null)
            {
                if (ScenarioVerbs.TryParse(alt, out var a)) { st.HasAlt = true; st.Alt = a; }
                else Err(s, ctx + "alt", $"알 수 없는 동사 '{alt}'");
            }

            if (s.TryGet("t", out var tv))
            {
                if (tv is double d) st.T = (float)d;
                else Err(s, ctx + "t", "숫자가 아니다");
            }

            var dest = Str(s, "to", ctx, false) ?? Str(s, "target", ctx, false) ?? Str(s, "zone", ctx, false);
            if (dest != null) ResolveTarget(s, st, dest, ctx, def);

            var from = Str(s, "from", ctx, false);
            if (from != null)
            {
                st.FromRegion = _zones.IndexOf(from);
                if (st.FromRegion < 0) Err(s, ctx + "from", $"알 수 없는 구역 '{from}'");
            }
            var lane = Str(s, "lane", ctx, false);
            if (lane != null)
            {
                st.LaneRegion = _zones.IndexOf(lane);
                if (st.LaneRegion < 0) Err(s, ctx + "lane", $"알 수 없는 구역 '{lane}'");
            }
            var dir = Str(s, "dir", ctx, false);
            if (dir == "inside") st.Direction = -1;
            else if (dir == "outside") st.Direction = 1;
            else if (dir != null && dir != "forward") Err(s, ctx + "dir", $"알 수 없는 방향 '{dir}'");
            if (Num(s, "distanceM", ctx, 0f, 120f, out var dist)) st.DistanceM = dist;
            return st;
        }

        void ResolveTarget(JsonObject s, ScenarioStep st, string dest, string ctx, ScenarioDefinition def)
        {
            var field = s.TryGet("to", out _) ? "to" : s.TryGet("target", out _) ? "target" : "zone";
            if (dest == "receiver") { st.TargetKind = StepTargetKind.Receiver; return; }
            var role = RoleIndex(def, dest);
            if (role >= 0) { st.TargetKind = StepTargetKind.Role; st.TargetRef = role; return; }
            var region = _zones.IndexOf(dest);
            if (region >= 0) { st.TargetKind = StepTargetKind.Region; st.TargetRef = region; return; }
            if (dest.StartsWith("opp_", StringComparison.Ordinal))
            {
                var token = dest.Substring(4);
                var u = token.IndexOf('_');
                if (u >= 0) token = token.Substring(0, u);
                if (Positions.TryParse(token, out var pm)) { st.TargetKind = StepTargetKind.OppPosition; st.OppPositions = pm; return; }
            }
            Err(s, ctx + field, $"존재하지 않는 배역·구역 참조 '{dest}'");
        }

        static int RoleIndex(ScenarioDefinition def, string name)
        {
            for (int i = 0; i < def.Cast.Length; i++)
                if (def.Cast[i].Name == name) return i;
            return -1;
        }

        void ParseBranches(JsonObject o, List<object> branches, ScenarioDefinition def)
        {
            var list = new List<ScenarioBranch>(branches.Count);
            for (int i = 0; i < branches.Count; i++)
            {
                var ctx = $"branches[{i}].";
                if (!(branches[i] is JsonObject b)) { Err(o, "branches", "분기가 객체가 아니다"); continue; }
                var br = new ScenarioBranch();
                var at = Str(b, "at", ctx, false);
                if (at != null)
                {
                    var dot = at.LastIndexOf('.');
                    var roleName = dot > 0 ? at.Substring(0, dot) : at;
                    var stepText = dot > 0 ? at.Substring(dot + 1) : "";
                    br.Role = RoleIndex(def, roleName);
                    if (br.Role < 0) { Err(b, ctx + "at", $"존재하지 않는 배역 참조 '{roleName}'"); continue; }
                    if (!int.TryParse(stepText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var step) || step < 1 || step > def.Cast[br.Role].Steps.Length)
                    {
                        Err(b, ctx + "at", $"단계 번호가 범위 밖이다 '{at}'");
                        continue;
                    }
                    br.Step = step - 1;
                    br.Success = ParseTarget(b, "success", ctx, def);
                    br.Fail = ParseTarget(b, "fail", ctx, def);
                    var alts = Arr(b, "alt", ctx, false);
                    if (alts != null)
                    {
                        var parsed = new List<BranchAlt>(alts.Count);
                        for (int a = 0; a < alts.Count; a++)
                        {
                            var actx = $"{ctx}alt[{a}].";
                            if (!(alts[a] is JsonObject ao)) { Err(b, ctx + "alt", "대안이 객체가 아니다"); continue; }
                            var cond = Str(ao, "if", actx, true);
                            if (cond == null) continue;
                            if (!BranchConditions.TryParse(cond, out var c)) { Err(ao, actx + "if", $"알 수 없는 조건 '{cond}'"); continue; }
                            parsed.Add(new BranchAlt { Condition = c, Target = ParseTargetObject(ao, actx, def) });
                        }
                        br.Alts = parsed.ToArray();
                    }
                }
                else
                {
                    var when = Str(b, "when", ctx, true);
                    if (when == null) continue;
                    if (!BranchConditions.TryParse(when, out var c)) { Err(b, ctx + "when", $"알 수 없는 조건 '{when}'"); continue; }
                    br.HasSituation = true;
                    br.Situation = c;
                    br.SituationTarget = ParseTargetObject(b, ctx, def);
                    if (br.SituationTarget.Kind == BranchTargetKind.Next) Err(b, ctx + "goto", "상황 분기는 'next'를 쓸 수 없다 (이어 갈 단계가 없다)");
                }
                list.Add(br);
            }
            def.Branches = list.ToArray();
        }

        /// <summary>success/fail 값: 문자열 또는 {goto, weights}. 없으면 null(기본 동작).</summary>
        BranchTarget ParseTarget(JsonObject b, string key, string ctx, ScenarioDefinition def)
        {
            if (!b.TryGet(key, out var v)) return null;
            if (v is string s) return Single(b, s, ctx + key, def);
            if (v is JsonObject o) return ParseTargetObject(o, ctx + key + ".", def);
            Err(b, ctx + key, "문자열 또는 객체여야 한다");
            return null;
        }

        BranchTarget ParseTargetObject(JsonObject o, string ctx, ScenarioDefinition def)
        {
            if (o.TryGet("do", out var doV))
            {
                if (doV is JsonObject act) return ParseJump(act, ctx + "do.", def);
                Err(o, ctx + "do", "객체가 아니다");
                return new BranchTarget { Kind = BranchTargetKind.Fallback };
            }
            if (!o.TryGet("goto", out var g))
            {
                Err(o, ctx + "goto", "필수 필드 누락");
                return new BranchTarget { Kind = BranchTargetKind.Fallback };
            }
            if (g is string s) return Single(o, s, ctx + "goto", def);
            if (!(g is List<object> list) || list.Count == 0)
            {
                Err(o, ctx + "goto", "문자열 또는 비어 있지 않은 배열이어야 한다");
                return new BranchTarget { Kind = BranchTargetKind.Fallback };
            }
            var ids = new string[list.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                if (list[i] is string id) ids[i] = id;
                else Err(o, ctx + "goto", "id는 문자열이어야 한다");
            }
            var weights = new float[ids.Length];
            var wl = Arr(o, "weights", ctx, false);
            if (wl != null)
            {
                if (wl.Count != ids.Length) Err(o, ctx + "weights", "goto와 길이가 다르다");
                float sum = 0f;
                for (int i = 0; i < wl.Count && i < weights.Length; i++)
                {
                    if (!(wl[i] is double d)) { Err(o, ctx + "weights", "숫자가 아니다"); continue; }
                    weights[i] = (float)d;
                    if (weights[i] < 0f) Err(o, ctx + "weights", "분기 가중치는 음수일 수 없다");
                    sum += weights[i];
                }
                if (!(sum > 0f)) Err(o, ctx + "weights", "분기 가중치 합이 0이다");
            }
            else for (int i = 0; i < weights.Length; i++) weights[i] = 1f;
            return new BranchTarget { Kind = BranchTargetKind.Chain, Ids = ids, Weights = weights };
        }

        BranchTarget ParseJump(JsonObject act, string ctx, ScenarioDefinition def)
        {
            var result = new BranchTarget { Kind = BranchTargetKind.JumpStep };
            var roleName = Str(act, "role", ctx, true);
            var role = roleName == null ? -1 : RoleIndex(def, roleName);
            if (role < 0)
            {
                if (roleName != null) Err(act, ctx + "role", $"존재하지 않는 배역 참조 '{roleName}'");
                result.Kind = BranchTargetKind.Fallback;
                return result;
            }
            var step = ParseStep(act, ctx, def);
            var old = def.Cast[role].Steps;
            var extended = new ScenarioStep[old.Length + 1];
            Array.Copy(old, extended, old.Length);
            extended[old.Length] = step;
            def.Cast[role].Steps = extended;
            result.JumpRole = role;
            result.JumpStep = old.Length;
            return result;
        }

        BranchTarget Single(JsonObject o, string s, string field, ScenarioDefinition def)
        {
            switch (s)
            {
                case "next": return new BranchTarget { Kind = BranchTargetKind.Next };
                case "fallback": return new BranchTarget { Kind = BranchTargetKind.Fallback };
                case "CT_opp": return new BranchTarget { Kind = BranchTargetKind.Loss };
                default:
                    if (s.Length == 0) Err(o, field, "빈 대상");
                    return new BranchTarget { Kind = BranchTargetKind.Chain, Ids = new[] { s }, Weights = new[] { 1f } };
            }
        }
    }
}
