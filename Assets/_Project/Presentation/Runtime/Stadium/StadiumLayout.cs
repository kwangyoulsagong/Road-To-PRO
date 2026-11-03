using System;
using System.Collections.Generic;
using System.Globalization;
using RoadToPro.Core.Serialization;
using UnityEngine;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>
    /// 경기장 생성기 산출물 `*.layout.json` (specs/044 plan '데이터 · 스키마', schemaVersion 1).
    /// 좌표는 모두 Unity 월드(미터, X 길이·Y 위·Z 폭, 센터마크 원점). 2D 라인 좌표는 (X, Z).
    /// </summary>
    public sealed class StadiumLayout
    {
        public const int SupportedSchema = 1;

        public struct Segment { public string Name; public Vector2 A, B; }
        public struct Arc { public string Name; public Vector2 Center; public float Radius, FromDeg, ToDeg; }
        public struct Spot { public string Name; public Vector2 Center; public float Radius; }

        public sealed class Goal
        {
            public int Side;
            public Vector3 PostA, PostB;     // 포스트 아래 중심 (S, N 순)
            public float PostRadius;
            public float CrossbarCenterY;
            public Bounds Net;
        }

        public sealed class SeatRow
        {
            public string Section;
            public int Tier, Row, Count;
            public bool Home;
            public Vector3 Start, Dir;
            public float Spacing;
        }

        public int SchemaVersion { get; private set; }
        public float Length { get; private set; }
        public float Width { get; private set; }
        public float LineWidth { get; private set; }
        public float Runoff { get; private set; }
        public float StripeWidth { get; private set; }
        public string StripePattern { get; private set; }
        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<Arc> Arcs = new List<Arc>();
        public readonly List<Spot> Spots = new List<Spot>();
        public readonly List<Goal> Goals = new List<Goal>();
        public readonly List<SeatRow> SeatRows = new List<SeatRow>();
        public int SeatCount { get; private set; }
        public Bounds MainMount { get; private set; }
        public Vector3 MainMountCenter { get; private set; }
        public readonly List<Bounds> NoGo = new List<Bounds>();
        public string LightMode { get; private set; }
        public readonly List<Vector3> LightEmitters = new List<Vector3>();
        public int TriangleCount { get; private set; }

        public static StadiumLayout Parse(string json)
        {
            if (!(JsonReader.Parse(json) is JsonObject root)) throw new JsonException("layout 최상위는 객체", 1, 1);
            var l = new StadiumLayout { SchemaVersion = (int)Num(root, "schemaVersion") };
            if (l.SchemaVersion != SupportedSchema) throw new JsonException($"layout schemaVersion {l.SchemaVersion} (지원 {SupportedSchema})", 1, 1);

            var pitch = Obj(root, "pitch");
            l.Length = Num(pitch, "length");
            l.Width = Num(pitch, "width");
            l.LineWidth = Num(pitch, "lineWidth");
            l.Runoff = Num(pitch, "runoff");
            var stripes = Obj(pitch, "stripes");
            l.StripeWidth = Num(stripes, "width");
            l.StripePattern = Str(stripes, "pattern");
            foreach (var o in Arr(pitch, "lines"))
            {
                var d = (JsonObject)o;
                if (Str(d, "kind") == "segment")
                    l.Segments.Add(new Segment { Name = Str(d, "name"), A = V2(d, "a"), B = V2(d, "b") });
                else
                    l.Arcs.Add(new Arc { Name = Str(d, "name"), Center = V2(d, "c"), Radius = Num(d, "r"), FromDeg = Num(d, "from"), ToDeg = Num(d, "to") });
            }
            foreach (var o in Arr(pitch, "spots"))
            {
                var d = (JsonObject)o;
                l.Spots.Add(new Spot { Name = Str(d, "name"), Center = V2(d, "c"), Radius = Num(d, "r") });
            }

            foreach (var o in Arr(root, "goals"))
            {
                var d = (JsonObject)o;
                var posts = Arr(d, "posts");
                var bar = Obj(d, "crossbar");
                l.Goals.Add(new Goal
                {
                    Side = (int)Num(d, "side"),
                    PostA = V3(Obj(posts[0]), "a"),
                    PostB = V3(Obj(posts[1]), "a"),
                    PostRadius = Num(Obj(posts[0]), "r"),
                    CrossbarCenterY = V3(bar, "a").y,
                    Net = Box(Obj(d, "net")),
                });
            }

            var seats = Obj(root, "seats");
            l.SeatCount = (int)Num(seats, "count");
            foreach (var o in Arr(seats, "rows"))
            {
                var d = (JsonObject)o;
                l.SeatRows.Add(new SeatRow
                {
                    Section = Str(d, "section"), Tier = (int)Num(d, "tier"), Row = (int)Num(d, "row"), Count = (int)Num(d, "count"),
                    Home = d.TryGet("home", out var h) && h is bool hb && hb,
                    Start = V3(d, "start"), Dir = V3(d, "dir"), Spacing = Num(d, "spacing"),
                });
            }

            var cams = Obj(root, "cameras");
            l.MainMount = Box(Obj(cams, "mainMount"));
            l.MainMountCenter = V3(cams, "mainMountCenter");
            foreach (var o in Arr(cams, "noGo")) l.NoGo.Add(Box((JsonObject)o));

            var lights = Obj(root, "lights");
            l.LightMode = Str(lights, "mode");
            foreach (var o in Arr(lights, "emitters")) l.LightEmitters.Add(ToV3((List<object>)o));

            var tri = Obj(Obj(root, "summary"), "triangles");
            l.TriangleCount = (int)Num(tri, "total");
            return l;
        }

        /// <summary>좌석 줄 → 좌석 위치 (045 관중 배치 입력).</summary>
        public void ExpandSeats(List<Vector3> into)
        {
            foreach (var r in SeatRows)
                for (int k = 0; k < r.Count; k++)
                    into.Add(r.Start + r.Dir * (k * r.Spacing));
        }

        // ---- 도우미 ----

        static JsonObject Obj(object o) => o as JsonObject ?? throw new JsonException("객체가 필요", 1, 1);

        static JsonObject Obj(JsonObject d, string k) =>
            d.TryGet(k, out var v) && v is JsonObject o ? o : throw new JsonException($"'{k}' 객체 필요", 1, 1);

        static List<object> Arr(JsonObject d, string k) =>
            d.TryGet(k, out var v) && v is List<object> a ? a : throw new JsonException($"'{k}' 배열 필요", 1, 1);

        static string Str(JsonObject d, string k) =>
            d.TryGet(k, out var v) && v is string s ? s : throw new JsonException($"'{k}' 문자열 필요", 1, 1);

        static float Num(JsonObject d, string k) =>
            d.TryGet(k, out var v) && v is double x ? (float)x : throw new JsonException($"'{k}' 숫자 필요", 1, 1);

        static Vector2 V2(JsonObject d, string k)
        {
            var a = Arr(d, k);
            if (a.Count != 2) throw new JsonException($"'{k}'는 [x, z]", 1, 1);
            return new Vector2(F(a[0]), F(a[1]));
        }

        static Vector3 V3(JsonObject d, string k) => ToV3(Arr(d, k));

        static Vector3 ToV3(List<object> a)
        {
            if (a == null || a.Count != 3) throw new JsonException("[x, y, z] 필요", 1, 1);
            return new Vector3(F(a[0]), F(a[1]), F(a[2]));
        }

        static Bounds Box(JsonObject d)
        {
            var b = new Bounds();
            b.SetMinMax(V3(d, "min"), V3(d, "max"));
            return b;
        }

        static float F(object o) => o is double x ? (float)x : throw new JsonException("숫자 필요: " + Convert.ToString(o, CultureInfo.InvariantCulture), 1, 1);
    }
}
