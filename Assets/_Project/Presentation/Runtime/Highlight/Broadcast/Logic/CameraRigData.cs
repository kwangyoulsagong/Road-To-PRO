using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Stadium;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>축 정렬 상자.</summary>
    public readonly struct CameraBox
    {
        public readonly Vec3 Min, Max;

        public CameraBox(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        public bool Contains(Vec3 p) =>
            p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;
    }

    /// <summary>수직 원기둥 (골대 기둥).</summary>
    public readonly struct CameraPost
    {
        public readonly float X, Z, Radius, Height;

        public CameraPost(float x, float z, float radius, float height)
        {
            X = x;
            Z = z;
            Radius = radius;
            Height = height;
        }
    }

    /// <summary>카메라 배치 입력 (specs/042 FR-06, 044 FR-26). 메인 스탠드는 −Z 쪽이다.</summary>
    public sealed class CameraRigData
    {
        public float HalfLength = 52.5f, HalfWidth = 34f;
        public CameraBox MainMount = new CameraBox(new Vec3(-6f, 24.9f, -81f), new Vec3(6f, 26.9f, -79f));
        public readonly List<CameraBox> NoGo = new List<CameraBox>();
        public readonly List<CameraPost> Posts = new List<CameraPost>();
        /// <summary>크로스바·그물처럼 시야를 막는 골대 구조물.</summary>
        public readonly List<CameraBox> GoalBlockers = new List<CameraBox>();

        /// <summary>경기장 데이터가 없을 때 쓰는 기본 경기장. 골대만 채운다.</summary>
        public static CameraRigData Default()
        {
            var rig = new CameraRigData();
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 52.5f;
                rig.Posts.Add(new CameraPost(x, -3.66f, 0.06f, 2.44f));
                rig.Posts.Add(new CameraPost(x, 3.66f, 0.06f, 2.44f));
                rig.GoalBlockers.Add(new CameraBox(new Vec3(x - 0.06f, 2.38f, -3.66f), new Vec3(x + 0.06f, 2.5f, 3.66f)));
                float back = s * 2f;
                rig.GoalBlockers.Add(new CameraBox(new Vec3(MathF.Min(x, x + back), 0f, -3.66f), new Vec3(MathF.Max(x, x + back), 2.44f, 3.66f)));
            }
            return rig;
        }

        /// <summary>044 layout에서 만든다. 없으면 기본 경기장 + 진단 1건 (FR-06, AC-16).</summary>
        public static CameraRigData From(StadiumLayout layout, Action<string> diagnostic)
        {
            if (layout == null)
            {
                diagnostic?.Invoke("경기장 데이터 없음: 기본 경기장 카메라 배치를 쓴다");
                return Default();
            }

            var rig = new CameraRigData { HalfLength = layout.Length * 0.5f, HalfWidth = layout.Width * 0.5f };
            rig.MainMount = new CameraBox(ToVec(layout.MainMount.min), ToVec(layout.MainMount.max));
            foreach (var b in layout.NoGo) rig.NoGo.Add(new CameraBox(ToVec(b.min), ToVec(b.max)));
            foreach (var g in layout.Goals)
            {
                rig.Posts.Add(new CameraPost(g.PostA.x, g.PostA.z, g.PostRadius, g.CrossbarCenterY));
                rig.Posts.Add(new CameraPost(g.PostB.x, g.PostB.z, g.PostRadius, g.CrossbarCenterY));
                float minZ = MathF.Min(g.PostA.z, g.PostB.z), maxZ = MathF.Max(g.PostA.z, g.PostB.z);
                rig.GoalBlockers.Add(new CameraBox(
                    new Vec3(g.PostA.x - g.PostRadius, g.CrossbarCenterY - g.PostRadius, minZ),
                    new Vec3(g.PostA.x + g.PostRadius, g.CrossbarCenterY + g.PostRadius, maxZ)));
                rig.GoalBlockers.Add(new CameraBox(ToVec(g.Net.min), ToVec(g.Net.max)));
            }
            return rig;
        }

        /// <summary>거치 범위 안에서 FR-01 범위에 가장 가까운 점 (거치 범위 우선, FR-06).</summary>
        public Vec3 MainPosition(CameraTuning t)
        {
            float x = Nearest(MainMount.Min.X, MainMount.Max.X, 0f, 0f);
            float y = Nearest(MainMount.Min.Y, MainMount.Max.Y, t.MainHeightMin, t.MainHeightMax);
            float z = Nearest(MainMount.Min.Z, MainMount.Max.Z, -HalfWidth - t.MainBehindMax, -HalfWidth - t.MainBehindMin);
            return new Vec3(x, y, z);
        }

        /// <summary>두 구간이 겹치면 겹침의 중앙, 아니면 구간 A에서 B에 가장 가까운 끝.</summary>
        static float Nearest(float aMin, float aMax, float bMin, float bMax)
        {
            float lo = MathF.Max(aMin, bMin), hi = MathF.Min(aMax, bMax);
            if (lo <= hi) return (lo + hi) * 0.5f;
            return aMax < bMin ? aMax : aMin;
        }

        static Vec3 ToVec(UnityEngine.Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
