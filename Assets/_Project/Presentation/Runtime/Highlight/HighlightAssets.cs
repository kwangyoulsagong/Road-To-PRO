using System;
using System.Collections;
using RoadToPro.Presentation.Highlight.Logic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadToPro.Presentation.Highlight
{
    /// <summary>3D 씬 수명 주기 (specs/041 FR-30·33·58). 모든 로드는 비동기이고, 잡은 씬은 핸들 목록으로 해제한다.</summary>
    public sealed class HighlightAssets
    {
        const int WarmupLods = 3;

        readonly string _sceneName;
        readonly LoadHandleTracker _handles = new LoadHandleTracker();
        Scene _scene;
        AsyncOperation _unload;

        public HighlightStage Stage { get; private set; }
        /// <summary>Low 프리셋 블롭 그림자 (없으면 null).</summary>
        public Stadium.PlayerBlobShadows Blobs { get; private set; }
        public int OutstandingHandles => _handles.Outstanding;
        public bool IsLoaded => Stage != null;

        public HighlightAssets(string sceneName) => _sceneName = sceneName;

        /// <param name="cancelled">true면 잡은 것을 모두 놓고 끝낸다.</param>
        public IEnumerator Load(Camera broadcastCamera, Func<bool> cancelled)
        {
            var op = SceneManager.LoadSceneAsync(_sceneName, LoadSceneMode.Additive);
            if (op == null) throw new InvalidOperationException("빌드에 없는 씬: " + _sceneName);
            _scene = SceneManager.GetSceneByName(_sceneName);
            var scene = _scene;
            _handles.Acquire(() => _unload = SceneManager.UnloadSceneAsync(scene));
            while (!op.isDone) yield return null;
            if (cancelled()) yield break;

            foreach (var root in _scene.GetRootGameObjects())
            {
                Stage = root.GetComponentInChildren<HighlightStage>();
                if (Stage != null) break;
            }
            if (Stage == null) throw new InvalidOperationException("씬에 HighlightStage가 없다: " + _sceneName);
            yield return Stage.Prepare(broadcastCamera);
            Rendering.RenderQualityApplier.ApplyCamera(Rendering.RenderQualityApplier.Current, broadcastCamera);
            Blobs = AttachBlobs(Stage);
        }

        /// <summary>블롭 프리셋(실제 Low)이면 선수 발밑 블롭을 붙인다 (specs/048 FR-01). 대역 프리셋은 기존 그림자 그대로.</summary>
        static Stadium.PlayerBlobShadows AttachBlobs(HighlightStage stage)
        {
            var preset = Rendering.RenderQualityApplier.Current;
            if (preset == null || preset.IsStandIn || preset.Shadows != Rendering.Logic.ShadowMode.Blob) return null;
            if (stage.BlobMaterial == null)
            {
                Debug.LogWarning("[highlight] 블롭 프리셋인데 무대에 블롭 머티리얼이 없다");
                return null;
            }
            var targets = new System.Collections.Generic.List<Transform>();
            foreach (var r in stage.Rigs)
                if (r != null) targets.Add(r.transform);
            var sun = stage.Sun;
            if (sun != null) sun.shadows = LightShadows.None;
            var blobs = stage.gameObject.AddComponent<Stadium.PlayerBlobShadows>();
            blobs.Configure(targets, null, stage.BlobMaterial,
                sun != null ? sun.transform.forward : new Vector3(0.3f, -0.8f, 0.5f), stage.NightEmitters,
                m => Debug.LogWarning("[highlight] " + m));
            Debug.Log($"[highlight] 블롭 그림자 {targets.Count}명 ({(blobs.IsNight ? "나이트 4장" : "낮 1장")})");
            return blobs;
        }

        /// <summary>모든 LOD를 로딩 화면 뒤에서 한 번씩 그려 셰이더를 준비한다 (FR-30·31).</summary>
        public IEnumerator WarmUp()
        {
            var rigs = Stage.Rigs;
            for (int lod = 0; lod < WarmupLods; lod++)
            {
                foreach (var r in rigs)
                    if (r != null) r.ForceLod(lod);
                yield return null;
            }
            foreach (var r in rigs)
                if (r != null) r.ForceLod(-1);
        }

        public IEnumerator Unload()
        {
            Stage = null;
            Blobs = null;
            _handles.ReleaseAll();
            while (_unload != null && !_unload.isDone) yield return null;
            _unload = null;
            yield return Resources.UnloadUnusedAssets();
        }
    }
}
