using System;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>
    /// 메인 중계 카메라 프레이밍 (specs/042 FR-02~04, 50~52, 55). 위치는 고정이고 시선·화각만 30 Hz 고정 스텝으로 감쇠한다.
    /// 시선은 각도가 아니라 벡터로 감쇠해 ±180° 경계에서 반대로 돌지 않는다.
    /// </summary>
    public sealed class FramingSolver
    {
        const float Dt = 1f / HighlightData.FrameRate;
        /// <summary>공은 화면 가장자리 5% 안쪽에 둔다 (AC-07).</summary>
        const float BallViewportScale = 1f / 0.9f;

        /// <summary>경기장 밖 공을 쫓아 관중석만 찍지 않도록 조준점을 피치 + 이 여백 안으로 묶는다 (AC-13).</summary>
        const float AimMargin = 5f, AimMaxHeight = 3f;

        readonly CameraTuning _t;
        readonly Vec3 _position;
        readonly float _aimX, _aimZ;
        readonly float _lookK, _zoomK;
        bool _hasState;
        Vec3 _forward;
        float _tanV, _tanH;

        public FramingSolver(CameraRigData rig, CameraTuning tuning)
        {
            _t = tuning;
            _position = rig.MainPosition(tuning);
            _aimX = rig.HalfLength + AimMargin;
            _aimZ = rig.HalfWidth + AimMargin;
            _lookK = CameraMath.DampFactor(Dt, tuning.LookDamp);
            _zoomK = CameraMath.DampFactor(Dt, tuning.ZoomDamp);
        }

        public Vec3 Position => _position;
        public bool HasState => _hasState;
        public CameraPose Current => new CameraPose(_position, _forward, _tanV, _tanH);

        public void Reset() => _hasState = false;

        /// <summary>대상으로 한 스텝 진행한다. 대상이 없으면 직전 프레이밍을 유지한다 (FR-52).</summary>
        public CameraPose Step(FramingTarget[] targets, int count)
        {
            if (count <= 0)
            {
                if (!_hasState)
                {
                    _forward = (new Vec3(0f, 0f, 0f) - _position).Normalized();
                    _tanV = _tanH = MathF.Tan(_t.WideFovDeg * 0.5f * CameraMath.Deg2Rad);
                    _hasState = true;
                }
                return Current;
            }

            var ball = targets[0].Pos;
            var desired = Aim(targets, count);
            Fit(targets, count, desired, out float tanV, out float tanH);

            float zone = CameraMath.Smoothstep(_t.BoxZoomStartX, _t.BoxZoomEndX, MathF.Abs(ball.X));
            float zoneFov = _t.WideFovDeg + (_t.BoxFovDeg - _t.WideFovDeg) * zone;
            tanV = MathF.Max(tanV, MathF.Tan(zoneFov * 0.5f * CameraMath.Deg2Rad));

            if (_hasState)
            {
                _forward = (_forward + (desired - _forward) * _lookK).Normalized();
                _tanV += (tanV - _tanV) * _zoomK;
                _tanH += (tanH - _tanH) * _zoomK;
            }
            else
            {
                _forward = desired;
                _tanV = tanV;
                _tanH = tanH;
                _hasState = true;
            }

            _forward = ClampAngles(KeepBallInCone(ClampAngles(_forward), ball));
            ExpandForBall(ball);
            return Current;
        }

        Vec3 Aim(FramingTarget[] targets, int count)
        {
            var sum = Vec3.Zero;
            float weight = 0f;
            for (int i = 0; i < count; i++)
            {
                sum += ClampAim(targets[i].Pos) * targets[i].Weight;
                weight += targets[i].Weight;
            }
            return (sum / weight - _position).Normalized();
        }

        Vec3 ClampAim(Vec3 p) => new Vec3(
            CameraMath.Clamp(p.X, -_aimX, _aimX),
            MathF.Min(p.Y, AimMaxHeight),
            CameraMath.Clamp(p.Z, -_aimZ, _aimZ));

        void Fit(FramingTarget[] targets, int count, Vec3 forward, out float tanV, out float tanH)
        {
            CameraMath.Basis(forward, out var right, out var up);
            tanV = tanH = 0f;
            for (int i = 0; i < count; i++)
            {
                FitPoint(targets[i].Pos, forward, right, up, ref tanV, ref tanH);
                if (targets[i].IsPlayer) FitPoint(targets[i].Pos + new Vec3(0f, 1.9f, 0f), forward, right, up, ref tanV, ref tanH);
            }
            tanV *= _t.FramingMargin;
            tanH *= _t.FramingMargin;
        }

        void FitPoint(Vec3 p, Vec3 forward, Vec3 right, Vec3 up, ref float tanV, ref float tanH)
        {
            var v = p - _position;
            float z = Vec3.Dot(v, forward);
            if (z <= 0.01f) return;
            tanH = MathF.Max(tanH, MathF.Abs(Vec3.Dot(v, right) / z));
            tanV = MathF.Max(tanV, MathF.Abs(Vec3.Dot(v, up) / z));
        }

        /// <summary>팬 ±한계, 틸트 한계 (FR-50·51).</summary>
        Vec3 ClampAngles(Vec3 f)
        {
            float pan = MathF.Atan2(f.X, f.Z);
            float tilt = MathF.Asin(CameraMath.Clamp(-f.Y, -1f, 1f));
            float limit = _t.PanLimitDeg * CameraMath.Deg2Rad;
            pan = CameraMath.Clamp(pan, -limit, limit);
            tilt = CameraMath.Clamp(tilt, 0.5f * CameraMath.Deg2Rad, _t.TiltMaxDeg * CameraMath.Deg2Rad);
            float c = MathF.Cos(tilt);
            return new Vec3(MathF.Sin(pan) * c, -MathF.Sin(tilt), MathF.Cos(pan) * c);
        }

        /// <summary>감쇠가 뒤처져도 공은 최대 화각 원뿔 안에 둔다. 경계가 연속이라 튀지 않는다.</summary>
        Vec3 KeepBallInCone(Vec3 forward, Vec3 ball)
        {
            var toBall = (ball - _position).Normalized();
            float halfTan = MathF.Tan(_t.FovMaxDeg * 0.5f * CameraMath.Deg2Rad) * 0.9f;
            float maxAngle = MathF.Atan(halfTan);
            float cos = CameraMath.Clamp(Vec3.Dot(forward, toBall), -1f, 1f);
            float angle = MathF.Acos(cos);
            if (angle <= maxAngle) return forward;
            float s = (angle - maxAngle) / angle;
            return (forward + (toBall - forward) * s).Normalized();
        }

        /// <summary>줌 감쇠가 뒤처져도 공은 화각 안에 든다. 넓히는 쪽만 즉시 적용한다.</summary>
        void ExpandForBall(Vec3 ball)
        {
            CameraMath.Basis(_forward, out var right, out var up);
            var v = ball - _position;
            float z = Vec3.Dot(v, _forward);
            if (z <= 0.01f) return;
            _tanH = MathF.Max(_tanH, MathF.Abs(Vec3.Dot(v, right) / z) * BallViewportScale);
            _tanV = MathF.Max(_tanV, MathF.Abs(Vec3.Dot(v, up) / z) * BallViewportScale);
        }
    }
}
