using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Hashing;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight;
using RoadToPro.Presentation.Highlight.Broadcast;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Highlight.Selection;
using RoadToPro.Presentation.Logging;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Rendering;
using RoadToPro.Presentation.Rendering.Logic;
using RoadToPro.Verification.Bench;
using Unity.Profiling;
using UnityEngine;
using RoadToPro.MatchEngine.Match.Events;
using UnityEngine.UIElements;

namespace RoadToPro.Verification.Samples.Highlight
{
    /// <summary>
    /// specs/041 E2E 호스트. 메뉴와 카메라·HUD를 소유하고 3D 무대 씬을 넣고 뺀다.
    /// 화면: highlightPlay, highlightKeyMoment, freeze.(first|goal|replayMid|afterTransition), highlightSpeed.X, highlightHud.off,
    /// highlightSkipAll, highlightWait, highlightLoop.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class HighlightScreen : MonoBehaviour, IFarmPlayback, IDeterminismProbe
    {
        const string StageScene = "Highlight3D";
        const ulong Seed = 20261109UL;
        /// <summary>specs/070 AC-11 결투 장면 경기 (DuelSceneSeedSearch로 고름 — 에디터 기준 4종 모두. 플레이어 빌드 경기는 약간 달라 테이크온 실패·슬라이딩은 대체 장면을 쓸 수 있다).</summary>
        const ulong Seed070 = 20261121UL;
        /// <summary>071 크로스·헤더·중거리 장면 경기 시드 (T12 시드 탐색 결과).</summary>
        const ulong Seed071 = 20261149UL; // 64시드 탐색: 장면 안 컷백 슛·헤더는 어느 시드에도 없음(크로스 경기당 0~2), 이 시드는 25 m 슛 2·25 m 안 슛 골 8
        /// <summary>
        /// specs/081 T19: 플레이어 빌드에서 고른 시드 — 에디터에서 찾은 시드(20261409·20261293)는 플레이어 빌드에선 다른 경기가 됐다(시뮬 차이).
        /// 플레이어 안 256시드 탐색(`FeelSceneCoverage`, 2026-11-16, 같은 빌드·플랫폼 재현): 주 장면 5종을 한 경기에 다 갖춘 시드 없음 →
        /// A = 돌파 성공·실패·스탠딩·헤딩슛·수비 장면, B = 늦은 슬라이딩, 컷백 슛 골 = 20261267(78abc53까지의 엔진에서만).
        /// </summary>
        const ulong Seed081 = 20261247UL, Seed081Slide = 20261220UL, Seed081Cutback = 20261267UL; // 2026-11-16 7b577c7 뒤 플레이어 256시드 재탐색. 컷백 슛 골은 없음(20261267은 78abc53까지의 경기)

        /// <summary>specs/082 AC-05: 점프 헤딩슛 · 늦은 슬라이딩 (082 엔진 뒤 플레이어 768시드 탐색 `tool-feel-search`).</summary>
        /// <summary>specs/083 AC-05: 놓친 공중볼 경합 점프 · 이긴 슬라이딩 접촉 (083 엔진 뒤 플레이어 768시드 탐색 `tool-feel-search`).</summary>
        const ulong Seed083 = 20261230UL, Seed083Slide = 20261230UL; // 2026-11-17 083 엔진 뒤 플레이어 768시드 탐색: 놓친 경합 + 이긴 슬라이딩 둘 다 있는 첫 시드
        const ulong Seed082 = 20261224UL, Seed082Slide = 20261192UL; // 2026-11-17 082 엔진(M-1 반영) 뒤 플레이어 768시드 탐색

        static void PlayFeelSeed(ulong seed)
        {
            if (_matchSeed != seed) { _matchSeed = seed; _match = null; }
        }

        static ulong _matchSeed = Seed;
        const int LoopCount = 10, WaitFrames = 60;
        const double TransitionWindow = 1d;
        static readonly Color MenuBackground = new Color(0.95f, 0.95f, 0.96f);

        [SerializeField] Camera cam;
        [SerializeField] UIDocument doc;
        [SerializeField] HighlightPlayer player;

        static MatchSim _match;
        static MatchTuning _tuning;

        readonly PlaybackSettings _settings = new PlaybackSettings();
        StandInHud _hud;
        LiveSceneList _live;
        LiveSceneBriefs _liveBriefs;
        HighlightFramePolicy _frames;
        readonly StandInAudio _audio = new StandInAudio();
        StandInKeyMoments _moments;
        readonly CameraSettings _cameraSettings = new CameraSettings();
        BroadcastCamera _broadcast;
        Label _menu, _fps, _framing, _keyLabel, _waitLabel, _memory, _handles, _sceneMatch;
        int _waitLeft = -1;
        ProfilerRecorder _shaders;
        double _lastFrame;
        double _frameMs;
        bool _inTransition;
        int _shaderFrame;
        int _shownFps = -1, _shownHandles = -1;
        bool _shownFramed;

        public void Configure(Camera c, UIDocument d, HighlightPlayer p)
        {
            cam = c;
            doc = d;
            player = p;
        }

        void OnEnable()
        {
            FarmContext.RegisterCamera(cam);
            FarmContext.RegisterDocument(doc);
            FarmContext.Playback = this;
            FarmContext.DeterminismProbe = this;
            var root = doc.rootVisualElement;
            _frames = new HighlightFramePolicy(new FrozenFrame(root), FarmContext.BatterySaver);
            _frames.SetCamera(cam);
            _hud = new StandInHud(root);
            _menu = StandInHud.Add(root, "menuLabel", 16f, 100f);
            _fps = StandInHud.Add(root, "fpsLabel", 16f, 128f);
            _framing = StandInHud.Add(root, "framingLabel", 16f, 156f);
            _keyLabel = StandInHud.Add(root, "keyMomentLabel", 16f, 184f);
            _waitLabel = StandInHud.Add(root, "waitLabel", 16f, 212f);
            _memory = StandInHud.Add(root, "memoryLabel", 16f, 240f);
            _handles = StandInHud.Add(root, "handlesLabel", 16f, 268f);
            _load = StandInHud.Add(root, "loadLabel", 16f, 296f);
            _load.style.opacity = 0f; // 048 AC-12 측정값일 뿐 캡처에 들어가지 않게
            _sceneMatch = StandInHud.Add(root, "sceneMatchLabel", 16f, 324f);
            _sceneMatch.style.opacity = 0f; // 058 AC-12 확인 신호 (캡처 제외)
            foreach (var l in new[] { _keyLabel, _waitLabel, _menu }) l.style.display = DisplayStyle.None;
            _waitLabel.style.opacity = 0f; // 대기 신호일 뿐 캡처 비교에 들어가지 않게

            player.PauseOnBackground = false;
            FarmContext.Screens["highlightMenu"] = ShowMenu;
            FarmContext.Screens["highlightEmpty"] = () => _frames.EnterHighlight(); // 프레임 판정 기준: 3D 없이 재생과 같은 목표 fps
            FarmContext.Screens["highlightPlay"] = () => { HighlightCastStage.NightRequested = false; Play(false); };
            FarmContext.Screens["highlightNight"] = () => { HighlightCastStage.NightRequested = true; Play(false); };
            FarmContext.Screens["highlightKeyMoment"] = () => Play(true);
            FarmContext.Screens["broadcastPlay"] = PlayBroadcast;
            RegisterLiveScreens();
            FarmContext.Screens["broadcast.freeze.closeup"] = () => player.Sequencer.SeekAndPause(0, BroadcastScenes.GoalTime + 1.2d);
            for (int i = 0; i < 3; i++)
            {
                int angle = i;
                FarmContext.Screens["broadcast.freeze.replay" + (angle + 1)] =
                    () => player.Sequencer.SeekAndPause(BroadcastScenes.FirstReplayIndex + angle, BroadcastScenes.ShotTime - 0.3d);
            }
            FarmContext.Screens["broadcast.freeze.kickoff"] = () => player.Sequencer.SeekAndPause(BroadcastScenes.TailIndex, BroadcastScenes.GoalTime + 9.5d);
            FarmContext.Screens["broadcast.mode.broadcast"] = () => SetCameraMode(CameraMode.Broadcast);
            FarmContext.Screens["broadcast.mode.top"] = () => SetCameraMode(CameraMode.TopView);
            FarmContext.Screens["broadcast.mode.mine"] = () => SetCameraMode(CameraMode.MyPlayer);
            FarmContext.Screens["broadcast.freeze.corner"] = () => player.Sequencer.SeekAndPause(BroadcastScenes.EdgeIndex, BroadcastScenes.EdgeCorner);
            FarmContext.Screens["broadcast.freeze.touchline"] = () => player.Sequencer.SeekAndPause(BroadcastScenes.EdgeIndex, BroadcastScenes.EdgeTouchline);
            FarmContext.Screens["broadcast.freeze.stands"] = () => player.Sequencer.SeekAndPause(BroadcastScenes.EdgeIndex, BroadcastScenes.EdgeStands);
            FarmContext.Screens["freeze.first"] = () => FreezeScene(0, 0.5d);
            FarmContext.Screens["freeze.goal"] = FreezeGoal;
            FarmContext.Screens["freeze.replayMid"] = FreezeReplay;
            FarmContext.Screens["freeze.afterTransition"] = () => FreezeScene(1, 0.2d); // 058: 리플레이 진입 블렌드(042 FR-14, 시뮬 0.15 s) 뒤
            foreach (var r in new[] { 1f, 1.5f, 2f, 3f })
            {
                float rate = r;
                FarmContext.Screens["highlightSpeed." + rate.ToString("0.0", CultureInfo.InvariantCulture)] = () => SetSpeed(rate);
            }
            FarmContext.Screens["highlightHud.off"] = () => _hud.SetVisible(false);
            FarmContext.Screens["highlightSkipAll"] = () => player.Sequencer.RequestSkipAll();
            FarmContext.Screens["highlightResume"] = () => player.Sequencer.RequestResume();
            FarmContext.Screens["highlightWait"] = () => { _waitLeft = WaitFrames; _waitLabel.style.display = DisplayStyle.None; };
            FarmContext.Screens["highlightLoop"] = () => StartCoroutine(EnterExitLoop());
            RegisterRenderScreens();
            FarmContext.CaptureRegions = null;
            FarmContext.PerfCounters["transitionFrameMs"] = () => _inTransition ? _frameMs : (double?)null;
            FarmContext.PerfCounters["shaderCompiles"] = () => player.IsLoaded ? _shaderFrame : (double?)null;
            FarmContext.PerfCounters["syncLoads"] = () => player.IsLoaded ? 0d : (double?)null;
            // 048 AC-17: 타임라인 구간 방송 카메라 프레임 수(0이어야 함)·구간 수·고정 이미지 표시
            FarmContext.PerfCounters["timelineCameraFrames"] = () => _frames.TimelineCameraFrames;
            FarmContext.PerfCounters["timelines"] = () => _frames.TimelineCount;
            FarmContext.PerfCounters["frozen"] = () => _frames.Frozen ? 1d : 0d;
            _shaders = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shader.CreateGPUProgram", 1);
            _lastFrame = Time.realtimeSinceStartupAsDouble;
            ShowMenu();
        }

        void OnDisable()
        {
            FarmContext.Cameras.Remove(cam);
            FarmContext.UnregisterDocument(doc);
            FarmContext.Playback = null;
            FarmContext.DeterminismProbe = null;
            FarmContext.PerfCounters.Remove("transitionFrameMs");
            FarmContext.PerfCounters.Remove("shaderCompiles");
            FarmContext.PerfCounters.Remove("syncLoads");
            FarmContext.PerfCounters.Remove("timelineCameraFrames");
            FarmContext.PerfCounters.Remove("timelines");
            FarmContext.PerfCounters.Remove("frozen");
            _frames?.Dispose();
            _shaders.Dispose();
        }

        void ShowMenu()
        {
            _menu.text = "menu";
            _menu.style.display = DisplayStyle.Flex;
            _hud.SetVisible(false);
            _framing.text = "";
            _keyLabel.style.display = DisplayStyle.None;
            ShowCounter(_fps, ref _shownFps, _frames.TargetFps);
        }

        static string DataDir => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(FarmContext.Args.ScenarioPath)), ".."));

        static MatchSim Match()
        {
            if (_match != null) return _match;
            var seed = _matchSeed;
            var work = new DecisionBenchWorkloads(DataDir); // specs/010 T16: 전술(009)·GK(011)·판단(010) 경로 — 이전엔 006 대체 단계(전원 전력질주)였다
            _tuning = LiveMatchFixture.Tuning(work.Tuning, DataDir); // 058: E2E 경기만 저득점
            // specs/058: 실경기 하이라이트 고정 경기 (퇴장 주입 — 파울 장면 대체, 내 선수 지정)
            _match = new MatchSim(LiveMatchFixture.Prepare(MatchBenchWorkloads.Setup(seed)), _tuning, LiveMatchFixture.Options());
            _match.RunToEnd();
            return _match;
        }

        void Play(bool keyMoment)
        {
            _menu.style.display = DisplayStyle.None;
            _keyLabel.style.display = DisplayStyle.None;
            _hud.SetVisible(true);
            var sim = Match();
            // specs/058 FR-20·40: 이벤트 기반 장면 선정 + 042 중계 카메라 (041 대체 카메라·순간이동 자르기 제거)
            var hl = RoadToPro.Core.Tuning.TuningBundle.Defaults(RoadToPro.Core.Tuning.TuningLoader.LoadDefinitionsFile(Path.Combine(DataDir, "tuning", "highlight.json")));
            _live = LiveSceneSelector.Select(sim.Result.EventLog, sim.Result.EndTick, PitchSpec.Default, LiveMatchFixture.MyPlayerId, SceneScoreTable.From(hl));
            var scenes = _live.Scenes;
            {
                // 081: 장면 목록 (역할·구간) — 배치 원인 경고 추적
                var sb = new System.Text.StringBuilder($"[highlight] 장면 목록 시드 {_matchSeed}:");
                foreach (var sc in scenes) sb.Append($" {sc.Id}:{(_live.Info.TryGetValue(sc.Id, out var inf) ? inf.Role.ToString() : "?")}[{sc.Start:0.0}-{sc.End:0.0}]");
                Debug.Log(sb.ToString());
            }
            if (keyMoment && scenes.Length > 0)
            {
                var f = scenes[0];
                var info = _live.Info[f.Id];
                scenes[0] = new HighlightScene(f.Id, f.Start, f.End, f.IsReplay, true, System.Math.Max(f.Start, info.EventTime - 2d), f.Priority, f.GroupId);
            }
            _liveBriefs = new LiveSceneBriefs(_live, (t, id) => SlotAt(sim, t, id), LiveMatchFixture.MyPlayerId, PitchSpec.Default);
            var registry = RoadToPro.Core.Tuning.TuningLoader.LoadDefinitionsFile(Path.Combine(DataDir, "tuning", "camera.json"));
            var tuning = CameraTuning.From(RoadToPro.Core.Tuning.TuningBundle.Defaults(registry));
            CameraRigData Rig() => CameraRigData.From(((HighlightCastStage)player.Assets.Stage).Layout, m => Debug.LogWarning("[broadcast] " + m));
            _broadcast = new BroadcastCamera(cam, _liveBriefs.Brief, Rig, tuning, _cameraSettings);
            Begin(scenes, sim.ExtractClip, _broadcast, _broadcast.OnSceneBuilt);
        }

        static int SlotAt(MatchSim sim, long tick, int playerId)
        {
            var f = sim.ExtractClip(tick, tick + 1).Frames[0];
            for (int s = 0; s < Squad.Slots; s++) if (f.PlayerIds[s] == playerId) return s;
            return -1;
        }

        /// <summary>specs/042: 합성 골 장면을 실제 중계 카메라로 재생한다.</summary>
        void PlayBroadcast()
        {
            _menu.style.display = DisplayStyle.None;
            _keyLabel.style.display = DisplayStyle.None;
            _hud.SetVisible(true);
            Match(); // 튜닝과 공 파라미터
            var registry = RoadToPro.Core.Tuning.TuningLoader.LoadDefinitionsFile(Path.Combine(DataDir, "tuning", "camera.json"));
            var tuning = CameraTuning.From(RoadToPro.Core.Tuning.TuningBundle.Defaults(registry));
            CameraRigData Rig() => CameraRigData.From(((HighlightCastStage)player.Assets.Stage).Layout, m => Debug.LogWarning("[broadcast] " + m));
            _live = null;
            _liveBriefs = null;
            _broadcast = new BroadcastCamera(cam, BroadcastScenes.Brief, Rig, tuning, _cameraSettings);
            Begin(BroadcastScenes.Scenes(), BroadcastScenes.Extract, _broadcast, _broadcast.OnSceneBuilt);
        }

        // ── specs/058 실경기 장면 고정 화면 ──

        Label _halves;

        void RegisterLiveScreens()
        {
            FarmContext.Screens["livePlay"] = () => { HighlightCastStage.NightRequested = false; Play(false); };
            FarmContext.Screens["livePlay070"] = () => { HighlightCastStage.NightRequested = false; if (_matchSeed != Seed070) { _matchSeed = Seed070; _match = null; } Play(false); }; // 070 결투 장면 경기
            FarmContext.Screens["live.freeze.net"] = () => FreezeGoal(0, b => double.IsNaN(b.NetContactTime) ? b.EventTime + 0.3d : b.NetContactTime + 0.1d);
            FarmContext.Screens["live.freeze.closeup"] = () => FreezeGoal(0, b => CloseupStart(b) + 1.0d);
            FarmContext.Screens["live.freeze.celebration"] = () => FreezeGoal(0, b => CloseupStart(b) + 2.5d + 1.5d);
            FarmContext.Screens["live.freeze.replay1"] = () => FreezeGroup(SceneRole.ReplayAngle, s => (s.Start + s.End) * 0.5d);
            FarmContext.Screens["live.freeze.kickoff"] = () => FreezeGroup(SceneRole.GoalTail, s => s.End - 0.8d);
            FarmContext.Screens["live.freeze.conceded"] = () => FreezeGoal(1, b => b.EventTime - 0.5d);
            FarmContext.Screens["live.freeze.woodwork"] = () => FreezeKind(i => i.Kind == LiveSceneKind.Woodwork || i.Kind == LiveSceneKind.Save, i => i.Kind == LiveSceneKind.ShotInBox);
            FarmContext.Screens["live.freeze.defensive"] = () => FreezeKind(i => i.Kind == LiveSceneKind.Defensive, null, 0d, "defensive"); // 장면 종류를 메시지에 (072 보류 무시 규칙이 이 종류만 맞춘다)
            // 011 AC-25: GK 장면 — 다이빙 선방(접촉 0.1 s 전), 펀칭(없으면 다른 선방), 실점(공이 골라인을 넘기 직전)
            FarmContext.Screens["live.freeze.gkdive"] = () => FreezeKind(i => i.Kind == LiveSceneKind.Save && (i.EventKind == SimEventKind.Parry || i.EventKind == SimEventKind.Catch), i => i.Kind == LiveSceneKind.Save, -0.1d);
            FarmContext.Screens["live.freeze.gkpunch"] = () => FreezeKind(i => i.Kind == LiveSceneKind.Save && i.EventKind == SimEventKind.Punch, i => i.Kind == LiveSceneKind.Save && i.EventKind == SimEventKind.Parry, 0d);
            FarmContext.Screens["live.freeze.gkconceded"] = () => FreezeGoal(0, b => b.EventTime - 0.05d);
            FarmContext.Screens["live.freeze.foul"] = () => FreezeKind(i => i.Kind == LiveSceneKind.RedCard || i.Kind == LiveSceneKind.YellowCard || i.Kind == LiveSceneKind.Foul, null);
            // specs/070 AC-11: 결투 장면 — 선정된 장면 구간 안의 테이크온·태클 이벤트 순간 (경기 이벤트 로그 A·B 비트)
            FarmContext.Screens["live.freeze.takeon_ok"] = () => FreezeEvent(e => e.Kind == SimEventKind.Dribble && (e.B & EventCodes.DribbleSuccess) != 0, null, "takeon_ok");
            FarmContext.Screens["live.freeze.takeon_lost"] = () => FreezeEvent(e => e.Kind == SimEventKind.Dribble && (e.B & EventCodes.DribbleSuccess) == 0,
                e => e.Kind == SimEventKind.Tackle && (e.A & EventCodes.TackleWon) != 0 && (e.A & EventCodes.TackleSliding) == 0, "takeon_lost"); // 대체: 공을 따낸 태클(탈취)
            FarmContext.Screens["live.freeze.tackle_stand"] = () => FreezeEvent(e => e.Kind == SimEventKind.Tackle && (e.A & EventCodes.TackleSliding) == 0 && (e.A & EventCodes.TackleWon) != 0,
                e => e.Kind == SimEventKind.Tackle && (e.A & EventCodes.TackleSliding) == 0, "tackle_stand");
            FarmContext.Screens["live.freeze.tackle_slide"] = () => FreezeEvent(e => e.Kind == SimEventKind.Tackle && (e.A & EventCodes.TackleSliding) != 0 && (e.A & EventCodes.TackleLate) != 0,
                e => e.Kind == SimEventKind.Tackle && ((e.A & EventCodes.TackleSliding) != 0 || (e.A & EventCodes.TackleWon) == 0), "tackle_slide"); // 대체: 슬라이딩 또는 빗나간 태클
            // specs/071 AC-12: 컷백 슛 골·크로스 공중볼 헤더·30 m 중거리 슛 (슛 이벤트 순간 — 공이 발·머리를 떠난 직후)
            FarmContext.Screens["livePlay071"] = () => { HighlightCastStage.NightRequested = false; if (_matchSeed != Seed071) { _matchSeed = Seed071; _match = null; } Play(false); };
            FarmContext.Screens["live.freeze.cutback_goal"] = () => FreezeEvent(e => e.Kind == SimEventKind.Shot && FollowsCutback(e) && ScoredAfter(e),
                e => e.Kind == SimEventKind.Shot && e.Part != BodyPart.Head && ShotDistance(e) < 25f && ScoredAfter(e), "cutback_goal"); // 대체: 25 m 안 슛 골 (중거리 장면과 겹치지 않게) (실경기 크로스 0~1개 — 072·010 몫, 081)
            FarmContext.Screens["live.freeze.header"] = () => FreezeEvent(e => e.Kind == SimEventKind.Shot && e.Part == BodyPart.Head,
                e => e.Kind == SimEventKind.Header, "header");
            FarmContext.Screens["live.freeze.longshot"] = () => FreezeEvent(e => e.Kind == SimEventKind.Shot && e.Part != BodyPart.Head && ShotDistance(e) >= 25f,
                e => e.Kind == SimEventKind.Shot && ShotDistance(e) >= 20f, "longshot");
            // specs/081 AC-07: 대체 장면 없이 (fallback 없음) — 없으면 에러 로그로 실패
            FarmContext.Screens["tool.feelSearch"] = () => StartCoroutine(FeelSearchLog()); // 081 도구: 플레이어 안 시드 탐색 (결과는 로그)
            FarmContext.Screens["livePlay081"] = () => { HighlightCastStage.NightRequested = false; PlayFeelSeed(Seed081); Play(false); };
            FarmContext.Screens["livePlay081slide"] = () => { HighlightCastStage.NightRequested = false; PlayFeelSeed(Seed081Slide); Play(false); };
            FarmContext.Screens["livePlay082"] = () => { HighlightCastStage.NightRequested = false; PlayFeelSeed(Seed082); Play(false); };
            FarmContext.Screens["livePlay082slide"] = () => { HighlightCastStage.NightRequested = false; PlayFeelSeed(Seed082Slide); Play(false); };
            FarmContext.Screens["livePlay083"] = () => { HighlightCastStage.NightRequested = false; PlayFeelSeed(Seed083); Play(false); };
            FarmContext.Screens["livePlay083slide"] = () => { HighlightCastStage.NightRequested = false; PlayFeelSeed(Seed083Slide); Play(false); };
            FarmContext.Screens["live.freeze083.missed"] = () => FreezeMissedAerial("missed_jump"); // 놓친 경합 기록 정점
            FarmContext.Screens["live.freeze083.slide"] = () => FreezeEvent(e => FeelSceneCoverage.IsSlideWin(e), null, "slide_win", 0d); // 이긴 슬라이딩 접촉 순간
            FarmContext.Screens["live.freeze082.header"] = () => { FreezeEvent(e => FeelSceneCoverage.IsJumpHeader(e), null, "header_jump", 0d); StartCoroutine(LogHeadBall()); }; // 접촉 순간
            FarmContext.Screens["live.freeze082.tackle_slide"] = () => FreezeEvent(e => e.Kind == SimEventKind.Tackle && (e.A & EventCodes.TackleSliding) != 0 && (e.A & EventCodes.TackleLate) != 0, null, "tackle_slide", 0.3d); // 접촉 + 0.3 s: 누운 자세
            FarmContext.Screens["livePlay081cutback"] = () => { HighlightCastStage.NightRequested = false; PlayFeelSeed(Seed081Cutback); Play(false); };
            FarmContext.Screens["live.freeze081.cutback_goal"] = () => FreezeEvent(e => e.Kind == SimEventKind.Shot && FollowsCutback(e) && ScoredAfter(e), null, "cutback_goal");
            FarmContext.Screens["live.freeze081.takeon_ok"] = () => FreezeEvent(e => e.Kind == SimEventKind.Dribble && (e.B & EventCodes.DribbleSuccess) != 0, null, "takeon_ok");
            FarmContext.Screens["live.freeze081.takeon_lost"] = () => FreezeEvent(e => e.Kind == SimEventKind.Dribble && (e.B & EventCodes.DribbleSuccess) == 0, null, "takeon_lost");
            FarmContext.Screens["live.freeze081.tackle_stand"] = () => FreezeEvent(e => e.Kind == SimEventKind.Tackle && (e.A & EventCodes.TackleSliding) == 0 && (e.A & EventCodes.TackleWon) != 0, null, "tackle_stand");
            FarmContext.Screens["live.freeze081.tackle_slide"] = () => FreezeEvent(e => e.Kind == SimEventKind.Tackle && (e.A & EventCodes.TackleSliding) != 0 && (e.A & EventCodes.TackleLate) != 0, null, "tackle_slide");
            FarmContext.Screens["live.freeze081.header"] = () => { FreezeEvent(e => e.Kind == SimEventKind.Shot && e.Part == BodyPart.Head, null, "header", 0d); StartCoroutine(LogHeadBall()); }; // 접촉 순간 (머리–공 거리)
            FarmContext.Screens["live.freeze081.defensive"] = () => FreezeKind(i => i.Kind == LiveSceneKind.Defensive, null, 0d, "defensive");
            _halves = StandInHud.Add(doc.rootVisualElement, "halvesLabel", 16f, 352f);
            _halves.style.opacity = 0f; // 058 AC-11 확인 신호 (캡처 제외)
        }

        /// <summary>081 도구: 플레이어 빌드 안 768시드 탐색 (백그라운드 스레드 — 메인 스레드를 막으면 farm heartbeat가 멈춘다). 종류별 첫 시드를 로그로.</summary>
        System.Collections.IEnumerator FeelSearchLog()
        {
            int[] masks = null;
            var dir = DataDir;
            var task = System.Threading.Tasks.Task.Run(() => masks = FeelSceneCoverage.Search(dir, 768));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) { Debug.LogError($"[highlight] 081 시드 탐색 실패: {task.Exception}"); yield break; }
            FeelSceneCoverage.Choose(masks, out var best, out var slide);
            int head = -1, cut = -1, jump = -1;
            for (int i = 0; i < masks.Length; i++)
            {
                if (jump < 0 && (masks[i] & FeelSceneCoverage.HeaderJumpShot) != 0) jump = i;
                if (head < 0 && (masks[i] & FeelSceneCoverage.HeaderShot) != 0) head = i;
                if (cut < 0 && (masks[i] & FeelSceneCoverage.CutbackGoal) != 0) cut = i;
            }
            string S(int i) => i < 0 ? "없음" : $"{FeelSceneCoverage.FirstSeed + (ulong)i} [{FeelSceneCoverage.Describe(masks[i])}]";
            int missed = -1, slideWin = -1, both = -1;
            for (int i = 0; i < masks.Length; i++)
            {
                if (missed < 0 && (masks[i] & FeelSceneCoverage.MissedAerial) != 0) missed = i;
                if (slideWin < 0 && (masks[i] & FeelSceneCoverage.SlideWin) != 0) slideWin = i;
                if (both < 0 && (masks[i] & FeelSceneCoverage.MissedAerial) != 0 && (masks[i] & FeelSceneCoverage.SlideWin) != 0) both = i;
            }
            // 084 T13 (AC-04): 컷백 슛 골 시드 수 — 앞 256·512·768시드 (FR-05 하한 판정)
            int c256 = 0, c512 = 0, c768 = 0;
            var cutSeeds = new System.Text.StringBuilder();
            for (int i = 0; i < masks.Length; i++)
            {
                if ((masks[i] & FeelSceneCoverage.CutbackGoal) == 0) continue;
                if (i < 256) c256++;
                if (i < 512) c512++;
                c768++;
                cutSeeds.Append(FeelSceneCoverage.FirstSeed + (ulong)i).Append(' ');
            }
            Debug.Log($"[highlight] 084 컷백 슛 골: 256시드 {c256} · 512시드 {c512} · {masks.Length}시드 {c768} — {cutSeeds}");
            Debug.Log($"[highlight] 081 탐색: A {S(best)} · 늦은 슬라이딩 {S(slide)} · 헤딩슛 {S(head)} · 컷백 슛 골 {S(cut)} · 082 점프 헤딩슛 {S(jump)} · 083 놓친 경합 {S(missed)} · 이긴 슬라이딩 {S(slideWin)} · 둘 다 {S(both)}");
        }

        /// <summary>081 AC-07: 헤더 접촉 프레임에서 공과 가장 가까운 머리 뼈까지 거리 (054 접촉 동기화 허용치와 비교 — 로그만).</summary>
        System.Collections.IEnumerator LogHeadBall()
        {
            for (int i = 0; i < 3; i++) yield return null; // 정지 프레임이 그려진 뒤
            var ball = GameObject.Find("Ball");
            if (ball == null) { Debug.LogWarning("[highlight] 헤더 머리–공 거리: 공 오브젝트 없음"); yield break; }
            var best = float.MaxValue;
            foreach (var r in Object.FindObjectsByType<RoadToPro.Presentation.Players.PlayerRig>(FindObjectsSortMode.None))
            {
                var h = r.Bone("Head");
                if (h == null) continue;
                var d = Vector3.Distance(h.position, ball.transform.position);
                if (d < best) best = d;
            }
            Debug.Log($"[highlight] 081 헤더 접촉 프레임 머리–공 거리 {best:0.000} m");
        }

        static double CloseupStart(SceneBrief b)
        {
            var t = new CameraTuning();
            double hold = double.IsNaN(b.NetContactTime) ? b.EventTime + t.NoNetHoldSeconds : b.NetContactTime + t.NetHoldSeconds;
            return System.Math.Min(hold, b.EventTime + t.GoalHoldMaxSeconds);
        }

        /// <summary>which = 0: 첫 골 메인, 1: 첫 골과 다른 팀의 첫 골 메인(실점 장면).</summary>
        void FreezeGoal(int which, System.Func<SceneBrief, double> at)
        {
            int firstTeam = -1;
            var scenes = player.Scenes;
            for (int i = 0; i < scenes.Count; i++)
            {
                var info = _liveBriefs?.InfoOf(scenes[i].Scene.Id);
                if (info == null || info.Role != SceneRole.GoalMain) continue;
                if (firstTeam < 0) firstTeam = info.Team;
                if (which == 1 && info.Team == firstTeam) continue;
                var b = _liveBriefs.Brief(scenes[i].Scene.Id);
                player.Sequencer.SeekAndPause(i, System.Math.Min(System.Math.Max(at(b), scenes[i].Scene.Start), scenes[i].Scene.End));
                return;
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < scenes.Count; i++) { var inf = _liveBriefs?.InfoOf(scenes[i].Scene.Id); if (inf != null) sb.Append($" {scenes[i].Scene.Id}:{inf.Role}/{inf.Kind}/t{inf.Team}"); }
            Debug.LogError($"[highlight] 058 골 장면 없음 ({which}) 장면:{sb}");
        }

        /// <summary>첫 골 묶음 안에서 역할이 role인 첫 장면.</summary>
        void FreezeGroup(SceneRole role, System.Func<HighlightScene, double> at)
        {
            var scenes = player.Scenes;
            int group = -1;
            for (int i = 0; i < scenes.Count; i++)
            {
                var info = _liveBriefs?.InfoOf(scenes[i].Scene.Id);
                if (info == null) continue;
                if (group < 0 && info.Role == SceneRole.GoalMain) group = scenes[i].Scene.Id;
                if (group >= 0 && scenes[i].Scene.GroupId == group && info.Role == role && info.AngleIndex == 0)
                {
                    player.Sequencer.SeekAndPause(i, at(scenes[i].Scene));
                    if (role == SceneRole.GoalTail) _checkHalvesAt = at(scenes[i].Scene);
                    return;
                }
            }
            Debug.LogError($"[highlight] 058 골 묶음에 {role} 없음");
        }

        void FreezeKind(System.Func<LiveSceneInfo, bool> want, System.Func<LiveSceneInfo, bool> fallback, double offset = 0d, string label = null)
        {
            var scenes = player.Scenes;
            for (int pass = 0; pass < 2; pass++)
            {
                var f = pass == 0 ? want : fallback;
                if (f == null) break;
                for (int i = 0; i < scenes.Count; i++)
                {
                    var info = _liveBriefs?.InfoOf(scenes[i].Scene.Id);
                    if (info == null || info.Role != SceneRole.Live || !f(info)) continue;
                    var s = scenes[i].Scene;
                    player.Sequencer.SeekAndPause(i, System.Math.Min(System.Math.Max(info.EventTime + offset, s.Start), s.End));
                    if (pass == 1) Debug.Log($"[highlight] 대체 장면 사용 ({info.EventKind})");
                    return;
                }
            }
            Debug.LogError(label == null ? "[highlight] 058 해당 종류 장면 없음" : $"[highlight] 058 해당 종류 장면 없음 ({label})");
        }

        /// <summary>
        /// specs/070: 경기 이벤트 로그에서 조건에 맞고 선정된 (리플레이 아닌) 장면 구간 안에 있는 첫 이벤트 순간(+0.05 s)에 멈춘다.
        /// 장면 클립은 순서대로 만들어지므로 이벤트 로그(A·B 비트 포함)로 찾는다. 없으면 대체 조건, 그것도 없으면 오류 로그.
        /// </summary>
        /// <summary>071: 슛 직전 10초 안에 같은 팀 컷백 크로스(Cross A 타입 = Cutback + 1)가 있었나.</summary>
        bool FollowsCutback(SimEvent shot)
        {
            var log = Match().Result.EventLog;
            for (int k = 0; k < log.Length; k++)
            {
                var e = log[k];
                if (e.Tick > shot.Tick) break;
                if (e.Kind == SimEventKind.Cross && e.Team == shot.Team && shot.Tick - e.Tick <= 100
                    && (e.A >> RoadToPro.MatchEngine.Actions.ActionSystem.CrossTypeShift) == (int)RoadToPro.MatchEngine.Actions.CrossType.Cutback + 1) return true;
            }
            return false;
        }

        bool ScoredAfter(SimEvent shot)
        {
            var log = Match().Result.EventLog;
            for (int k = 0; k < log.Length; k++)
            {
                var e = log[k];
                if (e.Tick < shot.Tick) continue;
                if (e.Tick > shot.Tick + 50) break;
                if (e.Kind == SimEventKind.Goal && e.Team == shot.Team) return true;
            }
            return false;
        }

        /// <summary>슛 위치(이벤트 X·Y는 0~1 정규화)에서 가까운 골문 중앙까지 거리 (m).</summary>
        static float ShotDistance(SimEvent e)
        {
            var pitch = PitchSpec.Default;
            double x = e.X * pitch.LengthM, y = e.Y * pitch.WidthM, cy = pitch.WidthM * 0.5;
            var a = System.Math.Sqrt(x * x + (y - cy) * (y - cy));
            var b = System.Math.Sqrt((x - pitch.LengthM) * (x - pitch.LengthM) + (y - cy) * (y - cy));
            return (float)System.Math.Min(a, b);
        }

        /// <summary>
        /// 083 AC-05: 놓친 공중볼 경합의 승자 기록 정점 시각에 정지 (점프 정점 — 두 선수가 뛰어 있고 공은 머리에 닿지 않는다).
        /// 장면 동작 계획에 점프가 들어간 참여자가 많고, 기록 점프 높이가 <see cref="MissedJumpShotMinJump"/> 이상이고(낮으면 0.10 m로 잘려 점프로 안 읽힌다), 정점 순간 공이 머리 높이 이상이고(공이 이미 무릎 높이면 경합으로 안 읽힌다, 2026-11-17 육안 검토),
        /// 그중 공에 가까운 경합을 고른다(공과 멀면 점프가 경합으로 안 읽힌다). 조건을 다 채우는 경합이 없으면 점수가 가장 좋은 경합.
        /// </summary>
        const float MissedJumpShotMinJump = 0.3f, MissedJumpShotMinBallY = 1.8f;

        void FreezeMissedAerial(string label)
        {
            var scenes = player.Scenes;
            var sim = Match();
            var log = sim.Result.EventLog;
            var rec = sim.Context.Actions.Details.Aerials;
            int bestScene = -1;
            double bestT = 0d;
            float bestScore = float.MaxValue;
            long bestSeq = -1;
            for (int k = 0; k < log.Length; k++)
            {
                var e = log[k];
                if (!FeelSceneCoverage.IsMissedAerial(e) || !rec.IsAvailable(e.B - 1)) continue;
                int first = e.B - 1, n = rec[first].GroupCount;
                double apex = double.NaN;
                for (int i = first; i < first + n && rec.IsAvailable(i); i++) if (rec[i].Won) apex = rec[i].ApexSubstep * 0.01d;
                if (double.IsNaN(apex)) continue;
                for (int i = 0; i < scenes.Count; i++)
                {
                    var s = scenes[i].Scene;
                    if (s.IsReplay || apex < s.Start || apex > s.End) continue;
                    float score = 1000f;
                    var d = scenes[i].Data;
                    var plan = scenes[i].Plan;
                    if (d != null && plan != null)
                    {
                        int planned = 0;
                        float near = float.MaxValue, low = float.MaxValue;
                        float t = (float)apex;
                        d.FrameAt(t, out var f, out var a);
                        var ball = d.Ball[f] + (d.Ball[System.Math.Min(f + 1, d.Frames - 1)] - d.Ball[f]) * a;
                        foreach (var j in d.AerialJumps)
                        {
                            if (j.Seq != e.Seq || j.Skip != null) continue;
                            var tl = plan.Timeline(j.Entity);
                            for (int q = 0; q < tl.Count; q++)
                                if (tl.SeqOf(q) == MissedJumpMotion.SeqOf(e.Seq))
                                {
                                    planned++;
                                    low = System.Math.Min(low, j.JumpHeight);
                                    var p = d.Pos(j.Entity, t);
                                    near = System.Math.Min(near, (float)System.Math.Sqrt((p.X - ball.X) * (p.X - ball.X) + (p.Z - ball.Z) * (p.Z - ball.Z)));
                                }
                        }
                        if (planned > 0) score = (2 - System.Math.Min(planned, 2)) * 100f + (low < MissedJumpShotMinJump ? 50f : 0f) + (ball.Y < MissedJumpShotMinBallY ? 50f : 0f) + near;
                    }
                    if (score < bestScore) { bestScore = score; bestScene = i; bestT = apex; bestSeq = e.Seq; }
                    break;
                }
            }
            if (bestScene < 0) { Debug.LogError($"[highlight] 083 놓친 경합 장면 없음 ({label}) 시드 {_matchSeed}"); return; }
            player.Sequencer.SeekAndPause(bestScene, bestT);
            Debug.Log($"[highlight] 결투·슈팅 장면 {label}: 장면 {scenes[bestScene].Scene.Id} t={bestT:0.00} (경합 이벤트 {bestSeq}, 점수 {bestScore:0.00} — 50 이상이면 점프 높이·공 높이 조건 미달)");
        }

        void FreezeEvent(System.Func<SimEvent, bool> want, System.Func<SimEvent, bool> fallback, string label, double offset = 0.05d)
        {
            var scenes = player.Scenes;
            var log = Match().Result.EventLog;
            for (int pass = 0; pass < 2; pass++)
            {
                var f = pass == 0 ? want : fallback;
                if (f == null) break;
                for (int k = 0; k < log.Length; k++)
                {
                    var e = log[k];
                    if (!f(e)) continue;
                    var t = e.Tick * 0.1d + System.Math.Max((int)e.Substep, 0) * 0.01d;
                    for (int i = 0; i < scenes.Count; i++)
                    {
                        var s = scenes[i].Scene;
                        if (s.IsReplay || t < s.Start || t > s.End) continue;
                        player.Sequencer.SeekAndPause(i, System.Math.Min(t + offset, s.End));
                        Debug.Log(pass == 0 ? $"[highlight] 결투·슈팅 장면 {label}: 장면 {s.Id} t={t:0.00}" : $"[highlight] 결투·슈팅 대체 장면 사용 ({label}): 장면 {s.Id}");
                        return;
                    }
                }
            }
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < log.Length; k++) if (log[k].Kind == SimEventKind.Dribble) sb.Append($" D@{log[k].Tick * 0.1d:0.0}b{log[k].B}");
            sb.Append(" | 장면:");
            for (int i = 0; i < scenes.Count; i++) sb.Append($" {scenes[i].Scene.Start:0}-{scenes[i].Scene.End:0}{(scenes[i].Scene.IsReplay ? "r" : "")}");
            Debug.LogError($"[highlight] 070 해당 이벤트 장면 없음 ({label}) 시드 {_matchSeed}{sb}");
        }

        double _checkHalvesAt = double.NaN;

        /// <summary>058 AC-11: 킥오프 와이드에서 양 팀 필드 선수가 각자 진영(키커는 센터 1 m 허용).</summary>
        void CheckHalves()
        {
            if (double.IsNaN(_checkHalvesAt) || player.Current == null) return;
            var d = player.Current.Data;
            float t = (float)_checkHalvesAt;
            int[] side = { 0, 0 };
            bool ok = true;
            for (int team = 0; team < 2 && ok; team++)
                for (int e = team * 11; e < team * 11 + 11; e++)
                {
                    var p = d.Pos(e, t);
                    if (System.Math.Abs(p.X) < 1f) continue;
                    int sgn = p.X > 0 ? 1 : -1;
                    if (side[team] == 0) side[team] = sgn;
                    else if (side[team] != sgn) { ok = false; break; }
                }
            ok &= side[0] != 0 && side[0] == -side[1];
            var text = ok ? "ok" : "bad";
            if (_halves.text != text) _halves.text = text;
        }

        void SetCameraMode(CameraMode mode)
        {
            _cameraSettings.Mode = mode;
            _broadcast.Rebuild(player.Scenes);
            player.Sequencer.SeekAndPause(0, BroadcastScenes.GoalTime - 3d);
        }

        void Begin(HighlightScene[] scenes, System.Func<long, long, RoadToPro.MatchEngine.Match.Replay.ReplayClip> extract, IHighlightCamera camera,
            System.Action<BuiltScene> onBuilt)
        {
            var stageHolder = new StageBinding();
            AnimPlan BuildPlan(HighlightData d)
            {
                stageHolder.Resolve(player);
                return AnimPlan.Build(stageHolder.Stage.Library, stageHolder.Stage.Map, d, new UnityLogger(RoadToPro.Core.Logging.LogLevel.Warning), null, stageHolder.Stage.Scales);
            }
            var builder = new SceneClipBuilder(scenes, extract, BuildPlan, PitchSpec.Default, new StandInDiagnostics(), onBuilt);
            _moments = new StandInKeyMoments();
            var ball = BallParams.Create(_tuning.Ball, SurfaceCondition.Dry, PitchSpec.Default);
            var goal = new GoalBox(PitchSpec.Default, BallParams.GoalWidth, BallParams.GoalHeight, ball.NetDepth, ball.Radius);
            var ports = new HighlightPlayer.Ports { KeyMoments = _moments, Hud = _hud, Camera = camera, Frames = _frames, Audio = _audio };
            _groups = null;
            _loadStart = Time.realtimeSinceStartupAsDouble;
            player.Begin(scenes, builder, new HighlightAssets(StageScene), cam, goal, _settings, TimelineGap(), ports);
        }

        static double TimelineGap()
        {
            var registry = RoadToPro.Core.Tuning.TuningLoader.LoadDefinitionsFile(Path.Combine(DataDir, "tuning", "highlight.json"));
            return RoadToPro.Core.Tuning.TuningBundle.Defaults(registry).Get("highlight.timelineGapSeconds");
        }

        sealed class StageBinding
        {
            public HighlightCastStage Stage;
            public void Resolve(HighlightPlayer p)
            {
                if (Stage == null) Stage = (HighlightCastStage)p.Assets.Stage;
            }
        }

        void FreezeScene(int index, double offset)
        {
            var s = player.Scenes[index].Scene;
            player.Sequencer.SeekAndPause(index, s.Start + offset);
        }

        void FreezeGoal()
        {
            var scenes = player.Scenes;
            for (int i = 0; i < scenes.Count; i++)
                if (scenes[i].Data.Events.Exists(e => e.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Goal) && !scenes[i].Scene.IsReplay)
                {
                    var goal = scenes[i].Data.Events.Find(e => e.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Goal);
                    player.Sequencer.SeekAndPause(i, System.Math.Min(goal.Time, scenes[i].Scene.End));
                    return;
                }
            Debug.LogError("[highlight] 골 장면이 없다");
        }

        void FreezeReplay()
        {
            var scenes = player.Scenes;
            for (int i = 0; i < scenes.Count; i++)
                if (scenes[i].Scene.IsReplay)
                {
                    player.Sequencer.SeekAndPause(i, (scenes[i].Scene.Start + scenes[i].Scene.End) * 0.5d);
                    return;
                }
            Debug.LogError("[highlight] 리플레이 장면이 없다");
        }

        public void SetSpeed(float speed) => player.Sequencer.RequestRate(speed);

        public double SimTimeSeconds => player.Sequencer != null ? player.Sequencer.Clock.Position : 0d;
        public int CurrentScene => player.Sequencer != null && player.IsLoaded ? player.Sequencer.SceneIndex : -1;

        public bool SeekAndFreeze(double? simTimeSeconds, int? scene)
        {
            if (!player.IsLoaded) return false;
            int index = scene ?? player.Sequencer.SceneIndex;
            player.Sequencer.SeekAndPause(index, simTimeSeconds ?? player.Scenes[index].Scene.Start);
            return true;
        }

        public IReadOnlyList<FarmCheckpoint> Checkpoints
        {
            get
            {
                var list = new List<FarmCheckpoint>();
                for (int i = 0; i < player.Scenes.Count; i++)
                {
                    var built = player.Scenes[i];
                    var h = StableHasher.Create();
                    h.Add(built.Data.Hash());
                    for (double t = built.Scene.Start; t < built.Scene.End; t += 1d / 60d)
                    {
                        var b = built.Plan.BallVisual((float)t);
                        h.Add(b.X);
                        h.Add(b.Y);
                        h.Add(b.Z);
                    }
                    list.Add(new FarmCheckpoint("scene" + i, (long)(built.Scene.Start * 10d), h.Hex));
                }
                return list;
            }
        }

        IEnumerator EnterExitLoop()
        {
            Match();
            yield return Resources.UnloadUnusedAssets();
            System.GC.Collect();
            yield return null;
            long before = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            for (int i = 0; i < LoopCount; i++)
            {
                Play(false);
                while (!player.IsLoaded) yield return null;
                player.Sequencer.RequestSkipAll();
                yield return null;
                while (!player.IsUnloaded) yield return null;
            }
            System.GC.Collect();
            yield return Resources.UnloadUnusedAssets();
            long grown = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - before;
            _memory.text = (grown / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture);
            Debug.Log($"[highlight] 진입·언로드 {LoopCount}회 뒤 메모리 증가 {_memory.text} MB");
            _waitLabel.text = "looped";
            _waitLabel.style.display = DisplayStyle.Flex;
        }

        // ── specs/048 렌더 검증 화면 ──────────────────────────────────────────────
        // preset.*: 재생 시작 전에만 (P1은 재생 중 프리셋 변경 없음, FR-03 후속). renderScale.*: URP 렌더 스케일 직접 (AC-06).
        // battery.on/off: 배터리 절약 토글 (AC-11). highlightExit: 모두 건너뛰고 언로드까지 (waitLabel "unloaded").
        // hide.<시스템>/show.all: 절제 측정 (AC-16).
        RenderGroups _groups;
        Label _load;
        double _loadStart = -1d;

        void RegisterRenderScreens()
        {
            foreach (var id in new[] { "Low", "Medium", "High" })
            {
                string name = id;
                FarmContext.Screens["preset." + name] = () => SetPreset(name);
            }
            foreach (var sc in new[] { 1.0f, 0.75f, 0.67f })
            {
                float scale = sc;
                FarmContext.Screens["renderScale." + scale.ToString("0.00", CultureInfo.InvariantCulture)] = () => SetRenderScale(scale);
            }
            FarmContext.Screens["battery.on"] = () => _frames.SetBatterySaver(true);
            FarmContext.Screens["battery.off"] = () => _frames.SetBatterySaver(false);
            FarmContext.Screens["highlightExit"] = () => StartCoroutine(Exit());
            foreach (var g in new[] { FrameBudget.Pitch, FrameBudget.Structure, FrameBudget.Players, FrameBudget.Props, FrameBudget.Post, FrameBudget.Hud })
            {
                string group = g;
                FarmContext.Screens["hide." + group] = () => HideGroup(group);
            }
            FarmContext.Screens["show.all"] = () => HideGroup(null);
            FarmContext.Screens["hide.scene"] = () =>
            {
                if (!player.IsLoaded) return;
                if (_groups == null) _groups = RenderGroups.Collect(player.Assets.Stage, player.Assets.Blobs);
                _groups.HideAll();
            };
        }

        void SetPreset(string name)
        {
            if (player.IsLoaded) Debug.LogWarning("[048] 재생 중 프리셋 변경은 후속 스펙 — 다음 진입부터 적용");
            var preset = RenderPresetTable.Find(name, m => Debug.LogWarning("[048] " + m));
            RenderQualityApplier.ApplyGlobal(preset, null, null,
                n => Resources.Load<FarmRenderAssets>("FarmRenderAssets")?.Find(n), m => Debug.LogError("[048] " + m));
            RenderQualityApplier.ApplyCamera(preset, cam);
            Debug.Log($"[048] 프리셋 {name}: 레벨 {RenderQualityApplier.CurrentLevelName}, 렌더 에셋 {UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name}");
        }

        void SetRenderScale(float scale)
        {
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.renderScale = scale;
            RenderQualityApplier.ResetHistory(cam);
        }

        IEnumerator Exit()
        {
            _waitLabel.style.display = DisplayStyle.None;
            if (player.Sequencer != null) player.Sequencer.RequestSkipAll();
            yield return null;
            while (player.Sequencer != null && !player.IsUnloaded) yield return null;
            _groups = null;
            _waitLabel.text = "unloaded";
            _waitLabel.style.display = DisplayStyle.Flex;
        }

        void HideGroup(string group)
        {
            if (!player.IsLoaded) return;
            if (_groups == null) _groups = RenderGroups.Collect(player.Assets.Stage, player.Assets.Blobs);
            _groups.HideOnly(group == FrameBudget.Post || group == FrameBudget.Hud ? null : group);
            if (RenderQualityApplier.Current != null && !RenderQualityApplier.Current.IsStandIn)
                UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).renderPostProcessing = group != FrameBudget.Post;
            _hud.SetVisible(group != FrameBudget.Hud);
            foreach (var l in new[] { _fps, _framing, _memory, _handles }) l.style.display = group == FrameBudget.Hud ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void Update()
        {
            // 048 T11: 엔진 프레임 간격(성능 구간 frameMs와 같은 값). Update 안 실시간 차이는 스크립트 실행 시점 흔들림이 섞여
            // 장면 교체 프레임에서 실제 프레임보다 크게 잡혔다 (릴리스: 실시간 27 ms vs 엔진 16.7 ms)
            _frameMs = Time.unscaledDeltaTime * 1000d;
            _shaderFrame = _shaders.Valid && _shaders.LastValue > 0 ? 1 : 0;

            var seq = player.Sequencer;
            // 048 AC-12: 진입 로딩 = 시작 요청 → 첫 장면 재생 시작 (초)
            if (_loadStart >= 0d && seq != null && player.IsLoaded && seq.State == SequencerState.Playing)
            {
                double sec = Time.realtimeSinceStartupAsDouble - _loadStart;
                _loadStart = -1d;
                _load.text = sec.ToString("0.00", CultureInfo.InvariantCulture);
                Debug.Log($"[048] 하이라이트 진입 로딩 {_load.text} s");
            }
            ShowCounter(_fps, ref _shownFps, _frames.TargetFps);
            ShowCounter(_handles, ref _shownHandles, player.Assets?.OutstandingHandles ?? 0);
            if (_waitLeft >= 0 && --_waitLeft < 0)
            {
                _waitLabel.text = "waited";
                _waitLabel.style.display = DisplayStyle.Flex;
            }
            if (seq == null) return;
            if (seq.State == SequencerState.Results && _menu.style.display != DisplayStyle.Flex) ShowMenu();
            if (_moments != null && _moments.Waiting)
            {
                _keyLabel.text = "key";
                _keyLabel.style.display = DisplayStyle.Flex;
            }
            _inTransition = player.IsLoaded && IsNearTransition(seq);
        }

        /// <summary>값이 바뀔 때만 글자를 만든다 (측정 구간 GC를 늘리지 않게).</summary>
        static void ShowCounter(Label label, ref int shown, int value)
        {
            if (value == shown) return;
            shown = value;
            label.text = value.ToString(CultureInfo.InvariantCulture);
        }

        bool IsNearTransition(HighlightSequencer seq)
        {
            if (seq.State == SequencerState.Timeline) return true;
            if (seq.State != SequencerState.Playing || player.Current == null) return false;
            var s = player.Current.Scene;
            double rate = seq.Clock.EffectiveRate;
            return (seq.Clock.Position - s.Start) / rate <= TransitionWindow || (s.End - seq.Clock.Position) / rate <= TransitionWindow;
        }

        void LateUpdate()
        {
            if (player.Current == null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = MenuBackground; // 경기장 조명이 바꾼 배경을 되돌린다
                return;
            }
            if (_broadcast != null) CheckBroadcastFraming();
            CheckSceneNumber();
            CheckHalves();
        }

        /// <summary>058 AC-12: HUD 장면 번호가 선정 목록의 현재 장면 순번과 같으면 ok.</summary>
        void CheckSceneNumber()
        {
            if (_live == null || player.Sequencer == null || !player.IsLoaded) return;
            var expect = (player.Sequencer.SceneIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + player.Scenes.Count.ToString(CultureInfo.InvariantCulture);
            var ok = _hud.SceneText == expect && player.Scenes.Count == _live.Scenes.Length ? "ok" : "bad";
            if (_sceneMatch.text != ok) _sceneMatch.text = ok;
        }

        /// <summary>AC-11·12·13: 공 또는 득점자가 화면 안(여백 5%)이면 ok.</summary>
        void CheckBroadcastFraming()
        {
            cam.clearFlags = CameraClearFlags.Skybox;
            var stage = (HighlightCastStage)player.Assets.Stage;
            int scorer = BroadcastScenes.Scorer;
            if (_liveBriefs != null)
            {
                var b = player.Current != null ? _liveBriefs.Brief(player.Current.Scene.Id) : null;
                scorer = b != null ? b.Scorer : -1;
            }
            var rig = scorer >= 0 ? stage.Rigs[scorer] : null;
            bool framed = InsideBroadcast(stage.Ball.position) || (rig != null && InsideBroadcast(rig.transform.position + Vector3.up));
            if (framed != _shownFramed || _framing.text.Length == 0)
            {
                _shownFramed = framed;
                _framing.text = framed ? "ok" : "bad";
            }
        }

        bool InsideBroadcast(Vector3 p)
        {
            const float margin = 0.05f;
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x >= margin && v.x <= 1f - margin && v.y >= margin && v.y <= 1f - margin;
        }
    }
}
