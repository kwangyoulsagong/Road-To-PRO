using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Presentation.Players.Logic
{
    /// <summary>공의 표시 위치를 재생 쪽에서 보정한다 (specs/041 FR-04).</summary>
    public interface IBallVisualFilter
    {
        Vec3 Filter(Vec3 visual, float time);
    }

    /// <summary>애니메이션이 반응하는 시뮬 이벤트 한 건 (SimEvent에서 041/어댑터가 만든다). 좌표는 Unity 월드(X 길이, Y 위, Z 폭).</summary>
    public readonly struct AnimEvent
    {
        public readonly float Time;
        public readonly long Seq;
        public readonly SimEventKind Kind;
        /// <summary>주체 엔티티(선수 슬롯 0~21, 심판 22~24). 없으면 −1.</summary>
        public readonly int Entity, Other;
        public readonly BodyPart Part;
        public readonly ActionOutcome Outcome;
        public readonly Vec3 BallPos, BallVelOut;

        public AnimEvent(float time, long seq, SimEventKind kind, int entity, int other, BodyPart part, ActionOutcome outcome,
            Vec3 ballPos, Vec3 ballVelOut)
        {
            Time = time;
            Seq = seq;
            Kind = kind;
            Entity = entity;
            Other = other;
            Part = part;
            Outcome = outcome;
            BallPos = ballPos;
            BallVelOut = ballVelOut;
        }
    }

    /// <summary>
    /// 하이라이트 구간의 시뮬 기록 (읽기 전용 입력). 30 Hz 프레임 × 엔티티(선수 22 + 심판 3)의 루트 위치·정면 각, 공 위치, 이벤트.
    /// 006 ReplayFrame/SimEvent → 이 구조로 옮기는 일은 재생기(041)가 한다. 애니메이션은 이 값을 바꾸지 않는다(비기능: 시뮬 불변).
    /// </summary>
    public sealed class HighlightData
    {
        public const float FrameRate = 30f;
        public const int Players = 22, Officials = 3, Entities = Players + Officials;

        public readonly float StartTime;
        public readonly int Frames;
        /// <summary>[frame * Entities + e]</summary>
        public readonly float[] X, Z, Yaw;
        public readonly bool[] Present;
        public readonly Vec3[] Ball;
        /// <summary>프레임별 공 소유 엔티티, 없으면 −1 (054 FR-37 드리블 런). ReplayFrame.BallOwnerSlot에서 041이 옮긴다.</summary>
        public readonly short[] BallOwner;
        public readonly List<AnimEvent> Events = new List<AnimEvent>();

        public HighlightData(float startTime, int frames)
        {
            if (frames < 2) throw new ArgumentException("프레임 2개 이상");
            StartTime = startTime;
            Frames = frames;
            X = new float[frames * Entities];
            Z = new float[frames * Entities];
            Yaw = new float[frames * Entities];
            Present = new bool[frames * Entities];
            Ball = new Vec3[frames];
            BallOwner = new short[frames];
            for (int f = 0; f < frames; f++) BallOwner[f] = -1;
        }

        public float EndTime => StartTime + (Frames - 1) / FrameRate;

        public void Set(int frame, int e, float x, float z, float yaw, bool present = true)
        {
            int i = frame * Entities + e;
            X[i] = x;
            Z[i] = z;
            Yaw[i] = yaw;
            Present[i] = present;
        }

        /// <summary>t의 프레임 위치(정수 프레임 + 비율), 범위로 자른다.</summary>
        public void FrameAt(float t, out int f, out float a)
        {
            float u = (t - StartTime) * FrameRate;
            if (u <= 0) { f = 0; a = 0; return; }
            if (u >= Frames - 1) { f = Frames - 2; a = 1; return; }
            f = (int)Math.Floor(u);
            a = u - f;
        }

        /// <summary>보간 상대 프레임이 기록 없음(Present = false)이면 보간하지 않고 있는 쪽 값을 쓴다(교체 투입·퇴장).</summary>
        void Pair(int e, float t, out int i0, out int i1, out float a)
        {
            FrameAt(t, out var f, out a);
            i0 = f * Entities + e;
            i1 = i0 + Entities;
            if (!Present[i1]) { i1 = i0; a = 0; }
            else if (!Present[i0]) { i0 = i1; a = 0; }
        }

        public Vec3 Pos(int e, float t)
        {
            Pair(e, t, out var i0, out var i1, out var a);
            return new Vec3(X[i0] + (X[i1] - X[i0]) * a, 0, Z[i0] + (Z[i1] - Z[i0]) * a);
        }

        public float YawAt(int e, float t)
        {
            Pair(e, t, out var i0, out var i1, out var a);
            float d = (float)Math.IEEERemainder(Yaw[i1] - Yaw[i0], 2 * Math.PI);
            return Yaw[i0] + d * a;
        }

        public Vec3 BallAt(float t)
        {
            FrameAt(t, out var f, out var a);
            return Ball[f] + (Ball[f + 1] - Ball[f]) * a;
        }

        /// <summary>시뮬 기록 해시 (애니메이션 전후 불변 검사).</summary>
        public ulong Hash()
        {
            var h = Core.Hashing.StableHasher.Create();
            h.Add(Frames);
            for (int i = 0; i < X.Length; i++) { h.Add(X[i]); h.Add(Z[i]); h.Add(Yaw[i]); h.Add(Present[i]); }
            for (int i = 0; i < Ball.Length; i++) { h.Add(Ball[i].X); h.Add(Ball[i].Y); h.Add(Ball[i].Z); h.Add((int)BallOwner[i]); }
            foreach (var e in Events)
            {
                h.Add(e.Time); h.Add(e.Seq); h.Add((int)e.Kind); h.Add(e.Entity); h.Add(e.Other); h.Add((int)e.Part); h.Add((int)e.Outcome);
                h.Add(e.BallPos.X); h.Add(e.BallPos.Y); h.Add(e.BallPos.Z); h.Add(e.BallVelOut.X); h.Add(e.BallVelOut.Y); h.Add(e.BallVelOut.Z);
            }
            return h.Value;
        }
    }
}
