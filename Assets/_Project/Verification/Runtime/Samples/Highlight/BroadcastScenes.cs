using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Verification.Samples.Highlight
{
    /// <summary>
    /// specs/042 검증용 합성 경기 (018·012 대체). 대체 심판은 재시작 때 선수를 순간이동시켜 골 뒤 프레임이 없어서,
    /// 골 뒤 11 s까지 이어지는 움직임을 직접 만든다. 012·018 연결 때 실제 클립으로 다시 판정한다.
    /// </summary>
    public static class BroadcastScenes
    {
        public const double ShotTime = 99.3d, GoalTime = 100d;
        public const int Scorer = 9;
        const float HalfL = 52.5f, HalfW = 34f, FullSpeedRun = 7f;
        static readonly Vec3 ShotPoint = new Vec3(30f, 0.11f, -6f);
        static readonly Vec3 GoalPoint = new Vec3(52.8f, 1.4f, 1.5f);
        static readonly Vec3 NetRest = new Vec3(53.6f, 0.11f, 1.5f);

        const int GoalMainId = 1, TailId = 5, EdgeId = 6;
        public const int FirstReplayIndex = 1, TailIndex = 4, EdgeIndex = 5;
        public const double EdgeCorner = 124d, EdgeTouchline = 128d, EdgeStands = 132d;

        public static HighlightScene[] Scenes() => new[]
        {
            new HighlightScene(GoalMainId, 92d, GoalTime + 6d, groupId: 1),
            new HighlightScene(2, 97.5d, 100.5d, isReplay: true, groupId: 1),
            new HighlightScene(3, 97.5d, 100.5d, isReplay: true, groupId: 1),
            new HighlightScene(4, 97.5d, 100.5d, isReplay: true, groupId: 1),
            new HighlightScene(TailId, GoalTime + 6d, GoalTime + 11d, groupId: 1),
            new HighlightScene(EdgeId, 120d, 135d),
        };

        public static SceneBrief Brief(int id)
        {
            var b = new SceneBrief { SceneId = id, EventTime = ShotTime, EventPos = new Vec3(ShotPoint.X, 0f, ShotPoint.Z), Shooter = Scorer, Scorer = Scorer, MyPlayer = Scorer };
            switch (id)
            {
                case GoalMainId:
                    b.Role = SceneRole.GoalMain;
                    b.EventTime = GoalTime;
                    b.MyPlayerInvolved = true;
                    return b;
                case 2:
                case 3:
                case 4:
                    b.Role = SceneRole.ReplayAngle;
                    b.AngleIndex = id - 2;
                    b.AngleCount = 3;
                    return b;
                case TailId:
                    b.Role = SceneRole.GoalTail;
                    return b;
                case EdgeId:
                    b.Role = SceneRole.Live;
                    return b;
                default:
                    return null;
            }
        }

        public static ReplayClip Extract(long fromTick, long toTick)
        {
            if (toTick <= fromTick) throw new ArgumentException("빈 구간");
            long from = fromTick * 3, to = toTick * 3;
            var frames = new ReplayFrame[to - from];
            for (long i = from; i < to; i++) frames[i - from] = Frame(i);

            var events = new List<SimEvent>();
            if (fromTick <= Tick(ShotTime) && Tick(ShotTime) < toTick) events.Add(Event(1, ShotTime, SimEventKind.Shot));
            if (fromTick <= Tick(GoalTime) && Tick(GoalTime) < toTick) events.Add(Event(2, GoalTime, SimEventKind.Goal));
            return new ReplayClip(fromTick, toTick, frames, events.ToArray());
        }

        static long Tick(double t) => (long)Math.Round(t * 10d);

        static SimEvent Event(long seq, double t, SimEventKind kind) => new SimEvent
        {
            Seq = seq,
            Tick = Tick(t),
            Substep = SimEvent.TickStart,
            Kind = kind,
            Team = 0,
            PlayerId = Scorer + 1,
            OtherId = -1,
            Part = BodyPart.RightFoot,
        };

        static ReplayFrame Frame(long index)
        {
            double t = index / 30d;
            var f = new ReplayFrame { Index = index, BallPhase = BallPhase.Rolling };
            var ball = BallAt(t, out int owner);
            f.BallOwnerSlot = owner;
            for (int e = 0; e < Squad.Slots; e++)
            {
                var p = PlayerAt(e, t, owner);
                var q = PlayerAt(e, t + 1d / 30d, owner);
                float dx = q.X - p.X, dz = q.Z - p.Z;
                float speed = MathF.Sqrt(dx * dx + dz * dz) * 30f;
                Write(f, e, p.X, 0f, p.Z, speed > 0.1f ? MathF.Atan2(dz, dx) : e < 11 ? 0f : MathF.PI);
                f.Hints[e] = HintFor(e, t, speed);
                f.PlayerIds[e] = e + 1;
            }

            Write(f, ReplayFrame.BallEntity, ball.X, ball.Y, ball.Z, 0f);
            f.Hints[ReplayFrame.BallEntity] = MotionHint.Idle;
            for (int o = 0; o < 3; o++)
            {
                int e = ReplayFrame.FirstOfficial + o;
                Write(f, e, o == 0 ? 8f : o == 1 ? -20f : 20f, 0f, o == 0 ? 6f : o == 1 ? -35f : 35f, 0f);
                f.Hints[e] = MotionHint.Idle;
            }
            return f;
        }

        static void Write(ReplayFrame f, int e, float x, float height, float z, float yaw)
        {
            int i = e * ReplayFrame.Stride;
            f.Values[i] = x + HalfL;
            f.Values[i + 1] = z + HalfW;
            f.Values[i + 2] = height;
            f.Values[i + 3] = yaw;
        }

        static MotionHint HintFor(int e, double t, float speed)
        {
            if (e == Scorer && t >= GoalTime + 0.6d && t < GoalTime + 11d && speed < 0.1f) return MotionHint.Celebrate;
            if (speed > 6f) return MotionHint.Sprint;
            if (speed > 3f) return MotionHint.Run;
            if (speed > 1.2f) return MotionHint.Jog;
            if (speed > 0.1f) return MotionHint.Walk;
            return MotionHint.Idle;
        }

        static Vec3 BallAt(double t, out int owner)
        {
            owner = -1;
            if (t < 112d)
            {
                if (t < ShotTime)
                {
                    owner = Scorer;
                    var p = PlayerAt(Scorer, t, Scorer);
                    return new Vec3(p.X + 0.5f, 0.11f, p.Z);
                }
                if (t < GoalTime)
                {
                    float a = (float)((t - ShotTime) / (GoalTime - ShotTime));
                    var v = ShotPoint + (GoalPoint - ShotPoint) * a;
                    return new Vec3(v.X, v.Y + 3f * a * (1f - a), v.Z);
                }
                // 꼬리 장면부터 공은 센터 서클에 있다. 장면 경계의 컷이 가린다.
                if (t >= GoalTime + 6d) return new Vec3(0f, 0.11f, 0f);
                float b = CameraMath.Clamp((float)((t - GoalTime) / 0.3d), 0f, 1f);
                return GoalPoint + (NetRest - GoalPoint) * b;
            }

            // 가장자리 장면: 중앙 → 코너 플래그 → 카메라 쪽 터치라인 → 관중석 쪽
            if (t < EdgeCorner) return Lerp(Vec3.Zero, new Vec3(52f, 0.11f, -33.5f), Ratio(t, 120d, EdgeCorner));
            if (t < EdgeTouchline) return Lerp(new Vec3(52f, 0.11f, -33.5f), new Vec3(0f, 0.11f, -34f), Ratio(t, EdgeCorner, EdgeTouchline));
            if (t < EdgeStands) return Lerp(new Vec3(0f, 0.11f, -34f), new Vec3(0f, 14f, -56f), Ratio(t, EdgeTouchline, EdgeStands));
            return new Vec3(0f, 14f, -56f);
        }

        static float Ratio(double t, double a, double b) => CameraMath.Clamp((float)((t - a) / (b - a)), 0f, 1f);

        static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        static Vec3 PlayerAt(int e, double t, int owner)
        {
            var home = Formation(e);
            if (e == Scorer) return ScorerAt(t);
            if (e < 11 && e >= 5 && t >= GoalTime + 0.6d && t < 112d)
            {
                // 동료는 득점자에게 달려간다 (순간이동 없음)
                var target = ScorerAt(GoalTime + 7d) + new Vec3(-2f - e % 5, 0f, e % 3 - 1f);
                float a = Ratio(t, GoalTime + 0.6d, GoalTime + 8d);
                return Lerp(home, target, a * a * (3f - 2f * a));
            }
            float drift = (float)Math.Sin(t * 0.3d + e) * 0.4f;
            return home + new Vec3(drift, 0f, 0f);
        }

        static Vec3 ScorerAt(double t)
        {
            var start = new Vec3(15f, 0f, -14f);
            var shot = new Vec3(ShotPoint.X - 0.5f, 0f, ShotPoint.Z);
            var corner = new Vec3(46f, 0f, -30f);
            if (t <= 92d) return start;
            if (t < ShotTime) return Lerp(start, shot, Ratio(t, 92d, ShotTime));
            if (t < GoalTime + 0.6d) return shot;
            float run = Vec3.Distance(shot, corner) / FullSpeedRun;
            return Lerp(shot, corner, Ratio(t, GoalTime + 0.6d, GoalTime + 0.6d + run));
        }

        static Vec3 Formation(int e)
        {
            int n = e % 11;
            bool home = e < 11;
            float x, z;
            if (n == 0)
            {
                x = home ? -45f : 50.5f;
                z = 0f;
            }
            else if (n <= 4)
            {
                x = home ? -15f : 38f;
                z = -22f + (n - 1) * 14.6f;
            }
            else if (n <= 7)
            {
                x = home ? 8f : 25f;
                z = -15f + (n - 5) * 15f;
            }
            else
            {
                x = home ? 25f : 10f;
                z = -14f + (n - 8) * 14f;
            }
            return new Vec3(x, 0f, z);
        }
    }
}
