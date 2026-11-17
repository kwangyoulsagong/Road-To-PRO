using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Highlight.Selection;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Motion;
using RoadToPro.Tests.EditMode.Presentation.Players;
using RoadToPro.Tests.EditMode.Presentation.Players.Motion;
using RoadToPro.Verification.Bench;
using RoadToPro.Verification.Samples.Highlight;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>
    /// specs/082 FR-05: 실경기 하이라이트 접촉 진단. 시드 → 90분 시뮬 → 058 장면 선정 → 클립 변환 → AnimPlan(선수 키 배율 포함) →
    /// 054 측정기(<see cref="MotionMeter"/>)로 헤더·슬라이딩 태클 사건마다 접촉 부위–공 거리·시각 차·고른 클립을 잰다.
    /// 시뮬은 읽기만 한다(진단 켬/끔 해시 같음). 리그·클립이 에디터 에셋이라 Verification이 아니라 테스트 어셈블리에 둔다(플랜과 다른 점).
    /// </summary>
    internal static class LiveContactAudit
    {
        public enum Kind { Header, Slide }

        public sealed class Row
        {
            public ulong Seed;
            public int Scene;
            public long Seq;
            public Kind Kind;
            public SimEventKind SimKind;
            public int Entity;
            public float Time;
            /// <summary>고른 클립 id. 이 사건에 동작이 없으면 null.</summary>
            public string Clip;
            public float Distance = float.NaN, DistanceAtContact = float.NaN, DistanceRaw = float.NaN, TimeError = float.NaN, DistanceLimit = float.NaN;
            /// <summary>사건 공 높이(m, 시뮬).</summary>
            public float BallHeight;
            public float Scale;
            public bool Missed, Excluded;
            public ActionOutcome Outcome;
            /// <summary>슬라이딩 다운 정지가 끝나는 재생 시각(정지 없으면 NaN) · 고른 동작의 리프트(m).</summary>
            public float DownUntil = float.NaN, Lift;
            public string Reason;
        }

        /// <summary>헤더 사건: 부위 Head인 슈팅·패스·걷어내기 (071 승자의 실행, 043 맵 "Header"는 기록뿐).</summary>
        public static bool IsHeader(in AnimEvent ev) =>
            (ev.Flags & AnimEvent.FromSim) != 0 && ev.Part == BodyPart.Head &&
            (ev.Kind == SimEventKind.Shot || ev.Kind == SimEventKind.Pass || ev.Kind == SimEventKind.Clearance || ev.Kind == SimEventKind.Cross);

        /// <summary>슬라이딩 태클 사건 (070 슬라이딩 비트).</summary>
        public static bool IsSlide(in AnimEvent ev) =>
            (ev.Flags & AnimEvent.FromSim) != 0 && ev.Kind == SimEventKind.Tackle && (ev.Flags & EventCodes.TackleSliding) != 0;

        /// <summary>장면 경계 (082 경계 · 예외): 접촉 시각이 장면 프레임 밖이면 진단 제외 사유. 안이면 null.</summary>
        public static string BoundaryReason(HighlightData d, float t)
        {
            if (t < d.StartTime) return "접촉 시각이 장면 시작 전";
            if (t > d.EndTime) return "접촉 시각이 장면 끝 뒤";
            return null;
        }

        /// <summary>하이라이트 캐스트와 같은 키 배율 (HighlightCastStage: BodyInput(4300 + e·13), 역할별 키 분포).</summary>
        public static float[] CastScales()
        {
            var h = LiveMatchFixture.CastHeightsCm();
            var s = new float[h.Length];
            for (int e = 0; e < s.Length; e++) s[e] = h[e] / AppearanceGenerator.RefHeightCm;
            return s;
        }

        static MatchTuning _tuning;
        static readonly object Gate = new object();
        static readonly Dictionary<ulong, MatchSim> Played = new Dictionary<ulong, MatchSim>();

        static MatchTuning Tuning()
        {
            lock (Gate)
            {
                if (_tuning == null)
                {
                    var data = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data");
                    _tuning = LiveMatchFixture.Tuning(new DecisionBenchWorkloads(data).Tuning, data);
                }
                return _tuning;
            }
        }

        /// <summary>플레이어 하이라이트와 같은 실경기 픽스처 (HighlightScreen·FeelSceneCoverage: 058 LiveMatchFixture + 판단 벤치 튜닝). 새 경기(아직 안 돌림).</summary>
        public static MatchSim NewSim(ulong seed) =>
            new MatchSim(LiveMatchFixture.Prepare(MatchBenchWorkloads.Setup(seed)), Tuning(), LiveMatchFixture.Options());

        /// <summary>
        /// 90분을 끝낸 경기 (시드별 1회, 테스트 클래스끼리 공유 — 경기는 결정론이라 결과가 같다). 읽기만 할 것.
        /// 2026-11-17 테스트 시간 단축: 같은 시드 경기를 클래스마다 다시 돌리던 것을 없앰.
        /// </summary>
        public static MatchSim Played90(ulong seed)
        {
            lock (Gate) if (Played.TryGetValue(seed, out var s)) return s;
            var sim = NewSim(seed);
            sim.RunToEnd();
            lock (Gate) Played[seed] = sim;
            return sim;
        }

        /// <summary>여러 시드를 미리 병렬로 돌린다 (경기 하나는 단일 스레드 — 20-match-engine 규칙: 병렬은 경기 여러 개를 나눌 때만).</summary>
        public static void Prewarm(IReadOnlyList<ulong> seeds)
        {
            Tuning();
            System.Threading.Tasks.Parallel.For(0, seeds.Count, i => Played90(seeds[i]));
        }

        /// <summary>시드 하나: 실경기 하이라이트 장면 전부의 헤더·슬라이딩 사건 행.</summary>
        public static List<Row> RunSeed(ulong seed, float[] scales)
        {
            var sim = Played90(seed);
            var list = LiveSceneSelector.Select(sim.Result.EventLog, sim.Result.EndTick, PitchSpec.Default, LiveMatchFixture.MyPlayerId, new SceneScoreTable());
            var rows = new List<Row>();
            for (int i = 0; i < list.Scenes.Length; i++)
            {
                var s = list.Scenes[i];
                var clip = sim.ExtractClip((long)Math.Floor(s.Start * 10d), (long)Math.Ceiling(s.End * 10d) + 1L);
                var data = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                rows.AddRange(Measure(data, scales, seed, i));
            }
            return rows;
        }

        /// <summary>장면 하나: 헤더·슬라이딩 사건 엔티티만 리그를 만들어 054 측정기로 잰다.</summary>
        public static List<Row> Measure(HighlightData data, float[] scales, ulong seed = 0, int scene = 0, Func<AnimEvent, Kind?> classify = null)
        {
            classify ??= ev => IsHeader(ev) ? Kind.Header : IsSlide(ev) ? Kind.Slide : (Kind?)null;
            var rows = new List<Row>();
            var actors = new List<int>();
            foreach (var ev in data.Events)
            {
                var k = classify(ev);
                if (k == null || ev.Entity < 0 || ev.Entity >= HighlightData.Players) continue;
                rows.Add(new Row
                {
                    Seed = seed, Scene = scene, Seq = ev.Seq, Kind = k.Value, SimKind = ev.Kind, Entity = ev.Entity, Time = ev.Time,
                    Outcome = ev.Outcome, BallHeight = ev.BallPos.Y, Scale = scales != null ? scales[ev.Entity] : 1f, Reason = BoundaryReason(data, ev.Time),
                });
                if (!actors.Contains(ev.Entity)) actors.Add(ev.Entity);
            }
            if (rows.Count == 0) return rows;
            foreach (var r in rows) r.Excluded = r.Reason != null;

            const float rate = 1.5f, fps = 60f;
            using var kit = new AnimRigKit(data, scales, actors.ToArray());
            var meter = new MotionMeter(kit.Plan, kit.Rigs, actors.ToArray(), MotionStandardsTests.Load(), $"live-{seed}-{scene}", rate, fps) { Posts = MotionRun.GoalPosts };
            // 접촉만 잰다: 포즈 = f(t)라 장면 전체 재생(속도·미끄러짐 지표용) 없이 이벤트 시각만 적용해도 같은 값 (MotionMeter.MeasureContacts 주석).
            // 전에는 장면마다 60 fps 전체 재생 — 진단 시간의 대부분이었다 (2026-11-17 테스트 시간 단축)
            meter.MeasureContacts(kit.System.ApplyAt);

            foreach (var r in rows)
            {
                var tl = kit.Plan.Timeline(r.Entity);
                for (int i = 0; i < tl.Count; i++)
                    if (tl.SeqOf(i) == r.Seq)
                    {
                        var a = tl.Get(i);
                        r.Clip = kit.Plan.Library.Get(a.Clip).Id;
                        r.Lift = a.Lift;
                        if (a.HoldFor > 0f) r.DownUntil = a.Start + a.HoldAt / a.Rate + a.HoldFor;
                        break;
                    }
                if (r.Clip == null && r.Reason == null) r.Reason = "동작 없음";
                foreach (var c in meter.Report.Contacts)
                {
                    if (c.Seq != r.Seq || c.Entity != r.Entity) continue;
                    r.Distance = c.Distance;
                    r.DistanceAtContact = c.DistanceAtContact;
                    r.DistanceRaw = c.DistanceRaw;
                    r.TimeError = c.TimeError;
                    r.DistanceLimit = c.DistanceLimit;
                    r.Missed = c.Missed;
                }
            }
            return rows;
        }

        public static string Json(IReadOnlyList<Row> rows)
        {
            var sb = new StringBuilder("[\n");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                sb.Append("  {")
                  .Append($"\"seed\": {r.Seed}, \"scene\": {r.Scene}, \"seq\": {r.Seq}, \"kind\": \"{r.Kind}\", \"simKind\": \"{r.SimKind}\", \"entity\": {r.Entity}, ")
                  .Append($"\"time\": {F(r.Time)}, \"clip\": {(r.Clip == null ? "null" : "\"" + r.Clip + "\"")}, \"ballHeight\": {F(r.BallHeight)}, \"scale\": {F(r.Scale)}, ")
                  .Append($"\"distance\": {F(r.Distance)}, \"distanceAtContact\": {F(r.DistanceAtContact)}, \"distanceRaw\": {F(r.DistanceRaw)}, \"timeError\": {F(r.TimeError)}, ")
                  .Append($"\"missed\": {(r.Missed ? "true" : "false")}, \"outcome\": \"{r.Outcome}\", \"lift\": {F(r.Lift)}, \"downUntil\": {F(r.DownUntil)}, \"excluded\": {(r.Excluded ? "true" : "false")}, \"reason\": {(r.Reason == null ? "null" : "\"" + r.Reason + "\"")}")
                  .Append(i + 1 < rows.Count ? "},\n" : "}\n");
            }
            return sb.Append("]\n").ToString();
        }

        static string F(float v) => float.IsNaN(v) ? "null" : v.ToString("0.0000", CultureInfo.InvariantCulture);

        public static string Summary(IReadOnlyList<Row> rows)
        {
            var sb = new StringBuilder();
            foreach (var r in rows)
                sb.AppendLine($"{r.Seed}#{r.Scene} seq {r.Seq} {r.Kind}/{r.SimKind} e{r.Entity} t {r.Time:0.00} clip {r.Clip ?? "-"} ballY {r.BallHeight:0.00} " +
                              $"d {r.Distance * 100:0.0} dC {r.DistanceAtContact * 100:0.0} dRaw {r.DistanceRaw * 100:0.0} cm dt {r.TimeError * 1000:0} ms miss {r.Missed} {r.Reason}");
            return sb.ToString();
        }
    }
}
