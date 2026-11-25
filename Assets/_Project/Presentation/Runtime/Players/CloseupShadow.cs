using System.Collections.Generic;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 088 T30i: 클로즈업 전용 그림자 맵. 경기장 그림자 맵은 얼굴 위 ≈ 3 mm/칸이라 코 그림자가 칼로 벤 직선·셀프 섀도 얼룩이 됐다(T30h).
    /// 클로즈업 머리(동시 최대 2명, Head_Closeup + 헤어 LOD0)와 최고 등급 LOD0 선수(T30o, 등급 합 최대 3명)만 태양 방향 정사영으로 2048² 깊이 맵에 그려
    /// 클로즈업 얼굴 피부와 최고 등급 LOD0 피부가 이 맵을 읽는다(PlayerBody.shader).
    /// 카메라 렌더 직전(스키닝 뒤)에 프레임당 1번. 리소스는 첫 사용 때 1번 만들고 프레임 경로 할당 없음.
    /// 전역: _CloseupShadowMap, _CloseupShadowMatrix(월드 → 맵 UV·깊이), _CloseupShadowParams(x 켬, y 텍셀 크기 m).
    /// </summary>
    public static class CloseupShadow
    {
        static readonly int IdMap = Shader.PropertyToID("_CloseupShadowMap");
        static readonly int IdMatrix = Shader.PropertyToID("_CloseupShadowMatrix");
        static readonly int IdParams = Shader.PropertyToID("_CloseupShadowParams");
        static readonly int IdShadowBias = Shader.PropertyToID("_ShadowBias");
        static readonly int IdLightDirection = Shader.PropertyToID("_LightDirection");
        /// <summary>캐스터 깊이·법선 편향 (텍셀 배수) [제안] — URP와 같은 부호(빛에서 멀어지게·안쪽으로).</summary>
        public const float DepthBiasTexels = 4f, NormalBiasTexels = 4f;

        static readonly List<PlayerRig> Rigs = new List<PlayerRig>(4);
        static readonly float[] Pos = new float[3 * QualityTierPolicy.MaxTier];
        static readonly PlayerRig[] Drawn = new PlayerRig[QualityTierPolicy.MaxTier];
        static readonly List<Material> Mats = new List<Material>(4);
        static RenderTexture _map;
        static CommandBuffer _cmd;
        static bool _hooked;
        static int _lastFrame = -1;
        static Light _sun;

        /// <summary>마지막 프레임에 그린 머리 수 (검증용).</summary>
        public static int LastDrawn { get; private set; }

        public static void Register(PlayerRig rig)
        {
            if (rig == null || Rigs.Contains(rig)) return;
            Rigs.Add(rig);
            if (!_hooked)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                _hooked = true;
            }
        }

        public static void Unregister(PlayerRig rig)
        {
            Rigs.Remove(rig);
            if (Rigs.Count == 0) Shader.SetGlobalVector(IdParams, Vector4.zero);
        }

        static Light Sun()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled) return RenderSettings.sun;
            if (_sun != null && _sun.isActiveAndEnabled && _sun.type == LightType.Directional) return _sun;
            _sun = null;
            float best = -1f;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))   // 태양이 바뀌었을 때만
                if (l.type == LightType.Directional && l.isActiveAndEnabled && l.intensity > best) { best = l.intensity; _sun = l; }
            return _sun;
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            LastDrawn = 0;
            int n = 0;
            for (int i = Rigs.Count - 1; i >= 0; i--)
            {
                var r = Rigs[i];
                if (r == null) { Rigs.RemoveAt(i); continue; }
                if (n >= Drawn.Length || !r.isActiveAndEnabled) continue;
                // 지난 프레임에 그려진(스키닝된) 렌더러만 — 막 켠 클로즈업 머리는 스킨 버퍼가 없어 DrawRenderer가
                // "mesh data size … does not match" 에러로 멈췄다. 켠 첫 프레임 하나만 얼굴 자기 그림자가 없다
                var caster = Caster(r);
                if (caster == null || !caster.isVisible) continue;
                var head = r.Bone("Head");
                var p = head != null ? head.position : r.transform.position;
                Pos[n * 3] = p.x; Pos[n * 3 + 1] = p.y; Pos[n * 3 + 2] = p.z;
                Drawn[n++] = r;
            }
            var sun = Sun();
            if (n == 0 || sun == null || !CloseupShadowFit.Fit(Pos, n, out float cx, out float cy, out float cz, out float radius))
            {
                Shader.SetGlobalVector(IdParams, Vector4.zero);
                return;
            }
            Ensure();
            var c = new Vector3(cx, cy, cz);
            var rot = sun.transform.rotation;
            var fwd = rot * Vector3.forward;
            float back = radius + 2f;   // 머리 위 헤어·어깨까지 + 여유
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(c - fwd * back, rot, Vector3.one).inverse;
            var proj = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0.01f, back + radius + 0.5f);
            float texel = CloseupShadowFit.TexelSize(radius, CloseupShadowFit.Resolution);

            _cmd.Clear();
            _cmd.SetRenderTarget(_map);
            _cmd.ClearRenderTarget(true, false, Color.clear);
            _cmd.SetViewProjectionMatrices(view, proj);
            _cmd.SetGlobalVector(IdShadowBias, new Vector4(-DepthBiasTexels * texel, -NormalBiasTexels * texel, 0f, 0f));
            _cmd.SetGlobalVector(IdLightDirection, -fwd);
            for (int k = 0; k < n; k++)
            {
                var r = Drawn[k];
                DrawCaster(Caster(r));
                // 클로즈업 중에도 LOD0 몸·키트(머리는 HideHead로 접힘)를 같이 — 목·어깨에 깃·턱 그늘 (T30o)
                if (r.CloseupActive && r.Lod(0) != null && r.Lod(0).isVisible) DrawCaster(r.Lod(0));
                Drawn[k] = null;
            }
            _cmd.SetGlobalTexture(IdMap, _map);
            _cmd.SetGlobalMatrix(IdMatrix, ShadowTransform(proj, view));
            _cmd.SetGlobalVector(IdParams, new Vector4(1f, texel, 0f, 0f));
            ctx.ExecuteCommandBuffer(_cmd);
            ctx.Submit();
            LastDrawn = n;
        }

        static void DrawCaster(SkinnedMeshRenderer smr)
        {
            smr.GetSharedMaterials(Mats);
            int subs = smr.sharedMesh != null ? smr.sharedMesh.subMeshCount : 0;
            for (int s = 0; s < subs && s < Mats.Count; s++)
            {
                var m = Mats[s];
                int pass = m != null ? m.FindPass("ShadowCaster") : -1;
                if (pass >= 0) _cmd.DrawRenderer(smr, m, s, pass);
            }
        }

        /// <summary>캐스터: 클로즈업이면 클로즈업 머리(머리·헤어·눈썹), 최고 등급 LOD0이면 LOD0 렌더러(몸·헤어·키트 — 088 T30o). 둘 다 아니면 null.</summary>
        static SkinnedMeshRenderer Caster(PlayerRig r)
        {
            if (r.CloseupActive) return r.CloseupRenderer;
            return r.TopTierPinned ? r.Lod(0) : null;
        }

        /// <summary>URP ShadowUtils.GetShadowTransform과 같은 변환: 월드 → (u, v, 깊이) [0, 1].</summary>
        static Matrix4x4 ShadowTransform(Matrix4x4 proj, Matrix4x4 view)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                proj.m20 = -proj.m20; proj.m21 = -proj.m21; proj.m22 = -proj.m22; proj.m23 = -proj.m23;
            }
            var ts = Matrix4x4.identity;
            ts.m00 = 0.5f; ts.m11 = 0.5f; ts.m22 = 0.5f;
            ts.m03 = 0.5f; ts.m13 = 0.5f; ts.m23 = 0.5f;
            return ts * proj * view;
        }

        static void Ensure()
        {
            if (_map == null)
            {
                _map = new RenderTexture(CloseupShadowFit.Resolution, CloseupShadowFit.Resolution, 32, RenderTextureFormat.Shadowmap)
                {
                    name = "088 Closeup Shadow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
                };
                _map.Create();
            }
            if (_cmd == null) _cmd = new CommandBuffer { name = "088 Closeup Shadow" };
        }
    }
}
