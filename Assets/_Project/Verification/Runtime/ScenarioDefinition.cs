using System;
using System.Collections.Generic;
using System.Globalization;

namespace RoadToPro.Verification
{
    /// <summary>시나리오 단계 하나. 필드는 원본 JSON 그대로 둔다.</summary>
    public sealed class StepDef
    {
        public int Index { get; }
        public string Type { get; }
        public Dictionary<string, object> Fields { get; }

        public StepDef(int index, Dictionary<string, object> fields)
        {
            Index = index;
            Fields = fields;
            Type = fields.TryGetValue("type", out var t) ? t as string : null;
        }

        public bool Has(string k) => Fields.ContainsKey(k) && Fields[k] != null;
        public string GetString(string k, string def = null) => Has(k) ? Convert.ToString(Fields[k], CultureInfo.InvariantCulture) : def;
        public double GetDouble(string k, double def = 0) => Has(k) ? Convert.ToDouble(Fields[k], CultureInfo.InvariantCulture) : def;
        public List<object> GetList(string k) => Has(k) ? Fields[k] as List<object> : new List<object>();
        public string Label => Has("name") ? $"#{Index} {Type} '{GetString("name")}'" : $"#{Index} {Type}";
    }

    /// <summary>
    /// E2E 시나리오 (specs/000 FR-20). 형식 원본은 Tools/devicefarm/farm/scenario.py와 같다.
    /// Python이 먼저 검증하지만, 드라이버도 실행에 필요한 규칙은 다시 확인한다.
    /// </summary>
    public sealed class ScenarioDefinition
    {
        static readonly Dictionary<string, string[]> Required = new Dictionary<string, string[]>
        {
            { "goto", new[] { "screen" } },
            { "tap", new[] { "element" } },
            { "type", new[] { "element", "text" } },
            { "waitFor", new string[0] },
            { "assertVisible", new[] { "element" } },
            { "assertText", new[] { "element", "text" } },
            { "assertNumberRange", new[] { "element", "min", "max" } },
            { "assertNoOverlap", new[] { "scope" } },
            { "assertNoClip", new[] { "scope" } },
            { "capture", new[] { "name" } },
            { "perfBegin", new[] { "name" } },
            { "perfEnd", new[] { "name" } },
            { "checkpoint", new[] { "name" } },
            { "freeze", new string[0] },
            { "inject", new[] { "what" } },
        };

        public string Id { get; private set; }
        public string Scene { get; private set; }
        public int Seed { get; private set; }
        public bool SelfTest { get; private set; }
        public double DefaultTimeoutSec { get; private set; } = 10;
        public List<StepDef> Steps { get; } = new List<StepDef>();

        public double TimeoutFor(StepDef s) => s.Has("timeoutSec") ? s.GetDouble("timeoutSec") : DefaultTimeoutSec;

        public static ScenarioDefinition Parse(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) throw new FormatException("시나리오 최상위는 객체");
            var s = new ScenarioDefinition
            {
                Id = Str(root, "id"),
                Scene = Str(root, "scene"),
                SelfTest = root.TryGetValue("selfTest", out var st) && st is bool b && b,
            };
            if (!(root.TryGetValue("fixture", out var fx) && fx is Dictionary<string, object> fixture && fixture.TryGetValue("seed", out var seed) && seed is long sl))
                throw new FormatException($"시나리오 {s.Id}: fixture.seed(정수) 필요");
            s.Seed = (int)sl;
            if (root.TryGetValue("defaults", out var d) && d is Dictionary<string, object> defs && defs.TryGetValue("timeoutSec", out var to) && to != null)
                s.DefaultTimeoutSec = Convert.ToDouble(to, CultureInfo.InvariantCulture);

            if (!(root.TryGetValue("steps", out var stepsObj) && stepsObj is List<object> steps) || steps.Count == 0)
                throw new FormatException($"시나리오 {s.Id}: steps가 비었다");

            string openPerf = null;
            for (int i = 0; i < steps.Count; i++)
            {
                if (!(steps[i] is Dictionary<string, object> f)) throw new FormatException($"단계 #{i}: 객체가 아니다");
                var step = new StepDef(i, f);
                if (step.Type == null || !Required.TryGetValue(step.Type, out var req))
                    throw new FormatException($"단계 #{i}: 알 수 없는 단계 종류 '{step.Type}'");
                foreach (var k in req)
                    if (!step.Has(k)) throw new FormatException($"단계 #{i}: '{step.Type}'에 '{k}' 필요");

                switch (step.Type)
                {
                    case "perfBegin":
                        if (openPerf != null) throw new FormatException($"단계 #{i}: 성능 구간 '{openPerf}'이 닫히기 전에 새 구간");
                        openPerf = step.GetString("name");
                        break;
                    case "perfEnd":
                        if (openPerf != step.GetString("name")) throw new FormatException($"단계 #{i}: 맞는 perfBegin 없음");
                        openPerf = null;
                        break;
                    case "capture":
                        if (openPerf != null) throw new FormatException($"단계 #{i}: 성능 구간 '{openPerf}' 안에서는 캡처 금지");
                        break;
                    case "inject":
                        if (!s.SelfTest) throw new FormatException($"단계 #{i}: inject는 셀프테스트 시나리오(selfTest: true)에서만 허용");
                        break;
                }
                s.Steps.Add(step);
            }
            if (openPerf != null) throw new FormatException($"성능 구간 '{openPerf}'이 닫히지 않음");
            return s;
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            if (d.TryGetValue(k, out var v) && v is string s && s.Length > 0) return s;
            throw new FormatException($"시나리오: '{k}' 필요");
        }
    }
}
