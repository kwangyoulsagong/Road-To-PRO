using System;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// 테스트용 합성 하이라이트 (specs/043 AC-07·09·10·12). 시드 고정 System.Random(테스트 입력 생성용 — 계획·재생 코드는 RNG를 쓰지 않는다).
    /// 엔티티 0이 정면(+Z)으로 달리다 ContactTime에 공을 찬다. 나머지는 흩어져 서 있다.
    /// </summary>
    internal static class SyntheticScenes
    {
        public const float Start = 10f;
        public const float Length = 4f;

        public static HighlightData Empty(int seed, out Random rng)
        {
            rng = new Random(seed);
            int frames = (int)(Length * HighlightData.FrameRate) + 1;
            var d = new HighlightData(Start, frames);
            for (int f = 0; f < frames; f++)
                for (int e = 1; e < HighlightData.Entities; e++)
                    d.Set(f, e, -40f + (e % 6) * 12f, -25f + (e / 6) * 10f, 0f);
            return d;
        }

        /// <summary>kind = Pass·Shot·Cross. 반환: 데이터, 접촉 시각.</summary>
        public static HighlightData Kick(int seed, SimEventKind kind, out float contact)
        {
            var d = Empty(seed, out var rng);
            float v = (float)(1.5 + rng.NextDouble() * 4.5);
            float yaw = (float)(rng.NextDouble() * Math.PI * 2);
            contact = Start + 1.5f + (float)rng.NextDouble();
            var fwd = new Vec3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
            var right = new Vec3(fwd.Z, 0, -fwd.X);
            var p0 = new Vec3(-10, 0, 3);
            // 접촉 0.4초 전부터 감속해 접촉 순간 속도 v·0.5
            for (int f = 0; f < d.Frames; f++)
            {
                float t = Start + f / HighlightData.FrameRate;
                float dist = Travel(t - Start, contact - Start, v);
                var p = p0 + fwd * dist;
                d.Set(f, 0, p.X, p.Z, yaw);
            }
            float cd = Travel(contact - Start, contact - Start, v);
            var cp = p0 + fwd * cd;
            float side = kind == SimEventKind.Cross ? 0.05f : (float)(rng.NextDouble() * 0.3 - 0.15);
            var ball = cp + fwd * (float)(0.45 + rng.NextDouble() * 0.15) + right * side + new Vec3(0, 0.11f, 0);
            float outSpeed = kind == SimEventKind.Shot ? 24f : kind == SimEventKind.Cross ? 20f : 10f;
            var vout = (fwd + right * (float)(rng.NextDouble() * 0.6 - 0.3)).Normalized() * outSpeed;
            for (int f = 0; f < d.Frames; f++)
            {
                float t = Start + f / HighlightData.FrameRate;
                if (t <= contact)
                {
                    // 앞에서 굴러와 접촉 지점에 멈춤(받아 놓은 공)
                    float k = Math.Max(0f, contact - t);
                    d.Ball[f] = ball + fwd * (k * 1.5f);
                }
                else d.Ball[f] = ball + vout * (t - contact);
            }
            d.Events.Add(new AnimEvent(contact, 1, kind, 0, -1, BodyPart.None, ActionOutcome.Success, ball, vout));
            return d;
        }

        static float Travel(float t, float tc, float v)
        {
            // 0 ~ tc − 0.4: 속도 v, 그 뒤 0.4 s 동안 v → 0.5v 선형 감속, 이후 0.5v
            float t1 = tc - 0.4f;
            if (t <= t1) return v * t;
            float a = Math.Min(t - t1, 0.4f);
            float d = v * t1 + v * a - 0.5f * (0.5f * v / 0.4f) * a * a;
            if (t > tc) d += 0.5f * v * (t - tc);
            return d;
        }

        /// <summary>엔티티 1이 엔티티 0에게 태클 파울(밀린 방향 = pushDir, 0의 정면 yaw0).</summary>
        public static HighlightData Foul(int seed, float yaw0, Vec3 fromDir, out float time)
        {
            var d = Empty(seed, out _);
            time = Start + 2f;
            var p = new Vec3(5, 0, 5);
            for (int f = 0; f < d.Frames; f++)
            {
                d.Set(f, 0, p.X, p.Z, yaw0);
                var q = p - fromDir * 1.2f;
                d.Set(f, 1, q.X, q.Z, (float)Math.Atan2(fromDir.X, fromDir.Z));
            }
            d.Events.Add(new AnimEvent(time, 1, SimEventKind.Foul, 1, 0, BodyPart.None, ActionOutcome.Success, p, Vec3.Zero));
            return d;
        }

        /// <summary>한 방향으로 일정 속도로 달리기만 (로코모션 미끄러짐·결정론).</summary>
        public static HighlightData Run(float speed, float yaw, float moveDirDeg, float turnRate = 0f)
        {
            var d = Empty(1, out _);
            var p = Vec3.Zero;
            float heading = yaw + moveDirDeg * (float)Math.PI / 180f;
            for (int f = 0; f < d.Frames; f++)
            {
                float y = yaw + turnRate * f / HighlightData.FrameRate;
                float h = heading + turnRate * f / HighlightData.FrameRate;
                d.Set(f, 0, p.X, p.Z, y);
                p += new Vec3((float)Math.Sin(h), 0, (float)Math.Cos(h)) * (speed / HighlightData.FrameRate);
            }
            return d;
        }
    }
}
