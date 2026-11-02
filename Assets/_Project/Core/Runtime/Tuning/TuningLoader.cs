using System;
using System.Collections.Generic;
using System.IO;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Core.Tuning
{
    /// <summary>
    /// 튜닝 JSON 로드 (specs/001 FR-80~82).
    /// 정의: <c>{"version":1,"definitions":[{"id","default","min","max","unit","tag","source"}]}</c>
    /// 값 묶음: <c>{"version":1,"values":[{"id","value","unit"?}]}</c> — 정의에 없는 id·중복 id·범위 밖·단위 불일치는 실패, 빠진 id는 기본값.
    /// </summary>
    public static class TuningLoader
    {
        public const int FormatVersion = 1;

        public static TuningRegistry ParseDefinitions(string json)
        {
            var root = ParseRoot(json, "definitions");
            var list = new List<TuningDefinition>();
            foreach (var item in root)
            {
                var o = AsObject(item, null);
                var id = GetString(o, "id", null, required: true);
                list.Add(new TuningDefinition(
                    id,
                    GetFloat(o, "default", id),
                    GetFloat(o, "min", id),
                    GetFloat(o, "max", id),
                    GetString(o, "unit", id, required: false),
                    GetString(o, "tag", id, required: true),
                    GetString(o, "source", id, required: false)));
            }
            return new TuningRegistry(list);
        }

        public static TuningBundle ParseBundle(TuningRegistry registry, string json)
        {
            var root = ParseRoot(json, "values");
            var values = new float[registry.Count];
            var seen = new bool[registry.Count];
            for (int i = 0; i < values.Length; i++) values[i] = registry.Definitions[i].Default;
            foreach (var item in root)
            {
                var o = AsObject(item, null);
                var id = GetString(o, "id", null, required: true);
                if (!registry.TryGetIndex(id, out var idx)) throw new TuningLoadException(id, "정의되지 않은 id");
                if (seen[idx]) throw new TuningLoadException(id, "값이 중복으로 들어 있다");
                seen[idx] = true;
                var def = registry.Definitions[idx];
                var unit = GetString(o, "unit", id, required: false);
                if (unit != null && !string.Equals(unit, def.Unit, StringComparison.Ordinal))
                    throw new TuningLoadException(id, FormattableString.Invariant($"단위 불일치: '{unit}' (정의 '{def.Unit}')"));
                var v = GetFloat(o, "value", id);
                if (!def.InRange(v)) throw new TuningLoadException(id, FormattableString.Invariant($"값 {v}이 범위 [{def.Min}, {def.Max}] 밖"));
                values[idx] = v;
            }
            return new TuningBundle(registry, values);
        }

        public static TuningRegistry LoadDefinitionsFile(string path) => ParseDefinitions(ReadFile(path));

        public static TuningBundle LoadBundleFile(TuningRegistry registry, string path) => ParseBundle(registry, ReadFile(path));

        static string ReadFile(string path)
        {
            if (!File.Exists(path)) throw new TuningLoadException(null, $"파일 없음: {path}");
            return File.ReadAllText(path);
        }

        static List<object> ParseRoot(string json, string listKey)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new TuningLoadException(null, "빈 파일");
            object root;
            try
            {
                root = JsonReader.Parse(json);
            }
            catch (JsonException e)
            {
                throw new TuningLoadException(null, "JSON 문법 오류: " + e.Message, e);
            }
            var o = AsObject(root, null);
            if (!o.TryGet("version", out var ver) || !(ver is double d) || d != FormatVersion)
                throw new TuningLoadException(null, $"version은 {FormatVersion}이어야 한다");
            if (!o.TryGet(listKey, out var list) || !(list is List<object> l))
                throw new TuningLoadException(null, $"'{listKey}' 배열이 없다");
            return l;
        }

        static JsonObject AsObject(object v, string id)
        {
            var o = v as JsonObject ?? throw new TuningLoadException(id, "객체가 아니다");
            for (int i = 1; i < o.Members.Count; i++)
                for (int j = 0; j < i; j++)
                    if (string.Equals(o.Members[i].Key, o.Members[j].Key, StringComparison.Ordinal))
                        throw new TuningLoadException(id, $"키 '{o.Members[i].Key}'가 한 객체에 두 번 있다");
            return o;
        }

        static string GetString(JsonObject o, string key, string id, bool required)
        {
            if (!o.TryGet(key, out var v) || v == null)
            {
                if (required) throw new TuningLoadException(id, $"'{key}' 필드가 없다");
                return null;
            }
            return v as string ?? throw new TuningLoadException(id, $"'{key}'는 문자열이어야 한다");
        }

        static float GetFloat(JsonObject o, string key, string id)
        {
            if (!o.TryGet(key, out var v) || !(v is double d)) throw new TuningLoadException(id, $"'{key}' 숫자 필드가 없다");
            var f = (float)d;
            if (float.IsInfinity(f)) throw new TuningLoadException(id, FormattableString.Invariant($"'{key}' = {d}: float 범위 밖"));
            return f;
        }
    }
}
