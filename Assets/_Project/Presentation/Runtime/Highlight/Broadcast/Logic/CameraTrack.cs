using System;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>재생 시각에서 읽은 카메라 상태.</summary>
    public readonly struct CameraSample
    {
        public readonly CameraPose Pose;
        public readonly ShotKind Shot;
        public readonly bool IsReverse;
        public readonly float FovMin, FovMax;

        public CameraSample(in CameraPose pose, ShotKind shot, bool isReverse, float fovMin, float fovMax)
        {
            Pose = pose;
            Shot = shot;
            IsReverse = isReverse;
            FovMin = fovMin;
            FovMax = fovMax;
        }

        public float FovDeg(float aspect) => Pose.FovDeg(aspect, FovMin, FovMax);
    }

    /// <summary>장면 하나의 카메라 경로 (specs/042). 30 Hz 프레임 포즈를 재생 시각으로 보간한다. 재생 중 할당 없음.</summary>
    public sealed class CameraTrack
    {
        public readonly double StartTime;
        public readonly CameraPose[] Poses;
        public readonly ShotKind[] Shots;
        /// <summary>이 프레임에서 컷이 일어난다. 앞 프레임과 보간하지 않는다.</summary>
        public readonly bool[] CutAt;
        /// <summary>긴 경로 리플레이의 슬로모 지점 (FR-25). 없으면 NaN.</summary>
        public double SlowMoMark = double.NaN;

        readonly float _mainMin, _mainMax;

        public CameraTrack(double startTime, int frames, float mainFovMin, float mainFovMax)
        {
            if (frames < 2) throw new ArgumentException("프레임 2개 이상");
            StartTime = startTime;
            Poses = new CameraPose[frames];
            Shots = new ShotKind[frames];
            CutAt = new bool[frames];
            _mainMin = mainFovMin;
            _mainMax = mainFovMax;
        }

        public int Frames => Poses.Length;

        public CameraSample Sample(double time)
        {
            double u = (time - StartTime) * HighlightData.FrameRate;
            int f;
            float a;
            if (u <= 0d)
            {
                f = 0;
                a = 0f;
            }
            else if (u >= Frames - 1)
            {
                f = Frames - 2;
                a = 1f;
            }
            else
            {
                f = (int)Math.Floor(u);
                a = (float)(u - f);
            }

            // 컷 프레임 직전 구간은 번지지 않게 앞 프레임을 유지한다.
            if (CutAt[f + 1]) a = a >= 1f ? 1f : 0f;
            var pose = a <= 0f ? Poses[f] : a >= 1f ? Poses[f + 1] : CameraPose.Lerp(Poses[f], Poses[f + 1], a);
            var shot = Shots[a >= 1f ? f + 1 : f];
            FovRange(shot, out float min, out float max);
            return new CameraSample(pose, shot, ReplayAnglePlacer.IsReverse(shot), min, max);
        }

        void FovRange(ShotKind shot, out float min, out float max)
        {
            switch (shot)
            {
                case ShotKind.Main:
                case ShotKind.ReactionWide:
                case ShotKind.KickoffWide:
                    min = _mainMin;
                    max = _mainMax;
                    return;
                case ShotKind.TopView:
                    min = 20f;
                    max = 90f;
                    return;
                case ShotKind.MyPlayer:
                    min = 20f;
                    max = 85f;
                    return;
                default:
                    min = 10f;
                    max = 60f;
                    return;
            }
        }
    }
}
