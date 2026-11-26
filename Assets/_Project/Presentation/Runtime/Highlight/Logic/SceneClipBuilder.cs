using System;
using System.Collections.Generic;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>재생에 필요한 장면 하나의 미리 계산된 데이터.</summary>
    public sealed class BuiltScene
    {
        public readonly HighlightScene Scene;
        public readonly HighlightData Data;
        public readonly AnimPlan Plan;
        public readonly PlaybackCue[] Cues;
        /// <summary>장면 그물 접촉 (specs/059 FR-34, 시각 순) — 그물 출렁임 입력. 없으면 빈 목록.</summary>
        public readonly List<NetContact> NetContacts;
        /// <summary>[골대 번호] 장면을 만들 때 구운 그물 움직임 (specs/059 FR-27). 접촉 없는 골대·굽기 없음은 null.</summary>
        public readonly Stadium.Net.NetBake[] NetBakes;

        public BuiltScene(HighlightScene scene, HighlightData data, AnimPlan plan, PlaybackCue[] cues, List<NetContact> netContacts = null,
            Stadium.Net.NetBake[] netBakes = null)
        {
            NetBakes = netBakes ?? new Stadium.Net.NetBake[2];
            NetContacts = netContacts ?? new List<NetContact>();
            Scene = scene;
            Data = data;
            Plan = plan;
            Cues = cues;
        }
    }

    /// <summary>
    /// 장면 목록을 클립·애니메이션 계획으로 미리 만든다 (specs/041 FR-02·31·53). 로딩 화면이 <see cref="Step"/>을 프레임 시간 안에서 나눠 부른다.
    /// 키 모먼트 재시뮬 뒤에는 새 시뮬의 추출 함수로 새 빌더를 만들어 바뀐 장면만 다시 만든다.
    /// </summary>
    public sealed class SceneClipBuilder
    {
        readonly Func<long, long, ReplayClip> _extract;
        readonly Func<HighlightData, AnimPlan> _plan;
        readonly PitchSpec _pitch;
        readonly IHighlightDiagnostics _diagnostics;
        readonly HighlightScene[] _scenes;
        readonly List<BuiltScene> _built;
        readonly Action<BuiltScene> _onBuilt;
        readonly GoalBox _netBox;
        readonly Stadium.Net.NetSceneBaker _netBaker;
        int _next;

        /// <param name="netBox">그물 접촉 검출용 그물 상자 (059). 없으면 피치 규격 + 기본 그물 깊이·공 반지름.</param>
        /// <param name="netBaker">그물 굽기 (059 FR-27). 없으면 그물은 정지 모양.</param>
        public SceneClipBuilder(HighlightScene[] scenes, Func<long, long, ReplayClip> extract, Func<HighlightData, AnimPlan> plan,
            PitchSpec pitch, IHighlightDiagnostics diagnostics, Action<BuiltScene> onBuilt = null, GoalBox? netBox = null,
            Stadium.Net.NetSceneBaker netBaker = null)
        {
            _netBaker = netBaker;
            _netBox = netBox ?? GoalBox.Default(pitch);
            _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
            _extract = extract ?? throw new ArgumentNullException(nameof(extract));
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _pitch = pitch;
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _built = new List<BuiltScene>(scenes.Length);
            _onBuilt = onBuilt;
        }

        public bool IsDone => _next >= _scenes.Length;
        public float Progress => _scenes.Length == 0 ? 1f : (float)_next / _scenes.Length;
        public IReadOnlyList<BuiltScene> Built => _built;

        /// <summary>장면 하나를 만든다. 끝났으면 false.</summary>
        public bool Step()
        {
            if (IsDone) return false;
            var scene = _scenes[_next++];
            BuiltScene built = null;
            try
            {
                var clip = _extract(TickFloor(scene.Start), TickCeil(scene.End));
                var data = ClipConverter.ToHighlight(clip, _pitch);
                var contacts = new List<NetContact>();
                var cues = ClipConverter.ToCues(clip, data, _netBox, contacts);
                built = new BuiltScene(scene, data, _plan(data), cues, contacts, _netBaker?.Bake(data, contacts));
                _built.Add(built);
            }
            catch (ArgumentException)
            {
                _diagnostics.OnSceneUnavailable(scene.Id);
            }
            if (built != null) _onBuilt?.Invoke(built);
            return true;
        }

        static long TickFloor(double seconds) => (long)Math.Floor(seconds * 10d);

        /// <summary>마지막 30 Hz 프레임이 장면 끝을 덮도록 한 틱 더 포함한다 (구간 끝은 배타적).</summary>
        static long TickCeil(double seconds) => (long)Math.Ceiling(seconds * 10d) + 1L;

        public HighlightScene[] Scenes()
        {
            var r = new HighlightScene[_built.Count];
            for (int i = 0; i < r.Length; i++) r[i] = _built[i].Scene;
            return r;
        }

        public PlaybackCue[][] Cues()
        {
            var r = new PlaybackCue[_built.Count][];
            for (int i = 0; i < r.Length; i++) r[i] = _built[i].Cues;
            return r;
        }
    }
}
