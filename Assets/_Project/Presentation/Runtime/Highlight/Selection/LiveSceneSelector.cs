using System;
using System.Collections.Generic;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Presentation.Highlight.Selection
{
    /// <summary>장면을 만든 사건 종류 (specs/058 FR-40·48).</summary>
    public enum LiveSceneKind : byte
    {
        Filler = 0,
        Goal = 1,
        RedCard = 2,
        Penalty = 3,
        Woodwork = 4,
        Save = 5,
        ShotInBox = 6,
        ShotOutBox = 7,
        Defensive = 8,
        YellowCard = 9,
        Foul = 10,
        SubIn = 11,
        SubOut = 12,
        /// <summary>VAR 판정 번복 (018 FR-01·22): 취소된 골은 골 장면이 아니라 이 장면으로 들어간다.</summary>
        VarOverturn = 13,
    }

    /// <summary>선정된 장면 하나의 연출 정보. <see cref="LiveSceneBriefs"/>가 042 <see cref="SceneBrief"/>로 바꾼다.</summary>
    public sealed class LiveSceneInfo
    {
        public int SceneId;
        public SceneRole Role;
        public LiveSceneKind Kind;
        public long EventTick;
        public double EventTime;
        public int Team = -1;
        /// <summary>득점자·사건 주체 선수 id (없으면 −1).</summary>
        public int PlayerId = -1;
        /// <summary>골 직전 슈팅·마지막 킥 선수 id.</summary>
        public int ShooterId = -1;
        public int AngleIndex, AngleCount;
        public float Score;
        public bool MyPlayerInvolved;
        /// <summary>골 꼬리가 킥오프 대신 기간 종료를 비춘다 (FR-60).</summary>
        public bool EndWide;
        /// <summary>사건 위치 (정규화 x·y).</summary>
        public float EventX, EventY;
        /// <summary>장면을 고른 시뮬 이벤트 종류 (011: 선방 장면이 잡기·쳐내기·펀칭 중 무엇인지).</summary>
        public SimEventKind EventKind;
    }

    /// <summary>선정 결과: 시간 순 장면 목록, 장면별 정보, 공 재배치 시각(FR-47 확인용).</summary>
    public sealed class LiveSceneList
    {
        public HighlightScene[] Scenes = Array.Empty<HighlightScene>();
        public readonly Dictionary<int, LiveSceneInfo> Info = new Dictionary<int, LiveSceneInfo>();
        public double[] Relocations = Array.Empty<double>();
        public DeadBallWindow[] DeadBalls = Array.Empty<DeadBallWindow>();
    }

    /// <summary>
    /// 이벤트 기반 장면 선정 1단계 (specs/058 FR-40~48, 018 대체). 경기 끝에 이벤트 로그를 한 번 훑어 후보·점수를 만들고, 같은 공격 흐름을
    /// 병합하고, 골(양 팀)·내 선수 교체 장면을 반드시 넣은 뒤 점수 순으로 8~15개를 고른다. 골은 메인 + 리플레이 3 + 꼬리 묶음이다.
    /// 같은 점수는 이벤트 틱 → 일련번호 순(FR-45)이라 결정론이다. 순수 로직 (Unity 없음).
    /// </summary>
    public static class LiveSceneSelector
    {
        const double TickSeconds = 0.1d;
        const float BoxDepth = 16.5f, BoxHalfWidth = 20.16f;
        const double PassLookback = 5d, ReplayLead = 2.5d, ReplayTail = 1d, OtherReplayLead = 3d;
        /// <summary>재배치 틱 경계의 부동소수 오차를 피하는 여유 (끝을 재배치 틱 시각보다 반 틱 앞에).</summary>
        const double HalfTick = 0.05d;
        /// <summary>킥오프 와이드는 킥 직전에 끝낸다 (킥 틱의 프레임을 담지 않는다).</summary>
        const double BeforeKick = 0.2d;

        struct Cand
        {
            public long Tick, Seq;
            public LiveSceneKind Kind;
            public SimEventKind EventKind;
            public int Team, Player, Other;
            public float X, Y;
            public float Score;
            /// <summary>VAR 번복 시각 (번복으로 바뀐 골 후보만, 없으면 −1). 장면 끝을 번복 뒤까지 늘린다.</summary>
            public long OverturnTick;
            public bool Must;
        }

        sealed class Cluster
        {
            public long FirstTick, LastTick, Seq;
            public Cand Best;
            public bool HasGoal, Must;
            public long GoalTick = -1;
            public bool Replay2;
            public double Start, End;
        }

        public static LiveSceneList Select(SimEvent[] events, long endTick, PitchSpec pitch, int myPlayerId, SceneScoreTable t)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            t ??= new SceneScoreTable();
            var result = new LiveSceneList();
            double matchEnd = (endTick + 1) * TickSeconds;

            // ── 1회 훑기: 기간·재배치·데드볼 창·후보 ──
            var relocations = new List<double>();
            var halves = new List<double>();
            var deadBalls = new List<DeadBallWindow>();
            var periodStartTick = new long[4];
            var cands = new List<Cand>();
            var goalsByTeam = new int[2];
            bool inGoalDead = false;
            double deadStart = -1;
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var e = ref events[i];
                switch (e.Kind)
                {
                    case SimEventKind.PeriodStart:
                        if (e.Period < 4) periodStartTick[e.Period] = e.Tick;
                        inGoalDead = false;
                        break;
                    case SimEventKind.PeriodEnd:
                        if (e.Tick < endTick) halves.Add((e.Tick + 1) * TickSeconds);
                        break;
                    case SimEventKind.BallRelocated:
                        relocations.Add(e.Tick * TickSeconds);
                        break;
                    case SimEventKind.DeadBallPhase:
                        if (e.A == 1 || e.A == 4) deadStart = Time(e); // 세리머니 · 재시작 접근
                        else if ((e.A == 3 || e.A == 5) && deadStart >= 0) { deadBalls.Add(new DeadBallWindow(deadStart, Time(e) + 1d)); deadStart = -1; }
                        break;
                    case SimEventKind.Kickoff:
                        inGoalDead = false;
                        break;
                    case SimEventKind.VarOverturn:
                        // 018: 취소된 골 → VAR 번복 장면 (필수, 골 순간부터 번복 뒤까지). 점수 보정용 골 수도 되돌린다
                        for (int k = cands.Count - 1; k >= 0; k--)
                        {
                            if (cands[k].Kind != LiveSceneKind.Goal || cands[k].Team != e.Team) continue;
                            var g = cands[k];
                            g.Kind = LiveSceneKind.VarOverturn;
                            g.Score = t.VarOverturn * (g.Score / t.Goal);
                            g.OverturnTick = e.Tick;
                            cands[k] = g;
                            if (e.Team >= 0 && e.Team < 2 && goalsByTeam[e.Team] > 0) goalsByTeam[e.Team]--;
                            break;
                        }
                        inGoalDead = false;
                        break;
                }

                var c = new Cand { EventKind = e.Kind, Tick = e.Tick, Seq = e.Seq, Team = e.Team, Player = e.PlayerId, Other = e.OtherId, X = e.X, Y = e.Y, OverturnTick = -1, Kind = LiveSceneKind.Filler, Score = 0f };
                switch (e.Kind)
                {
                    case SimEventKind.Goal:
                        c.Kind = LiveSceneKind.Goal; c.Score = t.Goal; c.Must = true;
                        break;
                    case SimEventKind.Card:
                        if (e.A == EventCodes.CardRed) { c.Kind = LiveSceneKind.RedCard; c.Score = t.RedCard; }
                        else { c.Kind = LiveSceneKind.YellowCard; c.Score = t.YellowCard; }
                        break;
                    case SimEventKind.Penalty:
                        c.Kind = LiveSceneKind.Penalty; c.Score = t.Penalty;
                        break;
                    case SimEventKind.WoodworkHit:
                        if (!inGoalDead) { c.Kind = LiveSceneKind.Woodwork; c.Score = t.Woodwork; }
                        break;
                    case SimEventKind.Save:
                    case SimEventKind.Catch:
                    case SimEventKind.Parry: // 011 쳐내기
                    case SimEventKind.Punch:
                        c.Kind = LiveSceneKind.Save; c.Score = t.Save;
                        break;
                    case SimEventKind.Shot:
                        if (InBox(e, pitch)) { c.Kind = LiveSceneKind.ShotInBox; c.Score = t.ShotInBox; }
                        else { c.Kind = LiveSceneKind.ShotOutBox; c.Score = t.ShotOutBox; }
                        break;
                    case SimEventKind.Tackle:
                    case SimEventKind.Block:
                    case SimEventKind.Interception:
                        if (e.Team >= 0 && OwnThirdDistance(e, pitch) < pitch.LengthM / 3f) { c.Kind = LiveSceneKind.Defensive; c.Score = t.Defensive; }
                        break;
                    case SimEventKind.Foul:
                        if (NearBox(e, pitch, t.FreeKickZone)) { c.Kind = LiveSceneKind.Foul; c.Score = t.Foul; }
                        break;
                    case SimEventKind.Substitution:
                        if (myPlayerId >= 0 && e.PlayerId == myPlayerId) { c.Kind = LiveSceneKind.SubIn; c.Score = t.Goal; c.Must = true; }
                        else if (myPlayerId >= 0 && e.OtherId == myPlayerId) { c.Kind = LiveSceneKind.SubOut; c.Score = t.Goal; c.Must = true; c.Player = e.OtherId; }
                        break;
                    case SimEventKind.PassReceived:
                    case SimEventKind.Clearance:
                        if (e.Team >= 0 && OwnThirdDistance(e, pitch) > pitch.LengthM * 2f / 3f) { c.Kind = LiveSceneKind.Filler; c.Score = t.Filler; }
                        break;
                }
                if (c.Score > 0f)
                {
                    if (!c.Must) c.Score *= Multiplier(events, i, e, goalsByTeam, periodStartTick, myPlayerId, t);
                    else if (c.Kind == LiveSceneKind.Goal) c.Score *= Multiplier(events, i, e, goalsByTeam, periodStartTick, myPlayerId, t);
                    cands.Add(c);
                }
                if (e.Kind == SimEventKind.Goal)
                {
                    if (e.Team >= 0 && e.Team < 2) goalsByTeam[e.Team]++;
                    inGoalDead = true;
                }
            }
            result.Relocations = relocations.ToArray();
            result.DeadBalls = deadBalls.ToArray();

            // ── 병합 (FR-43): 같은 공격 흐름 = 사건 사이 ≤ After, 골·교체·기간 경계에서 끊는다 ──
            var clusters = new List<Cluster>();
            Cluster cur = null;
            foreach (var c in cands)
            {
                bool standalone = c.Kind == LiveSceneKind.SubIn || c.Kind == LiveSceneKind.SubOut;
                bool join = cur != null && !standalone && !cur.HasGoal
                    && cur.Best.Kind != LiveSceneKind.SubIn && cur.Best.Kind != LiveSceneKind.SubOut
                    && (c.Tick - cur.LastTick) * TickSeconds <= t.After
                    && !CrossesBoundary(cur.LastTick, c.Tick, halves);
                if (!join)
                {
                    cur = new Cluster { FirstTick = c.Tick, LastTick = c.Tick, Seq = c.Seq, Best = c };
                    clusters.Add(cur);
                }
                else
                {
                    cur.LastTick = c.Tick;
                    if (c.Must || (c.Score > cur.Best.Score && !cur.Best.Must)) cur.Best = c; // 필수(골·VAR 번복)가 묶음 대표
                }
                if (c.OverturnTick > cur.LastTick) cur.LastTick = c.OverturnTick;
                if (c.Kind == LiveSceneKind.Goal) { cur.HasGoal = true; cur.GoalTick = c.Tick; }
                if (c.Must) cur.Must = true;
                if (c.Kind == LiveSceneKind.Woodwork || c.Kind == LiveSceneKind.RedCard || c.Kind == LiveSceneKind.Penalty || c.Kind == LiveSceneKind.VarOverturn
                    || (c.Kind == LiveSceneKind.Save && IsInBoxSaveAfterShot(events, c))) cur.Replay2 = true;
            }
            foreach (var cl in clusters) Window(cl, events, relocations, t, matchEnd);

            // ── 선택 (FR-42): 반드시(골·내 선수 교체) → 점수 순, 겹치면 건너뜀 → 하한까지 채움 ──
            var goals = clusters.FindAll(c => c.HasGoal);
            var chosen = new List<Cluster>();
            if (goals.Count > t.SceneMax) chosen.AddRange(goals);
            else
            {
                foreach (var c in clusters) if (c.Must) chosen.Add(c);
                var order = new List<Cluster>(clusters);
                order.Sort((a, b) =>
                {
                    int s = b.Best.Score.CompareTo(a.Best.Score);
                    if (s != 0) return s;
                    s = a.FirstTick.CompareTo(b.FirstTick);
                    return s != 0 ? s : a.Seq.CompareTo(b.Seq);
                });
                foreach (var c in order)
                {
                    if (chosen.Count >= t.SceneMax) break;
                    if (c.Must || c.Best.Kind == LiveSceneKind.Filler) continue;
                    if (Overlaps(c, chosen)) continue;
                    chosen.Add(c);
                }
                if (chosen.Count < t.SceneMin)
                    foreach (var c in order)
                    {
                        if (chosen.Count >= t.SceneMin) break;
                        if (c.Best.Kind != LiveSceneKind.Filler || Overlaps(c, chosen)) continue;
                        chosen.Add(c);
                    }
            }
            chosen.Sort((a, b) => a.FirstTick != b.FirstTick ? a.FirstTick.CompareTo(b.FirstTick) : a.Seq.CompareTo(b.Seq));

            // ── 장면·묶음 만들기 ──
            var scenes = new List<HighlightScene>();
            int id = 1;
            for (int k = 0; k < chosen.Count; k++)
            {
                var c = chosen[k];
                var b = c.Best;
                bool myInv = myPlayerId >= 0 && (b.Player == myPlayerId || b.Other == myPlayerId || MyPassBefore(events, b, myPlayerId));
                if (c.HasGoal)
                {
                    int main = id++;
                    double goalTime = c.GoalTick * TickSeconds;
                    var shooter = ShooterBefore(events, c.GoalTick, b.Team, b.Player);
                    scenes.Add(new HighlightScene(main, c.Start, c.End, groupId: main));
                    result.Info[main] = Info(main, SceneRole.GoalMain, b, goalTime, shooter, 0, 0, myInv);
                    double shotTime = ShotTimeBefore(events, c.GoalTick, b.Team, goalTime);
                    double rs = Math.Max(c.Start, shotTime - ReplayLead), re = Math.Min(goalTime + ReplayTail, c.End);
                    for (int a = 0; a < 3; a++)
                    {
                        int rid = id++;
                        scenes.Add(new HighlightScene(rid, rs, re, isReplay: true, groupId: main));
                        result.Info[rid] = Info(rid, SceneRole.ReplayAngle, b, goalTime, shooter, a, 3, myInv);
                    }
                    var next = k + 1 < chosen.Count ? chosen[k + 1] : null;
                    if (Tail(events, c.GoalTick, halves, matchEnd, relocations, next, t, out var ts, out var te, out var endWide))
                    {
                        int tid = id++;
                        scenes.Add(new HighlightScene(tid, ts, te, groupId: main));
                        var info = Info(tid, SceneRole.GoalTail, b, goalTime, shooter, 0, 0, myInv);
                        info.EndWide = endWide;
                        result.Info[tid] = info;
                    }
                    continue;
                }
                int sid = id++;
                double et = b.Tick * TickSeconds;
                if (c.Replay2)
                {
                    scenes.Add(new HighlightScene(sid, c.Start, c.End, groupId: sid));
                    result.Info[sid] = Info(sid, SceneRole.Live, b, et, b.Player, 0, 0, myInv);
                    double rs = Math.Max(c.Start, et - OtherReplayLead), re = Math.Min(et + ReplayTail, c.End);
                    for (int a = 0; a < 2; a++)
                    {
                        int rid = id++;
                        scenes.Add(new HighlightScene(rid, rs, re, isReplay: true, groupId: sid));
                        result.Info[rid] = Info(rid, SceneRole.ReplayAngle, b, et, b.Player, a, 2, myInv);
                    }
                }
                else
                {
                    scenes.Add(new HighlightScene(sid, c.Start, c.End));
                    result.Info[sid] = Info(sid, SceneRole.Live, b, et, b.Player, 0, 0, myInv);
                }
            }
            var arr = SceneListNormalizer.Normalize(scenes.ToArray(), matchEnd, halves.ToArray());
            // FR-32: 정규화로 짧아진 라이브 장면은 버린다 (묶음 꼬리 포함 — 리플레이는 유지)
            var kept = new List<HighlightScene>(arr.Length);
            foreach (var s in arr)
                if (s.IsReplay || s.End - s.Start >= t.MinShot - 1e-9) kept.Add(s);
                else result.Info.Remove(s.Id);
            result.Scenes = kept.ToArray();
            return result;
        }

        static double Time(in SimEvent e) => ClipConverter.EventTime(e);

        static LiveSceneInfo Info(int id, SceneRole role, in Cand b, double eventTime, int shooter, int angle, int angles, bool my) => new LiveSceneInfo
        {
            SceneId = id, Role = role, Kind = b.Kind, EventTick = b.Tick, EventTime = eventTime, Team = b.Team, PlayerId = b.Player,
            ShooterId = shooter, AngleIndex = angle, AngleCount = angles, Score = b.Score, MyPlayerInvolved = my, EventX = b.X, EventY = b.Y, EventKind = b.EventKind,
        };

        // ── 위치 판정 (이벤트 위치는 정규화 x·y) ──

        static int AttackDir(in SimEvent e) => ((e.Team == 0) ^ ((e.Period & 1) == 1)) ? 1 : -1;

        /// <summary>사건 팀 자기 골라인에서의 x 거리 (m).</summary>
        static float OwnThirdDistance(in SimEvent e, PitchSpec p)
        {
            var x = e.X * p.LengthM;
            return AttackDir(e) > 0 ? x : p.LengthM - x;
        }

        static bool InBox(in SimEvent e, PitchSpec p)
        {
            var x = e.X * p.LengthM;
            var y = e.Y * p.WidthM;
            var toGoal = AttackDir(e) > 0 ? p.LengthM - x : x;
            return toGoal <= BoxDepth && Math.Abs(y - p.WidthM * 0.5f) <= BoxHalfWidth;
        }

        /// <summary>파울 지점이 공격 방향 페널티 박스 끝에서 zone m 안 (FR-40). 파울 이벤트의 팀은 반칙한 팀 → 피해 팀 공격 방향.</summary>
        static bool NearBox(in SimEvent e, PitchSpec p, float zone)
        {
            var x = e.X * p.LengthM;
            var y = e.Y * p.WidthM;
            var victimDir = -AttackDir(e);
            var toGoal = victimDir > 0 ? p.LengthM - x : x;
            return toGoal <= BoxDepth + zone && Math.Abs(y - p.WidthM * 0.5f) <= BoxHalfWidth + zone;
        }

        // ── 보정 (FR-41) ──

        static float Multiplier(SimEvent[] events, int i, in SimEvent e, int[] goals, long[] periodStart, int my, SceneScoreTable t)
        {
            float m = 1f;
            bool late = e.Period >= 2 || (e.Period == 1 && 45d + (e.Tick - periodStart[1]) * TickSeconds / 60d >= 80d);
            if (late) m *= t.MultLate;
            if (Math.Abs(goals[0] - goals[1]) <= 1) m *= t.MultClose;
            if (my >= 0 && (e.PlayerId == my || e.OtherId == my || MyPassBefore(events, i, e, my))) m *= t.MultMyPlayer;
            return m;
        }

        static bool MyPassBefore(SimEvent[] events, int i, in SimEvent e, int my)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                ref readonly var p = ref events[j];
                if ((e.Tick - p.Tick) * TickSeconds > PassLookback) return false;
                if (p.Kind == SimEventKind.Pass || p.Kind == SimEventKind.Cross)
                    return p.Team == e.Team && p.PlayerId == my;
            }
            return false;
        }

        static bool MyPassBefore(SimEvent[] events, in Cand c, int my)
        {
            int i = IndexOfSeq(events, c.Seq);
            return i >= 0 && MyPassBefore(events, i, events[i], my);
        }

        static int IndexOfSeq(SimEvent[] events, long seq)
        {
            int lo = 0, hi = events.Length - 1;
            while (lo <= hi)
            {
                int m = (lo + hi) >> 1;
                if (events[m].Seq == seq) return m;
                if (events[m].Seq < seq) lo = m + 1; else hi = m - 1;
            }
            return -1;
        }

        static bool IsInBoxSaveAfterShot(SimEvent[] events, in Cand c)
        {
            int i = IndexOfSeq(events, c.Seq);
            for (int j = i - 1; j >= 0 && (c.Tick - events[j].Tick) * TickSeconds <= 3d; j--)
                if (events[j].Kind == SimEventKind.Shot) return true; // 유효 슈팅 → 선방 (FR-44 대체: 박스 안 판정은 슈팅 쪽)
            return false;
        }

        static int ShooterBefore(SimEvent[] events, long goalTick, int team, int fallback)
        {
            for (int j = events.Length - 1; j >= 0; j--)
            {
                ref readonly var e = ref events[j];
                if (e.Tick > goalTick) continue;
                if ((goalTick - e.Tick) * TickSeconds > PassLookback) break;
                if ((e.Kind == SimEventKind.Shot || e.Kind == SimEventKind.Pass || e.Kind == SimEventKind.Cross || e.Kind == SimEventKind.Clearance || e.Kind == SimEventKind.Tackle)
                    && e.PlayerId >= 0) return e.PlayerId;
            }
            return fallback;
        }

        static double ShotTimeBefore(SimEvent[] events, long goalTick, int team, double goalTime)
        {
            for (int j = events.Length - 1; j >= 0; j--)
            {
                ref readonly var e = ref events[j];
                if (e.Tick > goalTick) continue;
                if ((goalTick - e.Tick) * TickSeconds > PassLookback) break;
                if (e.Kind == SimEventKind.Shot || e.Kind == SimEventKind.Pass || e.Kind == SimEventKind.Cross || e.Kind == SimEventKind.Clearance)
                    return Time(e);
            }
            return goalTime - 2d;
        }

        // ── 구간 ──

        static bool CrossesBoundary(long a, long b, List<double> halves)
        {
            foreach (var h in halves) if (a * TickSeconds < h && b * TickSeconds >= h) return true;
            return false;
        }

        static bool Overlaps(Cluster c, List<Cluster> chosen)
        {
            foreach (var o in chosen) if (c.Start < o.End && o.Start < c.End) return true;
            return false;
        }

        /// <summary>장면 구간: [첫 사건 −8 s, 마지막 +3 s], 골은 +7 s, 교체는 6 s. 공 재배치 시각은 라이브 장면에 넣지 않는다 (FR-47). 2.5 s 미만은 넓힌다 (FR-32).</summary>
        static void Window(Cluster c, SimEvent[] events, List<double> relocations, SceneScoreTable t, double matchEnd)
        {
            double first = c.FirstTick * TickSeconds, last = c.LastTick * TickSeconds;
            double start, end;
            switch (c.Best.Kind)
            {
                case LiveSceneKind.SubIn:
                    start = first; end = first + t.SubSceneSeconds;
                    break;
                case LiveSceneKind.SubOut:
                    start = first - t.SubSceneSeconds; end = first + 0.5d;
                    var inj = InjuryBefore(events, c.Best);
                    if (inj >= 0) { start = inj - t.SubSceneSeconds * 0.5d; end = inj + t.SubSceneSeconds * 0.5d; } // 부상 교체: 쓰러짐 장면 우선 (027 전 대체)
                    break;
                default:
                    start = first - t.Before;
                    end = c.HasGoal ? c.GoalTick * TickSeconds + t.GoalAfter : last + t.After;
                    break;
            }
            double anchor = c.HasGoal ? c.GoalTick * TickSeconds : first;
            foreach (var r in relocations)
            {
                // 재배치는 틱 r의 끝: 틱 r까지의 프레임만 쓰려면 끝 ≤ r (SceneClipBuilder가 ⌈끝⌉ + 1 틱 앞까지 담는다)
                if (r >= anchor && r < end) end = r - HalfTick;
                else if (r < anchor && r + TickSeconds > start) start = r + 2 * TickSeconds;
            }
            if (end - start < t.MinShot) end = Math.Min(start + t.MinShot, end + t.MinShot);
            foreach (var r in relocations) if (r >= anchor && r - HalfTick < end) end = r - HalfTick;
            c.Start = Math.Max(0d, start);
            c.End = Math.Min(matchEnd, end);
        }

        static double InjuryBefore(SimEvent[] events, in Cand c)
        {
            int i = IndexOfSeq(events, c.Seq);
            for (int j = i - 1; j >= 0 && (c.Tick - events[j].Tick) * TickSeconds <= 120d; j--)
                if (events[j].Kind == SimEventKind.Injury && events[j].PlayerId == c.Player) return Time(events[j]);
            return -1d;
        }

        /// <summary>
        /// 골 꼬리 (FR-21·23·60·62): 준비 단계 시각을 비추는 [준비 + 1 s − 5 s, 준비 + 1 s] — 반응 2.5 s + 킥오프 와이드 2.5 s.
        /// 다음 골이 킥오프 뒤 60 s 안이면 생략, 기간이 먼저 끝나면 종료 와이드.
        /// </summary>
        static bool Tail(SimEvent[] events, long goalTick, List<double> halves, double matchEnd, List<double> relocations, Cluster next, SceneScoreTable t,
            out double start, out double end, out bool endWide)
        {
            start = end = 0; endWide = false;
            double ready = -1, kickoff = -1, periodEnd = -1;
            int gi = -1;
            for (int i = 0; i < events.Length; i++) if (events[i].Kind == SimEventKind.Goal && events[i].Tick == goalTick) { gi = i; break; }
            for (int i = gi + 1; gi >= 0 && i < events.Length; i++)
            {
                ref readonly var e = ref events[i];
                if (e.Kind == SimEventKind.DeadBallPhase && e.A == 3 && ready < 0) ready = Time(e);
                if (e.Kind == SimEventKind.Kickoff) { kickoff = Time(e); break; }
                if (e.Kind == SimEventKind.PeriodEnd) { periodEnd = (e.Tick + 1) * TickSeconds; break; }
                if (e.Kind == SimEventKind.Goal) break;
            }
            double goalAfter = goalTick * TickSeconds + t.GoalAfter;
            if (kickoff >= 0)
            {
                if (next != null && next.HasGoal && (next.GoalTick * TickSeconds - kickoff) <= t.ConsecutiveGoalSeconds) return false; // FR-62
                end = kickoff - BeforeKick;
                start = end - 2 * t.MinShot;
            }
            else if (periodEnd >= 0)
            {
                endWide = true;
                end = periodEnd;
                start = end - 2 * t.MinShot;
            }
            else return false;
            foreach (var r in relocations) if (r >= goalTick * TickSeconds && r + TickSeconds > start && r < end) start = r + 2 * TickSeconds;
            if (start < goalAfter) start = goalAfter;
            if (end - start < t.MinShot) return false;
            if (end > matchEnd) end = matchEnd;
            return end - start >= t.MinShot;
        }
    }
}
