using RoadToPro.Core.Hashing;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>단계 1 (specs/009 FR-20~24): 점유 추적과 팀별 국면 판정.</summary>
    public sealed class PhaseStage : IMatchStage
    {
        readonly PossessionTracker _possession = new PossessionTracker();
        readonly PhaseMachine[] _machine;
        readonly float _flightMinSpeedSq;

        public PhaseStage(ShapeParams p)
        {
            _machine = new[] { new PhaseMachine(p), new PhaseMachine(p) };
            _flightMinSpeedSq = p.FlightMinSpeed * p.FlightMinSpeed;
        }

        public StageId Id => StageId.Phase;

        public void Execute(MatchContext ctx)
        {
            ref var ball = ref ctx.Ball;
            var owner = ball.OwnerId >= 0 ? Squad.TeamOfSlot(ball.OwnerId) : -1;
            var flight = -1;
            if (owner < 0 && ctx.LastKickSlot >= 0 && ctx.LastKickKind != 0)
            {
                var v = ball.Velocity;
                if (v.X * v.X + v.Y * v.Y + v.Z * v.Z > _flightMinSpeedSq) flight = Squad.TeamOfSlot(ctx.LastKickSlot);
            }
            _possession.Update(ctx.Tick, owner, flight);

            var ballN = PitchCoords.ToNormalized(ball.Position.XY, ctx.Pitch);
            var dead = !ctx.Flow.InPlay;
            for (int t = 0; t < 2; t++)
            {
                var side = t == 0 ? TeamSide.Home : TeamSide.Away;
                var b = PitchCoords.ToPerspective(ballN, side, ctx.Flow.Period);
                var input = new PhaseInput
                {
                    Tick = ctx.Tick,
                    Team = t,
                    BallX = b.X,
                    PossessionTeam = _possession.Team,
                    Confirmed = _possession.Confirmed,
                    PossessionChangeTick = _possession.ChangeTick,
                    LooseTicks = _possession.LooseTicks(ctx.Tick),
                    DeadBall = dead,
                    OpponentsBehindBall = OpponentsBehind(ctx, t, ballN.X),
                    LineHeight = ctx.Board.Instructions(t).LineHeight,
                    BlockDrop = ctx.Squad.OnPitchCount(t) < Squad.SlotsPerTeam ? 1 : 0,
                };
                var m = _machine[t];
                if (m.Step(in input))
                {
                    var ev = SimEvent.Make(SimEventKind.PhaseChange, t);
                    ev.A = (int)m.Current;
                    ctx.Emit(ev);
                }
                ctx.Board.SetPhase(t, m.Current, m.Block, m.TransitionTicks(ctx.Tick));
            }
        }

        static int OpponentsBehind(MatchContext ctx, int team, float ballNormX)
        {
            var dir = ctx.AttackDir(team);
            var ballX = ballNormX * ctx.Pitch.LengthM;
            var opp = 1 - team;
            int n = 0;
            for (int s = opp * Squad.SlotsPerTeam; s < (opp + 1) * Squad.SlotsPerTeam; s++)
                if (ctx.IsSlotActive(s) && (ctx.SlotState(s).Position.X - ballX) * dir > 0f) n++;
            return n;
        }

        public void WriteState(StateWriter w) { _possession.Write(w); _machine[0].Write(w); _machine[1].Write(w); }
        public void ReadState(StateReader r) { _possession.Read(r); _machine[0].Read(r); _machine[1].Read(r); }
        public void AddTo(ref StableHasher h) { _possession.AddTo(ref h); _machine[0].AddTo(ref h); _machine[1].AddTo(ref h); }
    }
}
