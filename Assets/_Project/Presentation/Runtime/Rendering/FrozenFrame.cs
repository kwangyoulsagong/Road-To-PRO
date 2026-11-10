using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace RoadToPro.Presentation.Rendering
{
    /// <summary>
    /// 타임라인 구간의 마지막 3D 프레임 고정 (specs/048 FR-17). 진입 때 방송 카메라를 렌더 텍스처로 한 번 그리고,
    /// UI 맨 뒤 전체 화면 요소의 배경으로 보여 준다. 카메라가 꺼진 동안 화면은 이 이미지 + UI만 그린다.
    /// 렌더 텍스처는 크기가 같으면 재사용한다 (재생 경로 밖 — 타임라인 진입 시에만 생성).
    /// </summary>
    public sealed class FrozenFrame
    {
        readonly VisualElement _root;
        readonly VisualElement _image;
        RenderTexture _rt;

        public bool Visible { get; private set; }
        /// <summary>고정 이미지를 만들 때 쓴 카메라 렌더 중이면 true (3D 렌더 프레임 세기에서 뺀다).</summary>
        public bool Capturing { get; private set; }

        public FrozenFrame(VisualElement root)
        {
            _root = root;
            _image = new VisualElement { name = "frozenFrame", pickingMode = PickingMode.Ignore };
            _image.style.position = Position.Absolute;
            _image.style.left = 0;
            _image.style.top = 0;
            _image.style.right = 0;
            _image.style.bottom = 0;
            _image.style.display = DisplayStyle.None;
        }

        /// <summary>카메라의 현재 장면을 고정 이미지로 만들고 보인다. 렌더 요청을 지원하지 않으면 false (카메라를 끄지 않는다).</summary>
        public bool Show(Camera camera)
        {
            if (camera == null || _root == null) return false;
            int w = Mathf.Max(1, camera.pixelWidth), h = Mathf.Max(1, camera.pixelHeight);
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                Release();
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "FrozenFrame" };
                _rt.Create();
            }
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = _rt };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) return false;
            Capturing = true;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            finally
            {
                Capturing = false;
            }
            _image.style.backgroundImage = Background.FromRenderTexture(_rt);
            if (_image.parent != _root) _root.Insert(0, _image);
            _image.style.display = DisplayStyle.Flex;
            Visible = true;
            return true;
        }

        public void Hide()
        {
            _image.style.display = DisplayStyle.None;
            Visible = false;
        }

        /// <summary>결과 화면·취소로 끝날 때 렌더 텍스처를 놓는다.</summary>
        public void Release()
        {
            Hide();
            _image.style.backgroundImage = StyleKeyword.None;
            if (_rt != null)
            {
                _rt.Release();
                Object.Destroy(_rt);
                _rt = null;
            }
        }
    }
}
