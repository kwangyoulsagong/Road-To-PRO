using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>단계 2 (specs/009 FR-02·03): 양 팀 프레임을 만들어 <see cref="TeamShapeEngine"/>을 돌리고 결과를 보드에 쓴다.</summary>
    public sealed class ShapeStage : IMatchStage
    {
        readonly TeamShapeEngine[] _engine;
        readonly TeamFrame[] _frame = { new TeamFrame(), new TeamFrame() };
        readonly ShapeInputProviders _in;
        readonly ITeamShapeTelemetry _telemetry;
        readonly IShapeDiagnostics _diagnostics;
        readonly SeedRng _rng;
        readonly int[][] _manMark = new int[2][];
        readonly bool[] _overflow = new bool[2];

        public ShapeStage(ShapeParams p, FormationCatalog catalog, ShapeInputProviders inputs, MatchSetup setup, int home, int away, ITeamShapeTelemetry telemetry, IShapeDiagnostics diagnostics)
        {
            _in = inputs;
            _telemetry = telemetry;
            _diagnostics = diagnostics;
            _engine = new[] { new TeamShapeEngine(p, catalog, inputs, 0, home), new TeamShapeEngine(p, catalog, inputs, 1, away) };
            _manMark[0] = setup.Home.Tactics.ManMarkTargets;
            _manMark[1] = setup.Away.Tactics.ManMarkTargets;
            _rng = RngStreams.Create(setup.Seed, RngStreamId.TeamShape);
        }

        public StageId Id => StageId.Shape;

        /// <summary>마지막 결정 틱의 팀 엔진 (진단·테스트 읽기 전용).</summary>
        public TeamShapeEngine Engine(int team) => _engine[team];

        public void Execute(MatchContext ctx)
        {
            var board = ctx.Board;
            Fill(ctx, 0);
            Fill(ctx, 1);
            _frame[0].OppOffsideLine = DefensiveLine.OffsideLine(_frame[1].Own, _frame[1].OwnActive, _frame[1].Ball.X);
            _frame[1].OppOffsideLine = DefensiveLine.OffsideLine(_frame[0].Own, _frame[0].OwnActive, _frame[0].Ball.X);

            var targets = board.ShapeTargetsForWrite();
            for (int t = 0; t < 2; t++)
            {
                _in.Signals.Read(ctx, t, out var signals);
                var engine = _engine[t];
                engine.SetFormation(board.FormationIndex(t));
                engine.Step(_frame[t], in board.Instructions(t), _manMark[t], in signals, _rng, _telemetry);

                for (int i = 0; i < TeamFrame.N; i++)
                {
                    var s = t * Squad.SlotsPerTeam + i;
                    if (engine.HasTarget(i)) targets[s] = ctx.FromTeamPerspective(t, engine.Target(i));
                    else board.MarkNoShapeTarget(s);
                }
                board.SetShapeOutput(t, in engine.Output,
                    ctx.FromTeamPerspective(t, new Vec2(engine.Output.DefLineX, 0.5f)).X,
                    ctx.FromTeamPerspective(t, new Vec2(engine.Output.OffsideLineX, 0.5f)).X);

                if (engine.CastOverflow && !_overflow[t]) ctx.Emit(SimEvent.Make(SimEventKind.ShapeCastOverflow, t));
                _overflow[t] = engine.CastOverflow;
                _diagnostics?.OnTeam(t, ctx.Tick, engine);
            }
        }

        void Fill(MatchContext ctx, int team)
        {
            var f = _frame[team];
            var side = team == 0 ? TeamSide.Home : TeamSide.Away;
            var period = ctx.Flow.Period;
            var pitch = ctx.Pitch;
            var board = ctx.Board;
            var flip = ctx.AttackDir(team) < 0 ? -1f : 1f;
            var opp = 1 - team;

            f.Tick = ctx.Tick;
            f.Period = period;
            f.Ball = Persp(ctx.Ball.Position.XY, pitch, side, period);
            f.Phase = board.Phase(team);
            f.Block = board.Block(team);
            f.TransitionTicks = board.TransitionTicks(team);
            f.LineSyncAdvance = _in.Cast.IsLineSyncAdvance(team);

            var flow = ctx.Flow;
            f.Restart = flow.Restart;
            f.RestartAttacking = flow.RestartTeam == team;
            f.RestartSpot = Persp(flow.RestartSpot, pitch, side, period);
            f.RestartTaker = flow.RestartTaker >= 0 && Squad.TeamOfSlot(flow.RestartTaker) == team ? flow.RestartTaker % Squad.SlotsPerTeam : -1;

            for (int i = 0; i < TeamFrame.N; i++)
            {
                var s = team * Squad.SlotsPerTeam + i;
                var active = ctx.IsSlotActive(s);
                f.OwnActive[i] = active;
                f.Cast[i] = active && _in.Cast.IsCast(s);
                if (active)
                {
                    ref readonly var st = ref ctx.SlotState(s);
                    f.Own[i] = Persp(st.Position, pitch, side, period);
                    f.OwnVel[i] = st.Velocity * flip;
                    f.Fatigue[i] = st.C;
                }

                var o = opp * Squad.SlotsPerTeam + i;
                var oActive = ctx.IsSlotActive(o);
                f.OppActive[i] = oActive;
                if (oActive)
                {
                    ref readonly var st = ref ctx.SlotState(o);
                    f.Opp[i] = Persp(st.Position, pitch, side, period);
                    f.OppVel[i] = st.Velocity * flip;
                }
            }
        }

        static Vec2 Persp(Vec2 meters, PitchSpec pitch, TeamSide side, int period) =>
            PitchCoords.ToPerspective(PitchCoords.ToNormalized(meters, pitch), side, period);

        public void WriteState(StateWriter w)
        {
            w.Write(_rng.SaveState());
            _engine[0].Write(w);
            _engine[1].Write(w);
            w.Write(_overflow[0]);
            w.Write(_overflow[1]);
        }

        public void ReadState(StateReader r)
        {
            _rng.RestoreState(r.ReadRngState());
            _engine[0].Read(r);
            _engine[1].Read(r);
            _overflow[0] = r.ReadBool();
            _overflow[1] = r.ReadBool();
        }

        public void AddTo(ref StableHasher h)
        {
            var s = _rng.SaveState();
            h.Add(s.S0); h.Add(s.S1); h.Add(s.S2); h.Add(s.S3);
            _engine[0].AddTo(ref h);
            _engine[1].AddTo(ref h);
            h.Add(_overflow[0]);
            h.Add(_overflow[1]);
        }
    }
}
