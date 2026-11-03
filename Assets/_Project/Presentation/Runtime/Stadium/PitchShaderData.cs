using UnityEngine;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>
    /// 피치 셰이더 입력 (specs/044 FR-30~33). 라인 정의를 고정 크기 배열로 묶는다.
    /// 선분 (ax, az, bx, bz), 원호 (cx, cz, r, cos(반각)) + (가운데 방향 x, z, 0, 0), 마크 (cx, cz, r, 0).
    /// 원호 각도 검사는 atan2 대신 가운데 방향과의 내적 (전체 원은 cos = −2).
    /// 라인이 X·Z 두 축 모두 대칭이면(Law 1 피치는 항상) 셰이더가 |x|, |z|로 접고 1사분면에 걸친 라인만 넘긴다 — 픽셀당 계산 약 1/2.5.
    /// 셸·마모·젖음은 High 프리셋용 자리(048) — 지금은 0 고정.
    /// </summary>
    public sealed class PitchShaderData
    {
        public const int MaxSegments = 32;
        public const int MaxArcs = 16;
        public const int MaxSpots = 8;

        public readonly Vector4[] Segments = new Vector4[MaxSegments];
        public readonly Vector4[] Arcs = new Vector4[MaxArcs];
        public readonly Vector4[] ArcEnds = new Vector4[MaxArcs];
        public readonly Vector4[] Spots = new Vector4[MaxSpots];
        public int SegmentCount { get; private set; }
        public int ArcCount { get; private set; }
        public int SpotCount { get; private set; }
        public float HalfLineWidth { get; private set; }
        public float StripeWidth { get; private set; }
        public int StripePattern { get; private set; }  // 0 직선, 1 체커, 2 원형
        public bool Folded { get; private set; }
        public float ShellAmount => 0f;
        public float WearAmount => 0f;
        public float Wetness => 0f;

        public static PitchShaderData From(StadiumLayout l)
        {
            if (l.Segments.Count > MaxSegments || l.Arcs.Count > MaxArcs || l.Spots.Count > MaxSpots)
                throw new System.ArgumentException($"라인 수 초과: 선분 {l.Segments.Count}/{MaxSegments}, 원호 {l.Arcs.Count}/{MaxArcs}, 마크 {l.Spots.Count}/{MaxSpots}");
            var d = new PitchShaderData
            {
                HalfLineWidth = l.LineWidth * 0.5f,
                StripeWidth = l.StripeWidth,
                StripePattern = l.StripePattern == "checker" ? 1 : l.StripePattern == "circle" ? 2 : 0,
            };
            d.Folded = IsSymmetric(l);
            int ns = 0, na = 0, np = 0;
            foreach (var sg in l.Segments)
            {
                if (d.Folded && (Mathf.Max(sg.A.x, sg.B.x) < 0f || Mathf.Max(sg.A.y, sg.B.y) < 0f)) continue;
                d.Segments[ns++] = new Vector4(sg.A.x, sg.A.y, sg.B.x, sg.B.y);
            }
            foreach (var a in l.Arcs)
            {
                if (d.Folded && (a.Center.x < 0f || a.Center.y < 0f)) continue;
                var span = (a.ToDeg - a.FromDeg) * Mathf.Deg2Rad;
                var mid = (a.FromDeg + a.ToDeg) * 0.5f * Mathf.Deg2Rad;
                var cosHalf = span >= 2f * Mathf.PI - 1e-4f ? -2f : Mathf.Cos(span * 0.5f);
                d.Arcs[na] = new Vector4(a.Center.x, a.Center.y, a.Radius, cosHalf);
                d.ArcEnds[na++] = new Vector4(Mathf.Cos(mid), Mathf.Sin(mid), span, 0f);
            }
            foreach (var sp in l.Spots)
            {
                if (d.Folded && (sp.Center.x < 0f || sp.Center.y < 0f)) continue;
                d.Spots[np++] = new Vector4(sp.Center.x, sp.Center.y, sp.Radius, 0f);
            }
            d.SegmentCount = ns;
            d.ArcCount = na;
            d.SpotCount = np;
            return d;
        }

        /// <summary>모든 라인·마크가 X축·Z축 거울상을 가지면 true (1 cm 허용).</summary>
        public static bool IsSymmetric(StadiumLayout l)
        {
            const float tol = 0.01f;
            bool Has(System.Func<Vector2, Vector2> m)
            {
                foreach (var s in l.Segments)
                {
                    var a = m(s.A); var b = m(s.B);
                    var ok = false;
                    foreach (var t in l.Segments)
                        if ((Vector2.Distance(a, t.A) < tol && Vector2.Distance(b, t.B) < tol) || (Vector2.Distance(a, t.B) < tol && Vector2.Distance(b, t.A) < tol)) { ok = true; break; }
                    if (!ok) return false;
                }
                foreach (var s in l.Arcs)
                {
                    var c = m(s.Center);
                    var ok = false;
                    foreach (var t in l.Arcs)
                        if (Vector2.Distance(c, t.Center) < tol && Mathf.Abs(s.Radius - t.Radius) < tol && Mathf.Abs((s.ToDeg - s.FromDeg) - (t.ToDeg - t.FromDeg)) < 0.1f) { ok = true; break; }
                    if (!ok) return false;
                }
                foreach (var s in l.Spots)
                {
                    var c = m(s.Center);
                    var ok = false;
                    foreach (var t in l.Spots)
                        if (Vector2.Distance(c, t.Center) < tol) { ok = true; break; }
                    if (!ok) return false;
                }
                return true;
            }
            return Has(v => new Vector2(-v.x, v.y)) && Has(v => new Vector2(v.x, -v.y));
        }

        static readonly int IdSegments = Shader.PropertyToID("_LineSegments");
        static readonly int IdArcs = Shader.PropertyToID("_LineArcs");
        static readonly int IdArcEnds = Shader.PropertyToID("_LineArcEnds");
        static readonly int IdSpots = Shader.PropertyToID("_LineSpots");
        static readonly int IdCounts = Shader.PropertyToID("_LineCounts");
        static readonly int IdHalfWidth = Shader.PropertyToID("_LineHalfWidth");
        static readonly int IdStripe = Shader.PropertyToID("_StripeParams");
        static readonly int IdHigh = Shader.PropertyToID("_HighParams");

        public void ApplyTo(Material m)
        {
            m.SetVectorArray(IdSegments, Segments);
            m.SetVectorArray(IdArcs, Arcs);
            m.SetVectorArray(IdArcEnds, ArcEnds);
            m.SetVectorArray(IdSpots, Spots);
            m.SetVector(IdCounts, new Vector4(SegmentCount, ArcCount, SpotCount, Folded ? 1f : 0f));
            m.SetFloat(IdHalfWidth, HalfLineWidth);
            m.SetVector(IdStripe, new Vector4(StripeWidth, StripePattern, 0f, 0f));
            m.SetVector(IdHigh, new Vector4(ShellAmount, WearAmount, Wetness, 0f));
        }

        public void ApplyTo(MaterialPropertyBlock m)
        {
            m.SetVectorArray(IdSegments, Segments);
            m.SetVectorArray(IdArcs, Arcs);
            m.SetVectorArray(IdArcEnds, ArcEnds);
            m.SetVectorArray(IdSpots, Spots);
            m.SetVector(IdCounts, new Vector4(SegmentCount, ArcCount, SpotCount, Folded ? 1f : 0f));
            m.SetFloat(IdHalfWidth, HalfLineWidth);
            m.SetVector(IdStripe, new Vector4(StripeWidth, StripePattern, 0f, 0f));
            m.SetVector(IdHigh, new Vector4(ShellAmount, WearAmount, Wetness, 0f));
        }
    }
}
