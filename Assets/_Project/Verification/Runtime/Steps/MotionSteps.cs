using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RoadToPro.Verification.Samples;

namespace RoadToPro.Verification
{
    /// <summary>재생 측정 등록점 (specs/054 FR-70·71).</summary>
    public interface IFarmMotionMeasure
    {
        IEnumerator MeasureMotion(string set, IReadOnlyList<float> rates, string outDir, List<Dictionary<string, object>> results,
            List<FarmCheckpoint> checkpoints, System.Action<string> captureAt);
    }

    // motionMeasure 단계: 장면을 재생해 지표 → motion.json + 결정론 체크포인트, 위반 프레임 캡처
    public sealed partial class ScenarioRunner
    {
        IEnumerator MotionMeasure(StepDef s, StepOutcome o)
        {
            var m = FarmContext.MotionMeasure;
            if (m == null)
            {
                o.Fail("측정기(IFarmMotionMeasure)가 등록되지 않았다");
                yield break;
            }
            yield return null;
            var rates = new List<float>();
            foreach (var r in s.GetList("rates")) rates.Add(System.Convert.ToSingle(r, CultureInfo.InvariantCulture));
            if (rates.Count == 0) rates.Add(1.5f);
            var results = new List<Dictionary<string, object>>();
            var cps = new List<FarmCheckpoint>();
            var dir = Path.Combine(_args.OutDir, "motion");
            Directory.CreateDirectory(dir);
            yield return m.MeasureMotion(s.GetString("set", "all"), rates, dir, results, cps, file =>
            {
                _capture.ReadRaw();
                File.WriteAllBytes(Path.Combine(dir, file), _capture.EncodePng());
            });
            var path = Path.Combine(_args.OutDir, "motion.json");
            var prev = new List<object>();
            if (File.Exists(path) && MiniJson.Parse(File.ReadAllText(path)) is Dictionary<string, object> old && old.TryGetValue("scenes", out var os) && os is List<object> ol) prev = ol;
            foreach (var r in results) prev.Add(r);
            File.WriteAllText(path, MiniJson.Serialize(new Dictionary<string, object> { { "scenes", prev } }));
            foreach (var c in cps) _checkpoints.Add(new Dictionary<string, object> { { "name", c.Name }, { "tick", c.Tick }, { "hash", c.Hash } });
            int bad = 0;
            foreach (var r in results) bad += ((List<object>)r["violations"]).Count;
            o.Extra["scenes"] = results.Count;
            o.Extra["violations"] = bad;
            if (bad > 0) o.Fail($"움직임 지표 위반 {bad}건 (motion.json)");
        }
    }
}
