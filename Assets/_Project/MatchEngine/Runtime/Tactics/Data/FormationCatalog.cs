using System;
using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Tactics
{
    public sealed class FormationLoadException : Exception
    {
        public FormationLoadException(string message) : base(message) { }
    }

    /// <summary>포메이션 카탈로그 (FR-10·12). JSON 문자열을 받아 검증한다. 파일 읽기는 호출자가 한다. 순서는 파일 순서로 고정.</summary>
    public sealed class FormationCatalog
    {
        const float MaxOffset = 0.5f;
        static readonly string[] ProfileNames = { "BuildUp", "Progression", "FinalThird", "DefHigh", "DefMid", "DefLow" };

        readonly FormationDefinition[] _items;

        public ulong Hash { get; }
        public int Count => _items.Length;
        public FormationDefinition this[int index] => _items[index];

        FormationCatalog(FormationDefinition[] items, ulong hash)
        {
            _items = items;
            Hash = hash;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _items.Length; i++)
                if (string.Equals(_items[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        public static FormationCatalog Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormationLoadException("빈 포메이션 데이터");
            object root;
            try { root = JsonReader.Parse(json); }
            catch (JsonException e) { throw new FormationLoadException("JSON 문법 오류: " + e.Message); }
            var o = root as JsonObject ?? throw new FormationLoadException("최상위가 객체가 아니다");
            if (!o.TryGet("formations", out var fl) || !(fl is List<object> list) || list.Count == 0)
                throw new FormationLoadException("'formations' 배열이 없다");

            var items = new FormationDefinition[list.Count];
            var h = StableHasher.Create();
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = ParseFormation(list[i]);
                for (int k = 0; k < i; k++)
                    if (items[k].Id == items[i].Id) throw new FormationLoadException($"포메이션 id 중복: {items[i].Id}");
                AddTo(ref h, items[i]);
            }
            return new FormationCatalog(items, h.Value);
        }

        static FormationDefinition ParseFormation(object v)
        {
            var o = v as JsonObject ?? throw new FormationLoadException("포메이션이 객체가 아니다");
            var id = Str(o, "id", "포메이션");
            if (!o.TryGet("slots", out var sl) || !(sl is List<object> slots)) throw new FormationLoadException($"{id}: 'slots' 없음");
            if (slots.Count != FormationDefinition.SlotCount) throw new FormationLoadException($"{id}: 슬롯은 {FormationDefinition.SlotCount}개여야 한다 (받은 수 {slots.Count})");

            var def = new FormationDefinition { Id = id, Slots = new FormationSlot[slots.Count] };
            int gk = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = ParseSlot(slots[i], id);
                if (s.Position == SlotPosition.GK) gk++;
                for (int k = 0; k < i; k++)
                    if (def.Slots[k].Id == s.Id) throw new FormationLoadException($"{id}: 슬롯 id 중복 {s.Id}");
                def.Slots[i] = s;
            }
            if (gk != 1 || def.Slots[0].Position != SlotPosition.GK) throw new FormationLoadException($"{id}: GK 슬롯은 첫 번째 1개여야 한다");

            if (o.TryGet("reduced", out var rv))
            {
                var reduced = rv as List<object> ?? throw new FormationLoadException($"{id}: 'reduced'는 배열이어야 한다");
                def.Reduced = new ReducedVariant[reduced.Count];
                for (int i = 0; i < reduced.Count; i++) def.Reduced[i] = ParseReduced(reduced[i], def);
            }
            return def;
        }

        static FormationSlot ParseSlot(object v, string formation)
        {
            var o = v as JsonObject ?? throw new FormationLoadException($"{formation}: 슬롯이 객체가 아니다");
            var id = Str(o, "id", formation);
            var pos = Str(o, "pos", formation);
            if (!Enum.TryParse<SlotPosition>(pos, false, out var p)) throw new FormationLoadException($"{formation}/{id}: 포지션 '{pos}' 오류");
            if (!o.TryGet("anchors", out var a) || !(a is JsonObject anchors)) throw new FormationLoadException($"{formation}/{id}: 'anchors' 없음");

            var arr = new Vec2[ShapeProfiles.Count];
            for (int i = 0; i < arr.Length; i++)
            {
                if (!anchors.TryGet(ProfileNames[i], out var av) || !(av is List<object> pair) || pair.Count != 2 || !(pair[0] is double x) || !(pair[1] is double y))
                    throw new FormationLoadException($"{formation}/{id}: 앵커 {ProfileNames[i]} 없음");
                if (!(Math.Abs(x) <= MaxOffset && Math.Abs(y) <= MaxOffset))
                    throw new FormationLoadException($"{formation}/{id}: 앵커 {ProfileNames[i]}가 범위 밖");
                arr[i] = new Vec2((float)x, (float)y);
            }
            return new FormationSlot
            {
                Id = id,
                Position = p,
                IsLine = o.TryGet("line", out var l) && l is bool b && b,
                ZoneWeight = o.TryGet("zoneWeight", out var z) && z is double d && d > 0 ? (float)d : throw new FormationLoadException($"{formation}/{id}: zoneWeight 오류"),
                RoleId = o.TryGet("role", out var r) ? r as string : null,
                Anchors = arr,
            };
        }

        static ReducedVariant ParseReduced(object v, FormationDefinition def)
        {
            var o = v as JsonObject ?? throw new FormationLoadException($"{def.Id}: reduced 항목이 객체가 아니다");
            if (!o.TryGet("fieldPlayers", out var n) || !(n is double nd)) throw new FormationLoadException($"{def.Id}: fieldPlayers 없음");
            if (!o.TryGet("remove", out var rl) || !(rl is List<object> remove)) throw new FormationLoadException($"{def.Id}: remove 없음");
            var r = new ReducedVariant { FieldPlayers = (int)nd, RemovedSlots = new int[remove.Count] };
            for (int i = 0; i < remove.Count; i++)
            {
                var idx = Array.FindIndex(def.Slots, s => s.Id == remove[i] as string);
                if (idx <= 0) throw new FormationLoadException($"{def.Id}: 없는 슬롯 제거 {remove[i]}");
                r.RemovedSlots[i] = idx;
            }
            if (r.RemovedSlots.Length != FormationDefinition.SlotCount - 1 - r.FieldPlayers)
                throw new FormationLoadException($"{def.Id}: fieldPlayers {r.FieldPlayers}에 제거 슬롯 {r.RemovedSlots.Length}개는 맞지 않다");
            return r;
        }

        static string Str(JsonObject o, string key, string ctx) =>
            o.TryGet(key, out var v) && v is string s && s.Length > 0 ? s : throw new FormationLoadException($"{ctx}: '{key}' 없음");

        static void AddTo(ref StableHasher h, FormationDefinition f)
        {
            h.Add(f.Id);
            foreach (var s in f.Slots)
            {
                h.Add(s.Id); h.Add(s.RoleId ?? string.Empty); h.Add((byte)s.Position); h.Add(s.IsLine); h.Add(s.ZoneWeight);
                foreach (var a in s.Anchors) { h.Add(a.X); h.Add(a.Y); }
            }
            foreach (var r in f.Reduced)
            {
                h.Add(r.FieldPlayers);
                foreach (var x in r.RemovedSlots) h.Add(x);
            }
        }
    }
}
