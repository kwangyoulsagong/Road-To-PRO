using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine.UIElements;

namespace RoadToPro.Verification
{
    // 단언 단계 (specs/000 FR-20·26)
    public sealed partial class ScenarioRunner
    {
        IEnumerator AssertVisible(StepDef s, StepOutcome o)
        {
            yield return null;
            var name = s.GetString("element");
            if (!IsVisible(Find(name))) o.Fail($"'{name}'이 보이지 않는다");
        }

        IEnumerator AssertText(StepDef s, StepOutcome o)
        {
            yield return null;
            var name = s.GetString("element");
            if (!(Find(name) is TextElement te))
            {
                o.Fail($"'{name}' 글자 요소 없음");
                yield break;
            }
            // 언어별 기대값: "text": {"ko": "...", "en": "..."}
            var want = s.Fields["text"] is System.Collections.Generic.Dictionary<string, object> byLang
                ? (byLang.TryGetValue(FarmContext.Lang, out var w) ? w as string : null)
                : s.GetString("text");
            if (te.text != want) o.Fail($"'{name}' 글자 '{te.text}' ≠ 기대 '{want}'");
        }

        IEnumerator AssertNumberRange(StepDef s, StepOutcome o)
        {
            yield return null;
            var name = s.GetString("element");
            if (!(Find(name) is TextElement te))
            {
                o.Fail($"'{name}' 글자 요소 없음");
                yield break;
            }
            var sb = new StringBuilder();
            foreach (var c in te.text)
                if (char.IsDigit(c) || c == '.' || c == '-') sb.Append(c);
            if (!double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                o.Fail($"'{name}' 글자 '{te.text}'에서 숫자를 읽을 수 없다");
                yield break;
            }
            double min = s.GetDouble("min"), max = s.GetDouble("max");
            o.Extra["value"] = v;
            if (v < min || v > max) o.Fail($"'{name}' 값 {v} ∉ [{min}, {max}]");
        }

        IEnumerator AssertNoOverlap(StepDef s, StepOutcome o)
        {
            yield return null;
            var scope = Find(s.GetString("scope"));
            if (scope == null)
            {
                o.Fail($"범위 '{s.GetString("scope")}' 없음");
                yield break;
            }
            var r = LayoutAsserts.FindOverlaps(LayoutAsserts.CollectSiblings(scope));
            if (r.Count > 0)
            {
                o.Extra["problems"] = r;
                o.Fail($"겹침 {r.Count}건: {r[0]}");
            }
        }

        IEnumerator AssertNoClip(StepDef s, StepOutcome o)
        {
            yield return null;
            var scope = Find(s.GetString("scope"));
            if (scope == null)
            {
                o.Fail($"범위 '{s.GetString("scope")}' 없음");
                yield break;
            }
            var r = LayoutAsserts.FindClips(LayoutAsserts.CollectTextBoxes(scope));
            if (r.Count > 0)
            {
                o.Extra["problems"] = r;
                o.Fail($"잘림 {r.Count}건: {r[0]}");
            }
        }
    }
}
