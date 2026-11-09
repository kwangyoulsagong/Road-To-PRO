using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Scenarios;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>
    /// 단계 2 감싸개 (specs/013 플랜): 시나리오 수명 주기를 먼저 돌려, 끝난 배역이 같은 틱에 형태 목표를 받게 한다 (FR-33, AC-08).
    /// 런타임 상태는 <see cref="ScenarioStage"/>가 직렬화한다.
    /// </summary>
    public sealed class ScenarioShapeStage : IMatchStage
    {
        readonly ShapeStage _shape;
        readonly ScenarioRuntime _runtime;

        public ScenarioShapeStage(ShapeStage shape, ScenarioRuntime runtime)
        {
            _shape = shape;
            _runtime = runtime;
        }

        public StageId Id => StageId.Shape;
        public ShapeStage Inner => _shape;

        public void Execute(MatchContext ctx)
        {
            _runtime.Advance(ctx);
            _shape.Execute(ctx);
        }

        public void WriteState(StateWriter w) => _shape.WriteState(w);
        public void ReadState(StateReader r) => _shape.ReadState(r);
        public void AddTo(ref StableHasher h) => _shape.AddTo(ref h);
    }
}
