using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Scenarios;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 013 개정 전 의도 패킷 대체 (specs/010 FR-91). 시나리오 런타임의 현재 배역 단계(동사 → 고려 항목, 역할 목표 지점, 단계 시작·제한 시간,
    /// 추적 대상)를 읽어 패킷을 만든다. 부수효과 없음(읽기만). 공 소유자의 의도는 072 몫이라 만들지 않는다(대체 온볼 경로가 시나리오를 직접 실행).
    /// 오픈플레이에서는 공격 시나리오의 자기 팀 배역만 의도를 받는다(FR-97, 상대 배역·수비 시나리오는 판단 AI만). 013이 의도 패킷을 직접 내면 이 어댑터를 바꾸고 AC-37로 재확인한다.
    /// </summary>
    public sealed class ScenarioIntentAdapter : IIntentSource
    {
        readonly ScenarioRuntime _runtime;

        readonly DecisionParams _p;

        public ScenarioIntentAdapter(ScenarioRuntime runtime, DecisionParams p)
        {
            _runtime = runtime;
            _p = p;
        }

        public bool IsCast(int slot) => _runtime.IsCast(slot);

        public bool NotifyOpportunistic(MatchContext ctx, int slot) => _runtime.ReleaseOpportunistic(ctx, slot);

        public void Fill(MatchContext ctx, DecisionWorld w)
        {
            ref readonly var ball = ref ctx.Ball;
            var owner = ball.Phase == BallPhase.Controlled ? ball.OwnerId : -1;
            for (int s = 0; s < DecisionWorld.N; s++)
            {
                w.Intent[s] = IntentPacket.None;
                if (s == owner || !_runtime.TryGetCast(s, out var run, out var role)) continue;
                if (run.State[role] != ScenarioRun.Running) continue;
                // FR-97: 오픈플레이 수비에는 의도가 없다 — 공격 시나리오의 자기 팀 배역만(세트피스는 양 팀 관여 전원)
                if (!run.Def.IsSetPiece && (run.Def.Side != ScenarioSide.Attack || Squad.TeamOfSlot(s) != run.Team)) continue;
                var st = run.CurrentStep(role);
                var kind = KindOf(st.Verb, st.Class, owner >= 0 && Squad.TeamOfSlot(owner) != run.Team);
                var track = run.TrackSlot[role];
                var target = -1;
                var point = ctx.FromTeamPerspective(run.Team, run.Target[role]);
                if (kind == Consideration.Press && (owner < 0 || Squad.TeamOfSlot(owner) == Squad.TeamOfSlot(s))) kind = Consideration.Cover; // 동료 소유·느슨한 공이면 압박 대상 없음 (리뷰 M3)
                if (kind == Consideration.Press) { target = owner; point = ctx.SlotState(owner).Position; }
                else if (track >= 0 && ctx.IsSlotControllable(track)) { target = track; point = ctx.SlotState(track).Position; }
                else if (ScenarioVerbs.IsReceive(st.Verb) && ball.Phase != BallPhase.Controlled) { kind = Consideration.LooseBall; point = ball.Position.XY + ball.Velocity.XY * _p.LooseLookahead; }
                w.Intent[s] = new IntentPacket
                {
                    Has = true,
                    StepKey = unchecked(((int)(run.StartTick * 977L) ^ (run.Team << 30)) + role * 4096 + run.Step[role]), // 팀 비트 포함, 단계 < 4096 (리뷰 L6)
                    Role = role,
                    Kind = kind,
                    Point = point,
                    InWindow = ctx.Tick - run.StepStart[role] < _runtime.StepLimitTicks,
                    TargetSlot = target,
                    NextPassTarget = -1,
                };
            }
        }

        /// <summary>시나리오 동사 → 오프볼 고려 항목 (010 플랜 대응표).</summary>
        public static Consideration KindOf(ScenarioVerb v, VerbClass c, bool opponentOwns)
        {
            switch (v)
            {
                case ScenarioVerb.RunInBehind:
                case ScenarioVerb.Spin:
                case ScenarioVerb.BlindSideRun:
                case ScenarioVerb.ThirdManRun:
                case ScenarioVerb.DiagonalRun:
                case ScenarioVerb.DecoyRun:
                case ScenarioVerb.SprintTo: return Consideration.RunInBehind;
                case ScenarioVerb.Overlap: return Consideration.Overlap;
                case ScenarioVerb.Underlap:
                case ScenarioVerb.UnderlapSupport: return Consideration.Underlap;
                case ScenarioVerb.NearPostRun:
                case ScenarioVerb.FarPostRun:
                case ScenarioVerb.ArriveLate:
                case ScenarioVerb.CrashBox:
                case ScenarioVerb.AttackZone:
                case ScenarioVerb.FlickHeader: return Consideration.BoxCrash;
                case ScenarioVerb.CheckIn:
                case ScenarioVerb.Drop:
                case ScenarioVerb.Knockdown:
                case ScenarioVerb.FlickOn: return Consideration.DropToReceive;
                case ScenarioVerb.HoldWidth:
                case ScenarioVerb.Pin: return Consideration.StayWide;
                case ScenarioVerb.RestDefense: return Consideration.Cover;
                case ScenarioVerb.Engage:
                case ScenarioVerb.CurveRun: return Consideration.Press;
                case ScenarioVerb.MarkLane: return Consideration.PassLaneBlock;
                case ScenarioVerb.TightMark:
                case ScenarioVerb.TrackSt:
                case ScenarioVerb.StandInFront: return Consideration.Mark;
                case ScenarioVerb.CoverShadow: return Consideration.CoverShadow;
                default:
                    if (c == VerbClass.Defend) return opponentOwns ? Consideration.Mark : Consideration.Cover;
                    return Consideration.Support; // CheckOut·RotateTo·Receive·Dummy·Decoy·Screen·패스 대기 등 일반 이동
            }
        }
    }
}
