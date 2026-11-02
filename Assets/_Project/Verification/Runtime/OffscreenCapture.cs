using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 프로필 해상도 RenderTexture에 UI 패널을 그려 캡처한다 (specs/000 FR-23, 경계: 레티나 배율·창 잘림 무관).
    /// 패널 설정은 복제본에 targetTexture·고정 픽셀 배율(=프로필 UI 배율)을 건다. 원본 에셋은 건드리지 않는다.
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
            Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "FarmCapture",
                antiAliasing = 1,
            };
            Target.Create();
            _read = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
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
                clone.clearColor = true;
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

        public static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
