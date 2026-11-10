using System;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    public readonly struct FramingTarget
    {
        public readonly Vec3 Pos;
        public readonly float Weight;
        /// <summary>선수면 머리 높이까지 화각에 담는다.</summary>
        public readonly bool IsPlayer;

        public FramingTarget(Vec3 pos, float weight, bool isPlayer)
        {
            Pos = pos;
            Weight = weight;
            IsPlayer = isPlayer;
        }
    }

    /// <summary>프레이밍 대상 선택 (specs/042 FR-03·52). 가중치: 공 3, 캐리어 2, 최근접 수비 2명 각 2, 수신 후보 1.</summary>
    public static class FramingTargets
    {
        public const int Capacity = 5;
        public const float BallWeight = 3f, CarrierWeight = 2f, DefenderWeight = 2f, ReceiverWeight = 1f;

        /// <returns>대상 수. 프레임 데이터가 없으면 0 (호출자가 직전 프레이밍을 유지한다).</returns>
        public static int Collect(HighlightData d, int frame, FramingTarget[] into)
        {
            if (frame < 0 || frame >= d.Frames) return 0;
            var ball = d.Ball[frame];
            if (!IsFinite(ball)) return 0;

            int n = 0;
            into[n++] = new FramingTarget(ball, BallWeight, false);

            int carrier = d.BallOwner[frame];
            if (carrier < 0 || carrier >= HighlightData.Players || !d.Present[frame * HighlightData.Entities + carrier]) return n;
            into[n++] = new FramingTarget(PosAt(d, frame, carrier), CarrierWeight, true);

            int team = carrier < 11 ? 0 : 1;
            int first = -1, second = -1;
            float firstD = float.PositiveInfinity, secondD = float.PositiveInfinity;
            int from = team == 0 ? 11 : 0, to = from + 11;
            for (int e = from; e < to; e++)
            {
                if (!d.Present[frame * HighlightData.Entities + e]) continue;
                float dist = Vec3.DistanceSq(PosAt(d, frame, e), ball);
                if (dist < firstD)
                {
                    second = first;
                    secondD = firstD;
                    first = e;
                    firstD = dist;
                }
                else if (dist < secondD)
                {
                    second = e;
                    secondD = dist;
                }
            }
            if (first >= 0) into[n++] = new FramingTarget(PosAt(d, frame, first), DefenderWeight, true);
            if (second >= 0) into[n++] = new FramingTarget(PosAt(d, frame, second), DefenderWeight, true);

            int receiver = Receiver(d, frame, carrier, team);
            if (receiver >= 0) into[n++] = new FramingTarget(PosAt(d, frame, receiver), ReceiverWeight, true);
            return n;
        }

        /// <summary>008 전 대체: 이 프레임 뒤 첫 PassReceived의 주체, 없으면 공에 가장 가까운 동료.</summary>
        static int Receiver(HighlightData d, int frame, int carrier, int team)
        {
            float time = d.StartTime + frame / HighlightData.FrameRate;
            for (int i = 0; i < d.Events.Count; i++)
            {
                var ev = d.Events[i];
                if (ev.Kind != SimEventKind.PassReceived || ev.Time < time) continue;
                int e = ev.Entity;
                if (e >= 0 && e < HighlightData.Players && e != carrier && (e < 11 ? 0 : 1) == team
                    && d.Present[frame * HighlightData.Entities + e]) return e;
                break;
            }

            var ball = d.Ball[frame];
            int best = -1;
            float bestD = float.PositiveInfinity;
            int from = team == 0 ? 0 : 11, to = from + 11;
            for (int e = from; e < to; e++)
            {
                if (e == carrier || !d.Present[frame * HighlightData.Entities + e]) continue;
                float dist = Vec3.DistanceSq(PosAt(d, frame, e), ball);
                if (dist < bestD)
                {
                    bestD = dist;
                    best = e;
                }
            }
            return best;
        }

        public static Vec3 PosAt(HighlightData d, int frame, int e)
        {
            int i = frame * HighlightData.Entities + e;
            return new Vec3(d.X[i], 0f, d.Z[i]);
        }

        static bool IsFinite(Vec3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }
}
