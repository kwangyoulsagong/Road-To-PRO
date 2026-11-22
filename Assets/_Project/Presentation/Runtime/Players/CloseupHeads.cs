using System.Collections.Generic;
using RoadToPro.Presentation.Players.Logic;
using Unity.Profiling;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 클로즈업 머리 전환 (specs/056 FR-61). 매 프레임 카메라와 선수 머리 뼈로 얼굴 화면 비율을 재고 CloseupSelector로 최대 2명을 고른다.
    /// 062 FR-85~87: <see cref="Cinematic"/>(061)이 있으면 QualityTierPolicy로 — TopTier 대상 중 클로즈업 머리 최대 2명 + 나머지 LOD0 고정·고해상도.
    /// rigs 인덱스 = 엔티티 번호(061 TopTier와 같은 번호).
    /// 전환 프레임 비용은 프로파일 마커 "Players.CloseupSwitch"로 잰다(성능 예산 ≤ 2 ms). 할당 없음(배열은 Init에서).
    /// </summary>
    public sealed class CloseupHeads : MonoBehaviour
    {
        public const float HeadHeight = 0.24f; // 정수리~턱끝 (기준 키 1.80 m의 1/7.7, FR-12)
        static readonly ProfilerMarker Switch = new ProfilerMarker("Players.CloseupSwitch");

        Camera _cam;
        PlayerRig[] _rigs;
        float[] _ratio;
        bool[] _cur, _next, _pinCur, _pinNext;
        readonly int[] _top = new int[QualityTierPolicy.MaxTier];
        /// <summary>061 고품질 모드 신호 (없으면 기존 클로즈업만).</summary>
        public Rendering.CinematicModeController Cinematic;
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
            _pinCur = new bool[rigs.Count];
            _pinNext = new bool[rigs.Count];
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
                _pinCur[i] = vis && r.TopTierPinned;
            }
            int nTop = 0;
            if (Cinematic != null)
                for (int k = 0; k < Cinematic.TopTierCount && nTop < _top.Length; k++)
                {
                    int e = Cinematic.TopTier[k];
                    if (e >= 0 && e < _rigs.Length && _rigs[e] != null && _heads[e] != null && _rigs[e].isActiveAndEnabled) _top[nTop++] = e;
                }
            QualityTierPolicy.Select(_ratio, new System.ReadOnlySpan<int>(_top, 0, nTop), _cur, _pinCur, _next, _pinNext);
            ActiveCount = 0;
            for (int i = 0; i < _next.Length; i++) if (_next[i]) ActiveCount++;
            using (Switch.Auto())
            {
                for (int i = 0; i < _rigs.Length; i++)
                {
                    if (_rigs[i] == null) continue;
                    // 실제 상태와 비교 — 화면 밖으로 나간 선수도 등급을 푼다
                    if (_rigs[i].CloseupActive != _next[i]) _rigs[i].SetCloseup(_next[i]);
                    if (_rigs[i].TopTierPinned != _pinNext[i]) _rigs[i].SetTopTier(_pinNext[i]);
                }
            }
        }
    }
}
