using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 받는 지점 (specs/008 T14a): 나에게 오는 패스는 공 진행 직선 위 내 위치의 최근접점(최소 minAhead초 앞)으로 간다.
    /// 실행 오차(FR-13)로 공이 조준점에서 벗어나도 수신자가 경로에 맞춘다. 순수 함수 — 010 오프볼 LooseBall·대체 온볼 추격이 같이 쓴다.
    /// </summary>
    public static class ReceivePoint
    {
        public static Vec2 OnPath(Vec2 ball, Vec2 ballVel, Vec2 receiver, float minAhead)
        {
            var v2 = ballVel.LengthSq();
            if (v2 < 1e-4f) return ball;
            var t = Vec2.Dot(receiver - ball, ballVel) / v2;
            if (t < minAhead) t = minAhead;
            return ball + ballVel * t;
        }
    }
}
