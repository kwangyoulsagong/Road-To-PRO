using System;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>탑뷰 (specs/042 FR-31, FR-55 세로 화면 라이브). 공을 따라 하프 크기를 담는다.</summary>
    public sealed class TopViewCamera
    {
        const float Height = 70f, ZOffset = -10f, Tau = 0.5f, ViewHalfLength = 30f, ViewHalfWidth = 38f;
        readonly float _follow;
        readonly float _limitX;
        readonly float _k;
        bool _hasState;
        float _x;

        public TopViewCamera(CameraRigData rig)
        {
            _follow = rig.HalfLength * 0.5f;
            _limitX = rig.HalfLength - _follow;
            _k = CameraMath.DampFactor(1f / HighlightData.FrameRate, Tau);
        }

        public CameraPose Step(Vec3 ball)
        {
            float target = CameraMath.Clamp(ball.X, -_limitX, _limitX);
            _x = _hasState ? _x + (target - _x) * _k : target;
            _hasState = true;
            var pos = new Vec3(_x, Height, ZOffset);
            var forward = (new Vec3(_x, 0f, 0f) - pos).Normalized();
            return new CameraPose(pos, forward, ViewHalfWidth / Height, ViewHalfLength / Height);
        }
    }

    /// <summary>내 선수 추적 (specs/042 FR-32). 선수를 화면 정중앙에 두고, 공이 들어오도록 거리와 화각을 넓힌다.</summary>
    public sealed class MyPlayerCamera
    {
        const float MinDistance = 22f, BallFactor = 1.4f, HeightRatio = 0.5f, Tau = 0.6f;
        const float ChestHeight = 1.2f, BallViewportScale = 1f / 0.9f;
        const float BaseFovDeg = 28f;

        readonly float _k;
        bool _hasState;
        float _distance;
        float _tanV, _tanH;

        public MyPlayerCamera() => _k = CameraMath.DampFactor(1f / HighlightData.FrameRate, Tau);

        public CameraPose Step(Vec3 player, Vec3 ball)
        {
            var flat = new Vec3(ball.X - player.X, 0f, ball.Z - player.Z);
            float target = MathF.Max(MinDistance, flat.Length() * BallFactor);
            _distance = _hasState ? _distance + (target - _distance) * _k : target;

            var look = player + new Vec3(0f, ChestHeight, 0f);
            var pos = look + new Vec3(0f, _distance * HeightRatio, -_distance);
            var forward = (look - pos).Normalized();

            CameraMath.Basis(forward, out var right, out var up);
            var v = ball - pos;
            float z = MathF.Max(0.01f, Vec3.Dot(v, forward));
            float ballH = MathF.Abs(Vec3.Dot(v, right) / z) * BallViewportScale;
            float ballV = MathF.Abs(Vec3.Dot(v, up) / z) * BallViewportScale;
            float baseTan = MathF.Tan(BaseFovDeg * 0.5f * CameraMath.Deg2Rad);
            float wantV = MathF.Max(baseTan, ballV), wantH = ballH;

            if (_hasState)
            {
                _tanV = MathF.Max(wantV, _tanV + (wantV - _tanV) * _k);
                _tanH = MathF.Max(wantH, _tanH + (wantH - _tanH) * _k);
            }
            else
            {
                _tanV = wantV;
                _tanH = wantH;
                _hasState = true;
            }
            return new CameraPose(pos, forward, _tanV, _tanH);
        }
    }
}
