using System;
using System.Collections.Generic;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace RoadToPro.Verification.Samples.Highlight
{
    /// <summary>018 대체: 시뮬 이벤트에서 고정 규칙으로 장면을 고른다. 품질 선정은 018의 몫이다.</summary>
    public static class StandInSceneSelector
    {
        const int MaxScenes = 12;
        const double Before = 8d, After = 3d;
        const double MinSceneSeconds = 1.5d;
        /// <summary>재생 클립은 장면 끝보다 최대 0.2 s 더 담으므로, 그만큼 앞에서 끊어야 순간이동 프레임이 클립에 들어가지 않는다.</summary>
        const double ClipMarginSeconds = 0.25d;
        /// <summary>프레임당 0.5 m = 15 m/s. 선수 최고 속도보다 충분히 크다.</summary>
        const float MaxStepSq = 0.25f;

        public static HighlightScene[] Select(MatchSim sim, bool withKeyMoment)
        {
            var picked = new List<(double t, bool goal)>();
            foreach (var g in sim.Result.Goals) picked.Add((g.Tick * 0.1d, true));
            foreach (var e in sim.Result.EventLog)
                if (e.Kind == SimEventKind.Shot && picked.Count < MaxScenes && !Near(picked, e.Tick * 0.1d))
                    picked.Add((e.Tick * 0.1d, false));
            picked.Sort((a, b) => a.t.CompareTo(b.t));

            var scenes = new List<HighlightScene>();
            int id = 1;
            for (int i = 0; i < picked.Count && scenes.Count < MaxScenes; i++)
            {
                var (t, goal) = picked[i];
                bool key = withKeyMoment && i == 0;
                scenes.Add(new HighlightScene(id++, t - Before, t + After, isKeyMoment: key, keyMomentTime: t - 2d));
                if (goal && scenes.Count < MaxScenes) scenes.Add(new HighlightScene(id++, t - 3d, t + 1d, isReplay: true));
            }

            double end = sim.Result.EndTick * 0.1d;
            var halves = new List<double>();
            foreach (var e in sim.Result.EventLog)
                if (e.Kind == SimEventKind.PeriodEnd && e.Tick * 0.1d < end) halves.Add(e.Tick * 0.1d);
            var normalized = SceneListNormalizer.Normalize(scenes.ToArray(), end, halves.ToArray());
            var usable = new List<HighlightScene>(normalized.Length);
            foreach (var s in normalized)
            {
                var cut = CutAtTeleport(sim, s);
                if (cut.End - cut.Start >= MinSceneSeconds) usable.Add(cut);
            }
            return usable.ToArray();
        }

        /// <summary>
        /// 대체 심판(012 전)은 재시작 때 선수를 그 틱에 재시작 위치로 옮긴다. 그 프레임 앞에서 장면을 끝낸다.
        /// 012·018이 연결되면 사라질 처리이고 재생기에는 없다.
        /// </summary>
        static HighlightScene CutAtTeleport(MatchSim sim, in HighlightScene s)
        {
            var clip = sim.ExtractClip((long)Math.Floor(s.Start * 10d), (long)Math.Ceiling(s.End * 10d) + 1L);
            for (int f = 1; f < clip.Frames.Length; f++)
            {
                var a = clip.Frames[f - 1];
                var b = clip.Frames[f];
                for (int e = 0; e < Squad.Slots; e++)
                {
                    if (a.Hints[e] == MotionHint.Absent || b.Hints[e] == MotionHint.Absent) continue;
                    float dx = b.X(e) - a.X(e), dy = b.Y(e) - a.Y(e);
                    if (dx * dx + dy * dy <= MaxStepSq) continue;
                    double t = a.Index / 30d;
                    return s.WithRange(s.Start, Math.Max(s.Start, Math.Min(s.End, t - ClipMarginSeconds)));
                }
            }
            return s;
        }

        static bool Near(List<(double t, bool goal)> picked, double t)
        {
            foreach (var p in picked)
                if (Math.Abs(p.t - t) < Before + After) return true;
            return false;
        }
    }

    /// <summary>039 대체: 재생 상태를 라벨로 보여 시나리오가 읽게 한다.</summary>
    public sealed class StandInHud : IHighlightHud
    {
        readonly Label _speed, _scene, _loading, _replay;
        readonly VisualElement _overlay;
        float _shownRate = float.NaN;
        int _shownScene = -1, _shownCount = -1;
        bool _shownReplay;

        public StandInHud(VisualElement root)
        {
            _overlay = new VisualElement { name = "loadingOverlay" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.right = 0;
            _overlay.style.top = 0;
            _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = Color.black;
            _overlay.style.display = DisplayStyle.None;
            _loading = Add(_overlay, "loadingLabel", 16f, 16f);
            _loading.style.color = Color.white;
            root.Add(_overlay);
            _speed = Add(root, "speedLabel", 16f, 16f);
            _scene = Add(root, "sceneLabel", 16f, 44f);
            _replay = Add(root, "replayLabel", 16f, 72f);
        }

        internal static Label Add(VisualElement parent, string name, float left, float top)
        {
            var l = new Label { name = name };
            l.style.position = Position.Absolute;
            l.style.left = left;
            l.style.top = top;
            l.style.fontSize = 20f;
            l.style.color = Color.white;
            l.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            parent.Add(l);
            return l;
        }

        public void SetVisible(bool visible)
        {
            var d = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _speed.style.display = d;
            _scene.style.display = d;
            _replay.style.display = d;
        }

        public void OnLoadingProgress(float progress)
        {
            bool loading = progress < 1f;
            _overlay.style.display = loading ? DisplayStyle.Flex : DisplayStyle.None;
            if (loading) _loading.text = $"{(int)(progress * 100f)}%";
        }

        public void OnStatus(in PlaybackStatus s)
        {
            if (s.UserRate != _shownRate)
            {
                _shownRate = s.UserRate;
                _speed.text = s.UserRate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
            }
            if (s.SceneIndex != _shownScene || s.SceneCount != _shownCount)
            {
                _shownScene = s.SceneIndex;
                _shownCount = s.SceneCount;
                _scene.text = $"{s.SceneIndex + 1}/{s.SceneCount}";
            }
            if (s.IsReplay != _shownReplay)
            {
                _shownReplay = s.IsReplay;
                _replay.text = s.IsReplay ? "REPLAY" : "";
            }
        }
    }

    /// <summary>042 대체: 메인 스탠드 고정 위치에서 공과 주변 필드 선수를 화면비에 맞춰 담는다.</summary>
    public sealed class StandInCamera : IHighlightCamera
    {
        public bool IsReverseAngle => false;
        public void OnSceneBegin(in HighlightScene scene) { }
    }

    /// <summary>048 대체: 목표 fps와 타임라인 구간 렌더 빈도.</summary>
    public sealed class StandInFramePolicy : IFramePolicy
    {
        const int MenuFps = 30, HighlightFps = 60, BatteryFps = 30, TimelineInterval = 6;

        public int TargetFps { get; private set; } = MenuFps;
        public void EnterHighlight()
        {
            TargetFps = FarmContext.BatterySaver ? BatteryFps : HighlightFps;
            Application.targetFrameRate = TargetFps;
            OnDemandRendering.renderFrameInterval = 1;
        }

        public void EnterTimeline() => OnDemandRendering.renderFrameInterval = TimelineInterval;
        public void ExitTimeline() => OnDemandRendering.renderFrameInterval = 1;

        public void EnterMenu()
        {
            TargetFps = MenuFps;
            Application.targetFrameRate = MenuFps;
            OnDemandRendering.renderFrameInterval = 1;
            Debug.Log($"[highlight] 메뉴 목표 fps {MenuFps}");
        }
    }

    /// <summary>046 대체: 소리 없이 재생 상태만 받는다.</summary>
    public sealed class StandInAudio : IHighlightAudio
    {
        public int Cues { get; private set; }
        public void OnPlaybackState(float rate, bool paused) { }
        public void OnCue(in PlaybackCue cue) => Cues++;
    }

    /// <summary>019 대체: 선택 화면 대신 라벨을 띄우고 답하지 않는다.</summary>
    public sealed class StandInKeyMoments : IKeyMomentHandler
    {
        public bool Waiting { get; private set; }
        public int Requests { get; private set; }

        public void RequestChoice(int sceneId, double simTime, bool textOnly)
        {
            Waiting = true;
            Requests++;
        }
    }

    /// <summary>진단을 실패로 기록한다 (FR-53).</summary>
    public sealed class StandInDiagnostics : IHighlightDiagnostics
    {
        public void OnSceneUnavailable(int sceneId) => Debug.LogError($"[highlight] 재구성 불가 장면 {sceneId}");
    }
}
