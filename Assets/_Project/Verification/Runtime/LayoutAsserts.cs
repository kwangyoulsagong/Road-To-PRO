using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadToPro.Verification
{
    /// <summary>
    /// '잘림 없음'·'겹침 없음' 단언 (specs/000 FR-26). 판정은 사각형 계산만 하는 순수 함수이고,
    /// UI 트리에서 사각형을 모으는 부분은 따로 둔다 (판정은 EditMode에서 데이터로 테스트).
    /// </summary>
    public static class LayoutAsserts
    {
        public const float Tolerance = 1f;

        public readonly struct TextBox
        {
            public readonly string Name;
            public readonly Vector2 Measured; // 글자가 실제로 필요한 크기
            public readonly Rect Content;     // 요소의 내용 영역 (패딩 안쪽)
            public readonly Rect Bound;       // 요소의 패널 좌표 사각형
            public readonly Rect Parent;      // 부모의 패널 좌표 사각형

            public TextBox(string name, Vector2 measured, Rect content, Rect bound, Rect parent)
            {
                Name = name;
                Measured = measured;
                Content = content;
                Bound = bound;
                Parent = parent;
            }
        }

        static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        public static List<string> FindClips(IEnumerable<TextBox> boxes, float tol = Tolerance)
        {
            var r = new List<string>();
            foreach (var b in boxes)
            {
                if (b.Measured.x > b.Content.width + tol)
                    r.Add($"'{b.Name}' 가로 잘림: 글자 {F(b.Measured.x)}px > 칸 {F(b.Content.width)}px");
                else if (b.Measured.y > b.Content.height + tol)
                    r.Add($"'{b.Name}' 세로 잘림: 글자 {F(b.Measured.y)}px > 칸 {F(b.Content.height)}px");
                else if (b.Bound.xMin < b.Parent.xMin - tol || b.Bound.xMax > b.Parent.xMax + tol ||
                         b.Bound.yMin < b.Parent.yMin - tol || b.Bound.yMax > b.Parent.yMax + tol)
                    r.Add($"'{b.Name}' 부모 밖으로 나감: {b.Bound} ⊄ {b.Parent}");
            }
            return r;
        }

        public static List<string> FindOverlaps(IList<KeyValuePair<string, Rect>> siblings, float tol = Tolerance)
        {
            var r = new List<string>();
            for (int i = 0; i < siblings.Count; i++)
            for (int j = i + 1; j < siblings.Count; j++)
            {
                var a = siblings[i].Value;
                var b = siblings[j].Value;
                float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (w > tol && h > tol)
                    r.Add($"'{siblings[i].Key}'와 '{siblings[j].Key}' 겹침 {F(w)}×{F(h)}px");
            }
            return r;
        }

        static bool Visible(VisualElement e) =>
            e.resolvedStyle.display != DisplayStyle.None &&
            e.resolvedStyle.visibility == Visibility.Visible &&
            e.worldBound.width > 0 && e.worldBound.height > 0;

        static string NameOf(VisualElement e) => string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name;

        /// <summary>scope 아래 보이는 글자 요소 전부.</summary>
        public static List<TextBox> CollectTextBoxes(VisualElement scope)
        {
            var list = new List<TextBox>();
            scope.Query<TextElement>().ForEach(te =>
            {
                if (!Visible(te) || string.IsNullOrEmpty(te.text)) return;
                var content = te.contentRect;
                bool wrap = te.resolvedStyle.whiteSpace == WhiteSpace.Normal;
                var measured = wrap
                    ? te.MeasureTextSize(te.text, content.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined)
                    : te.MeasureTextSize(te.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                var parent = te.parent != null ? te.parent.worldBound : te.worldBound;
                list.Add(new TextBox(NameOf(te), measured, content, te.worldBound, parent));
            });
            return list;
        }

        /// <summary>scope의 직계 자식 중 보이는 것 (카드 목록 등 형제끼리 겹치면 안 되는 묶음).</summary>
        public static List<KeyValuePair<string, Rect>> CollectSiblings(VisualElement scope)
        {
            var list = new List<KeyValuePair<string, Rect>>();
            foreach (var c in scope.Children())
                if (Visible(c) && c.resolvedStyle.position != Position.Absolute)
                    list.Add(new KeyValuePair<string, Rect>(NameOf(c), c.worldBound));
            return list;
        }
    }
}
