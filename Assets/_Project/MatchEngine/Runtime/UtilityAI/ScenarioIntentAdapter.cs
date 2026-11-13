using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Scenarios;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 013 개정 전 의도 패킷 대체 (specs/010 FR-91). 시나리오 런타임의 현재 배역 단계(동사 → 고려 항목, 역할 목표 지점, 단계 시작·제한 시간,
    /// 추적 대상)를 읽어 패킷을 만든다. 부수효과 없음(읽기만). 공 소유 배역은 온볼 의도(다음 패스 대상·단계 행동, specs/072 FR-14)를 받는다.
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
                if (!_runtime.TryGetCast(s, out var run, out var role)) continue;
                if (run.State[role] != ScenarioRun.Running) continue;
                // FR-97: 오픈플레이 수비에는 의도가 없다 — 공격 시나리오의 자기 팀 배역만(세트피스는 양 팀 관여 전원)
                if (!run.Def.IsSetPiece && (run.Def.Side != ScenarioSide.Attack || Squad.TeamOfSlot(s) != run.Team)) continue;
                var st = run.CurrentStep(role);
                if (s == owner)
                {
                    if (OwnerKind(st.Verb, st.Class, out var ok)) w.Intent[s] = OwnerPacket(ctx, run, role, st, ok);
                    continue;
                }
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

        IntentPacket OwnerPacket(MatchContext ctx, ScenarioRun run, int role, ScenarioStep st, Consideration kind) => new IntentPacket
        {
            Has = true,
            StepKey = unchecked(((int)(run.StartTick * 977L) ^ (run.Team << 30)) + role * 4096 + run.Step[role]),
            Role = role,
            Kind = kind,
            Point = ctx.FromTeamPerspective(run.Team, run.Target[role]),
            InWindow = ctx.Tick - run.StepStart[role] < _runtime.StepLimitTicks,
            TargetSlot = -1,
            NextPassTarget = st.TargetKind == StepTargetKind.Role && st.TargetRef >= 0 ? run.Slot[st.TargetRef] : -1,
        };

        /// <summary>
        /// 공 소유 배역의 단계 동사 → 온볼 의도 종류 (specs/072 FR-14): 패스류 → Pass(크로스·딜리버 → Cross), 슈팅 → Shot, 돌파 → Dribble(컷인 → CutIn), 캐리 → Dribble, 대기·이동 → HoldBall.
        /// 수비 동사는 의도 없음(false).
        /// </summary>
        public static bool OwnerKind(ScenarioVerb v, VerbClass c, out Consideration kind)
        {
            switch (c)
            {
                case VerbClass.Pass:
                    kind = v == ScenarioVerb.CrossEarly || v == ScenarioVerb.CrossDrivenLow || v == ScenarioVerb.CrossLofted || v == ScenarioVerb.Deliver
                        ? Consideration.Cross : Consideration.Pass;
                    return true;
                case VerbClass.Shoot: kind = Consideration.Shot; return true;
                case VerbClass.TakeOn: kind = v == ScenarioVerb.CutInside ? Consideration.CutIn : Consideration.Dribble; return true;
                case VerbClass.Carry: kind = Consideration.Dribble; return true;
                case VerbClass.Move:
                case VerbClass.Wait: kind = Consideration.HoldBall; return true;
                default: kind = Consideration.HoldShape; return false;
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
