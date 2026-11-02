using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 품질 이름 → URP 에셋 표 (specs/000 qualityMap). macOS 빌드에는 'PC' 품질 레벨만 켜져 있어서
    /// 'Mobile'은 SetQualityLevel로 고를 수 없다. 그럴 때 이 표의 에셋으로 렌더 파이프라인을 직접 바꾼다.
    /// ProjectSettings(품질 레벨)는 건드리지 않는다 — 진짜 프리셋은 048에서 정한다.
    /// Resources/FarmRenderAssets.asset (FarmSampleSetup이 만든다).
    /// </summary>
    [CreateAssetMenu(menuName = "Road To PRO/Farm Render Assets")]
    public sealed class FarmRenderAssets : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string name;
            public RenderPipelineAsset asset;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public Entry[] Entries
        {
            get => entries;
            set => entries = value;
        }

        public RenderPipelineAsset Find(string name)
        {
            foreach (var e in entries)
                if (e.name == name) return e.asset;
            return null;
        }
    }
}
