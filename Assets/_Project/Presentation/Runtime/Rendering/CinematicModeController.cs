using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Rendering.Logic;
using RoadToPro.Presentation.Stadium;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RoadToPro.Presentation.Rendering
{
    /// <summary>specs/061 런타임 볼륨: 고품질 모드(피사계 심도만, FR-82)·비네트(Medium·High, FR-34). 프로필은 코드로 만든다 (에셋 로딩 없음).</summary>
    public static class CinematicVolumes
    {
        public static VolumeProfile CreateModeProfile()
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "061 Cinematic (DoF only)";
            var dof = p.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(10f);
            dof.gaussianEnd.Override(30f);
            dof.gaussianMaxRadius.Override(LookParameters.DofMaxRadius);
            dof.highQualitySampling.Override(false);
            return p;
        }

        public static VolumeProfile CreateVignetteProfile()
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "061 Vignette";
            var v = p.Add<Vignette>(true);
            v.intensity.Override(LookParameters.VignetteIntensity);
            v.smoothness.Override(LookParameters.VignetteSmoothness);
            return p;
        }
    }

    /// <summary>
    /// specs/061 FR-80~87: 연출 장면 고품질 모드 적용. 장면 빌드 때 <see cref="CinematicModeLogic"/>로 구간을 만들고,
    /// 재생 중에는 시뮬 시각으로 가중치를 얻어 볼륨 weight·DoF 초점·그림자 거리·SSAO·림 보정만 바꾼다 (색·톤·조명 방향 불변, FR-82).
    /// 모든 리소스는 생성 때 만든다 — 모드 진입 때 생성·로드 없음 (FR-85). 프레임 경로 할당 없음.
    /// </summary>
    public sealed class CinematicModeController : IDisposable
    {
        static readonly ProfilerMarker Marker = new ProfilerMarker("Highlight.CinematicMode");
        /// <summary>고품질 모드 그림자 거리 배율 — 같은 그림자 맵을 좁은 범위에 써서 근거리 해상도를 올린다 (재할당 없음).</summary>
        public const float ShadowDistanceScale = 0.35f;
        /// <summary>대상 선수 림 보정 배율 (FR-82 낙차 안). 전역 값이라 화면 속 선수 전원에 같이 적용된다.</summary>
        public const float RimBoost = 1.25f;

        readonly Camera _camera;
        readonly PresetId _preset;
        readonly StadiumLighting _lighting;
        readonly GrassShell _grass;
        /// <summary>눈 덮인 피치 (064 연결 전 항상 false, FR-89).</summary>
        public bool SnowCovered { get; set; }
        /// <summary>마지막 프레임에 그린 셸 수 (검증용).</summary>
        public int GrassShellsDrawn => _grass != null ? _grass.LastShellsDrawn : 0;
        readonly GameObject _root;
        readonly Volume _mode, _vignette;
        readonly DepthOfField _dof;
        readonly UniversalRenderPipelineAsset _urp;
        readonly float _baseShadowDistance;
        readonly ScriptableRendererFeature _ssao;
        readonly float _baseRim;
        readonly Dictionary<int, CinematicTimeline> _timelines = new Dictionary<int, CinematicTimeline>();
        readonly bool _dofOn, _shadowOn, _ssaoOn;
        bool _ssaoActive;

        public float Weight { get; private set; }
        /// <summary>검증용: 고품질 모드를 끈 채로 같은 프레임을 그린다 (061 FR-84 낙차 비교·마스크 캡처).</summary>
        public bool ForceNormal { get; set; }
        /// <summary>피사계 심도 볼륨 가중치 (검증용 — 프리셋에서 DoF가 꺼져 있으면 모드 중에도 0).</summary>
        public float DofWeight => _mode != null ? _mode.weight : 0f;

        /// <summary>최고 품질 등급 대상 (062 연결 신호, FR-81). 엔티티 번호, 앞 <see cref="TopTierCount"/>개가 유효.</summary>
        public readonly int[] TopTier = new int[3];
        public int TopTierCount { get; private set; }
        readonly List<Vec3> _players = new List<Vec3>(HighlightData.Players);

        public CinematicModeController(Camera camera, PresetId preset, StadiumLighting lighting, Renderer pitch = null)
        {
            _camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            _preset = preset;
            _lighting = lighting;
            _root = new GameObject("061 Cinematic Volumes") { hideFlags = HideFlags.DontSave };
            _mode = AddVolume(_root, CinematicVolumes.CreateModeProfile(), 10f, 0f);
            _mode.profile.TryGet(out _dof);
            _vignette = AddVolume(_root, CinematicVolumes.CreateVignetteProfile(), 5f,
                VisualFeatureTable.Get(preset, VisualFeature.Vignette) == FeatureLevel.Weak ? 1f : 0f);
            _dofOn = VisualFeatureTable.Cinematic(preset, CinematicFeature.DepthOfField).On;
            _shadowOn = VisualFeatureTable.Cinematic(preset, CinematicFeature.ShadowRes).On;
            _ssaoOn = VisualFeatureTable.Cinematic(preset, CinematicFeature.AoQuality).Param >= 2;
            _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            _baseShadowDistance = _urp != null ? _urp.shadowDistance : 0f;
            if (_urp != null && _ssaoOn)
                foreach (var data in _urp.rendererDataList)
                    if (data != null)
                        foreach (var f in data.rendererFeatures)
                            if (f is ScreenSpaceAmbientOcclusion) _ssao = f;
            _baseRim = Shader.GetGlobalFloat(StadiumLighting.IdRim);
            _ssao?.SetActive(false);
            if (pitch != null) _grass = new GrassShell(preset, pitch, lighting != null && lighting.Current != null ? lighting.Current.grassShellMaterial : null);
        }

        static Volume AddVolume(GameObject root, VolumeProfile profile, float priority, float weight)
        {
            var v = root.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.sharedProfile = profile;
            v.weight = weight;
            return v;
        }

        bool _warming;

        /// <summary>
        /// 셰이더 워밍업 (FR-85): 로딩 화면 뒤에서 한 프레임 동안 고품질 모드 구성(DoF·SSAO·셸 잔디)을 모두 켠 채로 그려
        /// 변형을 미리 컴파일한다. <see cref="EndWarmUp"/>까지 다음 렌더 프레임 하나를 기다린다.
        /// </summary>
        public void BeginWarmUp()
        {
            _warming = true;
            _mode.weight = _dofOn ? 1f : 0f;
            if (_ssao != null) _ssao.SetActive(true);
            _grass?.Draw(_camera, 1f, 0d, false);
        }

        public void EndWarmUp()
        {
            _warming = false;
            _mode.weight = 0f;
            if (_ssao != null) _ssao.SetActive(false);
            _ssaoActive = false;
        }

        public bool IsWarming => _warming;

        /// <summary>장면이 준비됐을 때 (키 모먼트 재구축 포함). 트랙이 없으면 그 장면은 일반 모드.</summary>
        public void OnSceneBuilt(int sceneId, CameraTrack track, HighlightData data, int subject)
        {
            if (track == null || data == null)
            {
                _timelines.Remove(sceneId);
                return;
            }
            Func<double, Vec3> ball = t => BallAt(data, t);
            Func<double, Vec3> who = subject >= 0 && subject < HighlightData.Players ? t => EntityAt(data, subject, t) : (Func<double, Vec3>)null;
            _timelines[sceneId] = CinematicModeLogic.Build(track, ball, who);
        }

        public CinematicTimeline TimelineOf(int sceneId) => _timelines.TryGetValue(sceneId, out var t) ? t : null;

        static Vec3 BallAt(HighlightData d, double t)
        {
            d.FrameAt((float)t, out int f, out float a);
            return d.Ball[f] + (d.Ball[f + 1] - d.Ball[f]) * a;
        }

        static Vec3 EntityAt(HighlightData d, int e, double t)
        {
            d.FrameAt((float)t, out int f, out float a);
            int i0 = f * HighlightData.Entities + e, i1 = (f + 1) * HighlightData.Entities + e;
            float x = d.X[i0] + (d.X[i1] - d.X[i0]) * a, z = d.Z[i0] + (d.Z[i1] - d.Z[i0]) * a;
            return new Vec3(x, 1.4f, z); // 얼굴·가슴 높이
        }

        /// <summary>매 렌더 프레임: 현재 장면·시뮬 시각으로 모드를 적용한다. 정지 중엔 같은 시각 → 같은 결과.</summary>
        public void Apply(int sceneId, double simTime, HighlightData data)
        {
            using (Marker.Auto())
            {
                _lighting?.SetSimTime(simTime);
                var tl = TimelineOf(sceneId);
                float w = tl != null && !ForceNormal ? tl.Weight(simTime) : 0f;
                Weight = w;
                _mode.weight = _dofOn ? w : 0f;
                if (_dofOn && w > 0f && _dof != null)
                {
                    var focus = tl.Focus(simTime);
                    float dist = Mathf.Max(1f, Vector3.Distance(_camera.transform.position, new Vector3(focus.X, focus.Y, focus.Z)));
                    _dof.gaussianStart.value = dist + LookParameters.DofStartBehindFocus;
                    _dof.gaussianEnd.value = dist + LookParameters.DofEndBehindFocus;
                }
                if (_urp != null && _shadowOn && _baseShadowDistance > 0f)
                    _urp.shadowDistance = Mathf.Lerp(_baseShadowDistance, _baseShadowDistance * ShadowDistanceScale, w);
                bool ssao = _ssaoOn && w >= 0.5f;
                if (_ssao != null && ssao != _ssaoActive)
                {
                    _ssao.SetActive(ssao);
                    _ssaoActive = ssao;
                }
                Shader.SetGlobalFloat(StadiumLighting.IdRim, _baseRim * Mathf.Lerp(1f, RimBoost, w));
                _grass?.Draw(_camera, w, simTime, SnowCovered);
                TopTierCount = 0;
                if (w > 0f && data != null)
                {
                    _players.Clear();
                    int f = data.FrameIndex((float)simTime);
                    for (int e = 0; e < HighlightData.Players; e++)
                    {
                        int i = f * HighlightData.Entities + e;
                        _players.Add(data.Present[i] ? new Vec3(data.X[i], 1f, data.Z[i]) : new Vec3(1e6f, 0f, 1e6f));
                    }
                    var c = _camera.transform.position;
                    TopTierCount = CinematicModeLogic.TopTier(new Vec3(c.x, c.y, c.z), _players,
                        VisualFeatureTable.Cinematic(_preset, CinematicFeature.PlayerTopTier).Param, TopTier);
                }
            }
        }

        public void Dispose()
        {
            if (_urp != null && _baseShadowDistance > 0f) _urp.shadowDistance = _baseShadowDistance;
            _ssao?.SetActive(true); // 에셋 기본(켬)으로 되돌림 — 빌드 셰이더 보존용, 하이라이트 밖에는 3D가 없다
            _grass?.Dispose();
            Shader.SetGlobalFloat(StadiumLighting.IdRim, _baseRim);
            ReleaseProfile(_mode != null ? _mode.sharedProfile : null);
            ReleaseProfile(_vignette != null ? _vignette.sharedProfile : null);
            Release(_root);
            _timelines.Clear();
        }

        static void ReleaseProfile(VolumeProfile p)
        {
            if (p == null) return;
            foreach (var c in p.components) Release(c);
            Release(p);
        }

        static void Release(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
