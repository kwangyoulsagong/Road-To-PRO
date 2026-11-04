using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.UIElements;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 프로필 해상도 RenderTexture에 UI 패널을 그려 캡처한다 (specs/000 FR-23, 경계: 레티나 배율·창 잘림 무관).
    /// 패널 설정은 복제본에 targetTexture·고정 픽셀 배율(=프로필 UI 배율)을 건다. 원본 에셋은 건드리지 않는다.
    /// 3D 장면(specs/044)은 카메라의 targetTexture를 같은 RT로 돌린다. 카메라가 있으면 UI 패널은 RT를 지우지 않고 위에 그린다.
    /// </summary>
    public sealed class OffscreenCapture
    {
        readonly Dictionary<PanelSettings, PanelSettings> _clones = new Dictionary<PanelSettings, PanelSettings>();
        Texture2D _read;

        public RenderTexture Target { get; }
        public int Width { get; }
        public int Height { get; }
        public float UiScale { get; }

        public OffscreenCapture(int width, int height, float uiScale)
        {
            Width = width;
            Height = height;
            UiScale = uiScale;
            Target = new RenderTexture(width, height, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "FarmCapture",
                antiAliasing = 1,
            };
            Target.Create();
            _read = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        }

        readonly List<Camera> _cameras = new List<Camera>();

        public bool HasCameras => _cameras.Count > 0;

        public void AttachCamera(Camera cam)
        {
            if (cam == null || _cameras.Contains(cam)) return;
            cam.targetTexture = Target;
            _cameras.Add(cam);
            foreach (var clone in _clones.Values) clone.clearColor = false;
        }

        public void Attach(UIDocument doc)
        {
            var src = doc.panelSettings;
            if (src == null) return;
            if (_clones.ContainsValue(src)) return;
            if (!_clones.TryGetValue(src, out var clone))
            {
                clone = Object.Instantiate(src);
                clone.name = src.name + " (Farm)";
                clone.targetTexture = Target;
                clone.scaleMode = PanelScaleMode.ConstantPixelSize;
                clone.scale = UiScale;
                clone.clearColor = !HasCameras;
                clone.colorClearValue = Color.black;
                _clones[src] = clone;
            }
            doc.panelSettings = clone;
        }

        /// <summary>현재 RT 픽셀 (위에서 아래 순서 아님 — PNG 인코딩이 뒤집는다). 프레임 끝에서 부른다.</summary>
        public byte[] ReadRaw()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = Target;
            _read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            _read.Apply(false);
            RenderTexture.active = prev;
            return _read.GetRawTextureData();
        }

        public byte[] EncodePng() => _read.EncodeToPNG();

        /// <summary>패널 좌표(논리 단위) 사각형 → 캡처 픽셀 사각형.</summary>
        public RectInt ToPixels(Rect panelRect) => new RectInt(
            Mathf.RoundToInt(panelRect.x * UiScale), Mathf.RoundToInt(panelRect.y * UiScale),
            Mathf.RoundToInt(panelRect.width * UiScale), Mathf.RoundToInt(panelRect.height * UiScale));

        /// <summary>
        /// 두 프레임에서 달라진 픽셀 수와 경계 상자(PNG 좌표, 왼쪽 위 원점) — 불안정 캡처 진단용 (specs/043).
        /// 픽셀당 4바이트(RGBA32), 행은 아래에서 위.
        /// </summary>
        public string DiffSummary(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return "비교 불가";
            int n = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, maxd = 0;
            int stride = Width * 4;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * stride + x * 4, d = 0;
                    for (int c = 0; c < 3; c++) d = Mathf.Max(d, Mathf.Abs(a[i + c] - b[i + c]));
                    if (d == 0) continue;
                    n++;
                    maxd = Mathf.Max(maxd, d);
                    int py = Height - 1 - y;
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, py); y1 = Mathf.Max(y1, py);
                }
            return n == 0 ? "차이 없음" : $"다른 픽셀 {n}개, 영역 [{x0},{y0},{x1 - x0 + 1},{y1 - y0 + 1}], 최대 채널 차 {maxd}";
        }

        /// <summary>원시 프레임 바이트를 PNG로 (진단용).</summary>
        public byte[] EncodeRaw(byte[] raw)
        {
            var t = new Texture2D(Width, Height, _read.format, false);
            t.LoadRawTextureData(raw);
            t.Apply(false);
            var png = t.EncodeToPNG();
            Object.Destroy(t);
            return png;
        }

        public static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
