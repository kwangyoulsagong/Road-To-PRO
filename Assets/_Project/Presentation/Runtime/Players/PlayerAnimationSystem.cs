using System;
using System.Collections.Generic;
using RoadToPro.Presentation.Players.Logic;
using Unity.Profiling;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 하이라이트 선수 애니메이션 갱신 (specs/043 FR-40~62, 성능 예산: 25명 CPU p95 ≤ 2 ms, GC 0 B/프레임).
    /// 매 프레임: 재생 시각 t → AnimPlan.Evaluate(엔티티, t) → PlayerAnimator.Apply → 발 접지. 프로파일 마커 "Players.Anim".
    /// 엔티티 순서 고정(0~24) — 결정론.
    /// </summary>
    public sealed class PlayerAnimationSystem : MonoBehaviour
    {
        public static readonly ProfilerMarker Marker = new ProfilerMarker("Players.Anim");
        /// <summary>054 처리(자세·발 고정·지면 보정) 몫 — 043 예산 안 (specs/054 성능 예산).</summary>
        public static readonly ProfilerMarker MotionMarker = new ProfilerMarker("Players.Motion");

        AnimPlan _plan;
        PlayerAnimator[] _anim;
        FootLock[] _lock;
        PoseSample[] _poses;
        UnityEngine.Playables.PlayableGraph _graph;   // 25명이 공유 — 한 번에 평가
        IPlaybackClock _clock;
        Transform _ball;
        Camera _cam;
        PoseSample _s;

        public AnimPlan Plan => _plan;
        public float LastTime { get; private set; } = float.NaN;
        public float MaxGroundLift { get; private set; }
        /// <summary>054 처리 시간 측정 (IL2CPP 벤치). 꺼져 있으면 타이머 호출 없음.</summary>
        public bool TimeMotion;
        public double LastMotionMs { get; private set; }
        /// <summary>검증 모드 진단 (specs/054 FR-70). null이면 호출 0.</summary>
        public Motion.IMotionDiagnostics Diagnostics { get; set; }
        /// <summary>공 표시 위치 보정 (specs/041 FR-04). null이면 계획 값을 그대로 쓴다.</summary>
        public IBallVisualFilter BallFilter { get; set; }

        /// <summary>rigs[e]가 null이면 그 엔티티는 그리지 않는다.</summary>
        public void Init(AnimPlan plan, PlayerRig[] rigs, AnimationClip[] clips, IPlaybackClock clock, Transform ball = null, Camera lodCamera = null)
        {
            Dispose();
            _plan = plan;
            // 새 선수·계획이면 같은 시각이라도 다시 적용해야 한다 (041 조기 반환이 043~057 정지 화면 재초기화를 막던 회귀, 048 T16)
            LastTime = float.NaN;
            _clock = clock;
            _ball = ball;
            _cam = lodCamera;
            _anim = new PlayerAnimator[rigs.Length];
            _lock = new FootLock[rigs.Length];
            _poses = new PoseSample[rigs.Length];
            _graph = UnityEngine.Playables.PlayableGraph.Create("Players");
            _graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
            for (int e = 0; e < rigs.Length; e++)
                if (rigs[e] != null)
                {
                    _anim[e] = new PlayerAnimator(rigs[e], clips, _graph);
                    _lock[e] = new FootLock(rigs[e]);
                }
        }

        /// <summary>
        /// 장면 전환 (specs/041 FR-31): 그래프·애니메이터를 재사용하고 계획만 바꾼다. 발 고정은 포즈 = f(t)라 넘길 상태가 없다.
        /// Init 뒤에만 부를 수 있고, 할당이 없다.
        /// </summary>
        public void SwapPlan(AnimPlan plan)
        {
            if (_anim == null) throw new System.InvalidOperationException("Init 뒤에 호출한다");
            _plan = plan ?? throw new System.ArgumentNullException(nameof(plan));
            LastTime = float.NaN;
        }

        /// <summary>ClipLibrary 인덱스 순서로 AnimationClip을 고른다. 없는 클립이 있으면 예외.</summary>
        public static AnimationClip[] Order(ClipLibrary lib, IEnumerable<AnimationClip> all)
        {
            var by = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var c in all)
                if (c != null && !c.name.StartsWith("__preview", StringComparison.Ordinal)) by[c.name] = c;
            var o = new AnimationClip[lib.Count];
            for (int i = 0; i < lib.Count; i++)
                if (!by.TryGetValue(lib.Get(i).Id, out o[i])) throw new ArgumentException("glb에 없는 클립: " + lib.Get(i).Id);
            return o;
        }

        /// <summary>시각 t의 포즈를 모든 선수에 적용 (테스트·E2E는 직접 부른다).</summary>
        public void ApplyAt(float t)
        {
            // 같은 시각이면 그래프가 뼈를 다시 쓰지 않아 자세 오버레이만 누적된다 (specs/041 T08a, 일시정지 중 선수가 굴러감).
            if (t == LastTime) return;
            using (Marker.Auto())
            {
                float lift = 0f;
                for (int e = 0; e < _anim.Length; e++)
                {
                    var a = _anim[e];
                    if (a == null) continue;
                    _plan.Evaluate(e, t, ref _s);
                    var go = a.Rig.gameObject;
                    if (go.activeSelf != _s.Present) go.SetActive(_s.Present);
                    if (!_s.Present) continue;
                    a.Prepare(_s);
                    _poses[e] = _s;
                    if (_cam != null) a.Rig.UpdateLod(_cam);
                }
                _graph.Evaluate(0f);
                long t0 = TimeMotion ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                using (MotionMarker.Auto())
                    for (int e = 0; e < _anim.Length; e++)
                    {
                        var a = _anim[e];
                        if (a == null || !a.Rig.gameObject.activeSelf) continue;
                        _lock[e].ApplyPosture(_poses[e].Roll, _poses[e].Pitch, _poses[e].Twist);
                        _lock[e].ApplyFeet(_plan.Plants, e, t);
                        lift = Mathf.Max(lift, FootGrounding.Apply(a.Rig));
                    }
                if (TimeMotion) LastMotionMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                MaxGroundLift = lift;
                if (_ball != null)
                {
                    var b = _plan.BallVisual(t);
                    if (BallFilter != null) b = BallFilter.Filter(b, t);
                    _ball.position = new Vector3(b.X, b.Y, b.Z);
                }
                LastTime = t;
            }
            Diagnostics?.OnApplied(t);
        }

        void LateUpdate()
        {
            if (_plan == null || _clock == null) return;
            ApplyAt(_clock.SimTime);
        }

        void Dispose()
        {
            if (_anim == null) return;
            foreach (var a in _anim) a?.Dispose();
            _anim = null;
            if (_graph.IsValid()) _graph.Destroy();
        }

        void OnDestroy() => Dispose();
    }
}
