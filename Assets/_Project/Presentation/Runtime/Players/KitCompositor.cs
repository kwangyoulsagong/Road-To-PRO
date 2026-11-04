using System;
using System.Collections.Generic;
using System.Diagnostics;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players.Logic;
using Unity.Collections;
using UnityEngine;
using ILogger = RoadToPro.Core.Logging.ILogger;

namespace RoadToPro.Presentation.Players
{
    /// <summary>팀 아틀라스에 넣을 선수 한 명 (칸 번호 = 목록 순서).</summary>
    public readonly struct KitEntry
    {
        public readonly int Number;
        public readonly string Name;

        public KitEntry(int number, string name)
        {
            Number = number;
            Name = name;
        }
    }

    /// <summary>
    /// 팀 번호·이름 아틀라스 합성 (specs/043 FR-21·23, AC-05·17). 팀당 1회, 하이라이트 로딩 중에만.
    /// CPU 합성(구운 글리프 아틀라스 → R8 1024²): 같은 입력 → 같은 바이트. 재생 중에 불리면 진단 로그 + 카운터(AC-17은 0이어야 한다).
    /// </summary>
    public sealed class KitCompositor
    {
        /// <summary>재생 중 표시 — 041 재생기(E2E는 화면 스크립트)가 켠다.</summary>
        public static bool PlaybackActive;
        /// <summary>재생 중에 일어난 합성 수 (AC-17: 0).</summary>
        public static int PlaybackCompositions;

        readonly GlyphAtlasData _data;
        readonly byte[] _names, _numbers;
        readonly int _nw, _nh, _uw, _uh;
        readonly List<GlyphQuad> _quads = new List<GlyphQuad>(64);

        public float LastMs { get; private set; }

        public KitCompositor(GlyphAtlasData data, Texture2D namesAtlas, Texture2D numbersAtlas)
        {
            _data = data;
            _names = Read(namesAtlas, out _nw, out _nh);
            _numbers = Read(numbersAtlas, out _uw, out _uh);
        }

        static byte[] Read(Texture2D t, out int w, out int h)
        {
            if (!t.isReadable) throw new ArgumentException(t.name + "이 읽기 불가 — PlayerImportSettings 확인");
            w = t.width;
            h = t.height;
            var px = t.GetPixels32();
            var o = new byte[px.Length];
            for (int i = 0; i < px.Length; i++) o[i] = px[i].r;
            return o;
        }

        /// <summary>선수 목록 → 팀 아틀라스(R8, 읽기 전용으로 업로드). 칸 수(24)를 넘는 선수는 번호·이름 없이 진단 로그.</summary>
        public Texture2D ComposeTeam(IReadOnlyList<KitEntry> players, ILogger log)
        {
            if (PlaybackActive)
            {
                PlaybackCompositions++;
                log.Warning("[kit] 재생 중 키트 합성 — 로딩 단계에서 해야 한다 (AC-17)");
            }
            var sw = Stopwatch.StartNew();
            var buf = new byte[PlayerLook.AtlasSize * PlayerLook.AtlasSize];
            for (int i = 0; i < players.Count; i++)
            {
                if (i >= PlayerLook.Cells)
                {
                    log.Warning("[kit] 팀 아틀라스 칸({0}) 초과 — {1}번 선수 번호·이름 생략", PlayerLook.Cells, players[i].Number);
                    continue;
                }
                var cell = PlayerLook.CellPixels(i);
                DrawName(buf, cell, players[i].Name, log);
                DrawNumber(buf, cell, players[i].Number, log);
            }
            var tex = new Texture2D(PlayerLook.AtlasSize, PlayerLook.AtlasSize, TextureFormat.R8, false, true)
            {
                name = "TeamKitAtlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var na = new NativeArray<byte>(buf, Allocator.Temp);
            tex.SetPixelData(na, 0);
            na.Dispose();
            tex.Apply(false, false);
            LastMs = (float)sw.Elapsed.TotalMilliseconds;
            return tex;
        }

        static RectInt Sub(RectInt cell, Vector4 sub) =>
            new RectInt(cell.x + Mathf.RoundToInt(sub.x * cell.width), cell.y + Mathf.RoundToInt(sub.y * cell.height),
                Mathf.RoundToInt(sub.z * cell.width), Mathf.RoundToInt(sub.w * cell.height));

        void DrawName(byte[] buf, RectInt cell, string name, ILogger log)
        {
            if (string.IsNullOrEmpty(name)) return;
            var r = Sub(cell, PlayerLook.NameSub);
            _quads.Clear();
            KitTextLayout.LayoutName(name, _data.Names, r.width, r.height, _quads, log);
            Blit(buf, r, _quads, _names, _nw, _nh);
        }

        void DrawNumber(byte[] buf, RectInt cell, int number, ILogger log)
        {
            var r = Sub(cell, PlayerLook.NumberSub);
            _quads.Clear();
            KitTextLayout.LayoutNumber(number, _data.Numbers, r.width, r.height, _quads, log);
            Blit(buf, r, _quads, _numbers, _uw, _uh);
        }

        /// <summary>글리프 사각형을 영역에 이중선형으로 복사(덮임은 최대값). 배치 좌표는 영역 왼쪽 위 원점·y 아래, 아틀라스 PNG도 위가 행 0.</summary>
        static void Blit(byte[] dst, RectInt area, List<GlyphQuad> quads, byte[] src, int sw, int sh)
        {
            int size = PlayerLook.AtlasSize;
            foreach (var q in quads)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(q.X)), x1 = Mathf.Min(area.width, Mathf.CeilToInt(q.X + q.W));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(q.Y)), y1 = Mathf.Min(area.height, Mathf.CeilToInt(q.Y + q.H));
                if (q.W <= 0 || q.H <= 0) continue;
                for (int y = y0; y < y1; y++)
                {
                    float gy = (y + 0.5f - q.Y) / q.H * q.Src.H - 0.5f;
                    int dy = area.y + area.height - 1 - y; // 영역 위 = 텍스처 행 큰 쪽
                    for (int x = x0; x < x1; x++)
                    {
                        float gx = (x + 0.5f - q.X) / q.W * q.Src.W - 0.5f;
                        float v = Sample(src, sw, sh, q.Src, gx, gy);
                        if (v <= 0) continue;
                        int di = dy * size + area.x + x;
                        byte b = (byte)Mathf.Min(255, Mathf.RoundToInt(v));
                        if (b > dst[di]) dst[di] = b;
                    }
                }
            }
        }

        /// <summary>글리프 로컬 좌표(gx, gy: 글리프 왼쪽 위 기준 픽셀)에서 이중선형 샘플. 글리프 밖은 0.</summary>
        static float Sample(byte[] src, int sw, int sh, Glyph g, float gx, float gy)
        {
            int ix = Mathf.FloorToInt(gx), iy = Mathf.FloorToInt(gy);
            float fx = gx - ix, fy = gy - iy;
            float a = Px(src, sw, sh, g, ix, iy), b = Px(src, sw, sh, g, ix + 1, iy);
            float c = Px(src, sw, sh, g, ix, iy + 1), d = Px(src, sw, sh, g, ix + 1, iy + 1);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        static float Px(byte[] src, int sw, int sh, Glyph g, int x, int y)
        {
            if (x < 0 || y < 0 || x >= g.W || y >= g.H) return 0;
            int ax = g.X + x, ayTop = g.Y + y;           // PNG 좌표(위 = 0)
            int row = sh - 1 - ayTop;                      // Unity 텍스처 행(아래 = 0)
            return src[row * sw + ax];
        }
    }
}
