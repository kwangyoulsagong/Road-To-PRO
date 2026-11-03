using System;
using System.Collections.Generic;
using RoadToPro.Presentation.Stadium;
using UnityEngine;

namespace RoadToPro.Verification.Samples
{
    /// <summary>
    /// specs/044 E2E 씬 제어. `goto` 화면 = 카메라 시점·조명·표시 묶음. 시점마다 Python 이미지 검사용 영역을 계산해 둔다.
    /// 게임 화면이 아니다 (경기장 검증 전용).
    /// </summary>
    public sealed class StadiumViewsScreen : MonoBehaviour
    {
        [Serializable]
        public struct View
        {
            public string name;
            public Vector3 position;
            public Vector3 target;
            public float fov;
            public bool night;
            public int show; // 0 전체, 1 없음, 2 피치만, 3 구조물만
        }

        // 줄무늬 띠 (스트라이프 폭 5.25 m): 띠A = x ∈ [0, 5.25), 띠B = x ∈ [−5.25, 0). 하프라인·센터마크를 피해 안쪽만
        static readonly Vector3[] BandA = { new(0.6f, 0f, -3f), new(4.6f, 0f, -3f), new(4.6f, 0f, 3f), new(0.6f, 0f, 3f) };
        static readonly Vector3[] BandB = { new(-4.6f, 0f, -3f), new(-0.6f, 0f, -3f), new(-0.6f, 0f, 3f), new(-4.6f, 0f, 3f) };
        const float LineSampleHalfPx = 130f;

        [SerializeField] Camera cam;
        [SerializeField] StadiumLighting lighting;
        [SerializeField] StadiumLightingPreset day;
        [SerializeField] StadiumLightingPreset night;
        [SerializeField] PitchMaterialBinder binder;
        [SerializeField] Renderer[] pitchRenderers;
        [SerializeField] Renderer[] structureRenderers;
        [SerializeField] Transform player;
        [SerializeField] BlobShadow4 blob;
        [SerializeField] View[] views;

        View _current;
        readonly BlobShadowPose[] _poses = new BlobShadowPose[4];

        public void Configure(Camera c, StadiumLighting l, StadiumLightingPreset d, StadiumLightingPreset n, PitchMaterialBinder b,
            Renderer[] pitch, Renderer[] structure, Transform p, BlobShadow4 bs, View[] v)
        {
            cam = c; lighting = l; day = d; night = n; binder = b;
            pitchRenderers = pitch; structureRenderers = structure; player = p; blob = bs; views = v;
        }

        void OnEnable()
        {
            FarmContext.RegisterCamera(cam);
            foreach (var v in views)
            {
                var view = v;
                FarmContext.Screens[v.name] = () => Show(view);
            }
            FarmContext.CaptureRegions = Regions;
            if (views.Length > 0) Show(views[0]);
        }

        void OnDisable()
        {
            FarmContext.Cameras.Remove(cam);
            foreach (var v in views) FarmContext.Screens.Remove(v.name);
            FarmContext.CaptureRegions = null;
        }

        public void Show(View v)
        {
            _current = v;
            cam.transform.position = v.position;
            var fwd = v.target - v.position;
            var up = Mathf.Abs(Vector3.Dot(fwd.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            cam.transform.rotation = Quaternion.LookRotation(fwd, up);
            cam.fieldOfView = v.fov;
            lighting.Apply(v.night ? night : day);
            foreach (var r in pitchRenderers) r.enabled = v.show == 0 || v.show == 2;
            foreach (var r in structureRenderers) r.enabled = v.show == 0 || v.show == 3;
            if (player != null) player.gameObject.SetActive(v.night);
            binder.Bind();
        }

        // ---- 캡처 영역 (PNG 픽셀, 왼쪽 위 원점) ----

        Vector2 Px(Vector3 world, int h)
        {
            var s = cam.WorldToScreenPoint(world);
            return new Vector2(s.x, h - s.y);
        }

        static List<object> L(Vector2 p) => new List<object> { Math.Round(p.x, 2), Math.Round(p.y, 2) };

        List<object> Rect(Vector3[] quad, int h, float shrink = 0.15f)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var q in quad)
            {
                var p = Px(q, h);
                x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
            }
            float dx = (x1 - x0) * shrink, dy = (y1 - y0) * shrink;
            return new List<object> { Math.Round(x0 + dx, 1), Math.Round(y0 + dy, 1), Math.Round(x1 - x0 - 2 * dx, 1), Math.Round(y1 - y0 - 2 * dy, 1) };
        }

        Dictionary<string, object> Regions(int w, int h)
        {
            var r = new Dictionary<string, object> { { "view", _current.name } };
            if (_current.name.StartsWith("mow", StringComparison.Ordinal))
            {
                r["bandA"] = Rect(BandA, h);
                r["bandB"] = Rect(BandB, h);
            }
            else if (_current.name.StartsWith("line", StringComparison.Ordinal))
            {
                var seg = PenaltyFrontLine();
                r["line"] = LineAround(seg.a, seg.b, _current.target, h);
            }
            else if (_current.name == "night" && player != null)
            {
                BlobShadow4.Compute(player.position, binder.Layout.LightEmitters, _poses);
                var blobs = new List<object>();
                var refs = new List<object>();
                foreach (var p in _poses)
                {
                    blobs.Add(L(Px(p.Center, h)));
                    // 기준 지면: 블롭 중심에서 수평 직각으로 1 m (블롭 폭 0.55 m 밖, 이웃 블롭 축에서도 0.8 m 이상)
                    var side = Vector3.Cross(Vector3.up, p.Direction).normalized;
                    refs.Add(new List<object> { L(Px(p.Center + side, h)), L(Px(p.Center - side, h)) });
                }
                r["blobs"] = blobs;
                r["refs"] = refs;
                r["foot"] = L(Px(new Vector3(player.position.x, 0f, player.position.z), h));
            }
            return r;
        }

        (Vector3 a, Vector3 b) PenaltyFrontLine()
        {
            foreach (var s in binder.Layout.Segments)
                if (s.Name == "PenaltyArea_L_Front")
                    return (new Vector3(s.A.x, 0f, s.A.y), new Vector3(s.B.x, 0f, s.B.y));
            throw new InvalidOperationException("PenaltyArea_L_Front 없음");
        }

        /// <summary>라인 위 target 근처에서 화면 길이 약 2×130 px 구간의 끝점 — 원근으로 폭이 크게 변하지 않게 짧게.</summary>
        Dictionary<string, object> LineAround(Vector3 a, Vector3 b, Vector3 target, int h)
        {
            var dir = (b - a).normalized;
            var t0 = Vector3.Dot(target - a, dir);
            var center = a + dir * t0;
            var c = Px(center, h);
            Vector3 End(float sign)
            {
                float step = 0.02f;
                var p = center;
                for (int i = 0; i < 4000; i++)
                {
                    var q = p + dir * (sign * step);
                    if (Vector2.Distance(Px(q, h), c) >= LineSampleHalfPx) return q;
                    p = q;
                }
                return p;
            }
            return new Dictionary<string, object> { { "a", L(Px(End(-1f), h)) }, { "b", L(Px(End(1f), h)) } };
        }
    }
}
