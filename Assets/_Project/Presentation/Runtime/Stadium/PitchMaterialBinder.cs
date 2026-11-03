using UnityEngine;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>
    /// 피치 렌더러에 layout.json 라인 정의를 건다 (specs/044 FR-32). 머티리얼 에셋은 건드리지 않고 프로퍼티 블록으로.
    /// </summary>
    [ExecuteAlways]
    public sealed class PitchMaterialBinder : MonoBehaviour
    {
        [SerializeField] TextAsset layoutJson;
        [SerializeField] Renderer pitchRenderer;

        StadiumLayout _layout;
        MaterialPropertyBlock _block;

        public StadiumLayout Layout => _layout ??= layoutJson != null ? StadiumLayout.Parse(layoutJson.text) : null;

        public void Configure(TextAsset layout, Renderer pitch)
        {
            layoutJson = layout;
            pitchRenderer = pitch;
            _layout = null;
            Bind();
        }

        void OnEnable() => Bind();

        public void Bind()
        {
            if (pitchRenderer == null || Layout == null) return;
            _block ??= new MaterialPropertyBlock();
            pitchRenderer.GetPropertyBlock(_block);
            PitchShaderData.From(Layout).ApplyTo(_block);
            pitchRenderer.SetPropertyBlock(_block);
        }
    }
}
