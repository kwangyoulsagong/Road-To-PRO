using RoadToPro.Presentation.Players;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>
    /// 재생 시계 (specs/041 FR-10~14). 실제 시간은 호출자가 넘기므로 시뮬·UI 시계와 분리된다.
    /// 속도를 바꿔도 현재 위치는 그대로라 화면이 튀지 않는다.
    /// </summary>
    public sealed class PlaybackClock : IPlaybackClock
    {
        public double Position { get; private set; }
        public float UserRate { get; private set; } = PlaybackSettings.DefaultRate;
        public bool IsReplay { get; private set; }

        public float EffectiveRate => IsReplay ? PlaybackSettings.ReplayRate : UserRate;
        public float SimTime => (float)Position;

        public void BeginScene(double start, bool isReplay, float userRate)
        {
            Position = start;
            IsReplay = isReplay;
            UserRate = userRate;
        }

        public void SetUserRate(float rate) => UserRate = rate;

        /// <summary>실제 시간 dt 동안 진행했을 때의 위치 (상태는 바꾸지 않는다).</summary>
        public double Peek(double realDt) => Position + realDt * EffectiveRate;

        public void JumpTo(double position) => Position = position;

        /// <summary>실제 시간 dt만큼 진행한다. limit을 넘지 않는다.</summary>
        public void Advance(double realDt, double limit)
        {
            var next = Peek(realDt);
            Position = next < limit ? next : limit;
        }
    }
}
