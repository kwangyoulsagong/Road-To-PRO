using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using RoadToPro.Verification.Samples;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/043 T27 애니메이션 벤치 (IL2CPP 릴리스 판정, AC-16). SimBench가 <c>-simBench 043</c>일 때 만든다.
    /// Player043 씬을 열고 25명 합성 하이라이트를 1.5x로 돌리며 프레임마다 애니메이션 갱신(AnimPlan 평가 + 포즈 적용 + 발 접지) 시간과
    /// 관리 힙 할당을 잰다 → bench.json(anim_p50_ms, anim_p95_ms, anim_max_ms, anim_gc_bytes) → 종료.
    /// </summary>
    public sealed class AnimBench : MonoBehaviour
    {
        public const int Warmup = 120, Frames = 1200;
        string _outDir, _spec;
        Action<Dictionary<string, double>> _done;
        Action<Exception> _fail;

        public static void Start(string outDir, string spec, Action<Dictionary<string, double>> done, Action<Exception> fail)
        {
            var go = new GameObject("AnimBench");
            DontDestroyOnLoad(go);
            var b = go.AddComponent<AnimBench>();
            b._outDir = outDir;
            b._spec = spec;
            b._done = done;
            b._fail = fail;
        }

        IEnumerator Start()
        {
            var op = SceneManager.LoadSceneAsync("Player043");
            while (op != null && !op.isDone) yield return null;
            yield return null; // 화면 OnEnable(로딩·키트 합성)
            Dictionary<string, double> m;
            try
            {
                var screen = FindFirstObjectByType<PlayerScreen>();
                if (screen == null) throw new InvalidOperationException("Player043 씬에 PlayerScreen 없음");
                m = _spec == "042" ? BroadcastCameraBench.Run(Warmup, Frames) : _spec == "041" ? screen.BenchHighlight(Warmup, Frames) : screen.BenchAnim(Warmup, Frames);
            }
            catch (Exception e)
            {
                _fail(e);
                yield break;
            }
            _done(m);
        }
    }
}
