using System;
using System.Collections.Generic;
using System.Diagnostics;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;
using Debug = UnityEngine.Debug;
using ILogger = RoadToPro.Core.Logging.ILogger;
using Vec3 = RoadToPro.Core.Math.Vec3;

namespace RoadToPro.Verification.Samples
{
    /// <summary>
    /// specs/043 E2E 씬 제어 (게임 화면 아님). goto 화면:
    /// - row1Front … row4Back: 25명(선수 22 + 심판 3)을 한 줄 7명 이하 4줄로 나눠 앞·뒤에서 캡처 (AC-13). 영역: 셔츠 앞판 색 그룹, 뒤 번호·이름.
    /// - perfEmpty / perfPlayers / perfFull: 25명이 합성 하이라이트를 1.5x로 뛰는 성능 구간 (AC-16·17). 카운터 animCpuMs·animGcBytes.
    /// </summary>
    public sealed class PlayerScreen : MonoBehaviour
    {
        [Serializable]
        public struct PlayerAssets
        {
            public GameObject prefab;
            public TextAsset clipsJson, mapJson, glyphJson;
            public Texture2D body, fabric, names, numbers;
            public Texture2D[] masks; // textures.PATTERNS 순서
            public Shader bodyShader, kitShader;
            public AnimationClip[] clips; // ClipLibrary 인덱스 순서
        }

        // 엔티티: 0 홈 GK, 1~10 홈 필드, 11 원정 GK, 12~21 원정 필드, 22~24 심판
        static readonly int[][] Rows =
        {
            new[] { 0, 1, 2, 3, 12, 13, 14 },
            new[] { 11, 4, 5, 6, 15, 16, 17 },
            new[] { 7, 8, 9, 18, 19, 20 },
            new[] { 10, 21, 22, 23, 24 },
        };

        // 가상 이름 (실존 인물 아님, 01-legal)
        static readonly string[] HomeNames = { "도하람", "Varnel", "Oskarby", "서윤결", "Telmuro", "Quissan", "Brandvik", "한누리", "Haldeno", "Morrevan", "Sennick" };
        static readonly string[] AwayNames = { "Talvorn", "Ulricco", "Peskara", "노을찬", "Wendrak", "Ivsoleno", "Garrimo", "Zelbach", "윤가람", "Crovanne", "Duskelli" };
        const float Spacing = 0.9f;

        [SerializeField] Camera cam;
        [SerializeField] PlayerAssets assets;
        [SerializeField] Renderer[] stadium;
        [SerializeField] Renderer pitch;
        [SerializeField] RoadToPro.Presentation.Stadium.StadiumLighting lighting;
        [SerializeField] RoadToPro.Presentation.Stadium.StadiumLightingPreset day;
        [SerializeField] RoadToPro.Presentation.Stadium.PitchMaterialBinder binder;

        ClipLibrary _lib;
        AnimationMap _map;
        readonly PlayerRig[] _rigs = new PlayerRig[HighlightData.Entities];
        readonly PlayerAppearance[] _looks = new PlayerAppearance[HighlightData.Entities];
        PlayerAnimationSystem _sys;
        KitAssignment _kits;
        Texture2D _homeAtlas, _awayAtlas;
        readonly FixedPlaybackClock _clock = new FixedPlaybackClock();
        string _view = "";
        bool _perf;
        float _cpuMs;
        long _gcBytes;
        bool _composeErrorLogged;
        readonly ILogger _log = new UnityLog();

        public void Configure(Camera c, PlayerAssets a, Renderer[] stadiumRenderers, Renderer pitchRenderer, RoadToPro.Presentation.Stadium.StadiumLighting l,
            RoadToPro.Presentation.Stadium.StadiumLightingPreset d, RoadToPro.Presentation.Stadium.PitchMaterialBinder b)
        {
            cam = c;
            assets = a;
            stadium = stadiumRenderers;
            pitch = pitchRenderer;
            lighting = l;
            day = d;
            binder = b;
        }

        void OnEnable()
        {
            if (lighting != null && day != null) lighting.Apply(day);
            if (binder != null) binder.Bind();
            Load();
            FarmContext.RegisterCamera(cam);
            for (int r = 0; r < Rows.Length; r++)
            {
                int row = r;
                FarmContext.Screens[$"row{r + 1}Front"] = () => ShowRow(row, false);
                FarmContext.Screens[$"row{r + 1}Back"] = () => ShowRow(row, true);
                FarmContext.Screens[$"row{r + 1}BackKey"] = () => ShowRow(row, true, true);
            }
            FarmContext.Screens["perfEmpty"] = () => ShowPerf(0);
            FarmContext.Screens["perfPlayers"] = () => ShowPerf(1);
            FarmContext.Screens["perfFull"] = () => ShowPerf(2);
            FarmContext.Screens["perfPlayersNoShadow"] = () => ShowPerf(3);
            FarmContext.CaptureRegions = Regions;
            FarmContext.PerfCounters["animCpuMs"] = () => _perf ? _cpuMs : (double?)null;
            FarmContext.PerfCounters["animGcBytes"] = () => _perf ? _gcBytes : (double?)null;
            ShowRow(0, false);
        }

        void OnDisable()
        {
            FarmContext.Cameras.Remove(cam);
            FarmContext.CaptureRegions = null;
            FarmContext.PerfCounters.Remove("animCpuMs");
            FarmContext.PerfCounters.Remove("animGcBytes");
            KitCompositor.PlaybackActive = false;
        }

        // ---------------------------------------------------------------- 준비 (로딩 단계)

        static TeamKits Team(uint home, uint second, uint third, uint gk, uint sec, uint acc) => new TeamKits(
            new KitColors(Rgb.FromHex(home), Rgb.FromHex(sec), Rgb.FromHex(acc)),
            new KitColors(Rgb.FromHex(second), Rgb.FromHex(0x111111), Rgb.FromHex(0xFFFFFF)),
            new KitColors(Rgb.FromHex(third), Rgb.FromHex(0xFFFFFF), Rgb.FromHex(0x111111)),
            new KitColors(Rgb.FromHex(gk), Rgb.FromHex(0x111111), Rgb.FromHex(0xFFFFFF)));

        void Load()
        {
            if (_lib != null) return;
            _lib = ClipLibrary.Parse(assets.clipsJson.text);
            _map = AnimationMap.Parse(assets.mapJson.text, _lib, _log);
            // 가상 팀 색: 홈 빨강, 원정 남색(실존 클럽 키트 아님)
            _kits = KitColorResolver.Resolve(Team(0xC8102E, 0xFFFFFF, 0x222222, 0x00A651, 0xFFFFFF, 0xFFFFFF),
                Team(0x1C3F94, 0xFFD100, 0xFFFFFF, 0xFF6F00, 0xFFFFFF, 0xFFFFFF));
            var comp = new KitCompositor(GlyphAtlasData.Parse(assets.glyphJson.text), assets.names, assets.numbers);
            _homeAtlas = comp.ComposeTeam(Squad(HomeNames), _log);
            float homeMs = comp.LastMs;
            _awayAtlas = comp.ComposeTeam(Squad(AwayNames), _log);
            Debug.Log($"[kit] 팀 아틀라스 합성 홈 {homeMs:0.0} ms, 원정 {comp.LastMs:0.0} ms (예산 50 ms, AC-17)");
            var inputs = new BodyInput[HighlightData.Entities];
            for (int e = 0; e < inputs.Length; e++) inputs[e] = new BodyInput(4300 + e * 13, null, null, RoleOf(e));
            AppearanceGenerator.GenerateGroup(inputs, _looks);
            for (int e = 0; e < HighlightData.Entities; e++)
            {
                var rig = PlayerRig.Create(assets.prefab, transform, assets.bodyShader, assets.kitShader, assets.body, assets.fabric);
                rig.name = $"Entity{e:00}";
                rig.ApplyAppearance(_looks[e]);
                KitColors k;
                Texture2D atlas = null;
                int cell = -1;
                if (e == 0) { k = _kits.HomeKeeper; atlas = _homeAtlas; cell = 0; }
                else if (e <= 10) { k = _kits.Home; atlas = _homeAtlas; cell = e; }
                else if (e == 11) { k = _kits.AwayKeeper; atlas = _awayAtlas; cell = 0; }
                else if (e <= 21) { k = _kits.Away; atlas = _awayAtlas; cell = e - 11; }
                else k = _kits.Referee;
                rig.ApplyKit(k, assets.masks[0], atlas, cell);
                _rigs[e] = rig;
            }
            _sys = gameObject.AddComponent<PlayerAnimationSystem>();
        }

        /// <summary>엔티티 → 키 분포 역할: 0·11 GK, 필드 1~4 수비·5~7 미드·8~10 공격, 22~24 심판.</summary>
        static BodyRole RoleOf(int e)
        {
            if (e >= 22) return BodyRole.Referee;
            int k = e % 11;
            return k == 0 ? BodyRole.Goalkeeper : k <= 4 ? BodyRole.Defender : k <= 7 ? BodyRole.Midfielder : BodyRole.Forward;
        }

        static List<KitEntry> Squad(string[] names)
        {
            var l = new List<KitEntry>();
            for (int i = 0; i < names.Length; i++) l.Add(new KitEntry(i == 0 ? 1 : i + 1, names[i]));
            return l;
        }

        // ---------------------------------------------------------------- 라인업 (AC-13)

        int[] _row = Array.Empty<int>();
        bool _back;

        static readonly Color TextKey = new Color(1f, 0f, 1f, 1f);
        bool _key;

        KitColors KitOf(int e) => e == 0 ? _kits.HomeKeeper : e <= 10 ? _kits.Home : e == 11 ? _kits.AwayKeeper : e <= 21 ? _kits.Away : _kits.Referee;

        /// <param name="key">글자를 키 색(마젠타)으로 — 번호·이름 영역 검사용 캡처.</param>
        void ShowRow(int row, bool back, bool key = false)
        {
            _perf = false;
            KitCompositor.PlaybackActive = false;
            _key = key;
            _view = $"row{row + 1}{(back ? "Back" : "Front")}{(key ? "Key" : "")}";
            for (int e = 0; e < _rigs.Length; e++) _rigs[e].SetTextKey(key ? TextKey : (Color?)null, KitOf(e));
            _row = Rows[row];
            _back = back;
            // 라인업: 피치만(관중석 원거리 픽셀이 프레임마다 미세하게 흔들려 캡처 안정 판정을 깬다 — 키트 검사에 배경 불필요)
            foreach (var r in stadium) r.enabled = false;
            pitch.enabled = true;
            var d = new HighlightData(0f, 4);
            for (int f = 0; f < d.Frames; f++)
                for (int e = 0; e < HighlightData.Entities; e++)
                {
                    int i = Array.IndexOf(_row, e);
                    float x = i < 0 ? 0f : (i - (_row.Length - 1) * 0.5f) * Spacing;
                    d.Set(f, e, x, 0f, 0f, i >= 0);
                }
            var plan = AnimPlan.Build(_lib, _map, d, _log);
            _clock.Start = 0.05f;
            _clock.Rate = 0f;
            _clock.Frame = 0;
            _sys.Init(plan, _rigs, assets.clips, null); // 정지 장면: 한 번만 적용
            _sys.ApplyAt(_clock.SimTime);
            foreach (var rig in _rigs)
            {
                rig.Freeze(false); // 이전 시점의 정적 메시를 풀고(LODGroup 다시 켬) 나서 LOD를 고정
                rig.ForceLod(0); // 크로스페이드 진행 없음 — 클로즈업은 LOD0
                if (rig.gameObject.activeSelf) rig.Freeze(true); // 정지 장면: 현재 포즈를 구운 정적 메시(스키닝 프레임 간 미세 차이 제거)
            }
            // 카메라: 줄 너비가 가로에, 키가 세로에 들어오게(여유 10%)
            float halfW = ((_row.Length - 1) * Spacing * 0.5f + 0.6f) * 1.1f;
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
            float dist = Mathf.Max(halfW / Mathf.Tan(hHalf), 1.15f / Mathf.Tan(vHalf));
            float z = back ? -dist : dist;
            cam.transform.position = new Vector3(0f, 1.15f, z);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.0f, 0f) - cam.transform.position, Vector3.up);
        }

        // ---------------------------------------------------------------- 성능 (AC-16·17)

        void ShowPerf(int show)
        {
            _view = show == 0 ? "perfEmpty" : show == 1 ? "perfPlayers" : show == 2 ? "perfFull" : "perfPlayersNoShadow";
            foreach (var r in stadium) r.enabled = show == 2;
            pitch.enabled = show == 2;
            var d = PerfHighlight.Build();
            var plan = AnimPlan.Build(_lib, _map, d, _log);
            _clock.Start = d.StartTime;
            _clock.Rate = 1.5f;
            _clock.Fps = 60f;
            _clock.Frame = 0;
            _sys.Init(plan, _rigs, assets.clips, null, null, cam);
            foreach (var rig in _rigs)
            {
                rig.gameObject.SetActive(show != 0);
                rig.Freeze(false);
                rig.SetShadow(show != 3);
                rig.ForceLod(-1);
            }
            _perf = show != 0;
            KitCompositor.PlaybackActive = true;
            cam.transform.position = new Vector3(0f, 16f, -38f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, 2f) - cam.transform.position, Vector3.up);
        }

        void Update()
        {
            if (!_perf || _sys.Plan == null) return;
            var d = _sys.Plan.Data;
            float len = d.EndTime - d.StartTime - 0.05f;
            _clock.Frame++;
            float t = d.StartTime + Mathf.Repeat(_clock.SimTime - d.StartTime, len);
            long gc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            _sys.ApplyAt(t);
            sw.Stop();
            _gcBytes = GC.GetAllocatedBytesForCurrentThread() - gc0;
            _cpuMs = (float)sw.Elapsed.TotalMilliseconds;
            if (_clock.Frame == 240)
            {
                int vis = 0, en = 0;
                foreach (var rig in _rigs)
                    foreach (var r in rig.GetComponentsInChildren<Renderer>())
                    {
                        if (r.enabled) en++;
                        if (r.isVisible) vis++;
                    }
                Debug.Log($"[perf] {_view} 렌더러 enabled {en}, visible {vis}");
            }
            if (KitCompositor.PlaybackCompositions > 0 && !_composeErrorLogged)
            {
                _composeErrorLogged = true;
                Debug.LogError($"[kit] 재생 중 키트 합성 {KitCompositor.PlaybackCompositions}회 (AC-17은 0)");
            }
        }

        // ---------------------------------------------------------------- 캡처 영역

        static readonly Vector4 ShirtCenter = new Vector4(0.12f, 0.60f, 0.38f, 0.76f);

        Dictionary<string, object> Regions(int w, int h)
        {
            var r = new Dictionary<string, object> { { "view", _view } };
            if (!_view.StartsWith("row", StringComparison.Ordinal)) return r;
            if (!_back)
            {
                var groups = new Dictionary<string, object>();
                foreach (var e in _row)
                {
                    string g = e == 0 ? "homeGk" : e <= 10 ? "home" : e == 11 ? "awayGk" : e <= 21 ? "away" : "referee";
                    if (!groups.TryGetValue(g, out var list)) groups[g] = list = new List<object>();
                    var rect = UvRect(_rigs[e], ShirtCenter, h, false);
                    if (rect != null) ((List<object>)list).Add(rect);
                }
                r["colorGroups"] = groups;
                var combos = new List<object>();
                foreach (var l in _looks) combos.Add(l.ComboKey);
                r["combos"] = combos;
            }
            else
            {
                var areas = new List<object>();
                foreach (var e in _row)
                {
                    if (e >= 22) continue; // 심판은 번호·이름 없음
                    var k = KitOf(e);
                    var text = _key ? new List<object> { 255.0, 0.0, 255.0 } : Rgb255(PlayerLook.TextColor(k));
                    var baseC = Rgb255(k.Primary);
                    var num = UvRect(_rigs[e], PlayerLook.NumberArea, h, true);
                    var name = UvRect(_rigs[e], PlayerLook.NameArea, h, true);
                    if (num != null) areas.Add(new Dictionary<string, object> { { "area", num }, { "text", text }, { "base", baseC }, { "kind", "number" }, { "entity", e } });
                    if (name != null) areas.Add(new Dictionary<string, object> { { "area", name }, { "text", text }, { "base", baseC }, { "kind", "name" }, { "entity", e } });
                }
                r["textAreas"] = areas;
            }
            return r;
        }

        static List<object> Rgb255(Rgb c) => new List<object> { Math.Round(c.R * 255.0), Math.Round(c.G * 255.0), Math.Round(c.B * 255.0) };

        /// <summary>LOD0 스킨 메시에서 UV가 영역(u0, v0, u1, v1)과 겹치는 삼각형의 정점들을 화면에 투영한 경계 상자 (PNG 픽셀, 왼쪽 위 원점).
        /// cover = true면 영역과 겹치는 삼각형 전부(넉넉하게 — 글자 영역 밖 판정용), false면 정점이 영역 안인 것만(색 샘플용).</summary>
        List<object> UvRect(PlayerRig rig, Vector4 area, int h, bool cover)
        {
            var smr = rig.Lod(0);
            // 그려지는 메시 기준: Freeze 중이면 정적 메시(그 트랜스폼), 아니면 지금 스키닝을 굽는다
            Mesh mesh;
            Matrix4x4 toWorld;
            bool owned = false;
            if (rig.FrozenMesh != null)
            {
                mesh = rig.FrozenMesh;
                toWorld = rig.FrozenTransform.localToWorldMatrix;
            }
            else
            {
                mesh = new Mesh();
                owned = true;
                smr.BakeMesh(mesh, true);
                toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            }
            var v = mesh.vertices;
            var uv = smr.sharedMesh.uv;
            var tris = smr.sharedMesh.GetTriangles(1); // 서브메시 1 = Kit
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            int n = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                bool any = false, all = true;
                float tu0 = float.MaxValue, tv0 = float.MaxValue, tu1 = float.MinValue, tv1 = float.MinValue;
                for (int k = 0; k < 3; k++)
                {
                    var u = uv[tris[i + k]];
                    bool inside = u.x >= area.x && u.x <= area.z && u.y >= area.y && u.y <= area.w;
                    any |= inside;
                    all &= inside;
                    tu0 = Mathf.Min(tu0, u.x); tv0 = Mathf.Min(tv0, u.y); tu1 = Mathf.Max(tu1, u.x); tv1 = Mathf.Max(tv1, u.y);
                }
                bool overlap = tu0 <= area.z && tu1 >= area.x && tv0 <= area.w && tv1 >= area.y && (tu1 - tu0) < 0.3f;
                if (cover ? !overlap : !all) continue;
                for (int k = 0; k < 3; k++)
                {
                    var wp = toWorld.MultiplyPoint3x4(v[tris[i + k]]);
                    var s = cam.WorldToScreenPoint(wp);
                    if (s.z <= 0) continue;
                    float px = s.x, py = h - s.y;
                    x0 = Mathf.Min(x0, px); y0 = Mathf.Min(y0, py); x1 = Mathf.Max(x1, px); y1 = Mathf.Max(y1, py);
                    n++;
                }
            }
            if (owned) Destroy(mesh);
            if (n == 0) return null;
            return new List<object> { Math.Round(x0, 1), Math.Round(y0, 1), Math.Round(x1 - x0, 1), Math.Round(y1 - y0, 1) };
        }

        sealed class UnityLog : ILogger
        {
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;

            public void Log(LogLevel level, string message)
            {
                if (level >= LogLevel.Error) Debug.LogError(message);
                else Debug.LogWarning(message);
            }
        }
    }

    /// <summary>
    /// 성능 측정용 합성 하이라이트 (specs/043 AC-16): 22명이 홈 위치 주변 타원을 1~7 m/s로 돌고, 0.4초마다 패스, 3초마다 파울.
    /// 결정론(난수 없음 — 엔티티 번호로 정한 값). 길이 12초, 화면은 반복 재생.
    /// </summary>
    public static class PerfHighlight
    {
        public const float Start = 100f, Length = 12f;

        public static HighlightData Build()
        {
            int frames = (int)(Length * HighlightData.FrameRate) + 1;
            var d = new HighlightData(Start, frames);
            for (int f = 0; f < frames; f++)
            {
                float t = f / HighlightData.FrameRate;
                for (int e = 0; e < HighlightData.Entities; e++)
                {
                    float hx = -20f + (e % 6) * 8f, hz = -12f + (e / 6) * 6f;
                    float rx = 3f + (e % 4) * 1.5f, rz = 2f + (e % 3);
                    float speed = 1f + (e % 7);
                    float w = speed / Mathf.Max(rx, rz);
                    float a = w * t + e;
                    float x = hx + rx * Mathf.Cos(a), z = hz + rz * Mathf.Sin(a);
                    float vx = -rx * Mathf.Sin(a) * w, vz = rz * Mathf.Cos(a) * w;
                    d.Set(f, e, x, z, Mathf.Atan2(vx, vz));
                }
                d.Ball[f] = new Vec3(0, 0.11f, 0);
            }
            long seq = 1;
            for (float t = 0.5f; t < Length - 1f; t += 0.4f)
            {
                int e = (int)(t / 0.4f) % HighlightData.Players;
                var p = d.Pos(e, Start + t);
                float yaw = d.YawAt(e, Start + t);
                var fwd = new Vec3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
                var ball = p + fwd * 0.5f + new Vec3(0, 0.11f, 0);
                d.Events.Add(new AnimEvent(Start + t, seq++, RoadToPro.MatchEngine.Match.Events.SimEventKind.Pass, e, -1,
                    RoadToPro.MatchEngine.Match.Events.BodyPart.None, ActionOutcome.Success, ball, fwd * 12f));
            }
            for (float t = 2f; t < Length - 2f; t += 3f)
            {
                int victim = (int)t % HighlightData.Players, attacker = (victim + 5) % HighlightData.Players;
                d.Events.Add(new AnimEvent(Start + t, seq++, RoadToPro.MatchEngine.Match.Events.SimEventKind.Foul, attacker, victim,
                    RoadToPro.MatchEngine.Match.Events.BodyPart.None, ActionOutcome.Success, d.Pos(victim, Start + t), Vec3.Zero));
            }
            return d;
        }
    }
}
