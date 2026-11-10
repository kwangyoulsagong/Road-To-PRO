using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>
    /// Low 프리셋 선수 발밑 블롭 그림자 (specs/048 FR-01 Low: "발밑 블롭 섀도 (나이트 경기 4장)").
    /// 낮: 선수마다 1장, 태양 반대쪽으로 고도에 따라 조금 늘린다. 나이트: 광원 4개마다 1장 (<see cref="BlobShadow4.Compute"/> 재사용).
    /// 공유 머티리얼 + GPU 인스턴싱 한 번에 그린다 (최대 25명 × 4장 = 100 인스턴스 → 드로우콜 1). 프레임 경로 할당 없음.
    /// </summary>
    public sealed class PlayerBlobShadows : MonoBehaviour
    {
        public const int MaxTargets = 25;
        public const int NightBlobs = 4;
        public const float DayWidth = 1.0f;
        /// <summary>낮 그림자 최대 길이 (해가 낮을 때).</summary>
        public const float DayMaxLength = 1.8f;
        /// <summary>
        /// 중계 거리(40~80 m)에서 보이는 진하기. 044 근접용 블롭(중심 알파 0.55, a² 감쇠)은 선수 크기에서 거의 안 보여
        /// 같은 셰이더로 하이라이트용 텍스처를 만든다. 나이트는 4장이 겹치므로 장당 더 옅게.
        /// </summary>
        public const float DayAlpha = 0.75f, NightAlpha = 0.45f;
        const int TexSize = 64;
        const float GroundOffset = 0.03f;

        Transform[] _targets = Array.Empty<Transform>();
        Mesh _quad;
        bool _ownQuad;
        Texture2D _tex;
        Material _material;
        bool _night;
        Vector3 _sunForward = new Vector3(0.3f, -0.8f, 0.5f);
        Vector3[] _emitters = Array.Empty<Vector3>();
        readonly BlobShadowPose[] _poses = new BlobShadowPose[NightBlobs];
        readonly Matrix4x4[] _matrices = new Matrix4x4[MaxTargets * NightBlobs];

        public bool IsNight => _night;
        /// <summary>마지막 프레임에 그린 블롭 수 (검증용).</summary>
        public int LastCount { get; private set; }

        /// <param name="nightEmitters">null이면 낮. 4개 미만이면 낮 방식으로 그리고 진단 1건.</param>
        public void Configure(IReadOnlyList<Transform> targets, Mesh quad, Material material, Vector3 sunForward,
            IReadOnlyList<Vector3> nightEmitters, Action<string> diagnostic)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            int n = Math.Min(targets.Count, MaxTargets);
            if (targets.Count > MaxTargets) diagnostic?.Invoke($"블롭 대상 {targets.Count}명 중 {MaxTargets}명만 그린다");
            _targets = new Transform[n];
            for (int i = 0; i < n; i++) _targets[i] = targets[i];
            if (_ownQuad && _quad != null && _quad != quad) Release(_quad);
            _ownQuad = quad == null;
            _quad = quad != null ? quad : CreateQuad();
            if (_material != null) Release(_material);
            _material = null;
            if (material != null)
            {
                if (_tex == null) _tex = CreateBlobTexture();
                _material = new Material(material) { name = material.name + " (Highlight)", enableInstancing = true };
                _material.SetTexture("_BaseMap", _tex);
            }
            _sunForward = sunForward;
            _night = false;
            if (nightEmitters != null)
            {
                if (nightEmitters.Count >= NightBlobs)
                {
                    _night = true;
                    _emitters = new Vector3[nightEmitters.Count];
                    for (int i = 0; i < _emitters.Length; i++) _emitters[i] = nightEmitters[i];
                }
                else diagnostic?.Invoke($"나이트 광원 {nightEmitters.Count}개 (4개 필요): 낮 블롭으로 그린다");
            }
            if (_material != null) _material.SetColor("_BaseColor", new Color(1f, 1f, 1f, _night ? NightAlpha : DayAlpha));
        }

        /// <summary>검정 원형 블롭, 가운데 알파 1에서 가장자리 0으로 부드럽게 (smoothstep). 결정론: 수식.</summary>
        public static Texture2D CreateBlobTexture()
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { name = "BlobHighlight", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
                for (int x = 0; x < TexSize; x++)
                {
                    float u = (x + 0.5f) / TexSize * 2f - 1f, v = (y + 0.5f) / TexSize * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = 1f - Mathf.SmoothStep(0.35f, 1f, r);
                    px[y * TexSize + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// 내장 Quad와 같은 모양(XY 평면 1×1, 법선 −Z)의 쿼드. 플레이어 빌드에서 내장 리소스 조회가 null일 수 있어 코드로 만든다.
        /// </summary>
        public static Mesh CreateQuad()
        {
            var m = new Mesh { name = "BlobQuad" };
            m.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) });
            m.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
            m.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetTriangles(new[] { 0, 3, 1, 3, 0, 2 }, 0);
            m.RecalculateBounds();
            return m;
        }

        void OnDestroy()
        {
            if (_ownQuad && _quad != null) Release(_quad);
            if (_material != null) Release(_material);
            if (_tex != null) Release(_tex);
        }

        static void Release(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        /// <summary>낮 블롭 1장: 태양 반대 방향으로 고도가 낮을수록 길게 (최대 <see cref="DayMaxLength"/>).</summary>
        public static BlobShadowPose DayPose(Vector3 foot, Vector3 sunForward)
        {
            var flat = new Vector3(sunForward.x, 0f, sunForward.z);
            float horizontal = flat.magnitude;
            float down = Mathf.Max(1e-3f, -sunForward.y);
            var dir = horizontal > 1e-6f ? flat / horizontal : Vector3.forward;
            // 키 약 1.8 m의 그림자 길이 비율(수평/수직)을 블롭 길이로 줄여 쓴다
            float length = Mathf.Clamp(DayWidth + 0.35f * horizontal / down, DayWidth, DayMaxLength);
            return new BlobShadowPose
            {
                Direction = dir,
                Center = new Vector3(foot.x, 0f, foot.z) + dir * ((length - DayWidth) * 0.5f),
                Length = length,
                Width = DayWidth,
            };
        }

        static Matrix4x4 ToMatrix(in BlobShadowPose p)
        {
            var rot = Quaternion.LookRotation(p.Direction, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            return Matrix4x4.TRS(p.Center + Vector3.up * GroundOffset, rot, new Vector3(p.Width, p.Length, 1f));
        }

        void LateUpdate()
        {
            LastCount = 0;
            if (_quad == null || _material == null) return;
            int count = 0;
            for (int i = 0; i < _targets.Length; i++)
            {
                var t = _targets[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                var foot = t.position;
                if (_night)
                {
                    BlobShadow4.Compute(foot, _emitters, _poses, BlobShadow4.DefaultLength, BlobShadow4.DefaultWidth);
                    for (int k = 0; k < NightBlobs; k++) _matrices[count++] = ToMatrix(_poses[k]);
                }
                else
                {
                    var p = DayPose(foot, _sunForward);
                    _matrices[count++] = ToMatrix(p);
                }
            }
            if (count == 0) return;
            Graphics.DrawMeshInstanced(_quad, 0, _material, _matrices, count, null, ShadowCastingMode.Off, false);
            LastCount = count;
        }
    }
}
