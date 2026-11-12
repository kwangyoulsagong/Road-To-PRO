using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Match.Stages.StandIn
{
    /// <summary>
    /// 단계 6 (005 Steering 목표 갱신): 이번 틱에 결정한 선수만 결정 → 조향 명령으로 바꿔 운동 시스템에 넣는다 (반응 지연은 005).
    /// 결정하지 않은 선수는 직전 명령을 유지한다 (FR-05). 직전에 넣은 명령과 같고 그 명령이 대기 중이거나 이미 활성이면
    /// 다시 넣지 않는다 (반응 지연 큐가 같은 명령을 활성화하므로 움직임은 같다). 이 단계만 명령을 넣으므로 대기 중 마지막 명령 = 직전 명령.
    /// </summary>
    public sealed class SimpleSteeringStage : IMatchStage
    {
        readonly SteeringCommand[] _last = new SteeringCommand[Squad.Slots];
        readonly bool[] _has = new bool[Squad.Slots];

        public StageId Id => StageId.SteeringTargets;

        public void Execute(MatchContext ctx)
        {
            var board = ctx.Board;
            var n = board.DecidedCount;
            for (int i = 0; i < n; i++)
            {
                var s = board.DecidedAt(i);
                ref readonly var d = ref board.GetDecision(s);
                SteeringCommand c;
                switch (d.Kind)
                {
                    case DecisionKind.Keep: continue;
                    case DecisionKind.HoldShape:
                        if (!board.HasShapeTarget(s)) continue; // 009 FR-90: 시나리오 배역
                        c = SteeringCommand.Formation(board.ShapeTarget(s), d.MaxSpeed); // 010 FR-70: 강도 속도 상한 (대체 결정은 0 = 최고 속도)
                        break;
                    case DecisionKind.MoveTo: c = SteeringCommand.Arrive(d.Point, d.MaxSpeed); break;
                    case DecisionKind.Chase: c = d.TargetSlot >= 0 ? SteeringCommand.Pursue(d.TargetSlot, d.MaxSpeed) : SteeringCommand.InterceptBall(d.Point); break;
                    case DecisionKind.Carry: c = SteeringCommand.Seek(d.Point, d.MaxSpeed); break;
                    default: c = SteeringCommand.Idle(); break;
                }
                board.SetSteering(s, in c);
                var k = ctx.Kinematics;
                if (_has[s] && Same(in _last[s], in c) && (k.PendingCommands(s) > 0 || Same(in k.State(s).Active, in c))) continue; // 배치(Place)는 큐를 비우고 Idle로 만든다 → 다시 넣음
                k.SetCommand(s, in c);
                _last[s] = c;
                _has[s] = true;
            }
        }

        static bool Same(in SteeringCommand a, in SteeringCommand b) =>
            a.Kind == b.Kind && a.Point.Equals(b.Point) && a.TargetId == b.TargetId && a.MaxSpeed.Equals(b.MaxSpeed) && a.Side.Equals(b.Side)
            && a.HasFormation == b.HasFormation && a.FormationPoint.Equals(b.FormationPoint);

        public void WriteState(StateWriter w)
        {
            for (int s = 0; s < Squad.Slots; s++)
            {
                w.Write(_has[s]);
                if (_has[s]) _last[s].Write(w);
            }
        }

        public void ReadState(StateReader r)
        {
            for (int s = 0; s < Squad.Slots; s++)
            {
                _has[s] = r.ReadBool();
                _last[s] = _has[s] ? SteeringCommand.Read(r) : default;
            }
        }

        public void AddTo(ref StableHasher h)
        {
            for (int s = 0; s < Squad.Slots; s++)
            {
                h.Add(_has[s]);
                if (_has[s]) _last[s].AddTo(ref h);
            }
        }
    }
}
