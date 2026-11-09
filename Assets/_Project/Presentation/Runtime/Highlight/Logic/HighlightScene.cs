namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>하이라이트 장면 한 개 (specs/041 FR-01). 시각은 시뮬 초. 구간은 018이 정하고 재생기는 그대로 쓴다.</summary>
    public readonly struct HighlightScene
    {
        public readonly int Id;
        public readonly double Start, End;
        public readonly bool IsReplay, IsKeyMoment;
        /// <summary>키 모먼트 장면에서 재생을 멈출 시각. 키 모먼트가 아니면 의미 없음.</summary>
        public readonly double KeyMomentTime;
        public readonly int Priority;

        public HighlightScene(int id, double start, double end, bool isReplay = false, bool isKeyMoment = false,
            double keyMomentTime = 0d, int priority = 0)
        {
            Id = id;
            Start = start;
            End = end;
            IsReplay = isReplay;
            IsKeyMoment = isKeyMoment;
            KeyMomentTime = keyMomentTime;
            Priority = priority;
        }

        public HighlightScene WithRange(double start, double end) =>
            new HighlightScene(Id, start, end, IsReplay, IsKeyMoment, KeyMomentTime, Priority);
    }

    /// <summary>재생 중 시뮬 시각에 맞춰 내보내는 표현 신호 (골망 흔들림·휘슬·관중 반응, FR-55).</summary>
    public readonly struct PlaybackCue
    {
        public readonly double Time;
        public readonly long Seq;
        public readonly int Kind;
        public readonly int Entity;

        public PlaybackCue(double time, long seq, int kind, int entity)
        {
            Time = time;
            Seq = seq;
            Kind = kind;
            Entity = entity;
        }
    }
}
