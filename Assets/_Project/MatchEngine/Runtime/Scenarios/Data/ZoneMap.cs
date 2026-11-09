using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>
    /// 구역 체계 (FR-10, 2026-11-09 결정): 18존(6열 × 3레인)은 사전 필터 키, 세부 구역은 좌표 영역.
    /// 모든 좌표는 팀 관점 정규화(x 0 = 자기 골라인 → 1 = 상대 골라인, y 0~1)다. 영역 파일은 표준 피치(105×68 m) 미터로 쓴다.
    /// </summary>
    public sealed class ZoneMap
    {
        public const int Columns = 6, Lanes = 3, Cells = Columns * Lanes;
        const float StdL = 105f, StdW = 68f;
        public const uint AllCells = (1u << Cells) - 1u;

        sealed class Region
        {
            public string Id;
            public bool BallSide;
            public float[] Rects; // x0,y0,x1,y1 정규화
        }

        readonly Region[] _regions;
        readonly Dictionary<string, int> _byId = new Dictionary<string, int>(StringComparer.Ordinal);

        ZoneMap(Region[] regions)
        {
            _regions = regions;
            for (int i = 0; i < regions.Length; i++) _byId[regions[i].Id] = i;
        }

        public int Count => _regions.Length;
        public string IdOf(int region) => _regions[region].Id;

        public int IndexOf(string id) => id != null && _byId.TryGetValue(id, out var i) ? i : -1;

        public static ZoneMap Parse(string json)
        {
            var root = JsonReader.Parse(json) as JsonObject ?? throw new FormatException("구역 파일 최상위가 객체가 아니다");
            if (!root.TryGet("regions", out var v) || !(v is List<object> list)) throw new FormatException("구역 파일에 'regions' 배열이 없다");
            var regions = new Region[list.Count];
            for (int i = 0; i < regions.Length; i++)
            {
                var o = list[i] as JsonObject ?? throw new FormatException("구역이 객체가 아니다");
                if (!o.TryGet("id", out var idv) || !(idv is string id)) throw new FormatException("구역 id 없음");
                if (!o.TryGet("rects", out var rv) || !(rv is List<object> rects) || rects.Count == 0) throw new FormatException($"구역 {id}: rects 없음");
                var r = new Region { Id = id, Rects = new float[rects.Count * 4] };
                if (o.TryGet("ballSide", out var bs) && bs is bool b) r.BallSide = b;
                for (int k = 0; k < rects.Count; k++)
                {
                    var q = rects[k] as List<object>;
                    if (q == null || q.Count != 4) throw new FormatException($"구역 {id}: 사각형은 [x0,y0,x1,y1]");
                    r.Rects[k * 4 + 0] = (float)((double)q[0] / StdL);
                    r.Rects[k * 4 + 1] = (float)((double)q[1] / StdW);
                    r.Rects[k * 4 + 2] = (float)((double)q[2] / StdL);
                    r.Rects[k * 4 + 3] = (float)((double)q[3] / StdW);
                }
                regions[i] = r;
            }
            return new ZoneMap(regions);
        }

        static float Flip(float y) => 1f - y;

        public static int CellOf(Vec2 p, bool mirror)
        {
            var y = mirror ? Flip(p.Y) : p.Y;
            var c = (int)(p.X * Columns);
            var l = (int)(y * Lanes);
            if (c < 0) c = 0; else if (c >= Columns) c = Columns - 1;
            if (l < 0) l = 0; else if (l >= Lanes) l = Lanes - 1;
            return c * Lanes + l;
        }

        /// <summary>열(1~6)·레인(0 L, 1 C, 2 R) → 비트.</summary>
        public static uint CellBit(int column1Based, int lane) => 1u << ((column1Based - 1) * Lanes + lane);

        public bool Contains(int region, Vec2 p, bool mirror)
        {
            var r = _regions[region];
            var y = p.Y;
            if (r.BallSide) { if (y > 0.5f) y = Flip(y); }
            else if (mirror) y = Flip(y);
            for (int i = 0; i < r.Rects.Length; i += 4)
                if (p.X >= r.Rects[i] && p.X <= r.Rects[i + 2] && y >= r.Rects[i + 1] && y <= r.Rects[i + 3]) return true;
            return false;
        }

        /// <summary>구역이 걸치는 18존 비트 (사전 필터, 반전 전 기준).</summary>
        public uint CellMask(int region)
        {
            var r = _regions[region];
            uint mask = 0;
            for (int i = 0; i < r.Rects.Length; i += 4)
            {
                for (int c = 0; c < Columns; c++)
                {
                    var x0 = (float)c / Columns; var x1 = (float)(c + 1) / Columns;
                    if (r.Rects[i + 2] < x0 || r.Rects[i] > x1) continue;
                    for (int l = 0; l < Lanes; l++)
                    {
                        var y0 = (float)l / Lanes; var y1 = (float)(l + 1) / Lanes;
                        if (r.BallSide)
                        {
                            // 공 쪽 기준 구역은 어느 쪽 측면이든 맞을 수 있다
                            if (!(r.Rects[i + 3] < y0 || r.Rects[i + 1] > y1) || !(r.Rects[i + 3] < 1f - y1 || r.Rects[i + 1] > 1f - y0)) mask |= 1u << (c * Lanes + l);
                        }
                        else if (!(r.Rects[i + 3] < y0 || r.Rects[i + 1] > y1)) mask |= 1u << (c * Lanes + l);
                    }
                }
            }
            return mask;
        }

        /// <summary>반전한 18존 비트 (레인 L↔R).</summary>
        public static uint MirrorMask(uint mask)
        {
            uint m = 0;
            for (int c = 0; c < Columns; c++)
                for (int l = 0; l < Lanes; l++)
                    if ((mask & (1u << (c * Lanes + l))) != 0) m |= 1u << (c * Lanes + (Lanes - 1 - l));
            return m;
        }

        /// <summary>구역 첫 사각형의 중심. 공 쪽 기준 구역은 공이 있는 측면으로 맞추고 반전하지 않는다.</summary>
        public Vec2 Center(int region, bool mirror, float ballY)
        {
            var r = _regions[region];
            var x = (r.Rects[0] + r.Rects[2]) * 0.5f;
            var y = (r.Rects[1] + r.Rects[3]) * 0.5f;
            if (r.BallSide) { if (ballY > 0.5f) y = Flip(y); }
            else if (mirror) y = Flip(y);
            return new Vec2(x, y);
        }
    }
}
