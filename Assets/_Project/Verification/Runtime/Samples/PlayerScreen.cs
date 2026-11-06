using System;
using System.Collections;
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
    /// - 057: collar1Front … collar8Back(칼라·머리카락 클로즈업, AC-07), hair1Front … hair6Far(헤어 30종 일람 5명씩, AC-08),
    ///   flickerNormal / flickerFlat(방송 원거리 얼굴 깜빡임 — 노멀맵 켬/끔, 카운터 faceFlicker, FR-22).
    /// </summary>
    public sealed partial class PlayerScreen : MonoBehaviour
    {
        [Serializable]
        public struct PlayerAssets
        {
            public GameObject prefab;
            public TextAsset clipsJson, mapJson, glyphJson;
            public PlayerBodyAssets body;
            public Texture2D fabric, names, numbers;
            public Texture2D[] masks; // textures.PATTERNS 순서
            public Shader bodyShader, kitShader;
            public AnimationClip[] clips; // ClipLibrary 인덱스 순서
            public TextAsset gaitReferenceJson; // 055 연구 기준 스틱 피겨 (player.gait-reference.json)
            public Material stickMaterial;      // 055 스틱 피겨 선 (URP Unlit)
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
        FaceShape[] _faces;
        BodyInput[] _inputs;
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
            RegisterMotionScreens();
            FarmContext.Screens["perfEmpty"] = () => ShowPerf(0);
            FarmContext.Screens["perfPlayers"] = () => ShowPerf(1);
            FarmContext.Screens["perfFull"] = () => ShowPerf(2);
            FarmContext.Screens["perfPlayersNoShadow"] = () => ShowPerf(3);
            foreach (var k in new[] { "Pass", "Shot", "Header", "Tackle" })
            {
                var kind = k;
                FarmContext.Screens["contact" + k] = () => ShowContact(kind);
            }
            // 056: 얼굴·체형 라인업 (AC-06·07)
            for (int f = 0; f < FacePlayers.Length; f++)
            {
                int fi = f;
                FarmContext.Screens[$"face{f + 1}Front"] = () => ShowFace(fi, 0f);
                FarmContext.Screens[$"face{f + 1}Three"] = () => ShowFace(fi, 35f);
                FarmContext.Screens[$"face{f + 1}Side"] = () => ShowFace(fi, 90f);
            }
            FarmContext.Screens["bodyFront"] = () => ShowBodies(0);
            FarmContext.Screens["bodySide"] = () => ShowBodies(1);
            FarmContext.Screens["bodyBack"] = () => ShowBodies(2);
            FarmContext.Screens["bodyBroadcast"] = () => ShowBodies(3);
            FarmContext.Screens["perfCloseup"] = () => ShowPerf(4);
            // 057: 칼라·머리카락 클로즈업 (AC-07), 헤어 30종 일람 (AC-08), 원거리 깜빡임 (FR-22)
            for (int i = 0; i < CollarHair.Length; i++)
            {
                int ci = i;
                FarmContext.Screens[$"collar{i + 1}Front"] = () => ShowCollar(ci, 0f);
                FarmContext.Screens[$"collar{i + 1}Three"] = () => ShowCollar(ci, 35f);
                FarmContext.Screens[$"collar{i + 1}Back"] = () => ShowCollar(ci, 180f);
            }
            for (int g = 0; g < AppearanceGenerator.HairStyles / HairGroup; g++)
            {
                int gi = g;
                FarmContext.Screens[$"hair{g + 1}Front"] = () => ShowHairGroup(gi, 0);
                FarmContext.Screens[$"hair{g + 1}Side"] = () => ShowHairGroup(gi, 1);
                FarmContext.Screens[$"hair{g + 1}Back"] = () => ShowHairGroup(gi, 2);
                FarmContext.Screens[$"hair{g + 1}Far"] = () => ShowHairGroup(gi, 3);
            }
            FarmContext.Screens["flickerNormal"] = () => ShowFlicker(false);
            FarmContext.Screens["flickerFlat"] = () => ShowFlicker(true);
            FarmContext.PerfCounters["faceFlicker"] = () => _flickerOn && _flickerValid ? _flicker : (double?)null;
            // 055: 보법 필름스트립(8위상 × 측면·정면 + 연구 기준 스틱 피겨, AC-11)·원형 질주 1.5x(AC-12)
            foreach (var kv in FilmClips)
            {
                string name = kv.Key, id = kv.Value;
                FarmContext.Screens[$"gaitFilm{name}Side"] = () => ShowGaitFilm(name, id, true);
                FarmContext.Screens[$"gaitFilm{name}Front"] = () => ShowGaitFilm(name, id, false);
            }
            for (int i = 0; i < SprintShots; i++)
            {
                int k = i;
                FarmContext.Screens[$"gaitSprint{i + 1}"] = () => ShowGaitSprint(k);
            }
            FarmContext.Screens["speed1x"] = () => ShowSpeed(1f, 90);
            FarmContext.Screens["speed15x"] = () => ShowSpeed(1.5f, 60);
            FarmContext.Screens["speed3x"] = () => ShowSpeed(3f, 30);
            FarmContext.CaptureRegions = Regions;
            FarmContext.PerfCounters["animCpuMs"] = () => _perf ? _cpuMs : (double?)null;
            FarmContext.PerfCounters["animGcBytes"] = () => _perf ? _gcBytes : (double?)null;
            FarmContext.PerfCounters["closeupSwitchMs"] = () => _closeupPerf ? _switchMs : (double?)null;
            ShowRow(0, false);
        }

        void OnDisable()
        {
            FarmContext.Cameras.Remove(cam);
            FarmContext.CaptureRegions = null;
            FarmContext.PerfCounters.Remove("animCpuMs");
            FarmContext.PerfCounters.Remove("animGcBytes");
            FarmContext.PerfCounters.Remove("closeupSwitchMs");
            FarmContext.PerfCounters.Remove("faceFlicker");
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
            // 056: 얼굴 — 같은 장면 안에서 서로 충분히 다르게(AC-04)
            var faceData = FaceData.Parse(assets.body.faceJson.text);
            var ids = new int[inputs.Length];
            for (int e = 0; e < ids.Length; e++) ids[e] = inputs[e].Id;
            var faces = new FaceShape[ids.Length];
            FaceGenerator.GenerateGroup(faceData, new FaceLandmarks(faceData), ids, faces);
            _faces = faces;
            _inputs = inputs;
            for (int e = 0; e < HighlightData.Entities; e++)
            {
                var rig = PlayerRig.Create(assets.prefab, transform, assets.bodyShader, assets.kitShader, assets.body, assets.fabric);
                rig.name = $"Entity{e:00}";
                rig.ApplyAppearance(_looks[e], faces[e], faceData);
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
            ClearSticks();
            if (_ball != null) _ball.gameObject.SetActive(false);
            _contactView = false;
            _perf = false;
            KitCompositor.PlaybackActive = false;
            _key = key;
            _view = $"row{row + 1}{(back ? "Back" : "Front")}{(key ? "Key" : "")}";
            if (_bodiesApplied) RestoreLooks();
            foreach (var rg in _rigs) rg.SetCloseup(false);
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

        // ---------------------------------------------------------------- 056 얼굴·체형 라인업 (AC-06·07)

        /// <summary>얼굴 라인업 엔티티 (홈 필드 1~8 — 서로 다른 얼굴·피부·머리).</summary>
        static readonly int[] FacePlayers = { 1, 2, 3, 4, 5, 6, 7, 8 };

        /// <summary>체형 라인업: 극단 4종(키 160·200 × 마른·무거운) + 보통 4명. (키 cm, 체중 kg; 0 = 생성값)</summary>
        static readonly (int e, float h, float w)[] BodyPlayers =
        {
            (1, 160f, 47f), (2, 160f, 75f), (3, 200f, 76f), (4, 200f, 108f), (5, 0f, 0f), (6, 0f, 0f), (7, 0f, 0f), (8, 0f, 0f),
        };

        bool _bodiesApplied;
        bool _closeupPerf;
        float _switchMs;

        // ---------------------------------------------------------------- 057 칼라·헤어·깜빡임 (AC-07·08, FR-22)

        /// <summary>칼라·머리카락 클로즈업 8명의 헤어스타일 (계열을 섞는다: 짧은·긴·쿠이프·커튼·포니테일·아프로·콘로·박스 브레이드).</summary>
        static readonly int[] CollarHair = { 1, 2, 14, 15, 22, 6, 19, 28 };
        const int HairGroup = 5;
        readonly HashSet<int> _overridden = new HashSet<int>();
        bool _flickerOn, _flickerValid;
        double _flicker;
        float[] _flickPrev;
        Coroutine _flickCo;

        static PlayerAppearance WithHair(in PlayerAppearance a, int hs) =>
            a.WithCombo(((a.SkinTone * AppearanceGenerator.Faces + a.Face) * AppearanceGenerator.HairStyles + hs) * AppearanceGenerator.HairColors + a.HairColor);

        void Override(int ent, int hs)
        {
            var faceData = FaceData.Parse(assets.body.faceJson.text);
            _rigs[ent].ApplyAppearance(WithHair(_looks[ent], hs), _faces[ent], faceData);
            _overridden.Add(ent);
        }

        void RestoreOverrides()
        {
            if (_overridden.Count == 0) return;
            var faceData = FaceData.Parse(assets.body.faceJson.text);
            foreach (int e in _overridden) _rigs[e].ApplyAppearance(_looks[e], _faces[e], faceData);
            _overridden.Clear();
        }

        /// <summary>칼라·머리카락 클로즈업: 머리 + 목 + 어깨 윗부분(약 0.42 m)이 화면 높이 60 %. 클로즈업 머리 켬. yaw 180 = 뒤.</summary>
        void ShowCollar(int idx, float yawDeg)
        {
            int ent = FacePlayers[idx];
            PrepareStill($"collar{idx + 1}{(yawDeg == 0f ? "Front" : yawDeg < 60f ? "Three" : "Back")}");
            if (_bodiesApplied) RestoreLooks();
            Override(ent, CollarHair[idx]);
            float yaw = yawDeg * Mathf.Deg2Rad;
            PoseStill(e => (e == ent, 0f, 0f, yaw), false, 0);
            var rig = _rigs[ent];
            rig.SetCloseup(true);
            float s = rig.transform.localScale.y;
            // smoke 캡처(2026-11-05)에서 Head 뼈 − 6 cm가 가슴 높이로 찍혀 머리 쪽으로 올린다(Head 뼈 + 12 cm ≈ 턱)
            var c = rig.Bone("Head").position + new Vector3(0f, 0.12f * s, 0f);
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float dist = 0.42f * s / (0.6f * 2f * Mathf.Tan(vHalf));
            cam.transform.position = c + new Vector3(0f, 0.04f, dist);
            cam.transform.rotation = Quaternion.LookRotation(c - cam.transform.position, Vector3.up);
        }

        /// <summary>헤어 일람: 스타일 g×5 … g×5+4를 5명에게 입혀 한 줄. view 0 정면 · 1 옆 · 2 뒤(LOD0 근거리) · 3 방송 원거리(LOD 자동).</summary>
        void ShowHairGroup(int g, int view)
        {
            PrepareStill($"hair{g + 1}{(view == 0 ? "Front" : view == 1 ? "Side" : view == 2 ? "Back" : "Far")}");
            if (_bodiesApplied) RestoreLooks();
            for (int i = 0; i < HairGroup; i++) Override(FacePlayers[i], g * HairGroup + i);
            const float gap = 0.5f;  // 머리 일람 — 어깨는 겹쳐도 머리는 떨어진다
            float yaw = view == 1 ? Mathf.PI * 0.5f : view == 2 ? Mathf.PI : 0f;
            PoseStill(e =>
            {
                int i = Array.IndexOf(FacePlayers, e);
                return (i >= 0 && i < HairGroup, (i - 2f) * gap, 0f, yaw);
            }, view != 3, view == 3 ? -1 : 0);
            if (view == 3)
            {
                cam.transform.position = new Vector3(0f, 16f, 30f);
                cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.2f, 0f) - cam.transform.position, Vector3.up);
                return;
            }
            float halfW = (2f * gap + 0.3f) * 1.1f;
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
            float dist = Mathf.Max(halfW / Mathf.Tan(hHalf), 0.4f / Mathf.Tan(vHalf));
            // 머리 높이를 화면 가운데로 (smoke 캡처에서 1.62 m 조준이 목·가슴에 찍혔다)
            cam.transform.position = new Vector3(0f, 1.86f, dist);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.82f, 0f) - cam.transform.position, Vector3.up);
        }

        /// <summary>FR-22 깜빡임: 8명을 방송 원거리(LOD 자동)에 세우고 카메라를 아주 천천히(프레임당 2 mm) 옮기며, 머리 둘레 12×12 px의
        /// 프레임 간 밝기 차 평균(0~255)을 카운터 faceFlicker로 낸다. flat = 노멀맵 세기 0(기하 기준선) — 판정은 비율.</summary>
        void ShowFlicker(bool flat)
        {
            PrepareStill(flat ? "flickerFlat" : "flickerNormal");
            if (_bodiesApplied) RestoreLooks();
            PoseStill(e =>
            {
                int i = Array.IndexOf(FacePlayers, e);
                return (i >= 0, (i - 3.5f) * 1.1f, 0f, 0f);
            }, false, -1);
            foreach (int e in FacePlayers) _rigs[e].BodyMaterial.SetFloat("_NormalStrength", flat ? 0f : 0.6f);
            cam.transform.position = new Vector3(-0.5f, 16f, 30f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(-0.5f, 1.2f, 0f) - cam.transform.position, Vector3.up);
            _flickPrev = null;
            _flickerValid = false;
            _flickerOn = true;
            if (_flickCo == null) _flickCo = StartCoroutine(FlickerLoop());
        }

        IEnumerator FlickerLoop()
        {
            var wait = new WaitForEndOfFrame();
            const int R = 12;
            var tex = new Texture2D(R, R, TextureFormat.RGB24, false);
            var cur = new float[FacePlayers.Length * R * R];
            while (_flickerOn)
            {
                yield return wait;
                if (!_flickerOn) break;
                int k = 0;
                bool ok = true;
                foreach (int e in FacePlayers)
                {
                    var sp = cam.WorldToScreenPoint(_rigs[e].Bone("Head").position);
                    // 카메라 픽셀 크기와 화면 크기가 다를 수 있다(렌더 스케일) — 화면 비율로 옮기고 화면 안으로 클램프
                    float fx = sp.x / Mathf.Max(1, cam.pixelWidth) * Screen.width, fy = sp.y / Mathf.Max(1, cam.pixelHeight) * Screen.height;
                    int x = Mathf.Clamp(Mathf.RoundToInt(fx) - R / 2, 0, Screen.width - R), y = Mathf.Clamp(Mathf.RoundToInt(fy) - R / 2, 0, Screen.height - R);
                    if (sp.z <= 0f) { ok = false; break; }
                    tex.ReadPixels(new Rect(x, y, R, R), 0, 0, false);
                    var px = tex.GetPixels32();
                    for (int i = 0; i < px.Length; i++) cur[k++] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
                }
                if (ok && _flickPrev != null)
                {
                    double sum = 0;
                    for (int i = 0; i < cur.Length; i++) sum += Math.Abs(cur[i] - _flickPrev[i]);
                    _flicker = sum / cur.Length;
                    _flickerValid = true;
                }
                if (ok) { _flickPrev ??= new float[cur.Length]; Array.Copy(cur, _flickPrev, cur.Length); }
                cam.transform.position += new Vector3(0.002f, 0f, 0f);
            }
            Destroy(tex);
            _flickCo = null;
        }

        void PrepareStill(string view)
        {
            ClearSticks();
            if (_flickerOn)
            {
                _flickerOn = false;
                foreach (int e in FacePlayers) _rigs[e].BodyMaterial.SetFloat("_NormalStrength", 0.6f);
            }
            RestoreOverrides();
            if (_ball != null) _ball.gameObject.SetActive(false);
            _contactView = false;
            _perf = false;
            KitCompositor.PlaybackActive = false;
            _key = false;
            _view = view;
            foreach (var r in stadium) r.enabled = false;
            pitch.enabled = true;
            for (int e = 0; e < _rigs.Length; e++)
            {
                _rigs[e].SetTextKey(null, KitOf(e));
                _rigs[e].SetCloseup(false);
            }
        }

        void PoseStill(Func<int, (bool on, float x, float z, float yaw)> place, bool freeze, int lod)
        {
            var d = new HighlightData(0f, 4);
            for (int f = 0; f < d.Frames; f++)
                for (int e = 0; e < HighlightData.Entities; e++)
                {
                    var p = place(e);
                    d.Set(f, e, p.x, p.z, p.yaw, p.on);
                }
            var plan = AnimPlan.Build(_lib, _map, d, _log);
            _clock.Start = 0.05f;
            _clock.Rate = 0f;
            _clock.Frame = 0;
            _sys.Init(plan, _rigs, assets.clips, null);
            _sys.ApplyAt(_clock.SimTime);
            foreach (var rig in _rigs)
            {
                rig.Freeze(false);
                rig.ForceLod(lod);
                if (freeze && rig.gameObject.activeSelf) rig.Freeze(true);
            }
        }

        /// <summary>얼굴 클로즈업 한 명: 클로즈업 머리(FR-61)를 켜고 머리 높이가 화면의 약 45 %가 되게. yaw = 선수가 카메라에서 돌아선 각.</summary>
        void ShowFace(int idx, float yawDeg)
        {
            int ent = FacePlayers[idx];
            PrepareStill($"face{idx + 1}{(yawDeg == 0f ? "Front" : yawDeg < 60f ? "Three" : "Side")}");
            if (_bodiesApplied) RestoreLooks();
            float yaw = yawDeg * Mathf.Deg2Rad;
            PoseStill(e => (e == ent, 0f, 0f, yaw), false, 0);
            var rig = _rigs[ent];
            rig.SetCloseup(true);
            var head = rig.Bone("Head").position + new Vector3(0f, 0.09f * rig.transform.localScale.y, 0f);
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float dist = 0.24f * rig.transform.localScale.y / (0.45f * 2f * Mathf.Tan(vHalf));
            cam.transform.position = head + new Vector3(0f, 0.02f, dist);
            cam.transform.rotation = Quaternion.LookRotation(head - cam.transform.position, Vector3.up);
        }

        void RestoreLooks()
        {
            var faceData = FaceData.Parse(assets.body.faceJson.text);
            foreach (var bp in BodyPlayers) _rigs[bp.e].ApplyAppearance(_looks[bp.e], _faces[bp.e], faceData);
            _bodiesApplied = false;
        }

        /// <summary>체형 라인업 8명. view 0 정면 · 1 옆 · 2 뒤 (LOD0 정지) · 3 방송 원거리(LOD 자동).</summary>
        void ShowBodies(int view)
        {
            PrepareStill(view == 0 ? "bodyFront" : view == 1 ? "bodySide" : view == 2 ? "bodyBack" : "bodyBroadcast");
            if (!_bodiesApplied)
            {
                var faceData = FaceData.Parse(assets.body.faceJson.text);
                foreach (var bp in BodyPlayers)
                {
                    var a = bp.h > 0f
                        ? AppearanceGenerator.Generate(new BodyInput(_inputs[bp.e].Id, bp.h, bp.w, RoleOf(bp.e)))
                        : _looks[bp.e];
                    a = a.WithCombo(_looks[bp.e].ComboKey);
                    _rigs[bp.e].ApplyAppearance(a, _faces[bp.e], faceData);
                }
                _bodiesApplied = true;
            }
            const float gap = 1.1f;
            float yaw = view == 1 ? Mathf.PI * 0.5f : 0f;
            PoseStill(e =>
            {
                int i = Array.FindIndex(BodyPlayers, b => b.e == e);
                return (i >= 0, (i - 3.5f) * gap, 0f, yaw);
            }, view != 3, view == 3 ? -1 : 0);
            if (view == 3)
            {
                cam.transform.position = new Vector3(0f, 16f, 30f);
                cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.9f, 0f) - cam.transform.position, Vector3.up);
                return;
            }
            float halfW = (3.5f * gap + 0.6f) * 1.1f;
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
            float dist = Mathf.Max(halfW / Mathf.Tan(hHalf), 1.2f / Mathf.Tan(vHalf));
            float z = view == 2 ? -dist : dist;
            cam.transform.position = new Vector3(0f, 1.1f, z);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.0f, 0f) - cam.transform.position, Vector3.up);
        }

        // ---------------------------------------------------------------- 성능 (AC-16·17)

        void ShowPerf(int show)
        {
            ClearSticks();
            if (_ball != null) _ball.gameObject.SetActive(false);
            _contactView = false;
            if (_bodiesApplied) RestoreLooks();
            _view = show == 0 ? "perfEmpty" : show == 1 ? "perfPlayers" : show == 2 ? "perfFull" : show == 3 ? "perfPlayersNoShadow" : "perfCloseup";
            _closeupPerf = show == 4;
            _switchMs = 0f;
            foreach (var r in stadium) r.enabled = show == 2;
            pitch.enabled = show == 2;
            var d = PerfHighlight.Build();
            var plan = AnimPlan.Build(_lib, _map, d, _log, PerfHighlight.Styles(), RigScales()); // 055: 원형 블렌드 포함 비용
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
            if (_closeupPerf && _clock.Frame % 90 == 0)
            {
                // 056 FR-61: 클로즈업 머리 전환 프레임 비용 (2명 켜기/끄기)
                bool on = !_rigs[9].CloseupActive;
                var s2 = Stopwatch.StartNew();
                _rigs[9].SetCloseup(on);
                _rigs[10].SetCloseup(on);
                s2.Stop();
                _switchMs = Mathf.Max(_switchMs, (float)s2.Elapsed.TotalMilliseconds);
            }
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

        /// <summary>
        /// specs/043 T27 IL2CPP 벤치: perfPlayers 화면을 띄우고 warmup 뒤 frames 프레임 동안 애니메이션 갱신 1회 시간(ms)·할당을 잰다.
        /// 렌더 없이(헤드리스) 같은 경로(AnimPlan 평가 + 포즈 적용 + 그래프 평가 + 발 접지)를 돈다.
        /// </summary>
        public Dictionary<string, double> BenchAnim(int warmup, int frames)
        {
            ShowPerf(1);
            _perf = false; // Update가 같이 돌지 않게
            var d = _sys.Plan.Data;
            float len = d.EndTime - d.StartTime - 0.05f;
            var ms = new double[frames];
            var motion = new double[frames];
            long gc = 0;
            _sys.TimeMotion = true;
            for (int i = -warmup; i < frames; i++)
            {
                _clock.Frame++;
                float t = d.StartTime + Mathf.Repeat(_clock.SimTime - d.StartTime, len);
                long g0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                _sys.ApplyAt(t);
                sw.Stop();
                long g = GC.GetAllocatedBytesForCurrentThread() - g0;
                if (i < 0) continue;
                ms[i] = sw.Elapsed.TotalMilliseconds;
                motion[i] = _sys.LastMotionMs;
                gc += g;
            }
            _sys.TimeMotion = false;
            Array.Sort(ms);
            Array.Sort(motion);
            double P(double q) => ms[Math.Min(frames - 1, Math.Max(0, (int)Math.Ceiling(q * frames) - 1))];
            double MotionP(double q) => motion[Math.Min(frames - 1, Math.Max(0, (int)Math.Ceiling(q * frames) - 1))];
            // 진단 켬 추가 비용 (054 성능 예산, 검증 모드 전용): 측정기 호출 시간만 잰다 (같은 포즈에서 프레임당)
            double DiagOnCost(float span)
            {
                var std = RoadToPro.Presentation.Players.Logic.Motion.MotionStandards.Parse(
                    "{\"schemaVersion\":1,\"thresholds\":{\"stanceHeight\":{\"max\":0.03},\"stanceFootSpeed\":{\"max\":1.0}}}");
                var actors = new int[HighlightData.Entities];
                for (int k = 0; k < actors.Length; k++) actors[k] = k;
                var meter = new RoadToPro.Presentation.Players.Motion.MotionMeter(_sys.Plan, _rigs, actors, std, "bench", 1.5f);
                var on = new double[120];
                for (int k = 0; k < on.Length; k++)
                {
                    float tk = d.StartTime + Mathf.Repeat(k * 0.025f, span);
                    _sys.ApplyAt(tk);
                    var s3 = Stopwatch.StartNew();
                    meter.OnApplied(tk);
                    s3.Stop();
                    on[k] = s3.Elapsed.TotalMilliseconds;
                }
                Array.Sort(on);
                return on[(int)(on.Length * 0.95)];
            }
            // 진단 꺼짐 비용 (054 FR-70): null 분기를 프레임마다 한 번 도는 비용을 100만 회로 잰다
            double DiagOffCost()
            {
                var s2 = Stopwatch.StartNew();
                for (int k = 0; k < 1000000; k++) _sys.Diagnostics?.OnApplied(0f);
                s2.Stop();
                return s2.Elapsed.TotalMilliseconds / 1000000.0;
            }
            return new Dictionary<string, double>
            {
                { "anim_p50_ms", P(0.50) },
                { "anim_p95_ms", P(0.95) },
                { "anim_max_ms", ms[frames - 1] },
                { "anim_gc_bytes", gc },
                { "anim_frames", frames },
                { "anim_players", HighlightData.Entities },
                { "motion_p95_ms", MotionP(0.95) },
                { "motion_max_ms", MotionP(1.0) },
                { "diag_off_ms", DiagOffCost() },
                { "diag_on_ms", DiagOnCost(len) },
            };
        }

        // ---------------------------------------------------------------- 접촉 클로즈업 (AC-14) · 배속 (AC-15)

        Transform _ball;
        RoadToPro.Presentation.Players.Logic.ContactRule _rule;
        bool _contactView;

        Transform Ball()
        {
            if (_ball != null) return _ball;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ball";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.22f;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = new Material(assets.kitShader) { name = "BallStandIn" };
            r.sharedMaterial.SetColor("_Primary", Color.white);
            r.sharedMaterial.SetColor("_BootColor", Color.white);
            r.sharedMaterial.SetVector("_TextCell", Vector4.zero);
            _ball = go.transform;
            return _ball;
        }

        /// <summary>접촉 장면: 엔티티 0이 +Z로 달려와 시작 + 2초에 공을 찬다(헤딩은 공 높이 2.25 m, 태클은 0.8 m 앞 땅볼).</summary>
        HighlightData ContactScene(string kind, out float contact)
        {
            const float start = 50f, len = 3f;
            int frames = (int)(len * HighlightData.FrameRate) + 1;
            var d = new HighlightData(start, frames);
            contact = start + 2f;
            float v = kind == "Tackle" ? 4f : kind == "Header" ? 2f : 3.5f;
            for (int f = 0; f < frames; f++)
            {
                float t = f / HighlightData.FrameRate;
                // 접촉 0.4 s 전부터 절반 속도로 감속, 이후 절반 속도
                float tc = 2f, t1 = tc - 0.4f;
                float z = t <= t1 ? v * t : v * t1 + v * Mathf.Min(t - t1, 0.4f) - 0.5f * (0.5f * v / 0.4f) * Mathf.Pow(Mathf.Min(t - t1, 0.4f), 2) + (t > tc ? 0.5f * v * (t - tc) : 0f);
                d.Set(f, 0, 0f, z - 7f, 0f);
                for (int e = 1; e < HighlightData.Entities; e++) d.Set(f, e, 0f, 0f, 0f, false);
            }
            var p = d.Pos(0, contact);
            Vec3 ball; Vec3 vel;
            var part = RoadToPro.MatchEngine.Match.Events.BodyPart.None;
            var ev = RoadToPro.MatchEngine.Match.Events.SimEventKind.Pass;
            switch (kind)
            {
                case "Shot": ball = p + new Vec3(0.05f, 0.11f, 0.58f); vel = new Vec3(0, 3, 24); ev = RoadToPro.MatchEngine.Match.Events.SimEventKind.Shot; part = RoadToPro.MatchEngine.Match.Events.BodyPart.RightFoot; break;
                case "Header": ball = p + new Vec3(0f, 2.15f, 0.34f); vel = new Vec3(0, -2, 12); ev = RoadToPro.MatchEngine.Match.Events.SimEventKind.Shot; part = RoadToPro.MatchEngine.Match.Events.BodyPart.Head; break;
                case "Tackle": ball = p + new Vec3(0.08f, 0.11f, 0.78f); vel = new Vec3(2, 0, 4); ev = RoadToPro.MatchEngine.Match.Events.SimEventKind.Tackle; part = RoadToPro.MatchEngine.Match.Events.BodyPart.RightFoot; break;
                default: ball = p + new Vec3(-0.13f, 0.11f, 0.49f); vel = new Vec3(-3, 0, 10); part = RoadToPro.MatchEngine.Match.Events.BodyPart.RightFoot; break;
            }
            for (int f = 0; f < frames; f++)
            {
                float t = start + f / HighlightData.FrameRate;
                d.Ball[f] = t <= contact ? ball + new Vec3(0, 0, Mathf.Max(0f, contact - t) * 2f) : ball + vel * (t - contact);
            }
            d.Events.Add(new AnimEvent(contact, 1, ev, 0, -1, part, ActionOutcome.Success, ball, vel));
            return d;
        }

        void Prepare(HighlightData d, float t, float rate, long frame, RunStyleChoice[] styles = null)
        {
            _perf = false;
            KitCompositor.PlaybackActive = false;
            foreach (var r in stadium) r.enabled = false;
            pitch.enabled = true;
            var plan = AnimPlan.Build(_lib, _map, d, _log, styles, RigScales());
            _clock.Start = d.StartTime;
            _clock.Fps = 60f;
            _clock.Rate = rate;
            _clock.Frame = frame;
            _sys.Init(plan, _rigs, assets.clips, null);
            foreach (var rig in _rigs) rig.Freeze(false);
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

        void ShowContact(string kind)
        {
            ClearSticks();
            _view = "contact" + kind;
            _contactView = true;
            var d = ContactScene(kind, out var contact);
            // 0.5x 리플레이: 접촉 시각이 되는 프레임 = (contact − start) · fps / 0.5
            long frame = (long)Math.Round((contact - d.StartTime) * 60.0 / 0.5);
            Prepare(d, contact, 0.5f, frame);
            var a = _sys.Plan.Timeline(0);
            _rule = _lib.Get(a.Get(0).Clip).Rule;
            // 옆(오른쪽)에서 본 클로즈업, 접촉점 높이
            var cp = ContactWorld();
            var target = new Vector3(cp.x, Mathf.Max(0.5f, cp.y), cp.z);
            cam.transform.position = target + new Vector3(3.2f, 0.4f, -0.6f);
            cam.transform.rotation = Quaternion.LookRotation(target - cam.transform.position, Vector3.up);
        }

        // ---------------------------------------------------------------- 055 필름스트립·원형 질주 (AC-11·12)

        /// <summary>필름스트립 클립 (화면 이름 → 클립 id). 보법 4 + 출발·감속·드리블 런.</summary>
        static readonly KeyValuePair<string, string>[] FilmClips =
        {
            new KeyValuePair<string, string>("Walk", "walk_f"), new KeyValuePair<string, string>("Jog", "jog_f"),
            new KeyValuePair<string, string>("Run", "run_f"), new KeyValuePair<string, string>("Sprint", "sprint_f"),
            new KeyValuePair<string, string>("Start", "start_f"), new KeyValuePair<string, string>("Stop", "stop_f"),
            new KeyValuePair<string, string>("Dribble", "dribble_run_50"),
        };
        const int FilmTiles = 8;
        const float FilmGap = 1.25f;    // 칸 간격 (m)
        const float StickRow = 2.25f;   // 스틱 피겨 줄 높이 (모델 줄 위)
        const int SprintShots = 4;
        readonly List<GameObject> _sticks = new List<GameObject>();
        GaitReference _ref;

        void ClearSticks()
        {
            foreach (var g in _sticks) if (g != null) Destroy(g);
            _sticks.Clear();
        }

        /// <summary>클립을 8위상(루프 = 주기 8등분, 비루프 = 길이 8등분)에 고정해 한 줄로 세운다. side = 오른쪽 측면에서, 아니면 정면에서.
        /// 위 줄에 같은 위상의 연구 기준 스틱 피겨(player.gait-reference.json, 보법 4종만).</summary>
        void ShowGaitFilm(string name, string id, bool side)
        {
            PrepareStill($"gaitFilm{name}{(side ? "Side" : "Front")}");
            if (_bodiesApplied) RestoreLooks();
            ClearSticks();
            int ci = _lib.IndexOf(id);
            if (ci < 0) throw new InvalidOperationException("클립 없음: " + id);
            var info = _lib.Get(ci);
            var clip = assets.clips[ci];
            // 모델: 측면이면 진행 방향(+Z)을 따라, 정면이면 X를 따라 늘어선다. 모두 +Z를 본다
            PoseStill(e =>
            {
                int i = Array.IndexOf(FacePlayers, e);
                float o = (i - (FilmTiles - 1) * 0.5f) * FilmGap;
                return (i >= 0 && i < FilmTiles, side ? 0f : -o, side ? o : 0f, 0f); // 정면은 카메라가 −Z를 보므로 X를 뒤집어 위상이 왼쪽 → 오른쪽
            }, false, 0);
            for (int i = 0; i < FilmTiles; i++)
            {
                var rig = _rigs[FacePlayers[i]];
                float t = info.Length * i / FilmTiles;
                clip.SampleAnimation(rig.Animator.gameObject, t);
                rig.ForceLod(0);
                rig.Freeze(true);
            }
            _ref ??= GaitReference.Parse(assets.gaitReferenceJson.text);
            var g = _ref.Get(info.Gait);
            if (g != null)
                for (int i = 0; i < FilmTiles; i++)
                {
                    float o = (i - (FilmTiles - 1) * 0.5f) * FilmGap;
                    var origin = side ? new Vector3(0f, StickRow, o) : new Vector3(-o, StickRow, 0f);
                    DrawStick(g, i, origin, side);
                }
            float halfW = (FilmTiles - 1) * FilmGap * 0.5f + 0.7f;
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
            float dist = Mathf.Max(halfW / Mathf.Tan(hHalf), 2.35f / Mathf.Tan(vHalf)) * 1.05f;
            var c = new Vector3(0f, 2.1f, 0f);
            cam.transform.position = side ? c + new Vector3(dist, 0f, 0f) : c + new Vector3(0f, 0f, dist);
            cam.transform.rotation = Quaternion.LookRotation(c - cam.transform.position, Vector3.up);
        }

        void Line(Vector3 a, Vector3 b, Color col)
        {
            var go = new GameObject("GaitStick");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = assets.stickMaterial;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = lr.endWidth = 0.035f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", col);
            lr.SetPropertyBlock(mpb);
            _sticks.Add(go);
        }

        static readonly Color StickLeft = new Color(0.25f, 0.55f, 1f), StickRight = new Color(1f, 0.6f, 0.2f), StickBody = new Color(0.95f, 0.95f, 0.95f);

        /// <summary>연구 기준 스틱 피겨 한 칸. 측면: 시상면 각(엉덩이·무릎은 몸통선 기준, 팔도), 정면: 골반 기울기·팔 벌림·발 간격.</summary>
        void DrawStick(GaitReference.Gait g, int phase, Vector3 o, bool side)
        {
            var sg = _ref.Segments;
            var sp = g.Side[phase];
            var fr = g.Front[phase];
            if (side)
            {
                // 측면: 화면 평면 = (Z 앞, Y 위). 각은 아래 방향에서 앞(+Z)으로 + (°)
                Vector3 Dir(float deg) => new Vector3(0f, -Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
                var hip = o + new Vector3(0f, sg.PelvisHeight + sp.PelvisY, 0f);
                float down = -sp.Trunk; // 몸통선 아래 방향의 각
                var neck = hip - Dir(down) * sg.Trunk;
                var head = neck - Dir(down) * (sg.Neck + sg.Head);
                Line(hip, neck, StickBody);
                Line(neck, head, StickBody);
                void Leg(float hipDeg, float kneeDeg, Color col)
                {
                    float a = down + hipDeg;
                    var knee = hip + Dir(a) * sg.Thigh;
                    var ankle = knee + Dir(a - kneeDeg) * sg.Shin;
                    Line(hip, knee, col);
                    Line(knee, ankle, col);
                    Line(ankle, ankle + new Vector3(0f, 0f, sg.Foot), col);
                }
                void Arm(float shDeg, float elDeg, Color col)
                {
                    var sh = neck + Dir(down) * 0.05f;
                    float a = down + shDeg;
                    var elbow = sh + Dir(a) * sg.UpperArm;
                    var hand = elbow + Dir(a + elDeg) * sg.ForeArm;
                    Line(sh, elbow, col);
                    Line(elbow, hand, col);
                }
                Leg(sp.HipL, sp.KneeL, StickLeft);
                Leg(sp.HipR, sp.KneeR, StickRight);
                Arm(sp.ShoulderL, sp.ElbowL, StickLeft);
                Arm(sp.ShoulderR, sp.ElbowR, StickRight);
                return;
            }
            // 정면: 화면 평면 = (X 오른쪽, Y 위). 골반 기울기(+ = 오른쪽 내려감), 팔 벌림, 발 간격
            float ob = fr.Obliquity * Mathf.Deg2Rad;
            var pc = o + new Vector3(0f, sg.PelvisHeight + sp.PelvisY, 0f);
            var hr = pc + new Vector3(0.1f * Mathf.Cos(ob), -0.1f * Mathf.Sin(ob), 0f);
            var hl = pc - new Vector3(0.1f * Mathf.Cos(ob), -0.1f * Mathf.Sin(ob), 0f);
            var nk = pc + new Vector3(0f, sg.Trunk, 0f);
            Line(hl, hr, StickBody);
            Line(pc, nk, StickBody);
            Line(nk, nk + new Vector3(0f, sg.Neck + sg.Head, 0f), StickBody);
            float fw = fr.FootWidth * 0.5f;
            Line(hl, o + new Vector3(-fw, 0.08f, 0f), StickLeft);
            Line(hr, o + new Vector3(fw, 0.08f, 0f), StickRight);
            float ab = fr.Abduction * Mathf.Deg2Rad, arm = sg.UpperArm + sg.ForeArm * 0.6f;
            var shl = nk + new Vector3(-0.18f, -0.05f, 0f);
            var shr = nk + new Vector3(0.18f, -0.05f, 0f);
            Line(shl, shl + new Vector3(-Mathf.Sin(ab), -Mathf.Cos(ab), 0f) * arm, StickLeft);
            Line(shr, shr + new Vector3(Mathf.Sin(ab), -Mathf.Cos(ab), 0f) * arm, StickRight);
        }

        /// <summary>AC-12: 1.5x 하이라이트 질주 — 두 선수(R1 저중심 숏스텝 · R2 장신 파워 스트라이드)가 8.8 m/s로 나란히. 연속 4장(0.1 s 간격) 측면.</summary>
        void ShowGaitSprint(int k)
        {
            ClearSticks();
            _view = $"gaitSprint{k + 1}";
            _contactView = false;
            if (_bodiesApplied) RestoreLooks();
            RestoreOverrides();
            var d = PerfHighlight.Build();
            for (int f = 0; f < d.Frames; f++)
                for (int e = 0; e < HighlightData.Entities; e++)
                {
                    bool on = e == 1 || e == 2;
                    float x = e == 1 ? -1.6f : 1.6f;
                    d.Set(f, e, on ? x : 0f, on ? -30f + 8.8f * f / HighlightData.FrameRate : 0f, 0f, on);
                }
            d.Events.Clear();
            var styles = new RunStyleChoice[HighlightData.Entities];
            styles[1] = new RunStyleChoice(RunArchetype.R1, KickArchetype.None, 0.5f);
            styles[2] = new RunStyleChoice(RunArchetype.R2, KickArchetype.None, 0.5f);
            // 1.5x: 시뮬 시각 = 시작 + 2 s + k·0.1 s
            float t = d.StartTime + 2f + 0.1f * k;
            long frame = (long)Math.Round((t - d.StartTime) * 60.0 / 1.5);
            Prepare(d, t, 1.5f, frame, styles);
            Ball().gameObject.SetActive(false);
            var mid = (_rigs[1].transform.position + _rigs[2].transform.position) * 0.5f;
            cam.transform.position = mid + new Vector3(7.0f, 1.5f, 5.5f); // 앞쪽 비스듬히 — 측면이면 두 선수가 겹친다
            cam.transform.rotation = Quaternion.LookRotation(mid + Vector3.up * 0.95f - cam.transform.position, Vector3.up);
        }

        void ShowSpeed(float rate, long frame)
        {
            ClearSticks();
            _view = $"speed{rate:0.#}x";
            _contactView = false;
            var d = PerfHighlight.Build();
            for (int f = 0; f < d.Frames; f++)
                for (int e = 0; e < HighlightData.Entities; e++)
                    d.Set(f, e, e == 0 ? 0f : 0f, e == 0 ? -20f + 7.6f * f / HighlightData.FrameRate : 0f, 0f, e == 0);
            d.Events.Clear();
            // 시뮬 시각 = 시작 + 프레임 · 배속 / 60 → 세 배속 모두 시작 + 1.5 s
            float t = d.StartTime + (float)(frame * (double)rate / 60.0);
            Prepare(d, t, rate, frame);
            Ball().gameObject.SetActive(false);
            var root = _rigs[0].transform.position;
            cam.transform.position = root + new Vector3(4.5f, 1.4f, 0.5f);
            cam.transform.rotation = Quaternion.LookRotation(root + Vector3.up * 0.9f - cam.transform.position, Vector3.up);
        }

        Vector3 ContactWorld()
        {
            var rig = _rigs[0];
            Vec3 V(Transform t) => new Vec3(t.position.x, t.position.y, t.position.z);
            var bone = V(rig.Bone(_rule.Bone));
            var toward = _rule.Toward != null ? V(rig.Bone(_rule.Toward)) : bone;
            var p = _rule.Evaluate(bone, toward, V(rig.Bone("LeftUpperLeg")), V(rig.Bone("RightUpperLeg")));
            return new Vector3(p.X, p.Y, p.Z);
        }

        // ---------------------------------------------------------------- 캡처 영역

        static readonly Vector4 ShirtCenter = new Vector4(0.12f, 0.60f, 0.38f, 0.76f);

        Dictionary<string, object> Regions(int w, int h)
        {
            var r = new Dictionary<string, object> { { "view", _view } };
            if (_view.StartsWith("contact", StringComparison.Ordinal) && _contactView)
            {
                var cp = cam.WorldToScreenPoint(ContactWorld());
                var bc = cam.WorldToScreenPoint(_ball.position);
                var edge = cam.WorldToScreenPoint(_ball.position + cam.transform.right * 0.11f);
                r["contact"] = new Dictionary<string, object>
                {
                    { "point", new List<object> { Math.Round(cp.x, 2), Math.Round(h - cp.y, 2) } },
                    { "ball", new List<object> { Math.Round(bc.x, 2), Math.Round(h - bc.y, 2) } },
                    { "ballDiameter", Math.Round(2.0 * Vector2.Distance(new Vector2(bc.x, bc.y), new Vector2(edge.x, edge.y)), 2) },
                    { "clip", _lib.Get(_sys.Plan.Timeline(0).Get(0).Clip).Id },
                };
                return r;
            }
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

        /// <summary>055: 엔티티마다 원형(GK는 GK, 필드는 R1~R8 순환)·킥 원형·흔들기 — 원형 블렌드까지 포함한 애니메이션 비용 측정용.</summary>
        public static RunStyleChoice[] Styles()
        {
            var s = new RunStyleChoice[HighlightData.Entities];
            for (int e = 0; e < HighlightData.Players; e++)
            {
                bool gk = e == 0 || e == 11;
                s[e] = new RunStyleChoice(gk ? RunArchetype.GK : (RunArchetype)(1 + e % 8), gk ? KickArchetype.K8 : (KickArchetype)(1 + e % 7),
                    (e % 5) / 4f);
            }
            return s;
        }

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
