using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Diagnostics;

namespace RoadToPro.Verification
{
    // 캡처·성능 구간·결정론 체크포인트·프레임 고정·셀프테스트 주입 단계 (specs/000 FR-20·23·25·40·51)
    public sealed partial class ScenarioRunner
    {
        IEnumerator Capture(StepDef s, StepOutcome o)
        {
            // 애니메이션·폰트 아틀라스가 가라앉을 때까지: 연속 2프레임이 바이트 단위로 같을 때 캡처
            byte[] prev = null;
            string diff = "";
            bool stable = false;
            for (int i = 0; i < MaxStabilityTries; i++)
            {
                yield return new WaitForEndOfFrame();
                var cur = _capture.ReadRaw();
                if (OffscreenCapture.SameBytes(prev, cur))
                {
                    stable = true;
                    break;
                }
                if (prev != null)
                {
                    diff = _capture.DiffSummary(prev, cur);
                    if (i == MaxStabilityTries - 1)
                    {
                        // 진단: 마지막 두 프레임을 남긴다 (captures/_unstable_a.png, _b.png)
                        File.WriteAllBytes(Path.Combine(_args.OutDir, "captures/_unstable_a.png"), _capture.EncodeRaw(prev));
                        File.WriteAllBytes(Path.Combine(_args.OutDir, "captures/_unstable_b.png"), _capture.EncodeRaw(cur));
                    }
                }
                prev = (byte[])cur.Clone();
                yield return null;
            }
            if (!stable)
            {
                o.Fail($"화면이 {MaxStabilityTries}프레임 안에 안정되지 않았다 (불안정) — 마지막 두 프레임: {diff}");
                yield break;
            }
            var name = s.GetString("name");
            var file = "captures/" + name + ".png";
            File.WriteAllBytes(Path.Combine(_args.OutDir, file), _capture.EncodePng());

            var masks = new List<object>();
            foreach (var m in s.GetList("masks"))
            {
                if (!(m is Dictionary<string, object> md)) continue;
                if (md.TryGetValue("element", out var en) && en is string elName)
                {
                    var e = Find(elName);
                    if (e == null)
                    {
                        o.Fail($"마스크 요소 '{elName}' 없음");
                        yield break;
                    }
                    var px = _capture.ToPixels(e.worldBound);
                    masks.Add(new Dictionary<string, object> { { "pixels", new List<object> { px.x, px.y, px.width, px.height } }, { "element", elName } });
                }
                else masks.Add(md);
            }
            var cap = new Dictionary<string, object>
            {
                { "file", file },
                { "width", _capture.Width },
                { "height", _capture.Height },
                { "masks", masks },
                { "kind", s.GetString("kind", "ui") },
            };
            var regions = FarmContext.CaptureRegions?.Invoke(_capture.Width, _capture.Height);
            if (regions != null) cap["regions"] = regions;
            o.Extra["capture"] = cap;
        }

        IEnumerator PerfBegin(StepDef s, StepOutcome o)
        {
            Time.captureDeltaTime = 0f; // 실제 프레임 시간으로 잰다
            // uncapped (specs/044): fps 상한을 풀어 GPU를 최고 클럭으로 — 가벼운 부하에서 동적 클럭이 낮아져 GPU ms가 부풀려지는 것 방지
            if (s.Has("uncapped") && s.Fields["uncapped"] is bool u && u) Application.targetFrameRate = -1;
            yield return null;
            _perf.Begin(s.GetString("name"),
                (int)s.GetDouble("warmupFrames", PerfSampler.DefaultWarmup),
                (int)s.GetDouble("minFrames", PerfSampler.DefaultMinFrames));
        }

        IEnumerator PerfEnd(StepDef s, StepOutcome o)
        {
            while (!_perf.Enough) yield return null;
            o.Extra["frames"] = _perf.Frames;
            _perf.End();
            Application.targetFrameRate = _args.TargetFps;
            Time.captureDeltaTime = FixedDelta;
            yield return null;
        }

        IEnumerator Checkpoint(StepDef s, StepOutcome o)
        {
            yield return null;
            var probe = FarmContext.DeterminismProbe;
            if (probe == null)
            {
                o.Fail("결정론 프로브가 등록되지 않았다");
                yield break;
            }
            var prefix = s.GetString("name") + "/";
            foreach (var c in probe.Checkpoints)
                _checkpoints.Add(new Dictionary<string, object> { { "name", prefix + c.Name }, { "tick", c.Tick }, { "hash", c.Hash } });
            o.Extra["checkpoints"] = probe.Checkpoints.Count;
            if (probe.Checkpoints.Count == 0) o.Fail("체크포인트가 비었다");
        }

        IEnumerator Freeze(StepDef s, StepOutcome o)
        {
            var pb = FarmContext.Playback;
            if (pb == null)
            {
                o.Fail("재생기(IFarmPlayback)가 등록되지 않았다");
                yield break;
            }
            if (s.Has("speed")) pb.SetSpeed((float)s.GetDouble("speed"));
            double? t = s.Has("simTime") ? s.GetDouble("simTime") : (double?)null;
            int? scene = s.Has("scene") ? (int)s.GetDouble("scene") : (int?)null;
            while (!pb.SeekAndFreeze(t, scene)) yield return null;
            yield return null;
        }

        IEnumerator Inject(StepDef s, StepOutcome o)
        {
            yield return null;
            switch (s.GetString("what"))
            {
                case "warning":
                    Debug.LogWarning("[farm] 셀프테스트: 경고 주입");
                    break;
                case "exception":
                    Debug.LogException(new InvalidOperationException("[farm] 셀프테스트: 예외 주입"));
                    break;
                case "crash":
                    Utils.ForceCrash(ForcedCrashCategory.Abort);
                    break;
            }
            yield return null;
        }
    }
}
