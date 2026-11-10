using System;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    static class BroadcastTestKit
    {
        public const float Aspect = 16f / 9f;

        /// <summary>선수 22명을 포메이션처럼 깔고 공 경로를 함수로 받는 장면.</summary>
        public static HighlightData Scene(float start, int frames, Func<int, Vec3> ball, int owner = 9)
        {
            var d = new HighlightData(start, frames);
            for (int f = 0; f < frames; f++)
            {
                var b = ball(f);
                d.Ball[f] = b;
                d.BallOwner[f] = (short)owner;
                for (int e = 0; e < HighlightData.Players; e++)
                {
                    float side = e < 11 ? -1f : 1f;
                    float x = side * (5f + e % 11 * 4f);
                    float z = (e % 5 - 2) * 10f;
                    d.Set(f, e, x, z, 0f);
                }
                if (owner >= 0) d.Set(f, owner, b.X, b.Z, 0f);
            }
            return d;
        }

        public static Vec3 Mirror(Vec3 v) => new Vec3(-v.X, v.Y, v.Z);

        public static ulong Hash(CameraTrack t)
        {
            ulong h = 14695981039346656037ul;
            void Add(float v)
            {
                h ^= BitConverter.ToUInt32(BitConverter.GetBytes(v), 0);
                h *= 1099511628211ul;
            }
            foreach (var p in t.Poses)
            {
                Add(p.Position.X); Add(p.Position.Y); Add(p.Position.Z);
                Add(p.Forward.X); Add(p.Forward.Y); Add(p.Forward.Z);
                Add(p.TanV); Add(p.TanH);
                Add(p.Shake.X); Add(p.Shake.Y); Add(p.Shake.Z);
            }
            return h;
        }
    }
}
