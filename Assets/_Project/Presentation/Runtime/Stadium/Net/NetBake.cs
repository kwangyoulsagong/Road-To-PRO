using System;
using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Stadium.Net
{
    /// <summary>
    /// 골대 하나·장면 하나의 구운 그물 움직임 (specs/059 FR-27~29). 장면을 만들 때(로딩) <see cref="NetCloth"/>를 첫 접촉 − 0.1 s부터
    /// 최대 bakeSeconds 240 Hz로 돌려 30 Hz 프레임마다 노드 위치를 정지 모양 대비 mm 오프셋(int16)으로 저장한다.
    /// 재생은 보간만 한다 — 렌더 fps·재생 속도·라이브/리플레이·중간 시작과 무관하게 같은 시각 같은 모양.
    /// 굽기 전 시각은 정지 모양, 끝 뒤 시각은 마지막 프레임(공이 그물에 기대 있으면 그 모양 유지).
    /// </summary>
    public sealed class NetBake
    {
        public const float FrameRate = 30f;
        const int StepsPerFrame = (int)(NetCloth.StepHz / FrameRate);   // 8
        const float LeadSeconds = 0.1f;

        public readonly int Side;
        public readonly double StartTime;
        public readonly int Frames;
        public readonly int Nodes;
        readonly short[] _mm;   // [frame * Nodes * 3 + node * 3 + k]

        public double EndTime => StartTime + (Frames - 1) / FrameRate;

        NetBake(int side, double start, int frames, int nodes)
        {
            Side = side;
            StartTime = start;
            Frames = frames;
            Nodes = nodes;
            _mm = new short[frames * nodes * 3];
        }

        /// <summary>
        /// 이 골대(side) 접촉이 있으면 굽고, 없으면 null. 공 궤적 = 장면 클립 공(30 Hz)을 화면과 같은 보간·보호로 240 Hz에서 읽는다.
        /// </summary>
        public static NetBake Build(NetCloth cloth, HighlightData data, List<NetContact> contacts, int side, float halfLength, in GoalBox box)
        {
            if (cloth == null || data == null || contacts == null) return null;
            double first = double.NaN;
            for (int i = 0; i < contacts.Count; i++)
                if (contacts[i].Side == side) { first = contacts[i].Time; break; }
            if (double.IsNaN(first)) return null;

            double start = Math.Max(data.StartTime, first - LeadSeconds);
            double end = Math.Min(data.EndTime, start + cloth.Tuning.BakeSeconds);
            int frames = Math.Max(2, (int)Math.Floor((end - start) * FrameRate) + 1);
            var bake = new NetBake(side, start, frames, cloth.NodeCount);
            cloth.Reset();
            var rest = cloth.Rest;
            double dt = 1.0 / NetCloth.StepHz;
            for (int f = 0; f < frames; f++)
            {
                if (f > 0)
                    for (int k = 0; k < StepsPerFrame; k++)
                    {
                        double t = start + ((f - 1) * StepsPerFrame + k + 1) * dt;
                        bool has = t >= data.StartTime && t <= data.EndTime;
                        var ball = has ? GoalLocal.ToLocal(BallAt(data, (float)t, box), side, halfLength) : default;
                        cloth.Step(has, ball);
                    }
                var x = cloth.Positions;
                int o = f * bake.Nodes * 3;
                for (int i = 0; i < bake.Nodes; i++)
                {
                    bake._mm[o + i * 3] = Mm(x[i].X - rest[i].X);
                    bake._mm[o + i * 3 + 1] = Mm(x[i].Y - rest[i].Y);
                    bake._mm[o + i * 3 + 2] = Mm(x[i].Z - rest[i].Z);
                }
            }
            return bake;
        }

        static short Mm(float m)
        {
            float v = (float)Math.Round(m * 1000f);
            return v > short.MaxValue ? short.MaxValue : v < short.MinValue ? short.MinValue : (short)v;
        }

        /// <summary>화면 공과 같은 값: 프레임 보간 + 041 FR-04 보간 보호.</summary>
        static Vec3 BallAt(HighlightData d, float t, in GoalBox box)
        {
            d.FrameAt(t, out int f, out float a);
            var p = d.Ball[f] + (d.Ball[f + 1] - d.Ball[f]) * a;
            return BallVisualGuard.Apply(p, d.Ball[f], d.Ball[f + 1], a, box);
        }

        /// <summary>시각 t의 노드 위치(골대 로컬)를 쓴다. 굽기 전 = 정지 모양, 끝 뒤 = 마지막 프레임.</summary>
        public void Sample(double t, Vec3[] rest, Vec3[] into)
        {
            if (t <= StartTime)
            {
                Array.Copy(rest, into, Nodes);
                return;
            }
            double u = (t - StartTime) * FrameRate;
            int f = (int)Math.Floor(u);
            float a = (float)(u - f);
            if (f >= Frames - 1) { f = Frames - 2; a = 1f; }
            int o0 = f * Nodes * 3, o1 = o0 + Nodes * 3;
            for (int i = 0; i < Nodes; i++)
            {
                int k0 = o0 + i * 3, k1 = o1 + i * 3;
                float dx = _mm[k0] + (_mm[k1] - _mm[k0]) * a;
                float dy = _mm[k0 + 1] + (_mm[k1 + 1] - _mm[k0 + 1]) * a;
                float dz = _mm[k0 + 2] + (_mm[k1 + 2] - _mm[k0 + 2]) * a;
                into[i] = new Vec3(rest[i].X + dx * 0.001f, rest[i].Y + dy * 0.001f, rest[i].Z + dz * 0.001f);
            }
        }

        /// <summary>구운 데이터 해시 (FR-28 — 두 번 구우면 같다).</summary>
        public ulong Hash()
        {
            var h = StableHasher.Create();
            h.Add(Side);
            h.Add(StartTime);
            h.Add(Frames);
            for (int i = 0; i < _mm.Length; i++) h.Add((int)_mm[i]);
            return h.Value;
        }

        /// <summary>프레임 f 노드 i의 정지 모양 대비 변위(m).</summary>
        public Vec3 Offset(int f, int i)
        {
            int k = f * Nodes * 3 + i * 3;
            return new Vec3(_mm[k] * 0.001f, _mm[k + 1] * 0.001f, _mm[k + 2] * 0.001f);
        }
    }

    /// <summary>하이라이트 좌표 ↔ 골대 로컬 (거울상 자동, FR-57).</summary>
    public static class GoalLocal
    {
        public static Vec3 ToLocal(Vec3 world, int side, float halfLength) => new Vec3(side * world.X - halfLength, world.Y, world.Z);
        public static Vec3 ToWorld(Vec3 local, int side, float halfLength) => new Vec3(side * (local.X + halfLength), local.Y, local.Z);
    }
}
