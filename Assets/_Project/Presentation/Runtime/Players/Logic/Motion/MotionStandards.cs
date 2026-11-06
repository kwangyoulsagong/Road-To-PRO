using System;
using System.Collections.Generic;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Presentation.Players.Logic.Motion
{
    /// <summary>기준값 하나. 없는 쪽 경계는 무한대.</summary>
    public readonly struct MotionThreshold
    {
        public readonly float Min, Max;
        public readonly string Unit, Source;

        public MotionThreshold(float min, float max, string unit, string source)
        {
            Min = min;
            Max = max;
            Unit = unit;
            Source = source;
        }

        public bool Passes(float v) => v >= Min && v <= Max;
    }

    /// <summary>걸음 빈도 표 한 줄 (055 FR-01, 054 FR-12).</summary>
    public readonly struct CadenceRow
    {
        public readonly string Gait;
        public readonly float Speed, Min, Max;

        public CadenceRow(string gait, float speed, float min, float max)
        {
            Gait = gait;
            Speed = speed;
            Min = min;
            Max = max;
        }
    }

    /// <summary>움직임 품질 기준값 (specs/054 FR-72). 원본: Presentation/Players/Data/054-motion-standards.json.</summary>
    public sealed class MotionStandards
    {
        readonly Dictionary<string, MotionThreshold> _t = new Dictionary<string, MotionThreshold>(StringComparer.Ordinal);
        readonly Dictionary<string, MotionThreshold> _prev = new Dictionary<string, MotionThreshold>(StringComparer.Ordinal);
        readonly List<CadenceRow> _cadence = new List<CadenceRow>();

        public int Version { get; private set; }
        public IReadOnlyList<CadenceRow> Cadence => _cadence;
        public IEnumerable<string> Ids => _t.Keys;

        public MotionThreshold this[string id] => _t.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException("기준값 없음: " + id);
        public float Max(string id) => this[id].Max;
        public float Min(string id) => this[id].Min;
        public bool TryGetPrevious(string id, out MotionThreshold t) => _prev.TryGetValue(id, out t);

        static MotionThreshold Read(string id, JsonObject o)
        {
            bool hasMin = o.TryGet("min", out var mn), hasMax = o.TryGet("max", out var mx);
            if (!hasMin && !hasMax) throw new FormatException($"기준값 {id}: min·max 둘 다 없음");
            float lo = hasMin ? (float)(double)mn : float.NegativeInfinity;
            float hi = hasMax ? (float)(double)mx : float.PositiveInfinity;
            if (lo > hi) throw new FormatException($"기준값 {id}: min > max");
            o.TryGet("unit", out var u);
            o.TryGet("fr", out var fr);
            return new MotionThreshold(lo, hi, u as string, fr as string);
        }

        public static MotionStandards Parse(string json)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            if (!root.TryGet("schemaVersion", out var sv) || (double)sv != 1) throw new FormatException("054-motion-standards.json schemaVersion 1 아님");
            var s = new MotionStandards();
            if (root.TryGet("version", out var ver)) s.Version = (int)(double)ver;
            if (!root.TryGet("thresholds", out var th) || !(th is JsonObject tho)) throw new FormatException("thresholds 없음");
            foreach (var m in tho.Members) s._t[m.Key] = Read(m.Key, (JsonObject)m.Value);
            if (root.TryGet("previous", out var pv) && pv is JsonObject pvo)
                foreach (var m in pvo.Members) s._prev[m.Key] = Read(m.Key, (JsonObject)m.Value);
            if (root.TryGet("cadenceTable", out var ct))
                foreach (var it in (List<object>)ct)
                {
                    var o = (JsonObject)it;
                    o.TryGet("gait", out var g); o.TryGet("speed", out var sp); o.TryGet("min", out var a); o.TryGet("max", out var b);
                    s._cadence.Add(new CadenceRow((string)g, (float)(double)sp, (float)(double)a, (float)(double)b));
                }
            return s;
        }
    }
}
