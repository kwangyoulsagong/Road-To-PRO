using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.PitchControl;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>물리 xPass 결과 (FR-10): 확률 4종 합 = 1.</summary>
    public struct XPassResult
    {
        public float Receiver, Teammate, Opponent, Out;
    }

    /// <summary>
    /// 물리 기반 xPass (specs/008 FR-10) = 007 궤적 질의 (specs/007 FR-12, T10 교체). 004 공 상태를 <see cref="TrajectorySamples.Step"/>으로 샘플링하고
    /// <see cref="PitchControlQuery.Trajectory"/>로 "수신자 / 다른 동료 / 상대 / 아웃"을 적분한다. 예전 근사(첫 도달 순간 수신 확률로 질량 이동)는 버렸다.
    /// 샘플 간격·상한은 007 `pcm.dt`·`pcm.horizon`(008 `act.xp.sampleDt`·`act.xp.horizon`은 이제 쓰지 않음). RNG 없음, 할당 없음(인스턴스 버퍼 재사용 — 경기당 1개).
    /// 틱 기본 경로에서는 이 함수를 쓰지 않는다(키 모먼트 019·계수 피팅·디버그). 결정 경로의 상위 K 재평가는 <see cref="PassModel.Refine"/>.
    /// </summary>
    public sealed class PhysicsXPass
    {
        readonly TrajectorySamples _tr = new TrajectorySamples();
        readonly PitchControlQuery _q;

        public PhysicsXPass() : this(PitchControlParams.Defaults) { }

        public PhysicsXPass(PitchControlParams pc) => _q = new PitchControlQuery(pc);

        public XPassResult Evaluate(ActionParams p, BallParams bp, ActionWorld w, int passer, int receiver, in BallState start)
        {
            var pc = _q.Params;
            _tr.Step(bp, in start, pc.Dt, pc.Horizon);
            var r = _q.Trajectory(w.ControlPlayersView, _tr, passer, receiver);
            return new XPassResult { Receiver = r.Receiver, Teammate = r.Teammate, Opponent = r.Opponent, Out = r.Out };
        }
    }
}
