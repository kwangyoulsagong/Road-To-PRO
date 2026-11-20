using System.Collections.Generic;
using RoadToPro.Presentation.Highlight;
using RoadToPro.Presentation.Rendering;
using RoadToPro.Presentation.Stadium;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadToPro.Verification.Samples.Highlight
{
    /// <summary>
    /// specs/061 FR-01 기준 장면 S1~S6과 화면 지표용 캡처 도구. 042 합성 골 장면(broadcastPlay)을 정해진 시각에 멈추고,
    /// 필요한 장면만 카메라를 고정 값으로 덮는다(S1 FOV 30°, S2 FOV 12°, S6 코너 하이앵글, S7 피치 레벨 저각).
    /// 낮 장면과 나이트 장면은 무대를 다시 올려야 하므로 시나리오가 나눠 부른다.
    /// 화면:
    /// - visual061.day / night / S1~S7 / release
    /// - visual061.mask / unmask: 같은 프레임을 분류 색으로 (하늘 자홍·경기장 밖 파랑·잔디 초록·선수 빨강·구조물 회색, 후처리 끔)
    /// - visual061.ss.on / off: 렌더 스케일 2.0(4배 픽셀) 슈퍼샘플 기준 (FR-60)
    /// - visual061.normal / auto: 고품질 모드 강제 끔 / 원래대로 (FR-84 낙차)
    /// - visual061.pan.0~4: 느린 팬(2°/s, 30 fps 한 프레임 = 0.067°) 연속 프레임 (FR-60 깜빡임)
    /// 캡처 영역(regions): scorer(득점자 화면 사각형), feet([[x, y, r]] 발 위치·0.3 m 반지름 px), groundBand(지면 경계 띠).
    /// </summary>
    [DefaultExecutionOrder(1000)] // HighlightScreen(200)·방송 카메라 뒤에 덮는다
    public sealed class Visual061Screen : MonoBehaviour
    {
        /// <summary>S6 코너 하이앵글 [제안]: 경기장 원점(센터 서클) 기준, 042 금지 영역 밖.</summary>
        static readonly Vector3 CornerPos = new Vector3(-62f, 28f, -46f);
        /// <summary>S7 피치 레벨 저각 [제안] (AC-08 지면 경계선): 공 옆 0.6 m 높이.</summary>
        static readonly Vector3 PitchLevelPos = new Vector3(-3f, 0.6f, -4f);
        const float CornerFov = 50f, S1Fov = 30f, S2Fov = 12f, PitchLevelFov = 40f, PanDegPerStep = 2f / 30f;

        static readonly Color MaskSky = new Color(1f, 0f, 1f), MaskGrass = new Color(0f, 1f, 0f), MaskPlayer = new Color(1f, 0f, 0f),
            MaskStructure = new Color(0.5f, 0.5f, 0.5f);

        Camera _cam;
        HighlightPlayer _player;
        bool _override, _keepPose;
        Vector3 _pos;
        Quaternion _rot;
        float _fov;
        float _panBase;
        bool _panning;

        readonly List<(Renderer r, Material[] mats)> _saved = new List<(Renderer, Material[])>();
        readonly Dictionary<Color, Material> _maskMats = new Dictionary<Color, Material>();
        bool _masked, _postBefore, _ss;
        float _scaleBefore;
        UpscalingFilterSelection _filterBefore;

        public static Visual061Screen Attach(GameObject host, Camera cam, HighlightPlayer player, System.Action playBroadcast)
        {
            var s = host.GetComponent<Visual061Screen>() ?? host.AddComponent<Visual061Screen>();
            s._cam = cam;
            s._player = player;
            FarmContext.Screens["visual061.day"] = () => { s.ReleaseAll(); HighlightCastStage.NightRequested = false; playBroadcast(); };
            FarmContext.Screens["visual061.night"] = () => { s.ReleaseAll(); HighlightCastStage.NightRequested = true; playBroadcast(); };
            // 메인 중계(골 장면 앞 빌드업), 리플레이 ①(골대 뒤 하이앵글)·②(슈터 추적), 득점자 클로즈업 — 042 화면과 같은 시각
            FarmContext.Screens["visual061.S1"] = () => s.Freeze(0, BroadcastScenes.GoalTime - 4d, S1Fov);
            FarmContext.Screens["visual061.S2"] = () => s.Freeze(0, BroadcastScenes.GoalTime - 4d, S2Fov);
            FarmContext.Screens["visual061.S3"] = () => s.Freeze(BroadcastScenes.FirstReplayIndex, BroadcastScenes.ShotTime - 0.3d, 0f);
            FarmContext.Screens["visual061.S4"] = () => s.Freeze(0, BroadcastScenes.GoalTime + 1.2d, 0f);
            FarmContext.Screens["visual061.S5"] = () => s.Freeze(BroadcastScenes.FirstReplayIndex + 1, BroadcastScenes.ShotTime - 0.3d, 0f);
            FarmContext.Screens["visual061.S6"] = () =>
            {
                s.Freeze(0, BroadcastScenes.GoalTime - 4d, 0f);
                s.Pin(CornerPos, Quaternion.LookRotation(-CornerPos.normalized, Vector3.up), CornerFov);
            };
            FarmContext.Screens["visual061.S7"] = () =>
            {
                // 고품질 모드 클로즈업 시각에 피치 레벨 저각 카메라 (잎 실루엣이 지평선에 걸리게)
                s.Freeze(0, BroadcastScenes.GoalTime + 1.2d, 0f);
                s.Pin(PitchLevelPos, Quaternion.LookRotation(new Vector3(0.4f, -0.02f, 1f).normalized, Vector3.up), PitchLevelFov);
            };
            FarmContext.Screens["visual061.release"] = s.Release;
            FarmContext.Screens["visual061.mask"] = s.Mask;
            FarmContext.Screens["visual061.unmask"] = s.Unmask;
            FarmContext.Screens["visual061.ss.on"] = () => s.SuperSample(true);
            FarmContext.Screens["visual061.ss.off"] = () => s.SuperSample(false);
            FarmContext.Screens["visual061.normal"] = () => s.ForceNormal(true);
            FarmContext.Screens["visual061.auto"] = () => s.ForceNormal(false);
            for (int k = 0; k < 5; k++)
            {
                int step = k;
                FarmContext.Screens["visual061.pan." + step] = () => s.Pan(step);
            }
            FarmContext.CaptureRegions = s.Regions;
            // 061 FR-85·AC-14: 고품질 모드 진입·퇴장 뒤 1 s 프레임 시간 (밖은 null → farm이 outsideModeTransitionFrameMs를 만든다), 전환 수
            FarmContext.PerfCounters["modeTransitionFrameMs"] = () => s._sinceChange <= 1d ? s._frameMs : (double?)null;
            FarmContext.PerfCounters["modeTransitions"] = () => s._transitions;
            FarmContext.PerfCounters["cinematicWeight"] = () => s._player.Cinematic != null ? s._player.Cinematic.Weight : 0d;
            return s;
        }

        void Freeze(int sceneIndex, double time, float fov)
        {
            Release();
            _player.Sequencer.SeekAndPause(sceneIndex, time);
            RenderQualityApplier.ResetHistory(_cam); // STP 히스토리를 같은 상태에서 시작 — 같은 장면 반복 캡처 동일 (AC-10)
            if (fov > 0f)
            {
                _keepPose = true;
                _fov = fov;
                _override = true;
            }
        }

        void Pin(Vector3 pos, Quaternion rot, float fov)
        {
            _keepPose = false;
            _pos = pos;
            _rot = rot;
            _fov = fov;
            _override = true;
        }

        /// <summary>현재 카메라 방향에서 시작해 수평으로 step × 0.067° 돌린다 (느린 팬).</summary>
        void Pan(int step)
        {
            if (!_panning)
            {
                _panning = true;
                _pos = _cam.transform.position;
                _rot = _cam.transform.rotation;
                _fov = _cam.fieldOfView;
                _panBase = _rot.eulerAngles.y;
            }
            var e = _rot.eulerAngles;
            _rot = Quaternion.Euler(e.x, _panBase + step * PanDegPerStep, e.z);
            _keepPose = false;
            _override = true;
        }

        void Release()
        {
            _override = false;
            _keepPose = false;
            _panning = false;
        }

        void ReleaseAll()
        {
            Release();
            Unmask();
            SuperSample(false);
            ForceNormal(false);
        }

        void ForceNormal(bool on)
        {
            if (_player.Cinematic != null) _player.Cinematic.ForceNormal = on;
        }

        Material MaskMat(Color c)
        {
            if (_maskMats.TryGetValue(c, out var m)) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "061 mask", hideFlags = HideFlags.DontSave };
            m.SetColor("_BaseColor", c);
            _maskMats[c] = m;
            return m;
        }

        void Mask()
        {
            if (_masked || _player.Assets == null || _player.Assets.Stage == null) return;
            _masked = true;
            var stage = _player.Assets.Stage;
            var players = new HashSet<Renderer>();
            foreach (var rig in stage.Rigs)
                if (rig != null)
                    foreach (var r in rig.GetComponentsInChildren<Renderer>(true)) players.Add(r);
            var pitch = stage.PitchRenderer;
            foreach (var root in stage.gameObject.scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var c = players.Contains(r) ? MaskPlayer : r == pitch ? MaskGrass : MaskStructure;
                    var mats = r.sharedMaterials;
                    _saved.Add((r, mats));
                    var mm = new Material[mats.Length];
                    for (int i = 0; i < mm.Length; i++) mm[i] = MaskMat(c);
                    r.sharedMaterials = mm;
                }
            if (RenderSettings.skybox != null) RenderSettings.skybox.EnableKeyword("_SKY_MASK");
            var data = _cam.GetUniversalAdditionalCameraData();
            _postBefore = data.renderPostProcessing;
            data.renderPostProcessing = false;
            if (_player.Assets.Blobs != null) _player.Assets.Blobs.enabled = false;
            ForceNormal(true); // 셸 잔디·DoF 없이
        }

        void Unmask()
        {
            if (!_masked) return;
            _masked = false;
            foreach (var (r, mats) in _saved)
                if (r != null) r.sharedMaterials = mats;
            _saved.Clear();
            if (RenderSettings.skybox != null) RenderSettings.skybox.DisableKeyword("_SKY_MASK");
            _cam.GetUniversalAdditionalCameraData().renderPostProcessing = _postBefore;
            if (_player.Assets != null && _player.Assets.Blobs != null) _player.Assets.Blobs.enabled = true;
            ForceNormal(false);
        }

        void SuperSample(bool on)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null || on == _ss) return;
            _ss = on;
            if (on)
            {
                _scaleBefore = urp.renderScale;
                _filterBefore = urp.upscalingFilter;
                urp.upscalingFilter = UpscalingFilterSelection.Auto;
                urp.renderScale = 2f; // 4배 픽셀로 그려 내려 받는다 = 슈퍼샘플 기준
            }
            else
            {
                urp.renderScale = _scaleBefore;
                urp.upscalingFilter = _filterBefore;
            }
            RenderQualityApplier.ResetHistory(_cam);
        }

        /// <summary>캡처 영역: 득점자 사각형, 발 위치(0.3 m 반지름 px), 지면 경계 띠(화면 가운데 가로 띠).</summary>
        Dictionary<string, object> Regions(int w, int h)
        {
            if (_player == null || !_player.IsLoaded || _player.Assets?.Stage == null) return null;
            var stage = _player.Assets.Stage;
            var regions = new Dictionary<string, object>();
            var feet = new List<object>();
            for (int e = 0; e < stage.Rigs.Length; e++)
            {
                var rig = stage.Rigs[e];
                if (rig == null || !rig.gameObject.activeInHierarchy) continue;
                var foot = rig.transform.position;
                var a = _cam.WorldToViewportPoint(foot);
                // 바닥의 0.3 m 원은 화면에서 타원 — 카메라 오른쪽(가로)·바닥 앞쪽(세로) 반지름을 따로 넘긴다
                var right = _cam.transform.right;
                var fwd = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
                var b = _cam.WorldToViewportPoint(foot + new Vector3(right.x, 0f, right.z).normalized * 0.3f);
                var c2 = _cam.WorldToViewportPoint(foot + fwd * 0.3f);
                bool footIn = a.z > 0f && a.x >= 0.05f && a.x <= 0.95f && a.y >= 0.05f && a.y <= 0.95f;
                float r = Mathf.Abs(b.x - a.x) * w, ry = Mathf.Abs(c2.y - a.y) * h;
                if (footIn && r >= 3f && ry >= 1f) feet.Add(new List<object> { (double)(a.x * w), (double)((1f - a.y) * h), (double)r, (double)ry });
                if (e == BroadcastScenes.Scorer && a.z > 0f)
                {
                    // 몸통만 (가슴~허리) — 넓게 잡으면 흐려진 배경이 섞여 선명도가 낮게 잡힌다
                    var bounds = new Bounds(foot + Vector3.up * 1.15f, new Vector3(0.3f, 0.5f, 0.2f));
                    Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        var v = _cam.WorldToViewportPoint(corner);
                        lo = Vector2.Min(lo, new Vector2(v.x * w, (1f - v.y) * h));
                        hi = Vector2.Max(hi, new Vector2(v.x * w, (1f - v.y) * h));
                    }
                    if (hi.x > 0 && lo.x < w && hi.y > 0 && lo.y < h)
                        regions["scorer"] = new List<object> { (double)Mathf.Max(0f, lo.x), (double)Mathf.Max(0f, lo.y), (double)(Mathf.Min(w, hi.x) - Mathf.Max(0f, lo.x)), (double)(Mathf.Min(h, hi.y) - Mathf.Max(0f, lo.y)) };
                }
            }
            regions["feet"] = feet;
            var blobs = _player.Assets.Blobs;
            var cp = Shader.GetGlobalVector("_ContactParams");
            var f0 = Shader.GetGlobalVectorArray("_ContactFeet");
            regions["contact"] = new List<object> { (double)cp.x, (double)cp.y, (double)cp.z, f0 != null && f0.Length > 0 ? (double)f0[0].x : -999d, f0 != null && f0.Length > 0 ? (double)f0[0].z : -999d };
            regions["blobs"] = blobs != null ? (object)new List<object> { (double)blobs.LastCount, (double)blobs.LastContactCount, blobs.enabled ? 1d : 0d } : null;
            regions["groundBand"] = new List<object> { (double)(w * 0.05f), (double)(h * 0.25f), (double)(w * 0.9f), (double)(h * 0.5f) };
            return regions;
        }

        double _sinceChange = double.MaxValue, _frameMs;
        float _lastWeight;
        int _transitions;

        void Update()
        {
            _frameMs = Time.unscaledDeltaTime * 1000d;
            var c = _player != null ? _player.Cinematic : null;
            float w = c != null ? c.Weight : 0f;
            if (Mathf.Abs(w - _lastWeight) > 1e-3f)
            {
                if ((w >= 0.5f) != (_lastWeight >= 0.5f)) _transitions++;
                _sinceChange = 0d;
            }
            else if (_sinceChange < double.MaxValue) _sinceChange += Time.unscaledDeltaTime;
            _lastWeight = w;
        }

        void LateUpdate()
        {
            if (!_override || _cam == null || _player == null || _player.Current == null) return;
            if (!_keepPose) _cam.transform.SetPositionAndRotation(_pos, _rot);
            _cam.fieldOfView = _fov;
        }

        void OnDestroy()
        {
            FarmContext.PerfCounters.Remove("modeTransitionFrameMs");
            FarmContext.PerfCounters.Remove("modeTransitions");
            FarmContext.PerfCounters.Remove("cinematicWeight");
            ReleaseAll();
            foreach (var m in _maskMats.Values) if (m != null) Destroy(m);
            _maskMats.Clear();
        }
    }
}
