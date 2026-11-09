using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages.StandIn;
using RoadToPro.MatchEngine.Scenarios;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>단계 5 (specs/013): 새 시나리오를 고른 뒤 폴백 판단을 돌린다. 배역 선수는 폴백 안에서 시나리오 결정으로 덮인다.</summary>
    public sealed class ScenarioStage : IMatchStage
    {
        readonly ScenarioRuntime _runtime;
        readonly SimpleDecisionStage _fallback;

        public ScenarioStage(ScenarioRuntime runtime)
        {
            _runtime = runtime;
            _fallback = new SimpleDecisionStage(runtime);
        }

        public StageId Id => StageId.ScenarioOrUtility;
        public ScenarioRuntime Runtime => _runtime;

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
