using System;
using System.Collections.Generic;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>쿼터니언 (x, y, z, w). Logic은 Unity 비참조라 따로 둔다 — 선수 좌표 = Unity 좌표.</summary>
    public readonly struct Quat4
    {
        public readonly float X, Y, Z, W;
        public Quat4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public static readonly Quat4 Identity = new Quat4(0f, 0f, 0f, 1f);
        public float Dot(in Quat4 o) => X * o.X + Y * o.Y + Z * o.Z + W * o.W;
    }

    /// <summary>
    /// 062 FR-11 손 포즈 표 (player.clips.json "handPoses", Blender `player/hands.py`). 손가락 뼈 30개 순서와
    /// 포즈별 레스트 축 회전 델타(선수 좌표). 런타임은 뼈 local = restLocal · (restModel⁻¹ · 델타 · restModel).
    /// </summary>
    public sealed class HandPoseTable
    {
        public const int Bones = 30;
        public const string DefaultPose = "open";

        readonly List<string> _names = new List<string>();
        readonly List<Quat4[]> _rot = new List<Quat4[]>();
        public readonly string[] BoneNames = new string[Bones];

        public int Count => _names.Count;
        public string Name(int i) => _names[i];
        public Quat4 Rotation(int pose, int bone) => _rot[pose][bone];

        /// <summary>이름 → 인덱스. 없으면(null 포함) 기본 포즈(open), 표에 open도 없으면 0.</summary>
        public int IndexOf(string name)
        {
            int i = name == null ? -1 : _names.IndexOf(name);
            if (i >= 0) return i;
            i = _names.IndexOf(DefaultPose);
            return i >= 0 ? i : 0;
        }

        public static HandPoseTable Parse(JsonObject o)
        {
            var t = new HandPoseTable();
            o.TryGet("bones", out var b);
            var bones = (List<object>)b;
            if (bones.Count != Bones) throw new FormatException($"handPoses.bones {bones.Count}개 (30개여야 함)");
            for (int i = 0; i < Bones; i++) t.BoneNames[i] = (string)bones[i];
            o.TryGet("poses", out var p);
            foreach (var kv in ((JsonObject)p).Members)
            {
                var list = (List<object>)kv.Value;
                if (list.Count != Bones) throw new FormatException($"handPoses.poses.{kv.Key} {list.Count}개");
                var q = new Quat4[Bones];
                for (int i = 0; i < Bones; i++)
                {
                    var v = (List<object>)list[i];
                    q[i] = new Quat4((float)(double)v[0], (float)(double)v[1], (float)(double)v[2], (float)(double)v[3]);
                }
                t._names.Add(kv.Key);
                t._rot.Add(q);
            }
            return t;
        }
    }

    /// <summary>
    /// 062 FR-11 손 포즈 섞기: 포즈 지시(PoseSample)의 클립 가중치로 손가락 회전을 섞는다 (부호 맞춘 정규화 가중 합).
    /// 클립 가중치가 이미 재생 시각의 함수(크로스페이드)라 상태 없이 fps·배속과 무관하게 같은 시각 → 같은 손 (AC-14).
    /// </summary>
    public static class HandPose
    {
        /// <summary>out30 = 손가락 뼈 30개 회전 델타. 표가 없거나 가중치가 0이면 항등.</summary>
        public static void Blend(in PoseSample s, ClipLibrary lib, Span<Quat4> out30)
        {
            Span<int> c = stackalloc int[PoseSample.Capacity];
            Span<float> w = stackalloc float[PoseSample.Capacity];
            for (int i = 0; i < s.Count; i++) { c[i] = s.Clip(i); w[i] = s.Weight(i); }
            Blend(c.Slice(0, s.Count), w.Slice(0, s.Count), lib, out30);
        }

        /// <summary>클립 인덱스·가중치 목록으로 섞는다 (PoseSample 없이 — 테스트·검증 장면).</summary>
        public static void Blend(ReadOnlySpan<int> clips, ReadOnlySpan<float> weights, ClipLibrary lib, Span<Quat4> out30)
        {
            if (out30.Length < HandPoseTable.Bones) throw new ArgumentException("out30이 작다");
            var t = lib?.HandPoses;
            for (int b = 0; b < HandPoseTable.Bones; b++)
            {
                if (t == null) { out30[b] = Quat4.Identity; continue; }
                float x = 0f, y = 0f, z = 0f, w = 0f, tot = 0f;
                for (int i = 0; i < clips.Length; i++)
                {
                    float wt = weights[i];
                    if (wt <= 0f) continue;
                    int c = clips[i];
                    int pi = c >= 0 && c < lib.Count ? lib.Get(c).HandPoseIndex : -1;
                    if (pi < 0) pi = t.IndexOf(null);
                    var q = t.Rotation(pi, b);
                    if (w * q.W + x * q.X + y * q.Y + z * q.Z < 0f) wt = -wt; // 같은 반구
                    x += q.X * wt; y += q.Y * wt; z += q.Z * wt; w += q.W * wt;
                    tot += Math.Abs(wt);
                }
                float n = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
                out30[b] = tot <= 0f || n < 1e-9f ? Quat4.Identity : new Quat4(x / n, y / n, z / n, w / n);
            }
        }
    }
}
