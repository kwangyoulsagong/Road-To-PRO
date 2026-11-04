using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Serialization;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>키트 번호·이름 합성 (specs/043 FR-21·23, AC-05·17).</summary>
    public class KitCompositorTests
    {
        internal const string FontsDir = "Assets/_Project/Presentation/Players/Fonts";

        /// <summary>
        /// T02 스파이크: 오프라인으로 구운 글리프 아틀라스(Tools/fonts/bake_glyphs.py)를 그래픽 장치 없이(-nographics)
        /// CPU로 읽어 한글·라틴·숫자 글리프를 꺼낼 수 있는가. (FontEngine에 CPU 래스터화 공개 API가 없어 대체.)
        /// </summary>
        [Test]
        public void Spike_HangulAndLatinGlyphsReadable()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(FontsDir + "/kit-glyphs.json");
            Assert.IsNotNull(json, "kit-glyphs.json 없음");
            var meta = JsonReader.Parse(json.text);
            Check(meta, "names", "김민준Smith", FontsDir + "/kit-glyphs-names.png");
            Check(meta, "numbers", "1079", FontsDir + "/kit-glyphs-numbers.png");
        }

        internal static KitCompositor NewCompositor()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(FontsDir + "/kit-glyphs.json");
            return new KitCompositor(GlyphAtlasData.Parse(json.text),
                AssetDatabase.LoadAssetAtPath<Texture2D>(FontsDir + "/kit-glyphs-names.png"),
                AssetDatabase.LoadAssetAtPath<Texture2D>(FontsDir + "/kit-glyphs-numbers.png"));
        }

        internal static List<KitEntry> Squad()
        {
            var names = new[] { "", "Kim", "Alexandersen", "Maximilian Oberhausenberg", "김민준", "Ünal-Østergård Lindqvist", "Wolfeschlegelsteinhausenbergerdorff" };
            var nums = new[] { 1, 7, 10, 99, 23, 4, 88, 0, 100 };
            var l = new List<KitEntry>();
            for (int i = 0; i < 24; i++) l.Add(new KitEntry(nums[i % nums.Length], names[i % names.Length]));
            return l;
        }

        static bool InsideSub(int x, int y, out int cell)
        {
            cell = -1;
            for (int i = 0; i < PlayerLook.Cells; i++)
            {
                var c = PlayerLook.CellPixels(i);
                foreach (var sub in new[] { PlayerLook.NameSub, PlayerLook.NumberSub })
                {
                    int sx = c.x + Mathf.RoundToInt(sub.x * c.width), sy = c.y + Mathf.RoundToInt(sub.y * c.height);
                    int sw = Mathf.RoundToInt(sub.z * c.width), sh = Mathf.RoundToInt(sub.w * c.height);
                    if (x >= sx && x < sx + sw && y >= sy && y < sy + sh) { cell = i; return true; }
                }
            }
            return false;
        }

        [Test]
        public void Glyphs_InsideCell()
        {
            var comp = NewCompositor();
            var tex = comp.ComposeTeam(Squad(), NullLogger.Instance);
            try
            {
                var px = tex.GetPixelData<byte>(0);
                var inkPerCell = new int[PlayerLook.Cells];
                for (int y = 0; y < tex.height; y++)
                    for (int x = 0; x < tex.width; x++)
                    {
                        if (px[y * tex.width + x] < 64) continue;
                        Assert.IsTrue(InsideSub(x, y, out var c), $"글자 픽셀이 칸 영역 밖 ({x},{y})");
                        inkPerCell[c]++;
                    }
                var squad = Squad();
                for (int i = 0; i < squad.Count; i++)
                {
                    bool expectInk = (squad[i].Number >= 1 && squad[i].Number <= 99) || squad[i].Name.Length > 0;
                    if (expectInk) Assert.Greater(inkPerCell[i], 30, "칸 " + i + " 잉크");
                    else Assert.AreEqual(0, inkPerCell[i], "칸 " + i + " 비어야 함");
                }
            }
            finally { Object.DestroyImmediate(tex); }
        }

        [Test]
        public void Compose_Deterministic()
        {
            var comp = NewCompositor();
            var a = comp.ComposeTeam(Squad(), NullLogger.Instance);
            var b = comp.ComposeTeam(Squad(), NullLogger.Instance);
            try { CollectionAssert.AreEqual(a.GetPixelData<byte>(0).ToArray(), b.GetPixelData<byte>(0).ToArray()); }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [Test]
        public void DuringPlayback_Zero()
        {
            KitCompositor.PlaybackCompositions = 0;
            KitCompositor.PlaybackActive = false;
            var comp = NewCompositor();
            Object.DestroyImmediate(comp.ComposeTeam(Squad(), NullLogger.Instance));
            Assert.AreEqual(0, KitCompositor.PlaybackCompositions, "로딩 중 합성은 세지 않는다");
            var log = new MemoryLogger(LogLevel.Warning);
            KitCompositor.PlaybackActive = true;
            try { Object.DestroyImmediate(comp.ComposeTeam(new List<KitEntry> { new KitEntry(9, "Kim") }, log)); }
            finally { KitCompositor.PlaybackActive = false; }
            Assert.AreEqual(1, KitCompositor.PlaybackCompositions, "재생 중 합성은 카운트 + 진단 로그");
            Assert.AreEqual(1, log.Entries.Count);
            KitCompositor.PlaybackCompositions = 0;
        }

        static JsonObject Obj(object o, string key)
        {
            Assert.IsTrue(((JsonObject)o).TryGet(key, out var v), "키 없음: " + key);
            return (JsonObject)v;
        }

        static void Check(object meta, string set, string text, string atlasPath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            Assert.IsNotNull(tex, atlasPath);
            Assert.IsTrue(tex.isReadable, atlasPath + " 읽기 불가");
            var px = tex.GetPixels32();
            var glyphs = Obj(Obj(Obj(meta, "sets"), set), "glyphs");
            foreach (var ch in text)
            {
                Assert.IsTrue(glyphs.TryGet(((int)ch).ToString(), out var o), set + " 글리프 없음: " + ch);
                var g = (System.Collections.Generic.List<object>)o;
                int x = (int)System.Convert.ToDouble(g[0]), y = (int)System.Convert.ToDouble(g[1]);
                int w = (int)System.Convert.ToDouble(g[2]), h = (int)System.Convert.ToDouble(g[3]);
                int ink = 0;
                for (int yy = y; yy < y + h; yy++)
                    for (int xx = x; xx < x + w; xx++)
                        if (px[(tex.height - 1 - yy) * tex.width + xx].r > 128) ink++; // PNG 위→아래, Unity 아래→위
                Assert.Greater(ink, 20, set + " 잉크 픽셀: " + ch);
            }
        }
    }
}
