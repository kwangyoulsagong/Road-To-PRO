using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Logic.Motion;
using RoadToPro.Presentation.Players.Motion;
using RoadToPro.Verification.Samples.Motion;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace RoadToPro.Verification.Samples
{
    /// <summary>
    /// specs/054 E2E 화면과 측정. 합성 장면 8종·경계 장면을 실제 리그로 재생해 지표를 잰다 (FR-70·71).
    /// 화면: motion.장면.키(1.5x 프레임 고정 캡처, AC-14), motionSpeed.배속(같은 시뮬 시각, AC-15).
    /// 튜닝·기준값은 레포 파일을 읽는다 — 시나리오 파일(Data/e2e/*.json)에서 레포 위치를 찾는다.
    /// </summary>
    public sealed partial class PlayerScreen : IFarmMotionMeasure
    {
        MotionSceneBuilder _motionScenes;
        MotionStandards _motionStd;

        static string RepoRoot
        {
            get
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(FarmContext.Args.ScenarioPath));   // Data/e2e
                return Path.GetFullPath(Path.Combine(dir, "..", ".."));
            }
        }

        void LoadMotion()
        {
            if (_motionScenes != null) return;
            var root = RepoRoot;
            string T(string f) => Path.Combine(root, "Data", "tuning", f);
            var kin = KinematicsParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(T("kinematics.json"))), PitchSpec.Default);
            var ball = BallParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(T("ball.json"))), SurfaceCondition.Dry, PitchSpec.Default);
            _motionScenes = new MotionSceneBuilder(kin, ball, _lib, PitchSpec.Default);
            _motionStd = MotionStandards.Parse(File.ReadAllText(Path.Combine(root, "Assets", "_Project", "Presentation", "Players", "Data", "054-motion-standards.json")));
        }

        void RegisterMotionScreens()
        {
            FarmContext.MotionMeasure = this;
            foreach (var id in MotionSceneBuilder.EvidenceIds)
                foreach (var key in MotionKeys(id))
                {
                    string sid = id, skey = key;
                    FarmContext.Screens[$"motion.{sid}.{skey}"] = () => ShowMotion(sid, skey, 1.5f);
                }
            // AC-19: 1.5x 연속 프레임 (시뮬 0.15 s 간격 8장)
            foreach (var id in FilmScenes)
                for (int i = 0; i < FilmFrames; i++)
                {
                    string sid = id;
                    int si = i;
                    FarmContext.Screens[$"motionFilm.{sid}.{si}"] = () => ShowFilm(sid, si);
                }
            foreach (var r in new[] { 0.5f, 1f, 1.5f, 3f })
            {
                float rate = r;
                FarmContext.Screens["motionSpeed." + rate.ToString("0.0", CultureInfo.InvariantCulture)] = () => ShowMotion("longPass", "contact", rate);
            }
        }

        /// <summary>장면별 캡처 키 — MotionSceneBuilder의 Key 호출과 같다 (Data/e2e/054-motion-set.json).</summary>
        static IEnumerable<string> MotionKeys(string id)
        {
            switch (id)
            {
                case "straight50": return new[] { "start", "accel", "top" };
                case "stopCut": return new[] { "decel", "plant", "exit" };
                case "turnBackSide": return new[] { "turn", "backpedal", "sidestep" };
                case "longPass": return new[] { "plant", "contact", "after" };
                case "volley": return new[] { "plant", "contact", "after" };
                case "tackle": return new[] { "tackleWin", "tackleWinAfter", "tackleFoul", "fall" };
                case "header": return new[] { "jump", "contact", "after" };
                case "keeper": return new[] { "save", "saveAfter", "beaten" };
                default: return Array.Empty<string>();
            }
        }

        static readonly string[] FilmScenes = { "stopCut", "longPass" };
        const int FilmFrames = 8;
        const float FilmStep = 0.15f;

        void ShowFilm(string id, int i)
        {
            LoadMotion();
            ClearSticks();
            if (_bodiesApplied) RestoreLooks();
            _contactView = false;
            _view = $"motionFilm.{id}.{i}";
            var m = _motionScenes.Build(id);
            HideBystanders(m);
            float anchor = m.Keys[1].Value;     // 감속·컷 / 접촉 근처
            long frame = (long)Math.Round((anchor - m.Data.StartTime + (i - FilmFrames / 2) * FilmStep) * 60.0 / 1.5);
            _clock.Start = m.Data.StartTime;
            _clock.Fps = 60f;
            _clock.Rate = 1.5f;
            _clock.Frame = frame;
            float t = _clock.SimTime;
            PrepareMotion(m.Data, t);
            FrameMotionCamera(m, t, 6f);
        }

        float[] RigScales()
        {
            var s = new float[HighlightData.Entities];
            for (int e = 0; e < s.Length; e++) s[e] = _rigs[e] != null ? _rigs[e].transform.localScale.y : 1f;
            return s;
        }

        /// <summary>움직이는 선수만 남긴다 (측면선 밖 대기 선수는 화면·측정 대상이 아니다).</summary>
        static void HideBystanders(MotionScene m)
        {
            const int E = HighlightData.Entities;
            var on = new bool[E];
            foreach (var a in m.Actors) on[a] = true;
            for (int f = 0; f < m.Data.Frames; f++)
                for (int e = 0; e < E; e++)
                    if (!on[e]) m.Data.Present[f * E + e] = false;
        }

        void FrameMotionCamera(MotionScene m, float t, float dist, string key = null, int entity = -1)
        {
            int e = entity >= 0 ? entity : key != null && m.Focus.TryGetValue(key, out var fe) ? fe : m.Actors[0];
            var p = m.Data.Pos(e, t);
            float yaw = m.Data.YawAt(e, t);
            var center = new Vector3(p.X, 0.9f, p.Z);
            var side = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            cam.transform.position = center + side * dist + Vector3.up * 0.5f;
            cam.transform.rotation = Quaternion.LookRotation(center - cam.transform.position, Vector3.up);
        }

        void ShowMotion(string id, string key, float rate)
        {
            LoadMotion();
            ClearSticks();
            if (_bodiesApplied) RestoreLooks();
            _contactView = false;
            _view = $"motion.{id}.{key}";
            var m = _motionScenes.Build(id);
            HideBystanders(m);
            float t = m.Data.StartTime;
            foreach (var kv in m.Keys)
                if (kv.Key == key) t = kv.Value;
            long frame = (long)Math.Round((t - m.Data.StartTime) * 60.0 / rate);
            _clock.Start = m.Data.StartTime;
            _clock.Fps = 60f;
            _clock.Rate = rate;
            _clock.Frame = frame;
            float exact = _clock.SimTime;
            PrepareMotion(m.Data, exact);
            FrameMotionCamera(m, exact, 6f, key);
        }

        void PrepareMotion(HighlightData d, float t)
        {
            _perf = false;
            KitCompositor.PlaybackActive = false;
            foreach (var r in stadium) r.enabled = false;
            pitch.enabled = true;
            var plan = AnimPlan.Build(_lib, _map, d, NullLogger.Instance, null, RigScales());
            _sys.Init(plan, _rigs, assets.clips, null);
            foreach (var rig in _rigs)
            {
                rig.Freeze(false);
                rig.gameObject.SetActive(true);
            }
            _sys.ApplyAt(t);
            foreach (var rig in _rigs)
            {
                rig.ForceLod(0);
                if (rig.gameObject.activeSelf) rig.Freeze(true);
            }
            var b = plan.BallVisual(t);
            Ball().position = new Vector3(b.X, b.Y, b.Z);
            Ball().gameObject.SetActive(true);
        }

        /// <summary>장면·배속당 위반 프레임 캡처 상한 (지표별 첫 프레임). 리포트 크기 때문 [제안].</summary>
        const int MaxViolationCaptures = 8;
        const float BinSeconds = 0.1f;

        static readonly RoadToPro.Core.Math.Vec3[] GoalPosts =
        {
            new RoadToPro.Core.Math.Vec3(-52.5f, 0f, -3.66f), new RoadToPro.Core.Math.Vec3(-52.5f, 2.44f, -3.66f),
            new RoadToPro.Core.Math.Vec3(-52.5f, 0f, 3.66f), new RoadToPro.Core.Math.Vec3(-52.5f, 2.44f, 3.66f),
            new RoadToPro.Core.Math.Vec3(52.5f, 0f, -3.66f), new RoadToPro.Core.Math.Vec3(52.5f, 2.44f, -3.66f),
            new RoadToPro.Core.Math.Vec3(52.5f, 0f, 3.66f), new RoadToPro.Core.Math.Vec3(52.5f, 2.44f, 3.66f),
        };

        /// <inheritdoc />
        public IEnumerator MeasureMotion(string set, IReadOnlyList<float> rates, string outDir, List<Dictionary<string, object>> results,
            List<FarmCheckpoint> checkpoints, Action<string> captureAt)
        {
            LoadMotion();
            var ids = new List<string>();
            if (set == "evidence" || set == "all") ids.AddRange(MotionSceneBuilder.EvidenceIds);
            if (set == "edge" || set == "all") ids.AddRange(MotionSceneBuilder.EdgeIds);
            if (ids.Count == 0) ids.Add(set);
            foreach (var id in ids)
                foreach (var rate in rates)
                {
                    var m = _motionScenes.Build(id);
                    HideBystanders(m);
                    PrepareMotion(m.Data, m.Data.StartTime);
                    foreach (var rig in _rigs) rig.Freeze(false);
                    var plan = _sys.Plan;
                    var meter = new MotionMeter(plan, _rigs, m.Actors, _motionStd, id, rate) { Posts = GoalPosts };
                    float interp = InterpP95(m);
                    _sys.Diagnostics = meter;
                    long n = (long)Math.Floor((m.Data.EndTime - m.Data.StartTime) * 60.0 / rate);
                    for (long k = 0; k <= n; k++)
                    {
                        _sys.ApplyAt(m.Data.StartTime + (float)(k * (double)rate / 60.0));
                        if (k % 60 == 59) yield return null;
                    }
                    meter.Finish();
                    _sys.Diagnostics = null;
                    meter.MeasureContacts(_sys.ApplyAt);
                    var report = meter.Report;
                    var violations = report.Violations(_motionStd);
                    // AC-11: 30 Hz 보간 vs 100 Hz 원본
                    if (interp > _motionStd.Max("interpP95")) violations.Add(new MotionViolation("interpP95", 0f, -1, interp, _motionStd.Max("interpP95")));
                    // FR-80: 시뮬 오류는 054 실패가 아니라 시뮬 실패다 — 기대하지 않은 장면이면 시뮬 쪽 실패로 보고한다
                    if (!m.ExpectSimFault)
                        foreach (var f in report.SimFaultEntities) violations.Add(new MotionViolation("simFaultUnexpected", 0f, f, 1f, 0f));
                    var json = ReportJson(report, violations, plan);
                    json["interpP95"] = R(interp);
                    json["expectSimFault"] = m.ExpectSimFault;
                    results.Add(json);
                    checkpoints.Add(new FarmCheckpoint($"{id}@{rate.ToString("0.0", CultureInfo.InvariantCulture)}", n, report.Hash().ToString("x16", CultureInfo.InvariantCulture)));
                    // 위반 프레임 자동 캡처 (FR-71)
                    int caps = 0;
                    var seen = new HashSet<string>();
                    foreach (var v in violations)
                    {
                        // 통계 위반(p95)은 시각이 없어 프레임을 찍을 수 없다 — 표에만 남는다. 공 지표는 엔티티 −1이라 첫 배우 시점
                        if (caps >= MaxViolationCaptures || v.Time < m.Data.StartTime || !seen.Add(v.Metric)) continue;
                        caps++;
                        _sys.ApplyAt(v.Time);
                        FrameMotionCamera(m, v.Time, 5f, null, v.Entity);
                        var b = plan.BallVisual(v.Time);
                        Ball().position = new Vector3(b.X, b.Y, b.Z);
                        yield return new WaitForEndOfFrame();
                        captureAt($"violation_{id}_{rate.ToString("0.0", CultureInfo.InvariantCulture)}_{v.Metric}.png");
                    }
                    yield return null;
                }
        }

        static float InterpP95(MotionScene m)
        {
            const int E = HighlightData.Entities;
            if (m.RawEdited) return 0f;
            var err = new List<float>();
            int steps = m.RawX.Length / E;
            for (int s = 0; s < steps; s++)
            {
                float t = m.Data.StartTime + s * MotionScene.RawDt;
                if (t > m.Data.EndTime) break;
                foreach (var e in m.Actors)
                {
                    var p = m.Data.Pos(e, t);
                    float dx = p.X - m.RawX[s * E + e], dz = p.Z - m.RawZ[s * E + e];
                    err.Add(Mathf.Sqrt(dx * dx + dz * dz));
                }
            }
            return RoadToPro.Presentation.Players.Logic.Motion.MotionStats.Percentile(err, 95f);
        }

        static double R(float v) => Math.Round(v, 4);

        static Dictionary<string, object> ReportJson(MotionReport r, List<MotionViolation> violations, AnimPlan plan)
        {
            var stats = new Dictionary<string, object>();
            var series = new Dictionary<string, object>();
            foreach (var kv in r.Metrics)
            {
                stats[kv.Key] = new Dictionary<string, object> { { "p50", R(r.Stat(kv.Key, 50)) }, { "p95", R(r.Stat(kv.Key, 95)) }, { "max", R(r.Max(kv.Key)) }, { "count", kv.Value.Count } };
                // 시계열: 0.1 s 구간 최대값 (리포트 그래프용, AC-16)
                var bins = new SortedDictionary<int, float>();
                foreach (var s in kv.Value)
                {
                    int b = (int)Math.Floor((s.Time - plan.Data.StartTime) / BinSeconds);
                    bins[b] = bins.TryGetValue(b, out var old) ? Math.Max(old, s.Value) : s.Value;
                }
                var pts = new List<object>();
                foreach (var b in bins) pts.Add(new List<object> { Math.Round(b.Key * BinSeconds, 2), R(b.Value) });
                series[kv.Key] = pts;
            }
            var contacts = new List<object>();
            foreach (var c in r.Contacts)
                contacts.Add(new Dictionary<string, object>
                {
                    { "time", Math.Round(c.Time - plan.Data.StartTime, 3) }, { "entity", c.Entity }, { "kind", c.Kind.ToString() }, { "clip", c.Clip },
                    { "timeError", R(c.TimeError) }, { "distance", R(c.Distance) }, { "limit", R(c.DistanceLimit) }, { "rootError", R(c.RootError) },
                    { "simPart", c.SimPart.ToString() }, { "clipPart", c.ClipPart.ToString() }, { "missed", c.Missed }, { "oneTouch", c.OneTouch },
                    { "isKick", c.IsKick }, { "plantSide", R(c.PlantSide) }, { "plantBack", R(c.PlantBack) },
                });
            var vio = new List<object>();
            foreach (var v in violations)
                vio.Add(new Dictionary<string, object> { { "metric", v.Metric }, { "time", Math.Round(v.Time - plan.Data.StartTime, 3) }, { "entity", v.Entity }, { "value", R(v.Value) }, { "limit", R(v.Limit) } });
            var faults = new List<object>();
            foreach (var e in r.SimFaultEntities) faults.Add(e);
            return new Dictionary<string, object>
            {
                { "scene", r.Scene }, { "rate", r.Rate }, { "stats", stats }, { "series", series }, { "contacts", contacts }, { "violations", vio },
                { "simFaults", faults }, { "clips", new List<object>(r.ClipSequence) }, { "hash", r.Hash().ToString("x16", CultureInfo.InvariantCulture) },
            };
        }
    }
}
