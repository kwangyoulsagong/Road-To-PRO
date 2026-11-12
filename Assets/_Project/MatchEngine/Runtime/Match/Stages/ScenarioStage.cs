using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages.StandIn;
using RoadToPro.MatchEngine.Scenarios;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>
    /// 단계 5 (specs/013): 새 시나리오를 고른 뒤 판단 단계를 돌린다. 판단 단계 = 010 <see cref="UtilityDecisionStage"/>(배역 비소유 선수는 의도 보너스만,
    /// 배역 소유자는 시나리오 실행), 또는 대체 <see cref="SimpleDecisionStage"/>(배역 선수 전원 시나리오 결정).
    /// </summary>
    public sealed class ScenarioStage : IMatchStage, IDecisionSchedulerProvider
    {
        readonly ScenarioRuntime _runtime;
        readonly IMatchStage _fallback;

        public ScenarioStage(ScenarioRuntime runtime) : this(runtime, new SimpleDecisionStage(runtime))
        {
        }

        public ScenarioStage(ScenarioRuntime runtime, IMatchStage decision)
        {
            _runtime = runtime;
            _fallback = decision;
        }

        public StageId Id => StageId.ScenarioOrUtility;
        public ScenarioRuntime Runtime => _runtime;
        /// <summary>판단 단계 (010이면 <see cref="UtilityDecisionStage"/>).</summary>
        public IMatchStage DecisionStage => _fallback;
        public IDecisionScheduler Scheduler => (_fallback as IDecisionSchedulerProvider)?.Scheduler;

        public void Execute(MatchContext ctx)
        {
            _runtime.Select(ctx);
            _fallback.Execute(ctx);
        }

        public void WriteState(StateWriter w)
        {
            _runtime.WriteState(w);
            _fallback.WriteState(w);
        }

        public void ReadState(StateReader r)
        {
            _runtime.ReadState(r);
            _fallback.ReadState(r);
        }

        public void AddTo(ref StableHasher h)
        {
            _runtime.AddTo(ref h);
            _fallback.AddTo(ref h);
        }
    }
}
