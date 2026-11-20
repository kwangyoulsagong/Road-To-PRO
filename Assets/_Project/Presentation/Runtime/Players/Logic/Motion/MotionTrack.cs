using System;
using System.Collections.Generic;

namespace RoadToPro.Presentation.Players.Logic.Motion
{
    /// <summary>계획이 쓰는 기본값. 054-motion-standards.json과 같아야 한다 (MotionStandardsTests가 대조, FR-72).</summary>
    public static class MotionDefaults
    {
        public const float AccelPosture = 2.78f, DecelPosture = 3.0f;
        public const float CutHeadingDeg = 45f, CutWindow = 0.35f, TurnHeadingDeg = 135f;
        public const float YawFrameDeltaDeg = 20f;
        public const float RootContactMax = 0.10f;
        public const float TorsoOverlap = 0.03f, PostPenetration = 0.05f;
        public const float MaxSimSpeed = 11f;      // 005 최고 9.7 m/s + 균형·수치 여유: 넘으면 시뮬 오류 (FR-80)
        public const float MaxSimAccel = 25f;      // LocomotionBlend.SimErrorAccel과 같은 값
        public const float StopSpeed = 0.3f;       // research 01 § B Arrive 정지 처리
        public const float PivotSpeed = 2f;        // research 01 저속 피벗
    }

    /// <summary>시뮬 한계 초과 기록 (FR-80): 054 실패가 아니라 시뮬 쪽 실패로 보고한다.</summary>
    public readonly struct SimFault
    {
        public readonly int Entity, Frame;
        public readonly float Speed;

        public SimFault(int entity, int frame, float speed)
        {
            Entity = entity;
            Frame = frame;
            Speed = speed;
        }
    }

    /// <summary>
    /// 시뮬 기록에서 구한 엔티티별 30 Hz 파생값 (specs/054 FR-20~25). 시뮬을 읽기만 한다.
    /// 값은 프레임 번호로만 정해진다 — 렌더 fps·배속과 무관.
    /// </summary>
    public sealed class MotionTrack
    {
        const int E = HighlightData.Entities;
        const float Dt = 1f / HighlightData.FrameRate;
        /// <summary>가속도 계산 창(프레임, 앞뒤). 0.2 s: 30 Hz 차분 잡음을 줄이면서 짧은 제동을 놓치지 않는다.</summary>
        const int AccelSpan = 3;
        const float Gravity = 9.81f;

        public readonly int Frames;
        readonly float[] _speed, _heading, _along, _lat;
        readonly List<SimFault> _faults = new List<SimFault>();
        readonly bool[] _faulted = new bool[E];

        public IReadOnlyList<SimFault> Faults => _faults;
        public bool HasFault(int e) => _faulted[e];
        public float Speed(int f, int e) => _speed[f * E + e];
        /// <summary>진행 방향(라디안, 0 = +Z). 정지 중에는 마지막 값.</summary>
        public float Heading(int f, int e) => _heading[f * E + e];
        /// <summary>진행 방향 가속도(m/s², − = 감속).</summary>
        public float AccelAlong(int f, int e) => _along[f * E + e];
        /// <summary>횡가속(m/s², + = 오른쪽으로 돎).</summary>
        public float AccelLat(int f, int e) => _lat[f * E + e];

        public MotionTrack(HighlightData d)
        {
            Frames = d.Frames;
            int n = Frames * E;
            _speed = new float[n];
            _heading = new float[n];
            _along = new float[n];
            _lat = new float[n];
            var vx = new float[Frames];
            var vz = new float[Frames];
            for (int e = 0; e < E; e++)
            {
                float heading = d.Yaw[e];
                bool fault = false;
                for (int f = 0; f < Frames; f++)
                {
                    int f0 = Math.Max(0, f - 1), f1 = Math.Min(Frames - 1, f + 1);
                    if (!d.Present[f0 * E + e]) f0 = f;
                    if (!d.Present[f1 * E + e]) f1 = f;
                    float span = Math.Max(1, f1 - f0) * Dt;
                    int a = f0 * E + e, b = f1 * E + e;
                    vx[f] = f1 == f0 || !d.Present[f * E + e] ? 0f : (d.X[b] - d.X[a]) / span;
                    vz[f] = f1 == f0 || !d.Present[f * E + e] ? 0f : (d.Z[b] - d.Z[a]) / span;
                    float v = (float)Math.Sqrt(vx[f] * vx[f] + vz[f] * vz[f]);
                    _speed[f * E + e] = v;
                    if (v > 0.2f) heading = (float)Math.Atan2(vx[f], vz[f]);
                    _heading[f * E + e] = heading;
                    if (v > MotionDefaults.MaxSimSpeed && !fault && d.Present[f * E + e])
                    {
                        fault = true;
                        _faults.Add(new SimFault(e, f, v));
                    }
                }
                for (int f = 0; f < Frames; f++)
                {
                    int f0 = Math.Max(0, f - AccelSpan), f1 = Math.Min(Frames - 1, f + AccelSpan);
                    float span = Math.Max(1, f1 - f0) * Dt;
                    float ax = (vx[f1] - vx[f0]) / span, az = (vz[f1] - vz[f0]) / span;
                    float h = _heading[f * E + e];
                    float sh = (float)Math.Sin(h), ch = (float)Math.Cos(h);
                    _along[f * E + e] = ax * sh + az * ch;
                    _lat[f * E + e] = ax * ch - az * sh;   // 오른쪽 = (cos h, −sin h)
                    // 0.2 s 평균 가속 (FR-80): 005 Separation의 순간 밀침은 평균에서 걸러진다. 투입·퇴장 경계는 재지 않는다
                    bool whole = d.Present[f0 * E + e] && d.Present[f1 * E + e];
                    if (!fault && whole && ax * ax + az * az > MotionDefaults.MaxSimAccel * MotionDefaults.MaxSimAccel)
                    {
                        fault = true;
                        _faults.Add(new SimFault(e, f, _speed[f * E + e]));
                    }
                }
                _faulted[e] = fault;
            }
        }

        /// <summary>횡가속에 맞는 몸 기울기(라디안, FR-22).</summary>
        public static float LeanFor(float aLat) => (float)Math.Atan(aLat / Gravity);
    }

    /// <summary>
    /// 렌더 몸 방향 (specs/054 FR-05·25). 시뮬 yaw를 따르되 각속도를 상한(60 fps 프레임당 20° = 1,200°/s)으로 자른다.
    /// 앞으로 한 번 훑어 프레임 값을 고정하므로 배속·렌더 fps와 무관하다.
    /// </summary>
    public sealed class YawTrack
    {
        const int E = HighlightData.Entities;
        readonly float[] _yaw;
        readonly HighlightData _d;

        public const float MaxRate = MotionDefaults.YawFrameDeltaDeg * 60f * (float)(Math.PI / 180.0);
        /// <summary>080 T12a: 방향 덮어쓰기 구간 앞뒤 섞기 (s). 180° 돌기 = 300°/s → 3배속 60 fps에서 프레임당 15°.</summary>
        public const float OverrideRamp = 0.6f;

        /// <summary>overrides: (엔티티, 시작, 끝, 목표 방향) — 그 구간 동안 시뮬 방향 대신 목표 방향으로 돈다(같은 회전 속도 상한). 080 T12a: GK 선방은 공이 오는 쪽.</summary>
        public YawTrack(HighlightData d, IReadOnlyList<(int e, float t0, float t1, float yaw)> overrides = null)
        {
            _d = d;
            _yaw = new float[d.Frames * E];
            float max = MaxRate / HighlightData.FrameRate;
            for (int e = 0; e < E; e++)
            {
                float cur = d.Yaw[e];
                for (int f = 0; f < d.Frames; f++)
                {
                    int i = f * E + e;
                    float target = d.Yaw[i];
                    if (overrides != null)
                    {
                        // 구간 안은 목표 방향, 앞뒤 OverrideRamp 동안 선형으로 섞는다 (순간 회전 054 FR-25 — 3배속 프레임당 ≤ 20°)
                        float tf = d.StartTime + f / HighlightData.FrameRate;
                        for (int k = 0; k < overrides.Count; k++)
                        {
                            var o = overrides[k];
                            if (o.e != e || tf < o.t0 - OverrideRamp || tf > o.t1 + OverrideRamp) continue;
                            float w = tf < o.t0 ? 1f - (o.t0 - tf) / OverrideRamp : tf > o.t1 ? 1f - (tf - o.t1) / OverrideRamp : 1f;
                            target += (float)Math.IEEERemainder(o.yaw - target, 2 * Math.PI) * w;
                        }
                    }
                    if (f > 0)
                    {
                        float delta = (float)Math.IEEERemainder(target - cur, 2 * Math.PI);
                        cur += delta > max ? max : delta < -max ? -max : delta;
                    }
                    _yaw[i] = cur;
                }
            }
        }

        public float At(int e, float t)
        {
            _d.FrameAt(t, out var f, out var a);
            int i0 = f * E + e;
            float y0 = _yaw[i0], y1 = _yaw[i0 + E];
            return y0 + (float)Math.IEEERemainder(y1 - y0, 2 * Math.PI) * a;
        }
    }
}
