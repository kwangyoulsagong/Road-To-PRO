using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>
    /// 결정론 카메라 흔들림 (specs/042 FR-40·41). 시드는 장면 id, 입력은 시뮬 시각뿐이다.
    /// 12 Hz 노이즈 지점을 만들어 보간하고 원천별 지수 감쇠 포락선을 곱한다.
    /// </summary>
    public sealed class CameraShake
    {
        const float KnotRate = 12f, DecaySeconds = 0.8f;
        const float GoalStrength = 0.06f, HardShotStrength = 0.03f;
        readonly Vec3[] _knots;
        readonly ShakeSource[] _sources;
        readonly double _start;

        public CameraShake(int sceneId, double start, double end, ShakeSource[] sources)
        {
            _start = start;
            _sources = sources ?? Array.Empty<ShakeSource>();
            int count = Math.Max(2, (int)Math.Ceiling((end - start) * KnotRate) + 2);
            _knots = new Vec3[count];
            var rng = new SeedRng(0x042CA0E5ul + (ulong)(uint)sceneId * 0x9E3779B97F4A7C15ul);
            for (int i = 0; i < count; i++)
                _knots[i] = new Vec3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));
        }

        /// <summary>이벤트에서 흔들림 원천을 뽑는다: 골, 강슛 (FR-40). 골대 타격은 장면 표시로 받는다.</summary>
        public static ShakeSource[] FromEvents(List<AnimEvent> events, float hardShotSpeed, ShakeSource[] extra)
        {
            var list = new List<ShakeSource>(extra ?? Array.Empty<ShakeSource>());
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev.Kind == SimEventKind.Goal) list.Add(new ShakeSource(ev.Time, GoalStrength));
                else if (ev.Kind == SimEventKind.Shot && ev.BallVelOut.Length() >= hardShotSpeed) list.Add(new ShakeSource(ev.Time, HardShotStrength));
            }
            return list.ToArray();
        }

        public Vec3 At(double time)
        {
            float envelope = 0f;
            for (int i = 0; i < _sources.Length; i++)
            {
                double dt = time - _sources[i].Time;
                if (dt < 0d) continue;
                envelope += _sources[i].Strength * MathF.Exp(-(float)dt / DecaySeconds);
            }
            if (envelope <= 0f) return Vec3.Zero;

            double u = Math.Max(0d, (time - _start) * KnotRate);
            int k = Math.Min((int)u, _knots.Length - 2);
            float a = CameraMath.Clamp((float)(u - k), 0f, 1f);
            return (_knots[k] + (_knots[k + 1] - _knots[k]) * a) * envelope;
        }

        /// <summary>FR-05·40: 메인·와이드 카메라는 흔들지 않는다.</summary>
        public static bool Applies(ShotKind kind)
        {
            switch (kind)
            {
                case ShotKind.Closeup:
                case ShotKind.Celebration:
                case ShotKind.ReplayHigh:
                case ShotKind.ReplayShooter:
                case ShotKind.ReplayReverse:
                case ShotKind.FallbackHigh:
                    return true;
                default:
                    return false;
            }
        }
    }
}
