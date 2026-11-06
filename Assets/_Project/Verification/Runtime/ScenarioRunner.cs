using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RoadToPro.Verification
{
    /// <summary>단계 하나의 결과.</summary>
    public sealed class StepOutcome
    {
        public bool Failed { get; private set; }
        public string Message { get; private set; } = "";
        public Dictionary<string, object> Extra { get; } = new Dictionary<string, object>();
        public void Fail(string msg) { Failed = true; Message = msg; }
    }

    /// <summary>
    /// 시나리오 단계 실행기 (specs/000 FR-20~25). 단계마다 steps.jsonl 한 줄, 끝나면 perf.json·checkpoints.json·exit.json.
    /// 고정 프레임 진행(Time.captureDeltaTime)으로 화면 결정론을 지키고, 성능 구간에서만 실제 프레임으로 잰다.
    /// 종료 코드: 0 전 단계 통과, 1 단계 실패, 2 시나리오·준비 오류.
    /// </summary>
    public sealed partial class ScenarioRunner : MonoBehaviour
    {
        const float FixedDelta = 1f / 60f;
        const int MaxStabilityTries = 10;

        FarmArgs _args;
        ScenarioDefinition _scenario;
        OffscreenCapture _capture;
        readonly PerfSampler _perf = new PerfSampler();
        readonly List<object> _checkpoints = new List<object>();
        StreamWriter _steps;
        string _heartbeat;
        float _lastBeat = -10f;

        void Start()
        {
            _args = FarmContext.Args;
            _heartbeat = Path.Combine(_args.OutDir, "heartbeat");
            StartCoroutine(RunAll());
        }

        void Update()
        {
            _perf.Sample();
            if (Time.realtimeSinceStartup - _lastBeat >= 1f)
            {
                _lastBeat = Time.realtimeSinceStartup;
                try { File.WriteAllText(_heartbeat, Time.frameCount.ToString()); }
                catch (IOException) { }
            }
        }

        void OnGUI()
        {
            // 사람이 볼 수 있게 캡처 대상을 창에 비춘다 (캡처에는 영향 없음).
            if (_capture != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _capture.Target, ScaleMode.ScaleToFit, false);
        }

        IEnumerator RunAll()
        {
            _steps = new StreamWriter(Path.Combine(_args.OutDir, "steps.jsonl"), false) { AutoFlush = true };
            try
            {
                _scenario = ScenarioDefinition.Parse(File.ReadAllText(_args.ScenarioPath));
            }
            catch (Exception e) when (e is IOException || e is FormatException)
            {
                Finish("error", -1, "시나리오를 읽을 수 없다: " + e.Message, 2);
                yield break;
            }

            Time.captureDeltaTime = FixedDelta;
            var load = SceneManager.LoadSceneAsync(_scenario.Scene);
            if (load == null)
            {
                Finish("error", -1, $"씬 '{_scenario.Scene}'이 빌드에 없다", 2);
                yield break;
            }
            while (!load.isDone) yield return null;
            yield return null;
            yield return null;

            if (FarmContext.Documents.Count == 0 && FarmContext.Cameras.Count == 0)
            {
                Finish("error", -1, "등록된 UI 문서도 카메라도 없다 (화면이 FarmContext.RegisterDocument/RegisterCamera를 부르지 않음)", 2);
                yield break;
            }
            _capture = new OffscreenCapture(_args.Width, _args.Height, _args.UiScale);
            foreach (var cam in FarmContext.Cameras) _capture.AttachCamera(cam);
            foreach (var doc in FarmContext.Documents) _capture.Attach(doc);
            yield return null;
            yield return null;

            for (int i = 0; i < _scenario.Steps.Count; i++)
            {
                var step = _scenario.Steps[i];
                var outcome = new StepOutcome();
                float t0 = Time.realtimeSinceStartup;
                double deadline = t0 + _scenario.TimeoutFor(step);
                var it = Execute(step, outcome);
                bool timedOut = false;
                while (true)
                {
                    bool more;
                    try { more = it.MoveNext(); }
                    catch (Exception e)
                    {
                        outcome.Fail($"예외: {e.GetType().Name}: {e.Message}");
                        Debug.LogException(e);
                        break;
                    }
                    if (!more || outcome.Failed) break;
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        timedOut = true;
                        outcome.Fail($"타임아웃 {_scenario.TimeoutFor(step)}초");
                        break;
                    }
                    yield return it.Current;
                }
                var line = new Dictionary<string, object>
                {
                    { "index", i },
                    { "type", step.Type },
                    { "name", step.GetString("name") },
                    { "status", outcome.Failed ? "failed" : "passed" },
                    { "ms", Math.Round((Time.realtimeSinceStartup - t0) * 1000.0, 1) },
                    { "message", outcome.Message },
                    { "timedOut", timedOut },
                };
                foreach (var kv in outcome.Extra) line[kv.Key] = kv.Value;
                if (outcome.Failed)
                {
                    if (_perf.Active) _perf.End();
                    Time.captureDeltaTime = FixedDelta;
                    yield return new WaitForEndOfFrame();
                    var file = $"_fail_{i:00}.png";
                    _capture.ReadRaw();
                    File.WriteAllBytes(Path.Combine(_args.OutDir, "captures", file), _capture.EncodePng());
                    line["failCapture"] = "captures/" + file;
                }
                _steps.WriteLine(MiniJson.Serialize(line));
                if (outcome.Failed)
                {
                    Finish("failed", i, $"{step.Label}: {outcome.Message}", 1);
                    yield break;
                }
            }
            Finish("passed", -1, "", 0);
        }

        IEnumerator Execute(StepDef s, StepOutcome o)
        {
            switch (s.Type)
            {
                case "goto": return Goto(s, o);
                case "tap": return Tap(s, o);
                case "type": return TypeText(s, o);
                case "waitFor": return WaitFor(s, o);
                case "assertVisible": return AssertVisible(s, o);
                case "assertText": return AssertText(s, o);
                case "assertNumberRange": return AssertNumberRange(s, o);
                case "assertNoOverlap": return AssertNoOverlap(s, o);
                case "assertNoClip": return AssertNoClip(s, o);
                case "capture": return Capture(s, o);
                case "perfBegin": return PerfBegin(s, o);
                case "perfEnd": return PerfEnd(s, o);
                case "checkpoint": return Checkpoint(s, o);
                case "motionMeasure": return MotionMeasure(s, o);
                case "freeze": return Freeze(s, o);
                case "inject": return Inject(s, o);
                default: return FailNow(o, $"알 수 없는 단계 '{s.Type}'");
            }
        }

        static IEnumerator FailNow(StepOutcome o, string msg)
        {
            o.Fail(msg);
            yield break;
        }

        void Finish(string status, int failedStep, string reason, int exitCode)
        {
            var dir = _args.OutDir;
            File.WriteAllText(Path.Combine(dir, "perf.json"), MiniJson.Serialize(new Dictionary<string, object> { { "sections", _perf.Sections } }));
            File.WriteAllText(Path.Combine(dir, "checkpoints.json"), MiniJson.Serialize(_checkpoints));
            File.WriteAllText(Path.Combine(dir, "exit.json"), MiniJson.Serialize(new Dictionary<string, object>
            {
                { "status", status },
                { "failedStep", failedStep },
                { "reason", reason },
                { "scenario", _scenario?.Id },
                { "frames", Time.frameCount },
            }));
            if (exitCode != 0) Debug.Log($"[farm] 시나리오 종료: {status} — {reason}");
            _steps?.Dispose();
            FarmLog.Stop();
            Application.Quit(exitCode);
        }

        // ---- 공통 도우미 ----

        VisualElement Find(string name)
        {
            foreach (var doc in FarmContext.Documents)
            {
                var root = doc.rootVisualElement;
                if (root == null) continue;
                if (name == "root") return root;
                var e = root.Q(name);
                if (e != null) return e;
            }
            return null;
        }

        static bool IsVisible(VisualElement e)
        {
            if (e == null || e.panel == null) return false;
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility != Visibility.Visible)
                    return false;
            return e.worldBound.width > 0 && e.worldBound.height > 0;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }
    }
}
