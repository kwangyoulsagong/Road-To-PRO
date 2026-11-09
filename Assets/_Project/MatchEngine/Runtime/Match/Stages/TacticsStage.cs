using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>단계 0 (specs/009 FR-70·71): 팀 지시를 정리하고 전술 변경 입력을 반영해 보드에 쓴다.</summary>
    public sealed class TacticsStage : IMatchStage
    {
        readonly TeamInstructions[] _ins = new TeamInstructions[2];
        readonly int[] _formation = new int[2];
        readonly int _catalogCount;
        bool _sanitized;

        public TacticsStage(MatchSetup setup, int catalogCount, int home, int away)
        {
            _ins[0] = setup.Home.Tactics.Instructions;
            _ins[1] = setup.Away.Tactics.Instructions;
            _formation[0] = home;
            _formation[1] = away;
            _catalogCount = catalogCount;
        }

        public StageId Id => StageId.TeamTactics;

        public void Execute(MatchContext ctx)
        {
            if (!_sanitized)
            {
                _sanitized = true;
                for (int t = 0; t < 2; t++) Warn(ctx, t, _ins[t].Sanitize());
            }

            var inputs = ctx.Inputs;
            for (int i = inputs.FirstAtOrAfter(ctx.Tick); i < inputs.Count && inputs[i].Tick == ctx.Tick; i++)
            {
                var e = inputs[i];
                if (e.Kind == InputKind.TeamInstruction) ApplyInstruction(ctx, e.PlayerId / 100, (InstructionId)(e.PlayerId % 100), e.Value);
                else if (e.Kind == InputKind.TeamFormation) ApplyFormation(ctx, e.PlayerId, e.Value >= 0f && e.Value < _catalogCount ? (int)e.Value : -1);
            }

            for (int t = 0; t < 2; t++)
            {
                ctx.Board.SetInstructions(t, in _ins[t]);
                ctx.Board.SetFormationIndex(t, _formation[t]);
            }
        }

        void ApplyInstruction(MatchContext ctx, int team, InstructionId id, float value)
        {
            if (team < 0 || team > 1 || (int)id >= TeamInstructions.Count) return;
            _ins[team].Set(id, value);
            Warn(ctx, team, _ins[team].Sanitize());
            var ev = SimEvent.Make(SimEventKind.TacticsChanged, team);
            ev.A = (int)id;
            ev.F0 = _ins[team].Get(id);
            ctx.Emit(ev);
        }

        void ApplyFormation(MatchContext ctx, int team, int index)
        {
            if (team < 0 || team > 1) return;
            if (index < 0 || index >= _catalogCount)
            {
                var w = SimEvent.Make(SimEventKind.TacticsInputClamped, team);
                w.A = -1;
                ctx.Emit(w);
                return;
            }
            _formation[team] = index;
            var ev = SimEvent.Make(SimEventKind.TacticsChanged, team);
            ev.A = -1;
            ev.F0 = index;
            ctx.Emit(ev);
        }

        static void Warn(MatchContext ctx, int team, int mask)
        {
            for (int b = 0; mask != 0 && b < TeamInstructions.Count; b++)
            {
                if ((mask & (1 << b)) == 0) continue;
                mask &= ~(1 << b);
                var ev = SimEvent.Make(SimEventKind.TacticsInputClamped, team);
                ev.A = b;
                ctx.Emit(ev);
            }
        }

        public void WriteState(StateWriter w)
        {
            w.Write(_sanitized);
            for (int t = 0; t < 2; t++)
            {
                w.Write(_formation[t]);
                for (int i = 0; i < TeamInstructions.Count; i++) w.Write(_ins[t].Get((InstructionId)i));
            }
        }

        public void ReadState(StateReader r)
        {
            _sanitized = r.ReadBool();
            for (int t = 0; t < 2; t++)
            {
                _formation[t] = r.ReadInt();
                for (int i = 0; i < TeamInstructions.Count; i++) _ins[t].Set((InstructionId)i, r.ReadFloat());
            }
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_sanitized);
            for (int t = 0; t < 2; t++)
            {
                h.Add(_formation[t]);
                for (int i = 0; i < TeamInstructions.Count; i++) h.Add(_ins[t].Get((InstructionId)i));
            }
        }
    }
}
