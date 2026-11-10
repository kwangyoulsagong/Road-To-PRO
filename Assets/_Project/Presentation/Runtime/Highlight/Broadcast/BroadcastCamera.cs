using System;
using System.Collections.Generic;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;
using Unity.Profiling;
using UnityEngine;

namespace RoadToPro.Presentation.Highlight.Broadcast
{
    /// <summary>
    /// 중계 카메라 (specs/042). 로딩 때 장면별 트랙을 만들고, 재생 중에는 트랙을 읽어 Camera에 적용만 한다.
    /// 시뮬 상태는 읽기만 한다. 재생 경로에서 할당이 없다.
    /// </summary>
    public sealed class BroadcastCamera : IHighlightCamera
    {
        static readonly ProfilerMarker Update = new ProfilerMarker("Highlight.CameraUpdate");

        readonly struct Entry
        {
            public readonly int Id;
            public readonly CameraTrack Track;

            public Entry(int id, CameraTrack track)
            {
                Id = id;
                Track = track;
            }
        }

        readonly Camera _camera;
        readonly Func<int, SceneBrief> _briefs;
        readonly Func<CameraRigData> _rig;
        readonly CameraTuning _tuning;
        readonly CameraSettings _settings;
        readonly List<Entry> _tracks = new List<Entry>();
        CameraTrackBuilder _builder;
        CameraTrack _current;
        int _currentId = -1;

        public BroadcastCamera(Camera camera, Func<int, SceneBrief> briefs, Func<CameraRigData> rig, CameraTuning tuning, CameraSettings settings)
        {
            _camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            _briefs = briefs ?? throw new ArgumentNullException(nameof(briefs));
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool IsReverseAngle { get; private set; }
        /// <summary>현재 샘플 (검증용).</summary>
        public CameraSample Last { get; private set; }

        /// <summary>장면 하나가 준비될 때 부른다. 키 모먼트 재시뮬로 같은 장면이 다시 오면 그 뒤를 버리고 새로 만든다.</summary>
        public void OnSceneBuilt(BuiltScene built)
        {
            var brief = _briefs(built.Scene.Id);
            if (brief == null) return;
            int at = IndexOf(built.Scene.Id);
            if (at >= 0) _tracks.RemoveRange(at, _tracks.Count - at);

            _builder ??= new CameraTrackBuilder(_rig(), _tuning, _settings);
            CameraPose? from = null;
            if (_tracks.Count > 0)
            {
                var previous = _tracks[_tracks.Count - 1].Track;
                from = previous.Poses[previous.Frames - 1];
            }
            _tracks.Add(new Entry(built.Scene.Id, _builder.Build(built.Data, brief, from, _camera.aspect < 1f)));
            if (built.Scene.Id == _currentId) _current = _tracks[_tracks.Count - 1].Track;
        }

        /// <summary>설정이 바뀌었을 때 전체 트랙을 다시 만든다.</summary>
        public void Rebuild(IReadOnlyList<BuiltScene> scenes)
        {
            _tracks.Clear();
            _current = null;
            for (int i = 0; i < scenes.Count; i++) OnSceneBuilt(scenes[i]);
        }

        public void OnSceneBegin(in HighlightScene scene)
        {
            _currentId = scene.Id;
            int at = IndexOf(scene.Id);
            _current = at >= 0 ? _tracks[at].Track : null;
            IsReverseAngle = _current != null && _current.Sample(scene.Start).IsReverse;
        }

        public void OnPlayback(double simTime)
        {
            if (_current == null) return;
            using (Update.Auto())
            {
                var s = _current.Sample(simTime);
                Last = s;
                IsReverseAngle = s.IsReverse;
                var p = s.Pose.Position + s.Pose.Shake;
                var f = s.Pose.Forward;
                _camera.transform.SetPositionAndRotation(new Vector3(p.X, p.Y, p.Z), Quaternion.LookRotation(new Vector3(f.X, f.Y, f.Z), Vector3.up));
                _camera.fieldOfView = s.FovDeg(_camera.aspect);
            }
        }

        int IndexOf(int id)
        {
            for (int i = 0; i < _tracks.Count; i++)
                if (_tracks[i].Id == id) return i;
            return -1;
        }
    }
}
