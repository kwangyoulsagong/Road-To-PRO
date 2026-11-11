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
    /// <summary>039 대체: 재생 상태를 라벨로 보여 시나리오가 읽게 한다.</summary>
    public sealed class StandInHud : IHighlightHud
    {
        readonly Label _speed, _scene, _loading, _replay, _reverse;
        readonly VisualElement _overlay;
        float _shownRate = float.NaN;
        int _shownScene = -1, _shownCount = -1;
        bool _shownReplay, _shownReverse;

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
            _reverse = Add(root, "reverseLabel", 120f, 72f);
            _reverse.text = "REVERSE ANGLE";
            _reverse.style.display = DisplayStyle.None;
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

        /// <summary>HUD 장면 번호 글자 ("i/n", specs/058 AC-12 확인용).</summary>
        public string SceneText => _scene.text;

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
            if (s.IsReverseAngle != _shownReverse)
            {
                _shownReverse = s.IsReverseAngle;
                _reverse.style.display = s.IsReverseAngle ? DisplayStyle.Flex : DisplayStyle.None;
            }
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
