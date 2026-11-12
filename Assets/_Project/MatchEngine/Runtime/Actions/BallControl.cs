using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 공 컨트롤 판정 확률 (specs/008 FR-10·45·67) — 물리 xPass(평가)와 실제 수신·인터셉트 판정(실행)이 같은 함수를 쓴다(FR-61 "같은 계수").
    /// 판정은 선수마다 공이 컨트롤 반경 안에서 가장 가까워지는 순간 1회. logit = base − distK·(d/R) − speedK·max(0, v − v_ref)
    /// + touchK·(FirstTouch − 0.5) − pressK·pressure − (상대 패스면 defPenalty) − (높은 공이면 highPenalty) + ln λ_recv.
    /// 필드 선수는 공 높이 > maxZ면 0 (AC-03).
    /// </summary>
    public static class BallControl
    {
        public static float Logit(ActionParams p, float distance, float ballSpeed, float ballZ, float aFirstTouch, float pressure, bool opponentOfKicker)
        {
            var r = DMath.Max(p.CtrlRadius, 1e-3f);
            var l = p.CtrlBase - p.CtrlDistK * (distance / r);
            var over = ballSpeed - p.CtrlSpeedRef;
            if (over > 0f) l -= p.CtrlSpeedK * over;
            l = ActionMath.LogitAdd(l, p.CtrlTouchK, aFirstTouch);
            l -= p.CtrlPressK * pressure;
            if (opponentOfKicker) l -= p.CtrlDefPenalty;
            if (ballZ > p.CtrlHighZ) l -= p.CtrlHighPenalty;
            return l + DMath.Log(PassModel.RecvDecay(p, ballSpeed, aFirstTouch));
        }

        /// <summary>판정 확률. 반경 밖·필드 선수 높이 초과 → 0, 느슨한 공(freeSpeed 미만) → 1.</summary>
        public static float Prob(ActionParams p, float distance, float ballSpeed, float ballZ, float aFirstTouch, float pressure, bool opponentOfKicker, bool keeperHands)
        {
            if (distance > p.CtrlRadius * (keeperHands ? p.CtrlKeeperMul : 1f)) return 0f;
            if (!keeperHands && ballZ > p.CtrlMaxZ) return 0f;
            if (ballSpeed < p.CtrlFreeSpeed) return 1f;
            return ActionMath.Prob(Logit(p, distance, ballSpeed, ballZ, aFirstTouch, pressure, opponentOfKicker), p);
        }
    }
}
