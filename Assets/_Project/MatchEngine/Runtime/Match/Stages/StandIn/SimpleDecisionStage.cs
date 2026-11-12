using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Match.Stages.StandIn
{
    /// <summary>시나리오 배역 선수의 결정을 대신 낸다 (specs/013). 없으면 폴백 판단 그대로다.</summary>
    public interface IDecisionOverride
    {
        bool IsCast(int slot);
        bool TryDecide(MatchContext ctx, int slot, ref bool hasAction, out Decision decision);
        /// <summary>배역 선수의 의도 지점만 읽는다 — RNG·공 동작·런 상태 등 부수효과 없음 (specs/011 FR-94, GK 의도).</summary>
        bool TryReadIntent(MatchContext ctx, int slot, out Vec2 point);
    }

    /// <summary>
    /// 단계 5 대체 (013 시나리오 · 010 Utility 자리, FR-04): "22명 위치 + 공 + 단순 패스/슈팅"(기획서 § 9 M0).
    /// 결정 대상(<see cref="MatchContext.DecisionList"/>, 오름차순)만 새로 정하고 나머지는 직전 의도를 유지한다 (FR-05).
    /// 공 소유자는 보유 시간이 지나면 사거리 안에서 확률로 슈팅, 아니면 앞쪽 동료에게 땅볼 패스(004 역산), 없으면 드리블.
    /// 소유 팀 상대의 가장 가까운 선수는 압박·태클, 공이 느슨하면 팀마다 가장 가까운 선수가 쫓는다. 나머지는 대형.
    /// 외부 입력(FR-62)은 그 틱에 대상 선수를 결정 대상으로 넣어 적용한다. 공 동작은 틱당 하나(먼저 정한 것).
    /// 결정 코드는 <see cref="StandInOnBall"/> (specs/010 T11에서 추출, 동작 동일).
    /// </summary>
    public sealed class SimpleDecisionStage : IMatchStage
    {
        public StageId Id => StageId.ScenarioOrUtility;

        readonly IDecisionOverride _override;
        readonly StandInOnBall _x;
        readonly Decision[] _intent = new Decision[Squad.Slots];

        public SimpleDecisionStage(IDecisionOverride decisionOverride = null)
        {
            _override = decisionOverride;
            _x = new StandInOnBall(decisionOverride);
        }

        public void Execute(MatchContext ctx)
        {
            var board = ctx.Board;
            _x.BeginTick();
            var n = _x.MergeInputs(ctx);
            _x.ComputeChasers(ctx);
            var hasAction = false;
            for (int i = 0; i < n; i++)
            {
                var s = _x.ListAt(i);
                if (!ctx.IsSlotControllable(s)) continue; // 058: 걷는 선수는 흐름이 직접 조향
                if (ctx.Keepers.IsBusySlot(s)) // 011 FR-17: 다이빙·일어나기 중 행동 불가
                {
                    _intent[s] = new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
                    board.MarkDecided(s);
                    continue;
                }
                Decision d;
                if (_x.KeeperDecides(ctx, s)) d = _x.DecideKeeper(ctx, s, ref hasAction);
                else if (_override == null || _x.InputOf(s) != default || ctx.Flow.DeadBall.Pending || !_override.TryDecide(ctx, s, ref hasAction, out d))
                    d = _x.Decide(ctx, s, ref hasAction);
                _intent[s] = d;
                board.MarkDecided(s);
            }
            for (int s = 0; s < Squad.Slots; s++) board.SetDecision(s, in _intent[s]);
        }

        public void WriteState(StateWriter w)
        {
            w.Write(_x.RestartKickTick);
            _x.WritePass(w);
            for (int s = 0; s < Squad.Slots; s++)
            {
                w.Write((byte)_intent[s].Kind); w.Write(_intent[s].Point); w.Write(_intent[s].TargetSlot); w.Write(_intent[s].MaxSpeed);
            }
        }

        public void ReadState(StateReader r)
        {
            _x.RestartKickTick = r.ReadLong();
            _x.ReadPass(r);
            for (int s = 0; s < Squad.Slots; s++)
                _intent[s] = new Decision { Kind = (DecisionKind)r.ReadByte(), Point = r.ReadVec2(), TargetSlot = r.ReadInt(), MaxSpeed = r.ReadFloat() };
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_x.RestartKickTick);
            _x.AddPassTo(ref h);
            for (int s = 0; s < Squad.Slots; s++)
            {
                h.Add((byte)_intent[s].Kind); h.Add(_intent[s].Point.X); h.Add(_intent[s].Point.Y); h.Add(_intent[s].TargetSlot); h.Add(_intent[s].MaxSpeed);
            }
        }
    }
}
