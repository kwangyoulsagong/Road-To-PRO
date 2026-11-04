using System.Collections.Generic;
using RoadToPro.Presentation.Players.Logic;
using Unity.Profiling;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 클로즈업 머리 전환 (specs/056 FR-61). 매 프레임 카메라와 선수 머리 뼈로 얼굴 화면 비율을 재고 CloseupSelector로 최대 2명을 고른다.
    /// 전환 프레임 비용은 프로파일 마커 "Players.CloseupSwitch"로 잰다(성능 예산 ≤ 2 ms). 할당 없음(배열은 Init에서).
    /// </summary>
    public sealed class CloseupHeads : MonoBehaviour
    {
        public const float HeadHeight = 0.24f; // 정수리~턱끝 (기준 키 1.80 m의 1/7.7, FR-12)
        static readonly ProfilerMarker Switch = new ProfilerMarker("Players.CloseupSwitch");

        Camera _cam;
        PlayerRig[] _rigs;
        float[] _ratio;
        bool[] _cur, _next;
        Transform[] _heads;
        public int ActiveCount { get; private set; }

        public void Init(Camera cam, IReadOnlyList<PlayerRig> rigs)
        {
            _cam = cam;
            _rigs = new PlayerRig[rigs.Count];
            _heads = new Transform[rigs.Count];
            for (int i = 0; i < rigs.Count; i++)
            {
                _rigs[i] = rigs[i];
                _heads[i] = rigs[i] != null ? rigs[i].Bone("Head") : null;
            }
            _ratio = new float[rigs.Count];
            _cur = new bool[rigs.Count];
            _next = new bool[rigs.Count];
        }

        /// <summary>얼굴 화면 높이 비율 = 머리 높이 × 스케일 / (2·거리·tan(세로 시야/2)).</summary>
        public static float Ratio(Camera cam, Vector3 head, float scale)
        {
            float d = Vector3.Distance(cam.transform.position, head);
            return HeadHeight * scale / Mathf.Max(1e-3f, 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        }

        void LateUpdate() => Tick();

        public void Tick()
        {
            if (_cam == null || _rigs == null) return;
            for (int i = 0; i < _rigs.Length; i++)
            {
                var r = _rigs[i];
                bool vis = r != null && r.isActiveAndEnabled && _heads[i] != null;
                _ratio[i] = vis ? Ratio(_cam, _heads[i].position, r.transform.localScale.y) : 0f;
                _cur[i] = vis && r.CloseupActive;
            }
            ActiveCount = CloseupSelector.Select(_ratio, _cur, _next);
            using (Switch.Auto())
            {
                for (int i = 0; i < _rigs.Length; i++)
                    if (_rigs[i] != null && _cur[i] != _next[i]) _rigs[i].SetCloseup(_next[i]);
            }
        }
    }
}
