using System;
using System.Collections.Generic;
using System.Globalization;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Presentation.Players.Logic
{
    public enum Gait : byte
    {
        Idle = 0,
        Walk = 1,
        Jog = 2,
        Run = 3,
        Sprint = 4,
    }

    /// <summary>
    /// 접촉점 규칙 (specs/043 FR-32, Art/Blender/scripts/player/clips/base.py와 같은 정의).
    /// 점 = lerp(본 머리, toward 머리, T) + 위·Up + 정면·Fwd + 오른쪽·Side. 정면은 BasisFoot이면 bone→toward 수평 방향, 아니면 골반 정면.
    /// 계산 결과는 접촉 순간 공 중심이 있어야 할 위치다.
    /// </summary>
    public readonly struct ContactRule
    {
        public readonly string Bone, Toward;
        public readonly float T, Up, Fwd, Side;
        public readonly bool BasisFoot;

        public ContactRule(string bone, string toward, float t, float up, float fwd, float side, bool basisFoot)
        {
            Bone = bone;
            Toward = toward;
            T = t;
            Up = up;
            Fwd = fwd;
            Side = side;
            BasisFoot = basisFoot;
        }

        /// <summary>본 머리 위치로 접촉점 계산. hipL·hipR = LeftUpperLeg·RightUpperLeg 머리(골반 정면 계산용).</summary>
        public Vec3 Evaluate(Vec3 bone, Vec3 toward, Vec3 hipL, Vec3 hipR)
        {
            var p = bone + (toward - bone) * T;
            Vec3 f;
            var d = toward - bone;
            var dh = new Vec3(d.X, 0, d.Z);
            if (BasisFoot && Toward != null && dh.LengthSq() > 1e-12f) f = dh.Normalized();
            else
            {
                var r = hipR - hipL;
                f = new Vec3(-r.Z, 0, r.X).Normalized(); // cross(r, up)
            }
            var side = new Vec3(f.Z, 0, -f.X);            // cross(up, f) = 오른쪽
            return p + new Vec3(0, Up, 0) + f * Fwd + side * Side;
        }
    }

    /// <summary>클립 하나의 태그 (player.clips.json).</summary>
    public sealed class ClipInfo
    {
        public const float Fps = 30f;

        public string Id, Group, Gait, Outcome, Kind;
        public int Index, Frames;
        public bool Loop;
        public float Speed = float.NaN;
        public int Dir = -1, HitDir = int.MinValue;
        public float RootYaw;
        public Vec2 Root;
        public float StanceL0, StanceL1, StanceR0, StanceR1;
        public bool HasContact;
        public int ContactFrame;
        public BodyPart ContactPart;
        public Vec3 ContactPoint;
        public ContactRule Rule;
        public float ApproachMin = -180, ApproachMax = 180, BallHeightMin, BallHeightMax = 3, BallSpeedMin, BallSpeedMax = 40;

        public float Length => Frames / Fps;
        public float ContactTime => ContactFrame / Fps;
    }

    /// <summary>클립 태그 모음 (specs/043 FR-32). 인덱스 = 파일 순서 = 생성기 고정 순서.</summary>
    public sealed class ClipLibrary
    {
        readonly List<ClipInfo> _clips = new List<ClipInfo>();
        readonly Dictionary<string, int> _byId = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly int[,] _loco = new int[5, 8];

        public int Count => _clips.Count;
        public int IdleIndex { get; private set; } = -1;
        public ClipInfo Get(int i) => _clips[i];
        public int IndexOf(string id) => id != null && _byId.TryGetValue(id, out var i) ? i : -1;

        public bool TryGet(string id, out ClipInfo c)
        {
            int i = IndexOf(id);
            c = i >= 0 ? _clips[i] : null;
            return i >= 0;
        }

        /// <summary>보법·방향(0~7, 45° 간격, 0 정면·2 오른쪽)의 로코모션 클립. 없으면 −1.</summary>
        public int Locomotion(Gait g, int dir) => _loco[(int)g, ((dir % 8) + 8) % 8];

        static float F(object o) => (float)(double)o;
        static List<object> L(JsonObject o, string k) => o.TryGet(k, out var v) ? v as List<object> : null;

        static string S(JsonObject o, string k) => o.TryGet(k, out var v) ? v as string : null;

        public static ClipLibrary Parse(string json)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            if (!root.TryGet("schemaVersion", out var sv) || (double)sv != 1) throw new FormatException("clips.json schemaVersion 1 아님");
            root.TryGet("clips", out var arr);
            var lib = new ClipLibrary();
            for (int g = 0; g < 5; g++)
                for (int d = 0; d < 8; d++)
                    lib._loco[g, d] = -1;
            foreach (var item in (List<object>)arr)
            {
                var o = (JsonObject)item;
                var c = new ClipInfo
                {
                    Id = S(o, "id"), Group = S(o, "group"), Gait = S(o, "gait"), Outcome = S(o, "outcome"), Kind = S(o, "kind"),
                    Index = lib._clips.Count,
                };
                o.TryGet("frames", out var fr);
                c.Frames = (int)(double)fr;
                c.Loop = o.TryGet("loop", out var lp) && lp is bool b && b;
                if (o.TryGet("speed", out var sp)) c.Speed = F(sp);
                if (o.TryGet("dir", out var dr)) c.Dir = (int)(double)dr;
                if (o.TryGet("hitDir", out var hd)) c.HitDir = (int)(double)hd;
                if (o.TryGet("rootYaw", out var ry)) c.RootYaw = F(ry);
                var rt = L(o, "root");
                if (rt != null) c.Root = new Vec2(F(rt[0]), F(rt[1]));
                var ap = L(o, "approach");
                if (ap != null) { c.ApproachMin = F(ap[0]); c.ApproachMax = F(ap[1]); }
                var bh = L(o, "ballHeight");
                if (bh != null) { c.BallHeightMin = F(bh[0]); c.BallHeightMax = F(bh[1]); }
                var bs = L(o, "ballSpeed");
                if (bs != null) { c.BallSpeedMin = F(bs[0]); c.BallSpeedMax = F(bs[1]); }
                if (o.TryGet("stance", out var st))
                {
                    var so = (JsonObject)st;
                    var l = L(so, "Left");
                    var r = L(so, "Right");
                    c.StanceL0 = F(l[0]); c.StanceL1 = F(l[1]); c.StanceR0 = F(r[0]); c.StanceR1 = F(r[1]);
                }
                if (o.TryGet("contact", out var ct) && ct is JsonObject co)
                {
                    c.HasContact = true;
                    co.TryGet("frame", out var cf);
                    c.ContactFrame = (int)(double)cf;
                    c.ContactPart = (BodyPart)Enum.Parse(typeof(BodyPart), S(co, "part"));
                    var pt = L(co, "point");
                    c.ContactPoint = new Vec3(F(pt[0]), F(pt[1]), F(pt[2]));
                    co.TryGet("rule", out var ru);
                    var rr = (JsonObject)ru;
                    float G(string k) => rr.TryGet(k, out var v) ? F(v) : 0f;
                    c.Rule = new ContactRule(S(rr, "bone"), S(rr, "toward"), G("t"), G("up"), G("fwd"), G("side"), S(rr, "basis") == "foot");
                }
                lib._byId[c.Id] = c.Index;
                lib._clips.Add(c);
                if (c.Id == "idle") lib.IdleIndex = c.Index;
                if (c.Group == "locomotion" && c.Loop && c.Gait != null && c.Dir >= 0 && c.Dir % 45 == 0)
                {
                    int g = c.Gait switch { "walk" => 1, "jog" => 2, "run" => 3, "sprint" => 4, _ => -1 };
                    if (g > 0) lib._loco[g, c.Dir / 45] = c.Index;
                }
            }
            return lib;
        }
    }
}
