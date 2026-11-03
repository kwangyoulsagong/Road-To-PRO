using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Match.Flow
{
    /// <summary>교체 요청 (팀 AI·025·테스트가 낸다).</summary>
    public struct SubstitutionRequest
    {
        public int Team;
        public int OutId;
        public int InId;
        public bool Concussion;
        /// <summary>부상 교체: 거부되면 나가는 선수는 교체 없이 빠진다 (인원 감소).</summary>
        public bool InjuryReplacement;
    }

    /// <summary>
    /// 교체 규칙 (specs/006 FR-12·13, AC-06). 요청은 큐에 쌓였다가 데드볼·기간 사이 휴식에서만 실행된다.
    /// 같은 데드볼의 여러 교체 = 창 1개. 하프타임·연장 전·연장 하프타임은 창을 쓰지 않는다.
    /// 연장에 들어가면 +1명·+1창(남은 몫에 더함). 뇌진탕 교체는 한도·창과 별개로 1회. 나간 선수는 다시 못 들어온다.
    /// </summary>
    public sealed class Substitutions
    {
        public const int MaxQueue = 16;

        readonly SubstitutionRequest[] _queue = new SubstitutionRequest[MaxQueue];
        int _count;
        readonly int[] _used = new int[2];
        readonly int[] _windows = new int[2];
        readonly int[] _concussion = new int[2];
        readonly int[] _lastWindowDeadBall = { -1, -1 };
        bool _extraGranted;

        public int Used(int team) => _used[team];
        public int WindowsUsed(int team) => _windows[team];
        public int ConcussionUsed(int team) => _concussion[team];
        public int Queued => _count;

        public int Limit(CompetitionRules r) => r.MaxSubstitutions + (_extraGranted ? r.ExtraTimeSubstitutions : 0);
        public int WindowLimit(CompetitionRules r) => r.MaxWindows + (_extraGranted ? r.ExtraTimeWindows : 0);

        /// <summary>연장 진입: 정규시간 미사용분에 더한다 (리셋 아님).</summary>
        public void GrantExtraTime() => _extraGranted = true;

        public bool CanSubstitute(int team, CompetitionRules r) => _used[team] < Limit(r);

        public void Enqueue(in SubstitutionRequest req)
        {
            if (_count == MaxQueue) throw new System.InvalidOperationException($"교체 요청 대기 {MaxQueue}건 초과");
            _queue[_count++] = req;
        }

        /// <summary>
        /// 대기 요청을 순서대로 처리한다. isBreak = 기간 사이 휴식(창 미소모). deadBallId = 지금 데드볼 번호.
        /// 실행한 교체 수를 돌려준다.
        /// </summary>
        public int Process(MatchContext ctx, bool isBreak, int deadBallId, bool matchOver)
        {
            int done = 0;
            var rules = ctx.Setup.Rules;
            for (int k = 0; k < _count; k++)
            {
                var q = _queue[k];
                var reason = Validate(ctx, in q, isBreak, deadBallId, matchOver, rules);
                if (reason != 0)
                {
                    var e = SimEvent.Make(SimEventKind.SubstitutionRejected, q.Team, q.InId, q.OutId);
                    e.Substep = SimEvent.TickEnd;
                    e.A = reason;
                    e.B = q.Concussion ? EventCodes.SubConcussion : EventCodes.SubNormal;
                    ctx.Emit(e);
                    if (q.InjuryReplacement && ctx.Squad.Has(q.OutId) && ctx.Squad.StatusOf(q.OutId) == PlayerStatus.OnPitch)
                        ctx.Flow.RemoveFromPitch(ctx.Squad.SlotOf(q.OutId), PlayerStatus.InjuredOff);
                    continue;
                }
                if (q.Concussion) _concussion[q.Team]++;
                else
                {
                    _used[q.Team]++;
                    if (!isBreak && _lastWindowDeadBall[q.Team] != deadBallId)
                    {
                        _windows[q.Team]++;
                        _lastWindowDeadBall[q.Team] = deadBallId;
                    }
                }
                var slot = ctx.Squad.SlotOf(q.OutId);
                var st = ctx.Kinematics.State(slot);
                ctx.Squad.Substitute(slot, q.InId, ctx.Tick, ctx.Kinematics.Stats(slot));
                var facing = st.Facing;
                ctx.Kinematics.ResetSlot(slot, ctx.Squad.AttributesOf(q.InId), st.Position, facing);
                var ev = SimEvent.Make(SimEventKind.Substitution, q.Team, q.InId, q.OutId);
                ev.Substep = SimEvent.TickEnd;
                ev.A = _windows[q.Team];
                ev.B = q.Concussion ? EventCodes.SubConcussion : EventCodes.SubNormal;
                ctx.SetPosition(ref ev, st.Position);
                ctx.Emit(ev);
                done++;
            }
            _count = 0;
            return done;
        }

        int Validate(MatchContext ctx, in SubstitutionRequest q, bool isBreak, int deadBallId, bool matchOver, CompetitionRules rules)
        {
            if (matchOver) return EventCodes.RejectMatchOver;
            var sq = ctx.Squad;
            if (!sq.Has(q.OutId) || sq.TeamOf(q.OutId) != q.Team || sq.StatusOf(q.OutId) != PlayerStatus.OnPitch) return EventCodes.RejectNotOnPitch;
            if (!sq.Has(q.InId) || sq.TeamOf(q.InId) != q.Team) return EventCodes.RejectNotOnBench;
            var st = sq.StatusOf(q.InId);
            if (st == PlayerStatus.SubbedOff || st == PlayerStatus.SentOff || st == PlayerStatus.InjuredOff) return EventCodes.RejectReentry;
            if (st != PlayerStatus.Bench) return EventCodes.RejectNotOnBench;
            if (q.Concussion)
                return _concussion[q.Team] >= rules.ConcussionSubstitutions ? EventCodes.RejectConcussionUsed : 0;
            if (_used[q.Team] >= Limit(rules)) return EventCodes.RejectLimit;
            if (!isBreak && _lastWindowDeadBall[q.Team] != deadBallId && _windows[q.Team] >= WindowLimit(rules)) return EventCodes.RejectWindows;
            return 0;
        }

        public void Write(StateWriter w)
        {
            w.Write(_count);
            for (int k = 0; k < _count; k++)
            {
                w.Write(_queue[k].Team); w.Write(_queue[k].OutId); w.Write(_queue[k].InId); w.Write(_queue[k].Concussion); w.Write(_queue[k].InjuryReplacement);
            }
            for (int t = 0; t < 2; t++) { w.Write(_used[t]); w.Write(_windows[t]); w.Write(_concussion[t]); w.Write(_lastWindowDeadBall[t]); }
            w.Write(_extraGranted);
        }

        public void Read(StateReader r)
        {
            _count = r.ReadInt();
            if (_count < 0 || _count > MaxQueue) throw new System.FormatException("교체 대기 수 오류");
            for (int k = 0; k < _count; k++)
                _queue[k] = new SubstitutionRequest { Team = r.ReadInt(), OutId = r.ReadInt(), InId = r.ReadInt(), Concussion = r.ReadBool(), InjuryReplacement = r.ReadBool() };
            for (int t = 0; t < 2; t++) { _used[t] = r.ReadInt(); _windows[t] = r.ReadInt(); _concussion[t] = r.ReadInt(); _lastWindowDeadBall[t] = r.ReadInt(); }
            _extraGranted = r.ReadBool();
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_count);
            for (int k = 0; k < _count; k++) { h.Add(_queue[k].Team); h.Add(_queue[k].OutId); h.Add(_queue[k].InId); h.Add(_queue[k].Concussion); h.Add(_queue[k].InjuryReplacement); }
            for (int t = 0; t < 2; t++) { h.Add(_used[t]); h.Add(_windows[t]); h.Add(_concussion[t]); h.Add(_lastWindowDeadBall[t]); }
            h.Add(_extraGranted);
        }
    }
}
