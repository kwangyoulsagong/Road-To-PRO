using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Motion;
using RoadToPro.Verification.Samples.Motion;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>
    /// specs/011 AC-24 (3D 쪽): 실제 경기(011 선방 모델) GK 접촉 장면을 054 측정기에 넣어 손–공 거리 ≤ 10 cm, 시각 차 ±33 ms,
    /// 결과와 동작 모순 0건을 본다. 장면은 기준 경기의 GK 접촉 이벤트 앞뒤 3 s 클립.
    /// </summary>
    public class GkContactMeterTests
    {
        static List<MotionReport> _reports;

        static List<MotionReport> Reports()
        {
            if (_reports != null) return _reports;
            _reports = new List<MotionReport>();
            var sim = Sim(20261103UL, WithCheckpoints(CheckpointMode.All));
            sim.RunUntilTick(36_000);
            int scenes = 0;
            for (int i = 0; i < sim.Log.Count && scenes < 6; i++)
            {
                var e = sim.Log[i];
                if (e.Kind != SimEventKind.Catch && e.Kind != SimEventKind.Parry && e.Kind != SimEventKind.Punch) continue;
                var clip = sim.ExtractClip(System.Math.Max(0, e.Tick - 30), e.Tick + 30);
                var data = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                var keeper = Squad.TeamOfSlot(sim.Context.Squad.SlotOf(e.PlayerId)) * Squad.SlotsPerTeam;
                var scene = new MotionScene { Id = $"gk-{e.Kind}-{e.Tick}", Data = data, Actors = new[] { keeper } };
                Scenes[scene.Id] = scene;
                _reports.Add(MotionRun.Measure(scene));
                scenes++;
            }
            return _reports;
        }

        /// <summary>시각 차 ±33 ms · 결과와 동작 모순 0 (부위·놓침 일치). 손–공 거리는 아래 보류 테스트.</summary>
        [Test]
        public void GkContacts_Timing33ms_NoContradiction()
        {
            var fails = new System.Text.StringBuilder();
            int measured = Check(fails, distance: false);
            Assert.Greater(measured, 0, "측정된 GK 접촉");
            Assert.IsEmpty(fails.ToString());
        }

        /// <summary>
        /// 011 AC-24 손–공 ≤ 10 cm (080 AC-02로 보류 해제, 2026-11-20): GK 클립 33개(높이 3구간 · 도달 2변형 · 땅볼 · 쳐내기 …)·
        /// 동작 종류 후보 · 선방 화면 방향(공이 오는 쪽) · 손 IK(≤ 30 cm). 080 T01 기준선 134~162 cm.
        /// </summary>
        [Test]
        public void GkContacts_HandBallWithin10cm()
        {
            var fails = new System.Text.StringBuilder();
            Check(fails, distance: true);
            Assert.IsEmpty(fails.ToString());
        }

        static List<MotionReport> _missed;

        /// <summary>기준 경기 실점 장면(골 앞 3 s) 중 실점 다이빙(DiveStart, 닿지 않음)이 있는 것 — 최대 6개.</summary>
        static List<MotionReport> MissedDives()
        {
            if (_missed != null) return _missed;
            _missed = new List<MotionReport>();
            var sim = Sim(20261103UL, WithCheckpoints(CheckpointMode.All));
            sim.RunUntilTick(36_000);
            for (int i = 0; i < sim.Log.Count && _missed.Count < 6; i++)
            {
                var e = sim.Log[i];
                if (e.Kind != SimEventKind.DiveStart) continue;
                var clip = sim.ExtractClip(System.Math.Max(0, e.Tick - 20), e.Tick + 40);
                var data = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                int keeper = -1;
                foreach (var ev in data.Events)
                    if (ev.Kind == SimEventKind.DiveStart && ev.Seq == e.Seq) keeper = ev.Entity;
                if (keeper < 0) continue; // 접촉 이벤트가 동작을 정한 다이빙 (변환기가 뺌)
                _missed.Add(MotionRun.Measure(new MotionScene { Id = $"gk-miss-{e.Tick}", Data = data, Actors = new[] { keeper } }));
            }
            return _missed;
        }

        /// <summary>080 AC-03: 실점 다이빙은 예정 접촉 시각 손–공 ≥ 15 cm (손끝 플래그면 ≤ 10 cm — 단위는 KeeperMotionTests).</summary>
        [Test]
        public void MissedDive_HandClearOfBall15cm()
        {
            int n = 0;
            var fails = new System.Text.StringBuilder();
            foreach (var r in MissedDives())
                foreach (var c in r.Contacts)
                {
                    if (c.Kind != SimEventKind.DiveStart) continue;
                    n++;
                    TestContext.WriteLine($"{r.Scene} {c.Clip} 손–공 {c.Distance * 100:0.0} cm");
                    if (c.Distance < KeeperMotion.MissClear - 0.005f) fails.AppendLine($"{r.Scene} {c.Clip} 손–공 {c.Distance * 100:0.0} cm");
                }
            Assert.Greater(n, 0, "실점 다이빙 장면");
            Assert.IsEmpty(fails.ToString());
        }

        /// <summary>
        /// 080 AC-06: GK 접촉 장면을 모든 배속(1·1.5·2·3x)에서 재생해 GK 클립 구간(동작 시작 − 0.3 s ~ 끝 + 0.3 s, 전환 포함)의
        /// 발 미끄러짐(054 FR-10: 표본 최대 slideMax · p95 slideP95)·순간 회전(FR-25: yawDelta, 장면 전체) 위반 0.
        /// 구간 밖 달리기 미끄러짐은 054·055 범위 (T13a: 장면 전체 p95는 표본 16개 안팎이라 다이빙 뒤 달리기 2표본으로 넘나든다).
        /// </summary>
        [Test]
        public void KeeperClips_NoSlideNoSnap_AllRates()
        {
            var std = MotionStandardsTests.Load();
            var fails = new System.Text.StringBuilder();
            foreach (var r0 in Reports())
            {
                var sc = SceneOf(r0.Scene);
                var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, sc.Data, RoadToPro.Core.Logging.NullLogger.Instance);
                var tl = plan.Timeline(sc.Actors[0]);
                bool InKeeperClip(float t)
                {
                    for (int k = 0; k < tl.Count; k++)
                    {
                        var a = tl.Get(k);
                        if (AnimTestKit.Lib.Get(a.Clip).Id.StartsWith("gk_", System.StringComparison.Ordinal) && t >= a.Start - 0.3f && t <= a.End + 0.3f) return true;
                    }
                    return false;
                }
                foreach (var rate in new[] { 1f, 1.5f, 2f, 3f })
                {
                    var r = MotionRun.Measure(sc, rate);
                    foreach (var v in r.Violations(std))
                        if (v.Metric == "yawDelta") fails.AppendLine($"{sc.Id} x{rate} yawDelta {v.Value:0.000} > {v.Limit:0.000} t {v.Time:0.00}");
                    if (!r.Metrics.TryGetValue("slide", out var l)) continue;
                    var vals = new List<float>();
                    foreach (var x in l) if (InKeeperClip(x.Time)) vals.Add(x.Value);
                    if (vals.Count == 0) continue;
                    vals.Sort();
                    float max = vals[vals.Count - 1], p95 = vals[System.Math.Min(vals.Count - 1, (int)System.Math.Ceiling(0.95 * vals.Count) - 1)];
                    TestContext.WriteLine($"{sc.Id} x{rate} GK 구간 미끄러짐 n {vals.Count} max {max:0.000} p95 {p95:0.000}");
                    if (max > std.Max("slideMax")) fails.AppendLine($"{sc.Id} x{rate} slide max {max:0.000} > {std.Max("slideMax"):0.000}");
                    if (p95 > std.Max("slideP95")) fails.AppendLine($"{sc.Id} x{rate} slide p95 {p95:0.000} > {std.Max("slideP95"):0.000}");
                }
            }
            Assert.IsEmpty(fails.ToString());
        }

        static List<(string kind, MotionReport report)> _live;

        /// <summary>
        /// 080 T13: 010 실경기(시드 7400~7403, 4-3-3 vs 4-4-2) GK 장면 — 073 스마더, GK 손 받기(크로스 클레임·느슨한 공), 074 굴리기·던지기.
        /// 종류마다 최대 4장면, 이벤트 앞뒤 1 s(30 틱) 클립. 경기 4개는 병렬로 한 번만.
        /// </summary>
        static List<(string, MotionReport)> LiveKeeperScenes()
        {
            if (_live != null) return _live;
            var sims = new MatchSim[4];
            System.Threading.Tasks.Parallel.For(0, sims.Length, m =>
            {
                var s = ShapeTestKit.Sim(WithCheckpoints(CheckpointMode.All), ShapeTestKit.Setup(seed: 7400UL + (ulong)m));
                s.RunToEnd();
                sims[m] = s;
            });
            var count = new Dictionary<string, int>();
            var o = new List<(string, MotionReport)>();
            foreach (var sim in sims)
                for (int i = 0; i < sim.Log.Count; i++)
                {
                    var e = sim.Log[i];
                    if (e.PlayerId < 0) continue;
                    int slot = sim.Context.Squad.SlotOf(e.PlayerId);
                    if (slot < 0 || !Squad.IsKeeperSlot(slot)) continue;
                    string kind = e.Kind == SimEventKind.Smother ? "smother"
                        : e.Part == BodyPart.Hand && (e.Kind == SimEventKind.BallRecovered || e.Kind == SimEventKind.Interception || e.Kind == SimEventKind.PassReceived) ? "claim"
                        : e.Kind == SimEventKind.Distribution && e.A == (int)RoadToPro.MatchEngine.Goalkeeper.DistributionKind.Roll ? "roll"
                        : e.Kind == SimEventKind.Distribution && e.A == (int)RoadToPro.MatchEngine.Goalkeeper.DistributionKind.Throw ? "throw" : null;
                    if (kind == null) continue;
                    count.TryGetValue(kind, out var n);
                    if (n >= 4) continue;
                    count[kind] = n + 1;
                    var clip = sim.ExtractClip(System.Math.Max(0, e.Tick - 30), e.Tick + 30);
                    var data = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                    var keeper = Squad.TeamOfSlot(slot) * Squad.SlotsPerTeam;
                    var ms = new MotionScene { Id = $"gk-{kind}-{e.Tick}", Data = data, Actors = new[] { keeper } };
                    LiveScenes[ms.Id] = ms;
                    o.Add((kind, MotionRun.Measure(ms)));
                }
            return _live = o;
        }

        /// <summary>080 AC-04: 스마더·클레임·굴리기 손–공 ≤ 10 cm, 시각 차 ±33 ms. 던지기는 클립·시각 차만, 손–공 기록(FR-06, D31).</summary>
        [Test]
        public void SmotherClaimDistribution_HandBallWithin10cm()
        {
            var fails = new System.Text.StringBuilder();
            var seen = new Dictionary<string, int>();
            foreach (var (kind, r) in LiveKeeperScenes())
                foreach (var c in r.Contacts)
                {
                    if (c.SimPart != BodyPart.Hand || c.Missed) continue;
                    bool receive = c.Kind == SimEventKind.BallRecovered || c.Kind == SimEventKind.Interception || c.Kind == SimEventKind.PassReceived;
                    bool target = kind == "smother" ? c.Kind == SimEventKind.Smother || (receive && c.Clip.StartsWith("gk_smother_", System.StringComparison.Ordinal)) || (receive && c.Clip == "gk_lunge_low")
                        : kind == "claim" ? receive
                        : c.Kind == SimEventKind.Pass;
                    TestContext.WriteLine($"{(target ? "" : "  (참고) ")}{kind} {r.Scene} {c.Kind} {c.Clip} 손–공 {c.Distance * 100:0.0} cm 시각 {c.TimeError * 1000:0} ms");
                    if (!target) continue;
                    seen.TryGetValue(kind, out var n);
                    seen[kind] = n + 1;
                    if (!c.Clip.StartsWith("gk_", System.StringComparison.Ordinal)) fails.AppendLine($"{r.Scene} GK 클립 아님 {c.Clip}");
                    if (c.TimeError > 0.0335f) fails.AppendLine($"{r.Scene} {c.Clip} 시각 차 {c.TimeError * 1000:0} ms");
                    if (kind != "throw" && c.Distance > 0.10f) fails.AppendLine($"{r.Scene} {c.Clip} 손–공 {c.Distance * 100:0.0} cm");
                }
            foreach (var k in new[] { "smother", "claim", "roll" })
                Assert.IsTrue(seen.ContainsKey(k), k + " 장면 없음");
            Assert.IsEmpty(fails.ToString());
        }

        /// <summary>080 T13 진단: 실경기 GK 장면의 대상 접촉 세부 (공·화면 방향·클립·루트·손 IK).</summary>
        [Test, Explicit("080 진단")]
        public void Diagnose_LiveKeeperDetail()
        {
            foreach (var (kind, r) in LiveKeeperScenes())
            {
                var sc = LiveScenes[r.Scene];
                var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, sc.Data, RoadToPro.Core.Logging.NullLogger.Instance);
                foreach (var ev in sc.Data.Events)
                {
                    if (ev.Entity != sc.Actors[0] || ev.Part != BodyPart.Hand) continue;
                    var tl = plan.Timeline(ev.Entity);
                    for (int k = 0; k < tl.Count; k++)
                    {
                        if (tl.SeqOf(k) != ev.Seq) continue;
                        var a = tl.Get(k);
                        var c = AnimTestKit.Lib.Get(a.Clip);
                        var p = sc.Data.Pos(ev.Entity, ev.Time);
                        var d = ev.BallPos - p;
                        float fy = plan.ScreenYaw(ev.Entity, a.ContactAt);
                        float fs = (float)System.Math.Sin(fy), fc = (float)System.Math.Cos(fy);
                        plan.HandReachAt(ev.Entity, a.ContactAt, out var reach, out _, out var w);
                        var v = sc.Data.Pos(ev.Entity, ev.Time + 0.05f) - sc.Data.Pos(ev.Entity, ev.Time - 0.05f);
                        TestContext.WriteLine($"{kind} {r.Scene} {ev.Kind} t {ev.Time:0.00} 공 옆 {d.X * fc - d.Z * fs:+0.00;-0.00} 높이 {ev.BallPos.Y:0.00} 앞 {d.X * fs + d.Z * fc:+0.00;-0.00} GK 속도 {v.Length() * 10:0.0} | {c.Id} cp ({c.ContactPoint.X:0.00},{c.ContactPoint.Y:0.00},{c.ContactPoint.Z:0.00}) start {a.Start:0.00} rate {a.Rate:0.00} root ({a.RootOffset.X:0.00},{a.RootOffset.Z:0.00}) snap {a.Snap} reach |{reach.Length():0.00}| w {w:0.00}");
                    }
                }
            }
        }

        /// <summary>080 T13a 진단: GK 장면 발 미끄러짐 표본 상위 (시각·값·그 순간 동작).</summary>
        [Test, Explicit("080 진단")]
        public void Diagnose_KeeperSlide()
        {
            Reports();
            foreach (var kv in Scenes)
            {
                var r = MotionRun.Measure(kv.Value, 1f);
                if (!r.Metrics.TryGetValue("slide", out var l)) continue;
                var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, kv.Value.Data, RoadToPro.Core.Logging.NullLogger.Instance);
                var tl = plan.Timeline(kv.Value.Actors[0]);
                var top = new List<(float t, float v)>();
                foreach (var x in l) if (x.Value > 0.015f) top.Add((x.Time, x.Value));
                TestContext.WriteLine($"{kv.Key} p95 {r.Stat("slide", 95):0.000} n {l.Count} >1.5cm {top.Count}");
                foreach (var (t, v) in top)
                {
                    int k = tl.ActiveAt(t);
                    TestContext.WriteLine($"   t {t:0.00} slide {v:0.000} 동작 {(k >= 0 ? AnimTestKit.Lib.Get(tl.Get(k).Clip).Id : "loco")}");
                }
            }
        }

        static readonly Dictionary<string, MotionScene> LiveScenes = new Dictionary<string, MotionScene>();

        static readonly Dictionary<string, MotionScene> Scenes = new Dictionary<string, MotionScene>();
        static MotionScene SceneOf(string id) => Scenes[id];

        /// <summary>
        /// 080 T01 진단: 실경기(010 경로, 시드 7400~7403 + 011 기준 경기) GK 동작 이벤트의 GK 기준 공 위치(옆·높이·앞, m) 분포.
        /// 클립 접촉점·높이 범위 설계용. 결과는 specs/080 evidence/baseline.md에 옮긴다.
        /// </summary>
        [Test, Explicit("080 진단")]
        public void Diagnose_KeeperContactGeometry()
        {
            var sims = new MatchSim[5];
            System.Threading.Tasks.Parallel.For(0, sims.Length, m =>
            {
                var sim = m == 4 ? Sim(20261103UL, WithCheckpoints(CheckpointMode.All))
                                 : ShapeTestKit.Sim(WithCheckpoints(CheckpointMode.All), ShapeTestKit.Setup(seed: 7400UL + (ulong)m));
                sim.RunToEnd();
                sims[m] = sim;
            });
            var rows = new SortedDictionary<string, List<string>>();
            for (int m = 0; m < sims.Length; m++)
            {
                var sim = sims[m];
                for (int i = 0; i < sim.Log.Count; i++)
                {
                    var e = sim.Log[i];
                    bool gk = e.Kind == SimEventKind.Catch || e.Kind == SimEventKind.Parry || e.Kind == SimEventKind.Punch || e.Kind == SimEventKind.Smother
                              || e.Kind == SimEventKind.ClaimFail || e.Kind == SimEventKind.DiveStart || e.Part == BodyPart.Hand;
                    if (!gk || e.PlayerId < 0) continue;
                    int slot = sim.Context.Squad.SlotOf(e.PlayerId);
                    if (slot < 0 || !Squad.IsKeeperSlot(slot)) continue;
                    var clip = sim.ExtractClip(System.Math.Max(0, e.Tick - 30), e.Tick + 30);
                    var data = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                    foreach (var ev in data.Events)
                    {
                        if (ev.Seq != e.Seq) continue;
                        var info = KeeperMotion.Decode(ev);
                        var p = data.Pos(ev.Entity, ev.Time);
                        float yaw = data.YawAt(ev.Entity, ev.Time);
                        float sy = (float)System.Math.Sin(yaw), cy = (float)System.Math.Cos(yaw);
                        var d = ev.BallPos - p;
                        float side = d.X * cy - d.Z * sy, fwd = d.X * sy + d.Z * cy;
                        string key = $"{ev.Kind}/{(info.Valid ? info.Kind.ToString() : "-")}/{(info.HasBand ? info.Band.ToString() : "-")}/{ev.Outcome}";
                        if (!rows.TryGetValue(key, out var l)) rows[key] = l = new List<string>();
                        l.Add($"side {side:+0.00;-0.00} up {ev.BallPos.Y:0.00} fwd {fwd:+0.00;-0.00} v {ev.BallVelOut.Length():0.0} dir {info.Direction}");
                    }
                }
            }
            foreach (var kv in rows)
            {
                TestContext.WriteLine($"## {kv.Key} ({kv.Value.Count})");
                foreach (var r in kv.Value) TestContext.WriteLine("  " + r);
            }
        }

        /// <summary>080 T12 진단: 기준 경기 GK 접촉마다 공(GK 루트 기준)·고른 클립 접촉점·루트 보정·손 IK·측정 거리.</summary>
        [Test, Explicit("080 진단")]
        public void Diagnose_GkContactsDetail()
        {
            var sim = Sim(20261103UL, WithCheckpoints(CheckpointMode.All));
            sim.RunUntilTick(36_000);
            for (int i = 0; i < sim.Log.Count; i++)
            {
                var e = sim.Log[i];
                if (e.Kind != SimEventKind.Catch && e.Kind != SimEventKind.Parry && e.Kind != SimEventKind.Punch) continue;
                var clip = sim.ExtractClip(System.Math.Max(0, e.Tick - 30), e.Tick + 30);
                var data = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, data, RoadToPro.Core.Logging.NullLogger.Instance);
                foreach (var ev in data.Events)
                {
                    if (ev.Seq != e.Seq) continue;
                    var tl = plan.Timeline(ev.Entity);
                    for (int k = 0; k < tl.Count; k++)
                    {
                        if (tl.SeqOf(k) != ev.Seq) continue;
                        var a = tl.Get(k);
                        var c = AnimTestKit.Lib.Get(a.Clip);
                        var p = data.Pos(ev.Entity, ev.Time);
                        float yaw = data.YawAt(ev.Entity, ev.Time);
                        float sy = (float)System.Math.Sin(yaw), cy = (float)System.Math.Cos(yaw);
                        var d = ev.BallPos - p;
                        plan.HandReachAt(ev.Entity, a.ContactAt, out var reach, out var hands, out var w);
                        var info = KeeperMotion.Decode(ev);
                        var sb = new System.Text.StringBuilder("  yaw°");
                        for (float dt = -1.0f; dt <= 0.61f; dt += 0.2f)
                        {
                            float tt = System.Math.Max(data.StartTime, System.Math.Min(data.EndTime, ev.Time + dt));
                            var v = data.Pos(ev.Entity, System.Math.Min(data.EndTime, tt + 0.05f)) - data.Pos(ev.Entity, tt);
                            sb.Append($" {dt:+0.0;-0.0}:{data.YawAt(ev.Entity, tt) * 57.3f:0}/v{System.Math.Atan2(v.X, v.Z) * 57.3f:0}");
                        }
                        var shot = data.BallAt(System.Math.Max(data.StartTime, ev.Time - 0.4f)) - p;
                        sb.Append($" | 공 0.4 s 전 방향 {System.Math.Atan2(shot.X, shot.Z) * 57.3f:0}");
                        float fy = plan.ScreenYaw(ev.Entity, a.ContactAt);
                        float fs = (float)System.Math.Sin(fy), fc = (float)System.Math.Cos(fy);
                        sb.Append($" | 화면 방향 {fy * 57.3f:0} 기준 공 옆 {d.X * fc - d.Z * fs:+0.00;-0.00} 앞 {d.X * fs + d.Z * fc:+0.00;-0.00}");
                        TestContext.WriteLine(sb.ToString());
                        TestContext.WriteLine($"{ev.Kind} {info.Kind}/{info.Band} t {ev.Time:0.00} ball side {d.X * cy - d.Z * sy:+0.00;-0.00} up {ev.BallPos.Y:0.00} fwd {d.X * sy + d.Z * cy:+0.00;-0.00} | clip {c.Id} cp ({c.ContactPoint.X:0.00},{c.ContactPoint.Y:0.00},{c.ContactPoint.Z:0.00}) rate {a.Rate:0.00} root ({a.RootOffset.X:0.00},{a.RootOffset.Z:0.00}) reach ({reach.X:0.00},{reach.Y:0.00},{reach.Z:0.00}) w {w:0.00} hands {hands} contactAt {a.ContactAt:0.00}");
                    }
                }
            }
            foreach (var r in Reports())
                foreach (var c in r.Contacts)
                    if (c.Kind == SimEventKind.Catch || c.Kind == SimEventKind.Parry || c.Kind == SimEventKind.Punch)
                        TestContext.WriteLine($"meter {r.Scene} {c.Clip} dist {c.Distance * 100:0.0} atContact {c.DistanceAtContact * 100:0.0} raw {c.DistanceRaw * 100:0.0} timeErr {c.TimeError * 1000:0} root {c.RootError * 100:0.0}");
        }

        static int Check(System.Text.StringBuilder fails, bool distance)
        {
            int measured = 0;
            foreach (var r in Reports())
            {
                TestContext.WriteLine(MotionRun.Summary(r));
                foreach (var c in r.Contacts)
                {
                    if (c.Kind != SimEventKind.Catch && c.Kind != SimEventKind.Parry && c.Kind != SimEventKind.Punch) continue;
                    if (c.Missed) continue;
                    measured++;
                    if (c.SimPart != BodyPart.Hand || c.ClipPart != BodyPart.Hand) fails.AppendLine($"{r.Scene} 부위 {c.ClipPart}/{c.SimPart}");
                    if (c.TimeError > 0.0335f) fails.AppendLine($"{r.Scene} {c.Clip} 시각 차 {c.TimeError * 1000:0} ms");
                    if (distance && c.Distance > 0.10f) fails.AppendLine($"{r.Scene} {c.Clip} 손–공 {c.Distance * 100:0.0} cm");
                }
            }
            return measured;
        }
    }
}
