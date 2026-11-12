namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 시나리오 의도 공급 (specs/010 FR-91·94·95). 매 결정 틱 22명의 의도 패킷을 채우고(배역이 아니면 <see cref="IntentPacket.None"/>),
    /// 기회주의 이탈은 같은 틱에 통보받아 그 선수 배역만 해제한다. 붕괴·종료 해제는 공급자가 다음 채우기에서 의도를 없애는 것으로 드러난다(= 보너스 소멸).
    /// </summary>
    public interface IIntentSource
    {
        void Fill(Match.MatchContext ctx, DecisionWorld w);
        /// <summary>기회주의 이탈 통보 (사유 opportunistic). 해제했으면 true.</summary>
        bool NotifyOpportunistic(Match.MatchContext ctx, int slot);
        /// <summary>배역 중인가 (LOD: 배역 선수는 매 틱 결정, FR-40).</summary>
        bool IsCast(int slot);
    }
}
