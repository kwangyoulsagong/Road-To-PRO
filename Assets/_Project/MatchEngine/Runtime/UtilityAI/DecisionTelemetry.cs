using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>후보 하나의 점수 분해 (FR-60).</summary>
    public struct CandidateTrace
    {
        public Consideration Kind;
        public float Gain, Role, Tendency, Habit, Instruction, Tactic, Loss, Noise, Match, Score;
    }

    /// <summary>결정 1회 기록 (FR-60·62): 선택·출처·온도·LOD 주기·상위 후보.</summary>
    public struct DecisionTrace
    {
        public long Tick;
        public int Slot;
        public Consideration Chosen;
        public DecisionSource Source;
        public Intensity Intensity;
        public float Temperature;
        public int LodGap;
        public int TopCount;
        public CandidateTrace Top0, Top1, Top2;

        public CandidateTrace Top(int i) => i == 0 ? Top0 : (i == 1 ? Top1 : Top2);
    }

    /// <summary>
    /// 판단 AI 텔레메트리·진단 (specs/010 FR-60·61·62·99·104). 읽기 전용 — 시뮬 상태·RNG를 바꾸지 않는다(AC-16·19).
    /// 모든 버퍼는 생성 때 할당(틱 경로 할당 0). 결정 기록은 링 버퍼(가득 차면 덮어씀), <see cref="SlotFilter"/>로 한 선수만(내 선수) 기록할 수 있다.
    /// 경기 집계: 선수별 고려 항목 선택 횟수(FR-61), 출처별 결정 수·의도 점유 시간·준수율·기회주의 횟수(FR-99),
    /// 질주 거리(속도 &gt; 7 m/s)·고강도 시간(&gt; 5.5 m/s)·압박 시도·게겐프레싱 참여(FR-104).
    /// </summary>
    public sealed class DecisionTelemetry : IDecisionDiagnostics
    {
        public const float SprintSpeed = 7.0f;
        public const float HighIntensitySpeed = 5.5f;

        readonly DecisionTrace[] _ring;
        readonly int _top;
        int _head, _count;

        public readonly int[,] Attempts = new int[DecisionWorld.N, Considerations.Count];
        public readonly int[,] SourceCount = new int[DecisionWorld.N, 8];
        public readonly long[] IntentTicks = new long[DecisionWorld.N];
        public readonly int[] PressAttempts = new int[DecisionWorld.N];
        public readonly int[] GegenPresses = new int[DecisionWorld.N];
        public readonly int[] SprintDecisions = new int[DecisionWorld.N];
        public readonly int[] OffBallDecisions = new int[DecisionWorld.N];
        public readonly float[] SprintDistance = new float[DecisionWorld.N];
        public readonly float[] HighIntensitySeconds = new float[DecisionWorld.N];
        public readonly float[] Distance = new float[DecisionWorld.N];
        readonly long[] _lastTick = new long[DecisionWorld.N];
        readonly Consideration[] _lastKind = new Consideration[DecisionWorld.N];
        readonly bool[] _lastHas = new bool[DecisionWorld.N];
        // 072 온볼 (FR-40)
        public readonly int[,] OnBallAttempts = new int[DecisionWorld.N, OnBallItems.Count];
        public readonly int[] InstructionIgnored = new int[DecisionWorld.N];
        public readonly int[] OnBallDecisions = new int[DecisionWorld.N];
        /// <summary>마지막 온볼 결정 진단 (040 '의사결정' 레이어: 시야 부채꼴·후보 점수 막대) — <see cref="SlotFilter"/> 선수(−1이면 아무나)의 최신 1건.</summary>
        public readonly OnBallSnapshot LastOnBall = new OnBallSnapshot();
        public long MaxSpaceQueries;
        public long SpaceQueries;
        public long Ticks;

        /// <summary>기록할 선수 (−1 = 전원).</summary>
        public int SlotFilter = -1;

        public DecisionTelemetry(int capacity = 4096, int top = 3)
        {
            _ring = new DecisionTrace[capacity < 1 ? 1 : capacity];
            _top = top < 0 ? 0 : (top > 3 ? 3 : top);
            for (int s = 0; s < DecisionWorld.N; s++) _lastTick[s] = long.MinValue;
        }

        public int Count => _count;

        /// <summary>오래된 순 i번째 기록.</summary>
        public DecisionTrace At(int i) => _ring[(_head - _count + i + _ring.Length * 2) % _ring.Length];

        public void OnDecision(long tick, int slot, in PlayerDecisionState state, OffBallDecider decider, DecisionWorld world)
        {
            SourceCount[slot, (int)state.Source]++;
            var gap = _lastTick[slot] == long.MinValue ? 0 : (int)(tick - _lastTick[slot]);
            _lastTick[slot] = tick;
            if (decider == null) { _lastHas[slot] = false; return; }

            OffBallDecisions[slot]++;
            if (state.Intensity == Intensity.Sprint) SprintDecisions[slot]++;
            var started = !_lastHas[slot] || _lastKind[slot] != state.Kind;
            if (started)
            {
                Attempts[slot, (int)state.Kind]++;
                if (state.Kind == Consideration.Press)
                {
                    PressAttempts[slot]++;
                    if (world.GegenActive[DecisionWorld.TeamOf(slot)]) GegenPresses[slot]++;
                }
            }
            _lastHas[slot] = true;
            _lastKind[slot] = state.Kind;

            if (SlotFilter >= 0 && SlotFilter != slot) return;
            ref var r = ref _ring[_head];
            r.Tick = tick;
            r.Slot = slot;
            r.Chosen = state.Kind;
            r.Source = state.Source;
            r.Intensity = state.Intensity;
            r.Temperature = decider.LastTemperature;
            r.LodGap = gap;
            FillTop(ref r, slot, decider);
            _head = (_head + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;
        }

        void FillTop(ref DecisionTrace r, int slot, OffBallDecider decider)
        {
            var c = decider.Candidates;
            var ch = decider.Chooser;
            int a = -1, b = -1, d = -1;
            for (int i = 0; i < c.Count; i++)
            {
                if (!ch.IsIn(i)) continue;
                var sc = ch.Score(i);
                if (a < 0 || sc > ch.Score(a)) { d = b; b = a; a = i; }
                else if (b < 0 || sc > ch.Score(b)) { d = b; b = i; }
                else if (d < 0 || sc > ch.Score(d)) d = i;
            }
            r.TopCount = 0;
            if (_top > 0 && a >= 0) { r.Top0 = Trace(slot, decider, a); r.TopCount = 1; }
            if (_top > 1 && b >= 0) { r.Top1 = Trace(slot, decider, b); r.TopCount = 2; }
            if (_top > 2 && d >= 0) { r.Top2 = Trace(slot, decider, d); r.TopCount = 3; }
        }

        CandidateTrace Trace(int slot, OffBallDecider decider, int i)
        {
            ref var c = ref decider.Candidates[i];
            decider.Style.Breakdown(slot, c.Kind, out var m);
            return new CandidateTrace
            {
                Kind = c.Kind, Gain = c.Gain, Role = m.Role, Tendency = m.Tendency, Habit = m.Habit, Instruction = m.Instruction,
                Tactic = c.Tactic, Loss = c.Loss, Noise = c.Noise, Match = c.Match, Score = decider.Chooser.Score(i),
            };
        }

        public void OnOnBallDecision(long tick, int slot, in OnBallResult result, OnBallDecider decider, DecisionWorld world)
        {
            OnBallDecisions[slot]++;
            OnBallAttempts[slot, (int)result.Item]++;
            if (result.IgnoredInstruction) InstructionIgnored[slot]++;
            SourceCount[slot, (int)result.Source]++;
            if (SlotFilter >= 0 && SlotFilter != slot) return;
            LastOnBall.Capture(tick, slot, in result, decider, world);
        }

        public void OnTick(long tick, DecisionWorld world, int spaceQueries)
        {
            Ticks++;
            SpaceQueries += spaceQueries;
            if (spaceQueries > MaxSpaceQueries) MaxSpaceQueries = spaceQueries;
            var dt = world.Dt;
            for (int s = 0; s < DecisionWorld.N; s++)
            {
                if (!world.Active[s]) continue;
                var v = world.Vel[s].Length();
                Distance[s] += v * dt;
                if (v > HighIntensitySpeed) HighIntensitySeconds[s] += dt;
                if (v > SprintSpeed) SprintDistance[s] += v * dt;
                if (world.Intent[s].Has) IntentTicks[s]++;
            }
        }

        /// <summary>선수 s의 의도 준수율 (의도가 있던 결정 중 준수 비율, FR-99). 의도 결정이 없으면 NaN.</summary>
        public float ComplianceRate(int s)
        {
            var yes = SourceCount[s, (int)DecisionSource.IntentMatch] + SourceCount[s, (int)DecisionSource.Opportunistic];
            var no = SourceCount[s, (int)DecisionSource.IntentIgnored];
            return yes + no == 0 ? float.NaN : (float)yes / (yes + no);
        }

        public int Opportunistic(int s) => SourceCount[s, (int)DecisionSource.Opportunistic];
    }
}

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 온볼 결정 1건 진단 (specs/072 FR-40, 040 '의사결정' 레이어 입력): 시야 부채꼴(위치·몸 방향·반경·시야각·집합 동료)과 후보 점수 막대(항목·대상·최종 점수·이득·손실·배수).
    /// 버퍼는 생성 때 고정 크기 — 기록에 할당 없음.
    /// </summary>
    public sealed class OnBallSnapshot
    {
        public long Tick = long.MinValue;
        public int Slot = -1;
        public RoadToPro.Core.Math.Vec2 Position, Facing;
        public float Radius, AngleDeg;
        public int VisionCount;
        public readonly int[] VisionSlots = new int[VisionSet.Capacity];
        public int CandidateCount;
        public readonly OnBallItem[] Items = new OnBallItem[UtilityChooser.MaxCandidates];
        public readonly int[] Targets = new int[UtilityChooser.MaxCandidates];
        public readonly float[] Scores = new float[UtilityChooser.MaxCandidates];
        public readonly float[] Gains = new float[UtilityChooser.MaxCandidates];
        public readonly float[] Losses = new float[UtilityChooser.MaxCandidates];
        public readonly float[] Mults = new float[UtilityChooser.MaxCandidates];
        public int Chosen = -1;

        public void Capture(long tick, int slot, in OnBallResult r, OnBallDecider d, DecisionWorld w)
        {
            Tick = tick;
            Slot = slot;
            Position = w.Pos[slot];
            var v = d.Candidates.Vision;
            Facing = v.Facing;
            Radius = v.Radius;
            AngleDeg = v.AngleDeg;
            VisionCount = v.Count;
            for (int i = 0; i < v.Count; i++) VisionSlots[i] = v.Slot(i);
            var c = d.Candidates;
            CandidateCount = c.Count;
            for (int i = 0; i < c.Count; i++)
            {
                Items[i] = c[i].Item;
                Targets[i] = c[i].Target;
                Scores[i] = d.Chooser.IsIn(i) ? d.Chooser.Score(i) : float.NegativeInfinity;
                Gains[i] = c[i].Gain;
                Losses[i] = c[i].Loss;
                Mults[i] = c[i].Mult;
            }
            Chosen = d.LastPick;
        }
    }
}
