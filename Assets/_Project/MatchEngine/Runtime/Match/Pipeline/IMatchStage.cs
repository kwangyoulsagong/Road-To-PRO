using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Match.Pipeline
{
    /// <summary>
    /// 파이프라인 단계 (specs/006 FR-01·04). 앞 단계 결과는 <see cref="TickBoard"/>로만 읽는다 (FR-02).
    /// 단계 내부 상태(쿨다운 등)는 스냅샷·해시에 들어가야 한다 (FR-60).
    /// </summary>
    public interface IMatchStage
    {
        StageId Id { get; }
        void Execute(MatchContext ctx);
        void WriteState(StateWriter w);
        void ReadState(StateReader r);
        void AddTo(ref StableHasher h);
    }

    /// <summary>내부 상태가 없는 단계의 기본 구현.</summary>
    public abstract class StatelessStage : IMatchStage
    {
        public abstract StageId Id { get; }
        public abstract void Execute(MatchContext ctx);
        public virtual void WriteState(StateWriter w) { }
        public virtual void ReadState(StateReader r) { }
        public virtual void AddTo(ref StableHasher h) { }
    }

    /// <summary>아직 소유 스펙이 없는 단계 자리 (국면·역할·성향, FR-04).</summary>
    public sealed class NoOpStage : StatelessStage
    {
        readonly StageId _id;
        public NoOpStage(StageId id) => _id = id;
        public override StageId Id => _id;
        public override void Execute(MatchContext ctx) { }
    }

    /// <summary>단계별 시간 측정 훅 (AC-21 단계별 프로파일). null이면 호출 0. 구현은 Verification(시계 API는 코어 밖).</summary>
    public interface IStageTimer
    {
        void Begin(StageId stage);
        void End(StageId stage);
    }
}
