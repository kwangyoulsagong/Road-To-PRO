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
        /// <summary>083: SlideWin = 이긴 슬라이딩(FR-03), MissedJump = 헤더 없이 끝난 경합 참여자 점프(FR-01) — 승자·가장 가까운 상대 각 1행.</summary>
        public enum Kind { Header, Slide, SlideWin, MissedJump }

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
            /// <summary>083 MissedJump: 기록 정점 시각(재생 초, 없으면 NaN)과 점프 접촉 시각 차(s).</summary>
            public float ApexTime = float.NaN, ApexError = float.NaN;
            /// <summary>083 SlideWin: 접근 구간(접촉 − 0.9 s ~ 접촉) 화면 루트 수평 속도의 프레임당 최대 변화(m/s, 60 fps).</summary>
            public float MaxSpeedDelta = float.NaN;
            /// <summary>083 T02 원인 표: 사건 시각 시뮬 선수 위치–이벤트 공 수평 거리(m), 같은 시각 화면 공(30 Hz 프레임)–이벤트 공 거리(m), 루트 보정 크기(m).</summary>
            public float SimReach = float.NaN, FrameBallGap = float.NaN, RootOffset = float.NaN, PlanOffset = float.NaN, Stride = float.NaN, ActionsNear = float.NaN;
            /// <summary>083 진단: 사건 전후 같은 선수 동작 (클립@시작/접촉/끝/가중치).</summary>
            public string Neighbors;
            /// <summary>083 진단: 리그 발 위치 − (화면 루트 + 정면 각으로 돌린 클립 접촉점 × 배율) 거리(m) — 포즈 쪽 차이. 화면 정면 각 − 시뮬 정면 각(도).</summary>
            public float PoseGap = float.NaN, YawDiffDeg = float.NaN;
        }

        /// <summary>071 Missed 결과 Header 이벤트 (경합 승자, 상대 = 가장 가까운 상대 참여자). A = 결과는 Flags 하위 비트.</summary>
        public static bool IsMissedAerial(in AnimEvent ev) =>
            (ev.Flags & AnimEvent.FromSim) != 0 && ev.Kind == SimEventKind.Header &&
            (ev.Flags & 0x7FFF) == (int)RoadToPro.MatchEngine.Actions.HeaderOutcome.Missed;

        /// <summary>놓친 경합 점프 액션의 타임라인 번호 (이벤트 번호와 겹치지 않게 음수).</summary>
        public static long JumpSeq(long headerSeq) => MissedJumpMotion.SeqOf(headerSeq);

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
            classify ??= ev => IsHeader(ev) ? Kind.Header : IsSlide(ev) ? (ev.Outcome == ActionOutcome.Win ? Kind.SlideWin : Kind.Slide) : IsMissedAerial(ev) ? Kind.MissedJump : (Kind?)null;
            var rows = new List<Row>();
            var actors = new List<int>();
            foreach (var ev in data.Events)
            {
                var k = classify(ev);
                if (k == Kind.MissedJump)
                {
                    AddMissed(rows, actors, data, ev, ev.Entity, scales, seed, scene);
                    if (ev.Other >= 0 && ev.Other < HighlightData.Players) AddMissed(rows, actors, data, ev, ev.Other, scales, seed, scene);
                    continue;
                }
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
                if (r.Kind == Kind.MissedJump) { MeasureMissed(r, kit, meter); continue; }
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
                if (r.Kind == Kind.SlideWin || r.Kind == Kind.Slide)
                {
                    r.MaxSpeedDelta = ApproachSpeedDelta(kit.Plan, r.Entity, r.Time);
                    var ev = FindEvent(data, r.Seq);
                    var sp = data.Pos(r.Entity, r.Time);
                    r.SimReach = new RoadToPro.Core.Math.Vec3(sp.X - ev.BallPos.X, 0, sp.Z - ev.BallPos.Z).Length();
                    data.FrameAt(r.Time, out var f, out var fa);
                    var fb = data.Ball[f] + (data.Ball[Math.Min(f + 1, data.Frames - 1)] - data.Ball[f]) * fa;
                    r.FrameBallGap = (fb - ev.BallPos).Length();
                    var ps = default(PoseSample);
                    kit.Plan.Evaluate(r.Entity, r.Time, ref ps);
                    r.RootOffset = ps.RootOffset.Length();
                    var tl2 = kit.Plan.Timeline(r.Entity);
                    int near = 0;
                    for (int i = 0; i < tl2.Count; i++)
                    {
                        var a2 = tl2.Get(i);
                        if (tl2.SeqOf(i) == r.Seq) { r.PlanOffset = (a2.RootOffset - ps.RootOffset).Length(); r.Stride = a2.StrideScale; }
                        else if (a2.Start - 1.2f < r.Time && a2.End + 0.3f > r.Time) near++;
                        if (a2.Start - 1.2f < r.Time && a2.End + 0.3f > r.Time)
                            r.Neighbors += $"{kit.Plan.Library.Get(a2.Clip).Id}@{a2.Start - r.Time:0.00}/{a2.ContactAt - r.Time:0.00}/{a2.End - r.Time:0.00} ";
                    }
                    r.ActionsNear = near;
                    var cl = kit.Plan.Library.Get(kit.Plan.Library.IndexOf("tackle_slide_r"));
                    kit.System.ApplyAt(r.Time);
                    var foot = MotionMeter.ContactPoint(kit.Rigs[r.Entity], cl.Rule);
                    float sy = (float)Math.Sin(ps.Yaw), cy = (float)Math.Cos(ps.Yaw);
                    var cp = cl.ContactPoint * r.Scale;
                    var want = new RoadToPro.Core.Math.Vec3(ps.Root.X + cp.X * cy + cp.Z * sy, cp.Y, ps.Root.Z - cp.X * sy + cp.Z * cy);
                    r.PoseGap = (foot - want).Length();
                    r.YawDiffDeg = (ps.Yaw - data.YawAt(r.Entity, r.Time)) * 57.2958f;
                }
            }
            return rows;
        }

        static AnimEvent FindEvent(HighlightData d, long seq)
        {
            foreach (var e in d.Events) if (e.Seq == seq) return e;
            return default;
        }

        static void AddMissed(List<Row> rows, List<int> actors, HighlightData data, in AnimEvent ev, int e, float[] scales, ulong seed, int scene)
        {
            float apex = ApexOf(data, ev.Seq, e);
            rows.Add(new Row
            {
                Seed = seed, Scene = scene, Seq = ev.Seq, Kind = Kind.MissedJump, SimKind = ev.Kind, Entity = e, Time = float.IsNaN(apex) ? ev.Time : apex,
                ApexTime = apex, Outcome = ev.Outcome, BallHeight = ev.BallPos.Y, Scale = scales != null ? scales[e] : 1f, Missed = true,
                Reason = float.IsNaN(apex) ? null : BoundaryReason(data, apex),
            });
            if (!actors.Contains(e)) actors.Add(e);
        }

        /// <summary>경합 기록의 정점 재생 시각 (083 FR-01 클립 변환이 옮긴 기록). 없으면 NaN.</summary>
        static float ApexOf(HighlightData data, long headerSeq, int e)
        {
            foreach (var j in data.AerialJumps) if (j.Seq == headerSeq && j.Entity == e && j.Skip == null) return j.ApexAt;
            return float.NaN;
        }

        /// <summary>놓친 경합 점프: 점프 액션이 있으면 접촉(정점) 시각 오차와 그 순간 머리–화면 공 거리. 닿으면 안 된다 (054 FR-43).</summary>
        static void MeasureMissed(Row r, AnimRigKit kit, MotionMeter meter)
        {
            var tl = kit.Plan.Timeline(r.Entity);
            for (int i = 0; i < tl.Count; i++)
            {
                if (tl.SeqOf(i) != JumpSeq(r.Seq)) continue;
                var a = tl.Get(i);
                var c = kit.Plan.Library.Get(a.Clip);
                r.Clip = c.Id;
                r.Lift = a.Lift;
                r.ApexError = float.IsNaN(r.ApexTime) ? float.NaN : Math.Abs(a.ContactAt - r.ApexTime);
                kit.System.ApplyAt(a.ContactAt);
                var cp = MotionMeter.ContactPoint(kit.Rigs[r.Entity], c.Rule);
                r.DistanceAtContact = (cp - kit.Plan.BallVisual(a.ContactAt)).Length();
                r.DistanceLimit = meter.HeadLimit;
                break;
            }
            if (r.Clip == null && r.Reason == null) r.Reason = SkipReason(kit.Plan, r) ?? "점프 없음";
            r.Excluded = r.Reason != null && r.Reason != "점프 없음";
        }

        /// <summary>점프를 넣지 않은 계획 사유(FR-02 겹침·GK 박스·기록 없음·장면 경계).</summary>
        static string SkipReason(AnimPlan plan, Row r) => plan.MissedJumpSkipReason(r.Seq, r.Entity);

        /// <summary>접근 구간 화면 루트 수평 속도의 프레임당 최대 변화 (054 FR-03 성격, 083 버그 방지: 슬라이딩 루트 보정 확대).</summary>
        static float ApproachSpeedDelta(AnimPlan plan, int e, float contact)
        {
            const float dt = 1f / 60f;
            var s = default(PoseSample);
            float t0 = Math.Max(plan.Data.StartTime, contact - ActionClipSelector.ApproachWindow), t1 = Math.Min(plan.Data.EndTime, contact + 0.2f);
            float max = 0f;
            RoadToPro.Core.Math.Vec3 p0 = default, v0 = default;
            int n = 0;
            for (float t = t0; t <= t1; t += dt, n++)
            {
                plan.Evaluate(e, t, ref s);
                var p = new RoadToPro.Core.Math.Vec3(s.Root.X, 0, s.Root.Z);
                if (n >= 1)
                {
                    var v = (p - p0) * (1f / dt);
                    if (n >= 2) max = Math.Max(max, (v - v0).Length());
                    v0 = v;
                }
                p0 = p;
            }
            return max;
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
                  .Append($"\"apexTime\": {F(r.ApexTime)}, \"apexError\": {F(r.ApexError)}, \"maxSpeedDelta\": {F(r.MaxSpeedDelta)}, \"simReach\": {F(r.SimReach)}, \"frameBallGap\": {F(r.FrameBallGap)}, \"rootOffset\": {F(r.RootOffset)}, \"planVsEval\": {F(r.PlanOffset)}, \"stride\": {F(r.Stride)}, \"actionsNear\": {F(r.ActionsNear)}, \"neighbors\": \"{r.Neighbors}\", \"poseGap\": {F(r.PoseGap)}, \"yawDiffDeg\": {F(r.YawDiffDeg)}, ")
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
                              $"apexErr {r.ApexError * 1000:0} ms dv {r.MaxSpeedDelta:0.00} reach {r.SimReach:0.00} gap {r.FrameBallGap:0.00} root {r.RootOffset:0.00} planVsEval {r.PlanOffset * 100:0.0} cm stride {r.Stride:0.00} near {r.ActionsNear} d {r.Distance * 100:0.0} dC {r.DistanceAtContact * 100:0.0} dRaw {r.DistanceRaw * 100:0.0} cm dt {r.TimeError * 1000:0} ms miss {r.Missed} {r.Reason}");
            return sb.ToString();
        }
    }
}
