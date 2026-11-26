using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Stadium;
using RoadToPro.Presentation.Stadium.Net;
using UnityEngine;
using Vec3 = RoadToPro.Core.Math.Vec3;

namespace RoadToPro.Verification.Samples
{
    /// <summary>
    /// specs/059 E2E (`e2e/059-net-closeup`·`059-net-ripple`) 검증 화면 — 경기장(044) 씬의 왼쪽 골(X = −52.5).
    /// ① 정지 그물: 시점 6개(메인 FOV 30·15·8, 골대 뒤 하이앵글, 피치 레벨 3·6 m) × 그물 켬/끔/균일 판(평균 덮임만) × 낮·나이트,
    ///    느린 팬 연속 프레임(켬·판), 거리 단계 전환 앞뒤 FOV, 정지 프레임 두 장.
    /// ② 출렁임: 합성 골 6종(실제 공 물리 059 그물 면으로 궤적 생성 → 접촉 검출) × 라이브(메인 FOV 15)·하이앵글, 첫 접촉 + 0.1·0.3·0.6·1.5·3.0 s.
    /// ③ 성능: 합성 골을 1.5배속으로 이어 재생(깨어 있음)·공이 먼 정지(잠듦) — 그물 갱신 CPU·GC를 PerfCounters로.
    /// 캡처 영역: 그물 상자 화면 사각형(net), 칸 하나의 화면 크기(cellPx). 게임 화면이 아니다.
    /// </summary>
    public sealed class GoalNetScreen : MonoBehaviour
    {
        const int Side = -1;
        const float HalfL = 52.5f;
        static readonly string[] Cases = { "topCorner", "bottomCorner", "straight", "sideInside", "crossbarIn", "weakGround" };
        static readonly int[] OffsetsMs = { 100, 300, 600, 1500, 4000 };   // 4.0 s = FR-23 개정(2026-11-26)

        [SerializeField] Camera cam;
        [SerializeField] StadiumLighting lighting;
        [SerializeField] StadiumLightingPreset day;
        [SerializeField] StadiumLightingPreset night;
        [SerializeField] Transform ball;

        struct View { public Vector3 Pos, Target; public float Fov; }

        static readonly Dictionary<string, View> Views = new Dictionary<string, View>
        {
            { "main30", new View { Pos = new Vector3(0f, 25.9f, -80f), Target = new Vector3(-52f, 1.2f, 0f), Fov = 30f } },
            { "main15", new View { Pos = new Vector3(0f, 25.9f, -80f), Target = new Vector3(-52f, 1.2f, 0f), Fov = 15f } },
            { "main8", new View { Pos = new Vector3(0f, 25.9f, -80f), Target = new Vector3(-52.8f, 1.2f, 0f), Fov = 8f } },
            { "high", new View { Pos = new Vector3(-66f, 9f, -3f), Target = new Vector3(-53.4f, 1.0f, 0f), Fov = 30f } },
            { "pitch3", new View { Pos = new Vector3(-50.5f, 1.3f, -1.2f), Target = new Vector3(-54.0f, 1.2f, 0.3f), Fov = 30f } },
            { "pitch6", new View { Pos = new Vector3(-47.5f, 1.5f, -2.0f), Target = new Vector3(-54.0f, 1.1f, 0.3f), Fov = 30f } },
            // 육안 판단용: 골 옆에서 골라인과 나란히 — 뒤 그물 부풂이 윤곽으로 보인다
            { "side", new View { Pos = new Vector3(-53.8f, 1.6f, -10.5f), Target = new Vector3(-53.8f, 1.1f, 0f), Fov = 30f } },
        };

        sealed class Synthetic
        {
            public string Name;
            public HighlightData Data;
            public readonly List<NetContact> Contacts = new List<NetContact>();
            public NetBake[] Bakes;
        }

        GoalNetView _view;
        NetRippleTuning _tuning;
        GoalBox _box;
        readonly List<Synthetic> _cases = new List<Synthetic>();
        string _viewName = "main30";
        string _mode = "on";
        int _scene = 1000;
        double _time;
        Synthetic _current;
        bool _perfRipple, _perfIdle;
        double _cpuMs, _gcBytes, _bakeMs;
        float _panDeg;
        MaterialPropertyBlock _plate;

        public void Configure(Camera c, StadiumLighting l, StadiumLightingPreset d, StadiumLightingPreset n, Transform b)
        {
            cam = c; lighting = l; day = d; night = n; ball = b;
        }

        static string DataDir => FarmContext.Args != null && !string.IsNullOrEmpty(FarmContext.Args.ScenarioPath)
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(FarmContext.Args.ScenarioPath)), ".."))
            : Path.GetFullPath("Data");

        static TuningBundle Bundle(string file) => TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(DataDir, "tuning", file)));

        void OnEnable()
        {
            var ballBundle = Bundle("ball.json");
            _tuning = NetRippleTuning.Create(Bundle("goalnet.json").Get, ballBundle.Get, new Presentation.Logging.UnityLogger(Core.Logging.LogLevel.Warning));
            var bp = BallParams.Create(ballBundle, SurfaceCondition.Dry, PitchSpec.Default);
            _box = new GoalBox(PitchSpec.Default, BallParams.GoalWidth, BallParams.GoalHeight, bp.NetDepth, bp.Radius, bp.NetPocket);
            BuildCases(bp);
            // 059 FR-27: 장면을 만들 때 그물 굽기 (로딩) — 합성 골마다, 시간 기록
            var baker = new NetSceneBaker(_tuning, _box);
            _bakeMs = 0d;
            foreach (var c in _cases)
            {
                c.Bakes = baker.Bake(c.Data, c.Contacts);
                _bakeMs = Math.Max(_bakeMs, baker.LastBakeMs);
            }
            _view = gameObject.AddComponent<GoalNetView>();
            if (!_view.Init(_tuning, HalfL, gameObject.scene.GetRootGameObjects(), new Presentation.Logging.UnityLogger(Core.Logging.LogLevel.Warning)))
                UnityEngine.Debug.LogError("[059] GoalNetView 초기화 실패");
            _plate = new MaterialPropertyBlock();
            _plate.SetFloat("_FadePixels", 1e6f);   // 균일 판 = 평균 덮임만 (FR-10 비교 기준·FR-12 팬 기준선)
            if (ball != null) ball.localScale = Vector3.one * (2f * bp.Radius);

            foreach (var v in Views.Keys)
                foreach (var mode in new[] { "on", "off", "plate", "matte" })
                    foreach (var light in new[] { "day", "night" })
                    {
                        string vv = v, mm = mode, ll = light;
                        FarmContext.Screens[$"net.view.{v}.{mode}.{light}"] = () => ShowStatic(vv, mm, ll == "night", 0f);
                    }
            for (int k = 0; k < 6; k++)
                foreach (var mode in new[] { "on", "plate" })
                {
                    int kk = k; string mm = mode;
                    FarmContext.Screens[$"net.pan.{k}.{mode}"] = () => ShowStatic("main15", mm, false, kk * 0.03f);
                }
            // 거리 단계 전환: 칸이 _FadePixels(3 px) 근처가 되는 FOV 앞뒤 (메인 시점에서 칸 ≈ 3 px인 FOV를 찾아 ±2 %)
            FarmContext.Screens["net.lod.a"] = () => ShowLod(0.98f);
            FarmContext.Screens["net.lod.b"] = () => ShowLod(1.02f);
            foreach (var c in _cases)
                foreach (var camName in new[] { "live", "high" })
                    foreach (var ms in OffsetsMs)
                    {
                        var cc = c; string cn = camName; int m = ms;
                        FarmContext.Screens[$"net.ripple.{c.Name}.{camName}.{ms}"] = () => ShowRipple(cc, cn, m / 1000d);
                    }
            // 육안 판단용 연속 프레임 (리포트 GIF): 접촉 −0.1 ~ +1.2 s, 50 ms 간격 (= 0.5x 리플레이 100 ms마다)
            foreach (var c in _cases)
                foreach (var camName in new[] { "live", "high", "side" })
                    for (int ms = -100; ms <= 1200; ms += 50)
                    {
                        var cc = c; string cn = camName; int m = ms;
                        FarmContext.Screens[$"net.film.{c.Name}.{camName}.{(ms < 0 ? "m" + (-ms) : ms.ToString())}"] = () => ShowRipple(cc, cn, m / 1000d);
                    }
            FarmContext.Screens["net.perf.ripple"] = () => { ShowStatic("main15", "on", false, 0f); _perfRipple = true; _perfIdle = false; _time = 0d; };
            FarmContext.Screens["net.perf.idle"] = () => { ShowStatic("main15", "on", false, 0f); _perfRipple = false; _perfIdle = true; };
            FarmContext.PerfCounters["netCpuMs"] = () => _perfRipple || _perfIdle ? _cpuMs : (double?)null;
            FarmContext.PerfCounters["netGcBytes"] = () => _perfRipple || _perfIdle ? _gcBytes : (double?)null;
            FarmContext.PerfCounters["netBakeMs"] = () => _perfRipple || _perfIdle ? _bakeMs : (double?)null;
            FarmContext.PerfCounters["netAwake"] = () => _perfRipple || _perfIdle ? (_view.Driver != null && _view.Driver.AnyAwake ? 1d : 0d) : (double?)null;
        }

        void OnDisable()
        {
            foreach (var k in new List<string>(FarmContext.Screens.Keys))
                if (k.StartsWith("net.", StringComparison.Ordinal)) FarmContext.Screens.Remove(k);
            FarmContext.PerfCounters.Remove("netCpuMs");
            FarmContext.PerfCounters.Remove("netGcBytes");
            FarmContext.PerfCounters.Remove("netAwake");
            FarmContext.PerfCounters.Remove("netBakeMs");
        }

        // ---- 합성 골 (실제 공 물리 → 30 Hz 기록 → 접촉 검출) ----

        void BuildCases(BallParams p)
        {
            float cy = p.GoalCenterY;
            Add(p, "topCorner", new Vec3(11f, cy + 2.5f, 0.3f), new Vec3(-2f, cy + 3.2f, 2.1f), 26f);
            Add(p, "bottomCorner", new Vec3(10f, cy - 2.0f, 0.2f), new Vec3(-2f, cy - 3.2f, 0.25f), 22f);
            Add(p, "straight", new Vec3(12f, cy, 0.3f), new Vec3(-2f, cy + 0.3f, 1.2f), 30f);
            Add(p, "sideInside", new Vec3(5f, cy - 6f, 0.3f), new Vec3(-1.0f, cy + 3.8f, 0.7f), 20f);
            Add(p, "crossbarIn", new Vec3(11f, cy + 0.5f, 0.3f), new Vec3(0f, cy + 0.6f, 2.36f), 24f);
            Add(p, "weakGround", new Vec3(4f, cy + 1f, p.Radius), new Vec3(-2f, cy + 1.6f, p.Radius), 5f);
        }

        void Add(BallParams p, string name, Vec3 from, Vec3 aim, float speed)
        {
            var d = aim - from;
            float len = (float)Math.Sqrt(Vec3.Dot(d, d));
            float t = len / speed;
            var v = d * (speed / len) + new Vec3(0f, 0f, 0.5f * 9.81f * t);   // 중력만큼 위로 (대략 조준)
            var s = from.Z <= p.Radius + 1e-4f
                ? BallState.RollingAt(new Core.Math.Vec2(from.X, from.Y), new Core.Math.Vec2(v.X, v.Y), p.Radius)
                : BallState.InAir(from, v, Vec3.Zero);
            const float Seconds = 6f;
            int frames = (int)(Seconds * HighlightData.FrameRate) + 1;
            var data = new HighlightData(0f, frames);
            int steps = (int)Math.Ceiling(Seconds * 100f) + 2;
            var pos = new Vec3[steps + 1];
            pos[0] = s.Position;
            var ev = new BallEventBuffer();
            for (int i = 1; i <= steps; i++)
            {
                if (s.Phase != BallPhase.Rest && s.Phase != BallPhase.Controlled) { ev.Clear(); BallStepper.Step(ref s, p, ev, out _); }
                pos[i] = s.Position;
            }
            float halfW = p.Pitch.WidthM * 0.5f;
            for (int f = 0; f < frames; f++)
            {
                double u = f * 100.0 / HighlightData.FrameRate;
                int i0 = (int)Math.Floor(u);
                float a = (float)(u - i0);
                var q = pos[i0] + (pos[i0 + 1] - pos[i0]) * a;
                data.Ball[f] = new Vec3(q.X - HalfL, q.Z, q.Y - halfW);
            }
            var c = new Synthetic { Name = name, Data = data };
            NetContactDetector.Find(data, _box, _tuning.ContactSettings, c.Contacts);
            if (c.Contacts.Count == 0) UnityEngine.Debug.LogWarning($"[059] 합성 골 {name}: 그물 접촉 없음");
            _cases.Add(c);
        }

        // ---- 화면 ----

        void Place(string viewName, float panDeg, float fovScale = 1f)
        {
            var v = Views[viewName];
            _viewName = viewName;
            _panDeg = panDeg;
            cam.transform.position = v.Pos;
            cam.transform.rotation = Quaternion.LookRotation(v.Target - v.Pos, Vector3.up) * Quaternion.Euler(0f, panDeg, 0f);
            cam.fieldOfView = v.Fov * fovScale;
        }

        /// <summary>on 켬 · off 끔 · plate 균일 판(평균 덮임만) · matte 그물만 검은 배경(구멍·끊긴 줄 측정 — 배경색에 줄이 묻히지 않게).</summary>
        void SetMode(string mode)
        {
            _mode = mode;
            var r = _view.RendererOf(Side);
            var other = _view.RendererOf(-Side);
            if (r == null) return;
            r.enabled = mode != "off";
            if (other != null) other.enabled = mode != "off";
            r.SetPropertyBlock(mode == "plate" ? _plate : null);
            bool matte = mode == "matte";
            if (matte && _hidden.Count == 0)
            {
                foreach (var root in gameObject.scene.GetRootGameObjects())
                    foreach (var x in root.GetComponentsInChildren<Renderer>())
                        if (x != r && x != other && x.enabled) { x.enabled = false; _hidden.Add(x); }
                _clear = cam.clearFlags;
                _bg = cam.backgroundColor;
            }
            if (matte)
            {
                // 조명 프리셋 적용이 카메라 배경(하늘)을 되돌리므로 matte마다 다시 검게
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
            else if (!matte && _hidden.Count > 0)
            {
                foreach (var x in _hidden) if (x != null) x.enabled = true;
                _hidden.Clear();
                cam.clearFlags = _clear;
                cam.backgroundColor = _bg;
            }
        }

        readonly List<Renderer> _hidden = new List<Renderer>();
        CameraClearFlags _clear;
        Color _bg;

        void ShowStatic(string viewName, string mode, bool isNight, float panDeg)
        {
            _perfRipple = _perfIdle = false;
            Place(viewName, panDeg);
            lighting.Apply(isNight ? night : day);
            SetMode(mode);
            _current = null;
            _view.Apply(++_scene, 0d, null);   // 정지 모양
            if (ball != null) ball.gameObject.SetActive(false);
            FarmContext.CaptureRegions = Regions;
        }

        void ShowLod(float scale)
        {
            ShowStatic("main15", "on", false, 0f);
            // 메인 시점에서 칸 = _FadePixels × 1.5 (페이드 구간 한가운데)가 되는 FOV
            float dist = Vector3.Distance(Views["main15"].Pos, new Vector3(-53.5f, 1.2f, 0f));
            float targetPx = _tuning.FadePixels * 1.5f;
            float tanHalf = _tuning.CellSize * Screen.height / (2f * dist * targetPx);
            cam.fieldOfView = 2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg * scale;
        }

        void ShowRipple(Synthetic c, string camName, double offset)
        {
            _perfRipple = _perfIdle = false;
            Place(camName == "live" ? "main15" : camName == "side" ? "side" : "high", 0f);
            lighting.Apply(day);
            SetMode("on");
            _current = c;
            double t0 = c.Contacts.Count > 0 ? c.Contacts[0].Time : 0.5d;
            _time = t0 + offset;
            int id = ++_scene;
            // 같은 장면으로 0부터 이 시각까지 (출렁임은 상태가 없어 마지막 호출만 의미 있다 — 결정론 확인 겸)
            _view.Apply(id, _time, c.Bakes);
            PlaceBall(c.Data, _time);
            FarmContext.CaptureRegions = Regions;
        }

        void PlaceBall(HighlightData d, double t)
        {
            if (ball == null) return;
            d.FrameAt((float)t, out int f, out float a);
            var p = BallVisualGuard.Apply(d.Ball[f] + (d.Ball[f + 1] - d.Ball[f]) * a, d.Ball[f], d.Ball[f + 1], a, _box);
            ball.gameObject.SetActive(true);
            ball.position = new Vector3(p.X, p.Y, p.Z);
        }

        void Update()
        {
            if (!_perfRipple && !_perfIdle) return;
            long gc0 = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            if (_perfRipple)
            {
                // 1.5배속 재생: 장면 하나 4 s씩 돌아가며
                _time += Time.unscaledDeltaTime * 1.5d;
                int k = (int)(_time / 4d) % _cases.Count;
                var c = _cases[k];
                double local = _time % 4d + (c.Contacts.Count > 0 ? Math.Max(0d, c.Contacts[0].Time - 0.5d) : 0d);
                _view.Apply(2000 + k + 10 * (int)(_time / (4d * _cases.Count)), local, c.Bakes);
                PlaceBall(c.Data, local);
            }
            else
            {
                _time += Time.unscaledDeltaTime;
                _view.Apply(3000, _time, null);   // 굽기 없음 = 정지
            }
            watch.Stop();
            _cpuMs = watch.Elapsed.TotalMilliseconds;
            _gcBytes = GC.GetAllocatedBytesForCurrentThread() - gc0;
        }

        // ---- 캡처 영역 (PNG 픽셀, 왼쪽 위 원점) ----

        /// <summary>월드 상자 8 꼭짓점의 화면 사각형 [x, y, w, h] (PNG 픽셀, 왼쪽 위 원점 — 캡처 렌더 타깃 기준, StadiumViewsScreen과 같은 방식).</summary>
        List<object> Box(Vector3 lo, Vector3 hi, int w, int h)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var x in new[] { lo.x, hi.x })
                foreach (var y in new[] { lo.y, hi.y })
                    foreach (var z in new[] { lo.z, hi.z })
                    {
                        var p = cam.WorldToScreenPoint(new Vector3(x, y, z));
                        if (p.z <= 0f) continue;
                        float px = p.x, py = h - p.y;
                        minX = Mathf.Min(minX, px); maxX = Mathf.Max(maxX, px); minY = Mathf.Min(minY, py); maxY = Mathf.Max(maxY, py);
                    }
            minX = Mathf.Clamp(minX, 0f, w); maxX = Mathf.Clamp(maxX, 0f, w); minY = Mathf.Clamp(minY, 0f, h); maxY = Mathf.Clamp(maxY, 0f, h);
            return new List<object> { Math.Round(minX, 1), Math.Round(minY, 1), Math.Round(maxX - minX, 1), Math.Round(maxY - minY, 1) };
        }

        Dictionary<string, object> Regions(int w, int h)
        {
            var s = _tuning.Shape;
            float x0 = -HalfL - s.Depth - s.BulgeBack, x1 = -HalfL;
            // 뒤 그물 가운데에서 칸 하나의 화면 크기
            var center = new Vector3(-HalfL - s.Depth, 1.2f, 0f);
            float dist = Vector3.Distance(cam.transform.position, center);
            float pxPerM = h / (2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            var r = new Dictionary<string, object>
            {
                { "view", _viewName }, { "mode", _mode },
                // 그물 상자 전체 / 뒤 그물 가운데 아래쪽(한 겹 — 위·옆 그물과 겹치지 않는 곳)
                { "net", Box(new Vector3(x0, 0f, -s.HalfWidth), new Vector3(x1, s.Top, s.HalfWidth), w, h) },
                { "netBack", Box(new Vector3(-HalfL - s.Depth, 0.3f, -2.4f), new Vector3(-HalfL - s.Depth, 1.0f, 2.4f), w, h) },
                { "cellPx", Math.Round(_tuning.CellSize * pxPerM, 3) },
                { "netActive", _view != null && _view.IsActive },
            };
            if (_current != null)
            {
                r["case"] = _current.Name;
                r["contacts"] = _current.Contacts.Count;
                r["time"] = Math.Round(_time, 4);
                if (_view.Driver != null)
                {
                    var rest = _view.Driver.RestRender;
                    var local = _view.Driver.Local[GoalNetDriver.IndexOf(Side)];
                    double maxDisp = 0d, maxFar = 0d;
                    var ballL = ball != null ? GoalLocal.ToLocal(new Vec3(ball.position.x, ball.position.y, ball.position.z), Side, HalfL) : new Vec3(-99f, 0f, 0f);
                    for (int i = 0; i < rest.Length; i++)
                    {
                        var d = local[i] - rest[i];
                        double m = Math.Sqrt(Vec3.Dot(d, d));
                        maxDisp = Math.Max(maxDisp, m);
                        var e = rest[i] - ballL;
                        if (Math.Sqrt(Vec3.Dot(e, e)) > 0.8) maxFar = Math.Max(maxFar, m);   // FR-23: 공이 기대 있는 자리(0.8 m) 밖
                    }
                    r["maxDisp"] = Math.Round(maxDisp, 4);
                    r["maxDispFar"] = Math.Round(maxFar, 4);
                    var b = _current.Bakes != null ? _current.Bakes[GoalNetDriver.IndexOf(Side)] : null;
                    r["stateHash"] = b != null ? b.Hash().ToString("x16") : "none";
                }
            }
            return r;
        }
    }
}
