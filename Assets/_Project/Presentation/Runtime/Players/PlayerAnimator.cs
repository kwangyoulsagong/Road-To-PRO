using System;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 선수 한 명의 포즈 적용 (specs/043 FR-41·50~52, AC-12). 공유 PlayableGraph(수동 평가)의 이 선수 출력 + 슬롯 7개 믹서.
    /// 매 프레임 PoseSample의 클립을 슬롯에 맞추고(바뀐 것만 재연결) 시각·가중치를 정한다. 시스템이 그래프를 한 번 평가한다.
    /// Animator 상태 머신·deltaTime을 쓰지 않는다 → 포즈 = f(재생 시각). 관리 힙 할당 없음.
    /// </summary>
    public sealed class PlayerAnimator : IDisposable
    {
        readonly PlayerRig _rig;
        PlayableGraph _graph;
        readonly bool _ownsGraph;
        AnimationMixerPlayable _mix;
        readonly AnimationClipPlayable[] _clips;   // 클립별 플레이어블(미리 생성, 연결은 쓸 때만)
        readonly int[] _slot = new int[PoseSample.Capacity];   // 슬롯에 연결된 클립(−1 없음)
        readonly bool[] _want = new bool[PoseSample.Capacity];

        public PlayerRig Rig => _rig;

        /// <param name="clips">ClipLibrary 인덱스 순서의 AnimationClip 배열.</param>
        /// <param name="shared">여러 선수가 함께 쓰는 그래프(한 번에 평가). 기본값이면 이 선수 전용 그래프를 만든다.</param>
        public PlayerAnimator(PlayerRig rig, AnimationClip[] clips, PlayableGraph shared = default)
        {
            _rig = rig;
            _ownsGraph = !shared.IsValid();
            _graph = _ownsGraph ? PlayableGraph.Create("Player " + rig.name) : shared;
            if (_ownsGraph) _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "Anim " + rig.name, rig.Animator);
            // 믹서 입력은 PoseSample 용량(7)만. 82개 상시 연결은 평가 비용이 더 크다(043 E2E 실측: 슬롯 p50 1.2 ms / 상시 연결 p95 ≈ 4 ms)
            _mix = AnimationMixerPlayable.Create(_graph, PoseSample.Capacity);
            output.SetSourcePlayable(_mix);
            _clips = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                var p = AnimationClipPlayable.Create(_graph, clips[i]);
                p.SetApplyFootIK(false);
                p.SetApplyPlayableIK(false);
                p.Pause(); // 시각은 우리가 정한다
                _clips[i] = p;
            }
            for (int i = 0; i < _slot.Length; i++) _slot[i] = -1;
        }

        /// <summary>포즈 지시를 적용하고 이 선수 그래프를 평가한다(전용 그래프일 때). 공유 그래프면 Prepare 뒤 시스템이 한 번 평가.</summary>
        public void Apply(in PoseSample s)
        {
            Prepare(s);
            if (_ownsGraph) _graph.Evaluate(0f);
        }

        public void Prepare(in PoseSample s)
        {
            // 1) 이미 슬롯에 있는 클립은 그대로 둔다
            for (int k = 0; k < _want.Length; k++) _want[k] = false;
            for (int i = 0; i < s.Count; i++)
                for (int k = 0; k < _slot.Length; k++)
                    if (_slot[k] == s.Clip(i)) { _want[k] = true; break; }
            // 2) 새 클립은 이번에 안 쓰는 슬롯에 바꿔 연결
            for (int i = 0; i < s.Count; i++)
            {
                int c = s.Clip(i);
                int k = Array.IndexOf(_slot, c);
                if (k < 0)
                {
                    for (k = 0; k < _slot.Length; k++)
                        if (!_want[k]) break;
                    if (_slot[k] >= 0) _graph.Disconnect(_mix, k);
                    _graph.Connect(_clips[c], 0, _mix, k);
                    _slot[k] = c;
                    _want[k] = true;
                }
                _clips[c].SetTime(s.Time(i));
                _mix.SetInputWeight(k, s.Weight(i));
            }
            for (int k = 0; k < _slot.Length; k++)
                if (!_want[k]) _mix.SetInputWeight(k, 0f);
            _rig.SetRoot(new Vector3(s.Root.X, 0f, s.Root.Z), s.Yaw);
        }

        public void Dispose()
        {
            if (_ownsGraph && _graph.IsValid()) _graph.Destroy();
        }
    }

    /// <summary>재생 시계 (specs/043 FR-50, 소유 041). 시뮬 초.</summary>
    public interface IPlaybackClock
    {
        float SimTime { get; }
    }

    /// <summary>
    /// 041 전까지의 고정 시계: 시각 = 시작 + 프레임 번호 / fps × 배속 — deltaTime을 누적하지 않는다(T17 리뷰: 30/60 fps 같은 시각이 비트 단위로 같게).
    /// </summary>
    public sealed class FixedPlaybackClock : IPlaybackClock
    {
        public float Start;
        public float Fps = 60f;
        public float Rate = 1.5f;
        public long Frame;

        public float SimTime => Start + (float)(Frame * (double)Rate / Fps);
    }
}
