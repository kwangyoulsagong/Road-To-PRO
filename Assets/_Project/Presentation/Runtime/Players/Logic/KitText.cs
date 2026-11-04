using System;
using System.Collections.Generic;
using System.Globalization;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>구운 글리프 하나 (Tools/fonts/bake_glyphs.py). 좌표는 아틀라스 왼쪽 위 원점·y 아래, 픽셀.</summary>
    public readonly struct Glyph
    {
        public readonly int X, Y, W, H;
        /// <summary>BearingX: 펜 위치 → 글리프 왼쪽, BearingY: 기준선 → 글리프 위쪽(위로 +).</summary>
        public readonly float BearingX, BearingY, Advance;

        public Glyph(int x, int y, int w, int h, float bx, float by, float adv)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
            BearingX = bx;
            BearingY = by;
            Advance = adv;
        }
    }

    /// <summary>글리프 세트 하나(이름용 또는 번호용).</summary>
    public sealed class GlyphSet
    {
        public readonly int Size, Ascent, Descent, AtlasW, AtlasH;
        readonly Dictionary<int, Glyph> _g;

        public GlyphSet(int size, int ascent, int descent, int atlasW, int atlasH, Dictionary<int, Glyph> glyphs)
        {
            Size = size;
            Ascent = ascent;
            Descent = descent;
            AtlasW = atlasW;
            AtlasH = atlasH;
            _g = glyphs;
        }

        public int Count => _g.Count;
        public bool TryGet(int codepoint, out Glyph g) => _g.TryGetValue(codepoint, out g);
    }

    /// <summary>kit-glyphs.json 파서 (schemaVersion 1).</summary>
    public sealed class GlyphAtlasData
    {
        public GlyphSet Names { get; private set; }
        public GlyphSet Numbers { get; private set; }

        public static GlyphAtlasData Parse(string json)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            if (!root.TryGet("schemaVersion", out var v) || Convert.ToInt32(v, CultureInfo.InvariantCulture) != 1)
                throw new FormatException("kit-glyphs.json schemaVersion 1이 아니다");
            root.TryGet("sets", out var sets);
            var s = (JsonObject)sets;
            return new GlyphAtlasData { Names = Set(s, "names"), Numbers = Set(s, "numbers") };
        }

        static int I(JsonObject o, string k)
        {
            if (!o.TryGet(k, out var v)) throw new FormatException("키 없음: " + k);
            return (int)Math.Round((double)v);
        }

        static GlyphSet Set(JsonObject sets, string name)
        {
            if (!sets.TryGet(name, out var so)) throw new FormatException("세트 없음: " + name);
            var o = (JsonObject)so;
            o.TryGet("atlas", out var atl);
            var atlas = (List<object>)atl;
            o.TryGet("glyphs", out var go);
            var dict = new Dictionary<int, Glyph>();
            foreach (var kv in ((JsonObject)go).Members)
            {
                var a = (List<object>)kv.Value;
                dict[int.Parse(kv.Key, CultureInfo.InvariantCulture)] = new Glyph(
                    (int)(double)a[0], (int)(double)a[1], (int)(double)a[2], (int)(double)a[3],
                    (float)(double)a[4], (float)(double)a[5], (float)(double)a[6]);
            }
            return new GlyphSet(I(o, "size"), I(o, "ascent"), I(o, "descent"), (int)(double)atlas[0], (int)(double)atlas[1], dict);
        }
    }

    /// <summary>배치된 글리프 (대상 영역 왼쪽 위 원점, y 아래, 픽셀).</summary>
    public readonly struct GlyphQuad
    {
        public readonly int Codepoint;
        public readonly float X, Y, W, H;
        public readonly Glyph Src;

        public GlyphQuad(int cp, float x, float y, float w, float h, Glyph src)
        {
            Codepoint = cp;
            X = x;
            Y = y;
            W = w;
            H = h;
            Src = src;
        }
    }

    public readonly struct NameLayoutResult
    {
        public readonly string Shown;
        public readonly float Tracking;
        public readonly bool SurnameOnly, Truncated;

        public NameLayoutResult(string shown, float tracking, bool surnameOnly, bool truncated)
        {
            Shown = shown;
            Tracking = tracking;
            SurnameOnly = surnameOnly;
            Truncated = truncated;
        }
    }

    /// <summary>
    /// 키트 번호·이름 배치 (specs/043 FR-21·23·65·66).
    /// 번호: 1~99만, 가운데 정렬, 숫자 높이가 영역 높이의 NumberFill. 넓으면 줄인다(번호는 자르지 않음).
    /// 이름: 높이 고정(영역 높이의 NameFill) → 넓으면 자간을 MinTracking까지 줄임 → 성만 → 그래도 넓으면 끝 글자부터 자름(말줄임 없음).
    /// 아틀라스에 없는 글자(KS X 1001 밖 한글 등)는 건너뛰고 진단 로그.
    /// </summary>
    public static class KitTextLayout
    {
        public const float NumberFill = 0.86f;
        public const float NameFill = 0.80f;
        /// <summary>자간 최소(em 비율, 음수 = 좁힘).</summary>
        public const float MinTracking = -0.08f;
        const float TrackingStep = 0.01f;

        public static int LayoutNumber(int number, GlyphSet set, float areaW, float areaH, List<GlyphQuad> output, ILogger log)
        {
            if (number < 1 || number > 99)
            {
                log.Warning("[kit] 등번호 {0}은 1~99 밖 — 표시하지 않음 (FR-65)", number);
                return 0;
            }
            Span<int> digits = stackalloc int[2];
            int n = number >= 10 ? 2 : 1;
            if (n == 2) { digits[0] = number / 10; digits[1] = number % 10; }
            else digits[0] = number;
            float top = float.MaxValue, bottom = float.MinValue, width = 0;
            for (int i = 0; i < n; i++)
            {
                set.TryGet('0' + digits[i], out var g);
                top = Math.Min(top, -g.BearingY);
                bottom = Math.Max(bottom, -g.BearingY + g.H);
                width += i == n - 1 ? g.BearingX + g.W : g.Advance;
            }
            float s = areaH * NumberFill / (bottom - top);
            if (width * s > areaW * 0.94f) s = areaW * 0.94f / width;
            float x0 = (areaW - width * s) * 0.5f;
            float baseline = (areaH - (bottom - top) * s) * 0.5f - top * s;
            float pen = x0;
            for (int i = 0; i < n; i++)
            {
                set.TryGet('0' + digits[i], out var g);
                output.Add(new GlyphQuad('0' + digits[i], pen + g.BearingX * s, baseline - g.BearingY * s, g.W * s, g.H * s, g));
                pen += g.Advance * s;
            }
            return n;
        }

        public static string Surname(string name)
        {
            name = name.Trim();
            int sp = name.LastIndexOf(' ');
            if (sp >= 0) return name.Substring(sp + 1);
            if (name.Length >= 2 && name[0] >= '가' && name[0] <= '힣') return name.Substring(0, 1);
            return name;
        }

        public static NameLayoutResult LayoutName(string name, GlyphSet set, float areaW, float areaH, List<GlyphQuad> output, ILogger log)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return new NameLayoutResult("", 0, false, false);
            name = Filter(name, set, log);
            float lineH = set.Ascent + set.Descent;
            float s = areaH * NameFill / lineH;
            bool surname = false, cut = false;
            float tr = 0;
            while (Width(name, set, s, tr) > areaW && tr > MinTracking + 1e-6f) tr = Math.Max(MinTracking, tr - TrackingStep);
            if (Width(name, set, s, tr) > areaW)
            {
                var sn = Surname(name);
                if (sn != name)
                {
                    name = sn;
                    surname = true;
                    tr = 0;
                    while (Width(name, set, s, tr) > areaW && tr > MinTracking + 1e-6f) tr = Math.Max(MinTracking, tr - TrackingStep);
                }
            }
            while (name.Length > 0 && Width(name, set, s, tr) > areaW)
            {
                name = name.Substring(0, name.Length - 1).TrimEnd();
                cut = true;
            }
            float w = Width(name, set, s, tr, out float minX);
            float pen = (areaW - w) * 0.5f - minX * s;
            float baseline = (areaH - lineH * s) * 0.5f + set.Ascent * s;
            for (int i = 0; i < name.Length; i++)
            {
                if (!set.TryGet(name[i], out var g)) continue;
                if (g.W > 0 && g.H > 0)
                    output.Add(new GlyphQuad(name[i], pen + g.BearingX * s, baseline - g.BearingY * s, g.W * s, g.H * s, g));
                pen += (g.Advance + tr * set.Size) * s;
            }
            ClampVertical(output, areaH);
            return new NameLayoutResult(name, tr, surname, cut);
        }

        static string Filter(string name, GlyphSet set, ILogger log)
        {
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (set.TryGet(ch, out _)) sb.Append(ch);
                else log.Warning("[kit] 이름 글자 '{0}' (U+{1:X4})가 글리프 아틀라스에 없다 — 건너뜀", ch, (int)ch);
            }
            return sb.ToString();
        }

        /// <summary>글자 상자 기준 폭(첫 글자 왼쪽 ~ 마지막 글자 오른쪽).</summary>
        static float Width(string text, GlyphSet set, float s, float tr) => Width(text, set, s, tr, out _);

        static float Width(string text, GlyphSet set, float s, float tr, out float minXRel)
        {
            minXRel = 0;
            if (text.Length == 0) return 0;
            float pen = 0, minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < text.Length; i++)
            {
                if (!set.TryGet(text[i], out var g)) continue;
                if (g.W > 0)
                {
                    minX = Math.Min(minX, pen + g.BearingX);
                    maxX = Math.Max(maxX, pen + g.BearingX + g.W);
                }
                pen += g.Advance + tr * set.Size;
            }
            if (maxX < minX) return 0;
            minXRel = minX;
            return (maxX - minX) * s;
        }

        /// <summary>악센트 등으로 어센트 밖에 나간 글자가 있으면 그 줄 전체를 아래·위 안으로 옮긴다(크기 유지).</summary>
        static void ClampVertical(List<GlyphQuad> q, float areaH)
        {
            if (q.Count == 0) return;
            float top = float.MaxValue, bottom = float.MinValue;
            foreach (var g in q) { top = Math.Min(top, g.Y); bottom = Math.Max(bottom, g.Y + g.H); }
            float shift = top < 0 ? -top : bottom > areaH ? areaH - bottom : 0;
            float span = bottom - top;
            float scale = span > areaH ? areaH / span : 1f;
            if (shift == 0 && scale == 1f) return;
            for (int i = 0; i < q.Count; i++)
            {
                var g = q[i];
                float y = scale == 1f ? g.Y + shift : (g.Y - top) * scale;
                float h = g.H * scale;
                float cx = g.X + g.W * 0.5f;
                float w = g.W * scale;
                q[i] = new GlyphQuad(g.Codepoint, scale == 1f ? g.X : cx - w * 0.5f, y, w, h, g.Src);
            }
        }
    }
}
