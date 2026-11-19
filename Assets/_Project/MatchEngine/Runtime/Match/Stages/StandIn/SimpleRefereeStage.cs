using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Match.Stages.StandIn
{
    /// <summary>
    /// 단계 8 대체 (012 자리, FR-04): 물리 사건(FR-03, 서브스텝 순)으로 골·아웃·재시작을 판정하고, 예정 심판 사건
    /// (<see cref="Input.RefereeScript"/>: 퇴장·부상·페널티·정지 시간·골 취소 — 검증용 주입 훅)을 실행한다.
    /// 팀 인원이 7명 미만이 되면 012 FR-91 이벤트를 내고 경기 중단을 요청한다. 심판 3명의 목표 위치도 정한다.
    /// specs/012 T18: 처리는 <see cref="RefereeCommon"/>으로 옮겼고 동작은 그대로다 — 006 대체 파이프라인(<c>DefaultStages</c>) 계약 테스트용.
    /// 실경기(전술 경기)는 <see cref="RefereeStage"/>.
    /// </summary>
    public sealed class SimpleRefereeStage : StatelessStage
    {
        public override StageId Id => StageId.Referee;

        bool _belowReported;

        public override void Execute(MatchContext ctx)
        {
            RefereeCommon.RunScript(ctx);
            var board = ctx.Board;
            var n = board.PhysicsCallCount;
            for (int i = 0; i < n; i++)
            {
                ref readonly var c = ref board.PhysicsCallAt(i);
                if (c.Kind == PhysicsCallKind.WoodworkHit)
                {
                    RefereeCommon.EmitWoodwork(ctx, in c);
                    continue;
                }
                if (!ctx.Flow.InPlay) continue;
                if (ctx.Flow.PenaltyKickTick >= 0) ctx.Flow.ClearPenalty();
                if (RefereeCommon.IsGoal(in c)) RefereeCommon.EmitGoal(ctx, in c);
                else RefereeCommon.Restart(ctx, RefereeCommon.OutDecision(ctx, in c), c.Substep, false);
                break; // 첫 라인 통과 뒤로는 데드볼
            }
            _belowReported = RefereeCommon.CheckMinimum(ctx, _belowReported);
            SetOfficials(ctx);
        }

        static void SetOfficials(MatchContext ctx)
        {
            var b = ctx.Ball.Position.XY;
            var L = ctx.Pitch.LengthM;
            var W = ctx.Pitch.WidthM;
            var rx = b.X - 8f; if (rx < 2f) rx = b.X + 8f;
            var ry = b.Y - 6f; if (ry < 2f) ry = b.Y + 6f;
            ctx.Officials.SetTarget(0, new Vec2(rx, ry));
            ctx.Officials.SetTarget(1, new Vec2(b.X < L * 0.5f ? L * 0.5f : (b.X > L ? L : b.X), -1.5f));
            ctx.Officials.SetTarget(2, new Vec2(b.X > L * 0.5f ? L * 0.5f : (b.X < 0f ? 0f : b.X), W + 1.5f));
        }

        public override void WriteState(Core.Serialization.StateWriter w) => w.Write(_belowReported);
        public override void ReadState(Core.Serialization.StateReader r) => _belowReported = r.ReadBool();
        public override void AddTo(ref Core.Hashing.StableHasher h) => h.Add(_belowReported);
    }
}
