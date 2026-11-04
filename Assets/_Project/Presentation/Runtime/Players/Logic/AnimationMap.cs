using System;
using System.Collections.Generic;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Replay;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>시뮬이 정한 액션 결과 (FR-48). 006/008이 결과 코드를 주기 전까지 호출자가 채운다.</summary>
    public enum ActionOutcome : byte
    {
        Success = 0,
        Fail = 1,
        Win = 2,
    }

    /// <summary>이벤트에 반응하는 엔티티.</summary>
    public enum MapWho : byte
    {
        Player = 0,
        Other = 1,
        Referee = 2,
        Scorer = 3,
    }

    /// <summary>
    /// 시뮬 상태·이벤트 → 클립 후보 매핑 표 (specs/043 FR-40·48, AC-06). 데이터: Presentation/Players/Data/043-anim-map.json.
    /// 모든 SimEventKind·MotionHint는 표에 있어야 한다(후보 또는 none). 표에 없는 종류가 들어오면 진단 로그 + 기본 클립.
    /// </summary>
    public sealed class AnimationMap
    {
        static readonly IReadOnlyList<int> Empty = Array.Empty<int>();

        readonly Dictionary<SimEventKind, int[]> _events = new Dictionary<SimEventKind, int[]>();
        readonly Dictionary<SimEventKind, MapWho> _who = new Dictionary<SimEventKind, MapWho>();
        readonly HashSet<SimEventKind> _none = new HashSet<SimEventKind>();
        readonly Dictionary<(SimEventKind, ActionOutcome), int[]> _outcomes = new Dictionary<(SimEventKind, ActionOutcome), int[]>();
        readonly Dictionary<MotionHint, int[]> _hints = new Dictionary<MotionHint, int[]>();
        readonly Dictionary<BallActionKind, int[]> _ball = new Dictionary<BallActionKind, int[]>();
        readonly int[] _default = new int[1];
        readonly List<string> _missing = new List<string>();

        public int DefaultClip => _default[0];
        /// <summary>표에 빠진 enum 값 목록 (AC-06: 비어 있어야 한다).</summary>
        public IReadOnlyList<string> Missing => _missing;

        public bool Has(SimEventKind k) => _events.ContainsKey(k) || _none.Contains(k);
        public bool Has(MotionHint h) => _hints.ContainsKey(h);
        public bool IsNone(SimEventKind k) => _none.Contains(k);
        public MapWho Who(SimEventKind k) => _who.TryGetValue(k, out var w) ? w : MapWho.Player;
        public IReadOnlyList<int> Candidates(SimEventKind k) => _events.TryGetValue(k, out var c) ? c : Empty;
        public IReadOnlyList<int> Candidates(MotionHint h) => _hints.TryGetValue(h, out var c) ? c : Empty;
        public IReadOnlyList<int> Candidates(BallActionKind a) => _ball.TryGetValue(a, out var c) ? c : Empty;

        /// <summary>결과별 후보 (FR-48). 결과 전용 후보가 있으면 그것, 없으면 결과 태그가 맞지 않는 클립(fail·win 전용)을 뺀 기본 후보.</summary>
        public IReadOnlyList<int> Candidates(SimEventKind k, ActionOutcome o)
        {
            if (o != ActionOutcome.Success && _outcomes.TryGetValue((k, o), out var c)) return c;
            if (_outcomes.TryGetValue((k, ActionOutcome.Success), out var s)) return s;
            return Candidates(k);
        }

        /// <summary>종류 → 후보. 표에 없으면 진단 로그 + 기본 클립 (AC-06).</summary>
        public IReadOnlyList<int> Resolve(SimEventKind k, ILogger log)
        {
            if (_events.TryGetValue(k, out var c)) return c;
            if (_none.Contains(k)) return Empty;
            log.Warning("[anim] 매핑 표에 없는 SimEvent 종류 {0} — 기본 클립 사용", (int)k);
            return _default;
        }

        static int[] Ids(List<object> arr, ClipLibrary lib, string where)
        {
            var o = new int[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                var id = (string)arr[i];
                o[i] = lib.IndexOf(id);
                if (o[i] < 0) throw new FormatException($"매핑 표 {where}: 없는 클립 '{id}'");
            }
            return o;
        }

        public static AnimationMap Parse(string json, ClipLibrary lib, ILogger log)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            if (!root.TryGet("schemaVersion", out var sv) || (double)sv != 1) throw new FormatException("anim-map schemaVersion 1 아님");
            var m = new AnimationMap();
            root.TryGet("default", out var def);
            m._default[0] = lib.IndexOf((string)def);
            if (m._default[0] < 0) throw new FormatException("기본 클립 없음: " + def);

            root.TryGet("motionHints", out var mh);
            foreach (var kv in ((JsonObject)mh).Members)
            {
                if (!Enum.TryParse(kv.Key, out MotionHint h)) throw new FormatException("모르는 MotionHint: " + kv.Key);
                m._hints[h] = Ids((List<object>)kv.Value, lib, kv.Key);
            }
            root.TryGet("ballActions", out var ba);
            foreach (var kv in ((JsonObject)ba).Members)
            {
                if (!Enum.TryParse(kv.Key, out BallActionKind a)) throw new FormatException("모르는 BallActionKind: " + kv.Key);
                m._ball[a] = Ids((List<object>)kv.Value, lib, kv.Key);
            }
            root.TryGet("events", out var ev);
            foreach (var kv in ((JsonObject)ev).Members)
            {
                if (!Enum.TryParse(kv.Key, out SimEventKind k)) throw new FormatException("모르는 SimEventKind: " + kv.Key);
                var o = (JsonObject)kv.Value;
                if (o.TryGet("none", out _))
                {
                    m._none.Add(k);
                    continue;
                }
                o.TryGet("clips", out var cl);
                var ids = Ids((List<object>)cl, lib, kv.Key);
                m._events[k] = ids;
                if (o.TryGet("who", out var who))
                    m._who[k] = (string)who switch { "other" => MapWho.Other, "referee" => MapWho.Referee, "scorer" => MapWho.Scorer, _ => MapWho.Player };
                if (o.TryGet("outcomes", out var oc))
                {
                    foreach (var okv in ((JsonObject)oc).Members)
                    {
                        var oo = okv.Key == "fail" ? ActionOutcome.Fail : okv.Key == "win" ? ActionOutcome.Win : ActionOutcome.Success;
                        m._outcomes[(k, oo)] = Ids((List<object>)okv.Value, lib, kv.Key + "." + okv.Key);
                    }
                    // 성공 후보: 기본 후보 중 fail·win 전용 태그가 아닌 것
                    var succ = new List<int>();
                    foreach (var i in ids)
                        if (lib.Get(i).Outcome != "fail" && lib.Get(i).Outcome != "win") succ.Add(i);
                    m._outcomes[(k, ActionOutcome.Success)] = succ.ToArray();
                }
            }
            foreach (SimEventKind k in Enum.GetValues(typeof(SimEventKind)))
                if (k != SimEventKind.None && !m.Has(k)) m._missing.Add("SimEventKind." + k);
            foreach (MotionHint h in Enum.GetValues(typeof(MotionHint)))
                if (!m.Has(h)) m._missing.Add("MotionHint." + h);
            foreach (BallActionKind a in Enum.GetValues(typeof(BallActionKind)))
                if (a != BallActionKind.None && !m._ball.ContainsKey(a)) m._missing.Add("BallActionKind." + a);
            foreach (var s in m._missing) log.Warning("[anim] 매핑 표 누락: {0}", s);
            return m;
        }
    }

    /// <summary>넘어짐 방향 (FR-47): 밀린 방향을 선수 기준 각도로 바꿔 8방향 중 가장 가까운 넘어짐 클립.</summary>
    public static class HitDirection
    {
        /// <summary>push = 밀린(넘어질) 방향 월드 벡터, yaw = 선수 정면 각(라디안, 0 = +Z, +X 쪽이 +).</summary>
        public static int FallClip(ClipLibrary lib, Vec3 push, float yaw)
        {
            float world = (float)Math.Atan2(push.X, push.Z);
            float rel = (world - yaw) * 180f / (float)Math.PI;
            rel = ((rel % 360f) + 360f) % 360f;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < lib.Count; i++)
            {
                var c = lib.Get(i);
                if (c.Group != "fall" || c.HitDir == int.MinValue || c.Id == "hit_stumble") continue;
                float d = Math.Abs(((rel - c.HitDir) % 360f + 540f) % 360f - 180f);
                if (d < bestD - 1e-4f) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
