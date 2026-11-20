using System;
using RoadToPro.Presentation.Rendering.Logic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>
    /// specs/061 FR-50~58: 연출 장면 근거리 입체 잔디 (셸). 카메라 앞 격자 한 장을 셸 높이만큼 겹쳐 그리고,
    /// 셰이더(`RoadToPro/GrassShell`)가 잎 모양만 남긴다. 색·줄무늬·라인은 피치 셰이더와 같은 텍스처·데이터를 쓴다.
    /// 흔들림·잎 방향은 시뮬 시각·위치만의 함수 (아래 C# 식 = 셰이더 식). 일반 모드는 그리지 않는다 (FR-51).
    /// </summary>
    public sealed class GrassShell : IDisposable
    {
        /// <summary>잎 높이 m [제안] (FR-52 ≤ 3 cm).</summary>
        public const float LeafHeight = 0.028f;
        /// <summary>격자 셀 m — 카메라가 움직여도 격자가 미끄러지지 않게 이 단위로 붙인다.</summary>
        public const float SnapMeters = 0.5f;
        /// <summary>격자 칸 m — 셸 면은 평평해 정점이 많을 필요가 없다 (T20: 0.5 m 칸은 High 셸 12겹 ≈ 15만 삼각형 → 2 m 칸 ≈ 1만).</summary>
        public const float CellMeters = 2f;
        /// <summary>흔들림 폭 = 잎 높이 배율 (FR-55 ≤ 20%).</summary>
        public const float WindAmplitude = 0.18f;
        const float WindPeriod = 4.7f;
        public const string ShaderName = "RoadToPro/GrassShell";

        static readonly int IdShellParams = Shader.PropertyToID("_ShellParams");   // (잎 높이, 카메라 페이드 시작, 반경, 가중치)
        static readonly int IdWindPhase = Shader.PropertyToID("_WindPhase");       // (0~1 위상 두 개, 진폭, 0)

        public static int Shells(PresetId p) => VisualFeatureTable.Cinematic(p, CinematicFeature.NearGrass).Param;
        public static float Radius(PresetId p) => VisualFeatureTable.GrassRadius(p);

        /// <summary>카메라 거리별 잎 남김 비율 (반경 안 1 → 반경에서 0, smoothstep).</summary>
        public static float Fade(float distance, PresetId p)
        {
            float a = VisualFeatureTable.GrassFadeStart(p), b = Radius(p);
            if (distance <= a) return 1f;
            if (distance >= b) return 0f;
            float t = (distance - a) / (b - a);
            return 1f - t * t * (3f - 2f * t);
        }

        public static bool ShouldDraw(PresetId p, float cinematicWeight, bool snowCovered)
            => cinematicWeight > 0f && !snowCovered && VisualFeatureTable.Cinematic(p, CinematicFeature.NearGrass).On && Shells(p) > 0;

        /// <summary>잎이 눕는 방향 = 044 깎기 띠 방향 (Pitch.shader StripeShade와 같은 식).</summary>
        public static Vector2 LeanDirection(Vector2 p, float stripeWidth, int pattern)
        {
            float w = Mathf.Max(stripeWidth, 0.1f);
            if (pattern == 2)
            {
                float band = Mathf.Floor(p.magnitude / w);
                float s = band % 2f < 0.5f ? 1f : -1f;
                var r = p + new Vector2(1e-4f, 1e-4f);
                return r.normalized * s;
            }
            float bx = Mathf.Floor(p.x / w);
            float parity = pattern == 1 ? bx + Mathf.Floor(p.y / w) : bx;
            return new Vector2(Mathf.Abs(parity % 2f) < 0.5f ? 1f : -1f, 0f);
        }

        /// <summary>바람 위상 (0~1로 감쌈 — double 시뮬 시각, 경기 끝에서도 정밀도 유지).</summary>
        public static Vector2 WindPhase(double simTime)
        {
            double a = simTime / WindPeriod, b = simTime / (WindPeriod * 1.63d);
            return new Vector2((float)(a - Math.Floor(a)), (float)(b - Math.Floor(b)));
        }

        /// <summary>위치의 흔들림 (잎 높이 배율, 크기 ≤ <see cref="WindAmplitude"/>). 셰이더 GrassWind와 같은 식.</summary>
        public static Vector2 Wind(double simTime, Vector2 p)
        {
            var ph = WindPhase(simTime);
            const float tau = Mathf.PI * 2f;
            float u = Mathf.Sin((ph.x + (p.x * 0.11f + p.y * 0.07f)) * tau);
            float v = Mathf.Sin((ph.y + (p.x * 0.05f - p.y * 0.13f)) * tau);
            var d = new Vector2(0.8f * u + 0.2f * v, 0.3f * v) * WindAmplitude;
            float m = d.magnitude;
            return m > WindAmplitude ? d * (WindAmplitude / m) : d;
        }

        /// <summary>격자 중심: 카메라 바닥 위치에서 시선 쪽으로 반경의 절반, 셀 단위로 붙임.</summary>
        public static Vector3 PatchCenter(Vector3 camera, Vector3 forward, PresetId p)
        {
            var f = new Vector3(forward.x, 0f, forward.z);
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            var c = new Vector3(camera.x, 0f, camera.z) + f * (Radius(p) * 0.5f);
            return new Vector3(Mathf.Round(c.x / SnapMeters) * SnapMeters, 0f, Mathf.Round(c.z / SnapMeters) * SnapMeters);
        }

        // ── 그리기 ──

        readonly PresetId _preset;
        readonly Mesh _patch;
        readonly Material _material;
        readonly MaterialPropertyBlock _props = new MaterialPropertyBlock();
        readonly Renderer _pitch;
        readonly int _shells;
        readonly Matrix4x4[] _matrices;
        bool _warned;

        public int LastShellsDrawn { get; private set; }

        /// <param name="pitch">피치 렌더러 — 같은 텍스처(머티리얼)·라인·줄무늬 데이터(프로퍼티 블록)를 잔디가 쓴다.</param>
        /// <param name="template">RoadToPro/GrassShell 머티리얼 (조명 프리셋이 참조 — 빌드 포함). null이면 Shader.Find로 시도.</param>
        public GrassShell(PresetId preset, Renderer pitch, Material template = null)
        {
            _preset = preset;
            _pitch = pitch;
            _shells = Shells(preset);
            _matrices = new Matrix4x4[Math.Max(1, _shells)];
            var shader = template != null ? template.shader : Shader.Find(ShaderName);
            if (shader == null || pitch == null || pitch.sharedMaterial == null)
            {
                Debug.LogWarning($"[061] 근거리 잔디 준비 실패 (셰이더 {(shader != null)} · 피치 {(pitch != null)}): 그리지 않는다");
                _warned = true;
                return;
            }
            _material = new Material(shader) { name = "061 GrassShell", enableInstancing = true, hideFlags = HideFlags.DontSave };
            _material.CopyPropertiesFromMaterial(pitch.sharedMaterial);
            _material.enableInstancing = true; // 복사가 플래그를 덮어쓴다 — RenderMeshInstanced에 필요
            _patch = CreatePatch(Radius(preset));
        }

        /// <summary>정사각 격자 (한 변 2R, 칸 CellMeters). 법선 +Y.</summary>
        static Mesh CreatePatch(float radius)
        {
            int n = Mathf.CeilToInt(radius * 2f / CellMeters);
            float half = n * CellMeters * 0.5f;
            var v = new Vector3[(n + 1) * (n + 1)];
            var nrm = new Vector3[v.Length];
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                {
                    v[z * (n + 1) + x] = new Vector3(x * CellMeters - half, 0f, z * CellMeters - half);
                    nrm[z * (n + 1) + x] = Vector3.up;
                }
            var idx = new int[n * n * 6];
            int k = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int a = z * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                    idx[k++] = a; idx[k++] = c; idx[k++] = b;
                    idx[k++] = b; idx[k++] = c; idx[k++] = d;
                }
            var m = new Mesh { name = "061 GrassPatch", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v);
            m.SetNormals(nrm);
            m.SetTriangles(idx, 0);
            m.bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 1f, half * 2f));
            return m;
        }

        public void Draw(Camera camera, float weight, double simTime, bool snowCovered)
        {
            LastShellsDrawn = 0;
            if (_material == null || camera == null || !ShouldDraw(_preset, weight, snowCovered)) return;
            var center = PatchCenter(camera.transform.position, camera.transform.forward, _preset);
            _pitch.GetPropertyBlock(_props);
            var ph = WindPhase(simTime);
            _props.SetVector(IdWindPhase, new Vector4(ph.x, ph.y, WindAmplitude, 0f));
            _props.SetVector(IdShellParams, new Vector4(LeafHeight, VisualFeatureTable.GrassFadeStart(_preset), Radius(_preset), weight));
            for (int i = 0; i < _shells; i++)
                _matrices[i] = Matrix4x4.Translate(center + Vector3.up * (LeafHeight * (i + 1) / _shells));
            var rp = new RenderParams(_material)
            {
                matProps = _props,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                camera = camera,
                worldBounds = new Bounds(center, new Vector3(Radius(_preset) * 2f + 1f, 1f, Radius(_preset) * 2f + 1f)),
            };
            Graphics.RenderMeshInstanced(rp, _patch, 0, _matrices, _shells);
            LastShellsDrawn = _shells;
        }

        public void Dispose()
        {
            if (_material != null) Release(_material);
            if (_patch != null) Release(_patch);
        }

        static void Release(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
