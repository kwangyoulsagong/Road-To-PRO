using System;
using RoadToPro.Core.Math;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>장면의 연출 역할 (specs/042 FR-20).</summary>
    public enum SceneRole
    {
        Live = 0,
        GoalMain = 1,
        ReplayAngle = 2,
        GoalTail = 3,
    }

    /// <summary>공이 멈춘 구간. 라이브 컷은 이 안에서만 된다 (FR-10·11).</summary>
    public readonly struct DeadBallWindow
    {
        public readonly double Start, End;

        public DeadBallWindow(double start, double end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>라이브 컷 요청 (FR-11·56). Priority가 큰 쪽이 같은 시각 경합에서 이긴다.</summary>
    public readonly struct LiveCutRequest
    {
        public readonly double Time;
        public readonly ShotKind Shot;
        public readonly int Priority, Focus;
        public readonly bool FarSide;

        public LiveCutRequest(double time, ShotKind shot, int priority, int focus = -1, bool farSide = false)
        {
            Time = time;
            Shot = shot;
            Priority = priority;
            Focus = focus;
            FarSide = farSide;
        }
    }

    /// <summary>흔들림 원천 (FR-40).</summary>
    public readonly struct ShakeSource
    {
        public readonly double Time;
        public readonly float Strength;

        public ShakeSource(double time, float strength)
        {
            Time = time;
            Strength = strength;
        }
    }

    /// <summary>장면별 연출 입력. 018이 채울 형식이고, 그 전에는 Verification의 대체물이 만든다.</summary>
    public sealed class SceneBrief
    {
        public int SceneId;
        public SceneRole Role;
        public double EventTime;
        public Vec3 EventPos;
        public int Scorer = -1, Shooter = -1;
        public int AngleIndex, AngleCount;
        public bool LongPath;
        public int MyPlayer = -1;
        public bool MyPlayerInvolved;
        /// <summary>골 꼬리가 킥오프 대신 기간 종료 와이드를 쓴다 (specs/058 FR-60).</summary>
        public bool EndWide;
        /// <summary>골 뒤 첫 그물 접촉 시각 (058 FR-22). 없으면 NaN. 트랙 빌더가 클립에서 채운다.</summary>
        public double NetContactTime = double.NaN;
        public LiveCutRequest[] LiveCuts = Array.Empty<LiveCutRequest>();
        public DeadBallWindow[] DeadBalls = Array.Empty<DeadBallWindow>();
        public ShakeSource[] Shakes = Array.Empty<ShakeSource>();
    }
}
