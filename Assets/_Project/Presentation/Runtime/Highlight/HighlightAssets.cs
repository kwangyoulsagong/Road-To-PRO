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
            _handles.ReleaseAll();
            while (_unload != null && !_unload.isDone) yield return null;
            _unload = null;
            yield return Resources.UnloadUnusedAssets();
        }
    }
}
