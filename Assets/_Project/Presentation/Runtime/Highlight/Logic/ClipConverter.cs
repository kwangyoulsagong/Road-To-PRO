using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>
    /// 006 재구성 클립을 재생 입력으로 옮긴다 (specs/041 FR-02·03). 좌표는 044 FR-01 (X = x − L/2, Y = 높이, Z = y − W/2).
    /// 정면 각은 시뮬 atan2(fy, fx)에서 애니메이션 규약 atan2(fx, fy)로 바꾸고 프레임 사이 연속이 되게 푼다.
    /// </summary>
    public static class ClipConverter
    {
        const int FirstActionKind = 200, EndActionKind = 600;
        const double HalfPi = Math.PI * 0.5d, TwoPi = Math.PI * 2d;

        /// <summary>시뮬 이벤트의 재생 시각. 서브스텝이 없는 이벤트(틱 시작)는 틱 시작 시각이다.</summary>
        public static double EventTime(in SimEvent e) =>
            e.Tick * 0.1d + Math.Max((int)e.Substep, 0) * 0.01d;

        public static HighlightData ToHighlight(ReplayClip clip, PitchSpec pitch)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (clip.Frames.Length < 2) throw new ArgumentException("프레임 2개 이상이 필요하다");

            float halfL = pitch.LengthM * 0.5f, halfW = pitch.WidthM * 0.5f;
            var data = new HighlightData(clip.Frames[0].Index / HighlightData.FrameRate, clip.Frames.Length);
            var lastYaw = new float[HighlightData.Entities];
            var hasYaw = new bool[HighlightData.Entities];

            for (int f = 0; f < clip.Frames.Length; f++)
            {
                var src = clip.Frames[f];
                for (int e = 0; e < HighlightData.Entities; e++)
                {
                    int r = e < HighlightData.Players ? e : ReplayFrame.FirstOfficial + (e - HighlightData.Players);
                    bool present = src.Hints[r] != MotionHint.Absent;
                    float yaw = lastYaw[e];
                    if (present)
                    {
                        yaw = (float)(HalfPi - src.Yaw(r));
                        if (hasYaw[e]) yaw += (float)(TwoPi * Math.Round((lastYaw[e] - yaw) / TwoPi));
                        lastYaw[e] = yaw;
                        hasYaw[e] = true;
                    }
                    data.Set(f, e, src.X(r) - halfL, src.Y(r) - halfW, yaw, present);
                }
                data.Ball[f] = new Vec3(
                    src.X(ReplayFrame.BallEntity) - halfL,
                    src.Z(ReplayFrame.BallEntity),
                    src.Y(ReplayFrame.BallEntity) - halfW);
                data.BallOwner[f] = (short)src.BallOwnerSlot;
            }

            foreach (var ev in clip.Events)
            {
                if ((int)ev.Kind < FirstActionKind || (int)ev.Kind >= EndActionKind) continue;
                double time = EventTime(ev);
                int frame = FrameIndex(clip, time);
                int entity = SlotOf(clip.Frames[frame], ev.PlayerId);
                if (entity < 0) continue;
                data.Events.Add(new AnimEvent((float)time, ev.Seq, ev.Kind, entity, SlotOf(clip.Frames[frame], ev.OtherId), ev.Part,
                    ActionOutcome.Success, EventBall(ev, data.Ball[frame], pitch), BallVelocityAfter(data, frame)));
            }
            return data;
        }

        /// <summary>장면 구간의 표현 큐. 시각 순으로 정렬해 돌려준다.</summary>
        public static PlaybackCue[] ToCues(ReplayClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            var cues = new PlaybackCue[clip.Events.Length];
            for (int i = 0; i < cues.Length; i++)
            {
                var ev = clip.Events[i];
                cues[i] = new PlaybackCue(EventTime(ev), ev.Seq, (int)ev.Kind, ev.PlayerId);
            }
            CueDispatcher.Sort(cues);
            return cues;
        }

        static int FrameIndex(ReplayClip clip, double time)
        {
            int f = (int)Math.Round(time * HighlightData.FrameRate) - (int)clip.Frames[0].Index;
            return f < 0 ? 0 : f >= clip.Frames.Length ? clip.Frames.Length - 1 : f;
        }

        static int SlotOf(ReplayFrame frame, int playerId)
        {
            if (playerId < 0) return -1;
            for (int i = 0; i < frame.PlayerIds.Length; i++)
                if (frame.PlayerIds[i] == playerId) return i;
            return -1;
        }

        /// <summary>이벤트 위치는 정규화 x·y + 미터 z다. 위치를 쓰지 않은 이벤트는 그 프레임의 공 위치를 쓴다.</summary>
        static Vec3 EventBall(in SimEvent ev, Vec3 frameBall, PitchSpec pitch)
        {
            if (ev.X == 0f && ev.Y == 0f && ev.Z == 0f) return frameBall;
            return new Vec3((ev.X - 0.5f) * pitch.LengthM, ev.Z, (ev.Y - 0.5f) * pitch.WidthM);
        }

        static Vec3 BallVelocityAfter(HighlightData d, int frame)
        {
            int a = Math.Min(frame, d.Frames - 2), b = Math.Min(a + 2, d.Frames - 1);
            return (d.Ball[b] - d.Ball[a]) * (HighlightData.FrameRate / (b - a));
        }
    }
}
