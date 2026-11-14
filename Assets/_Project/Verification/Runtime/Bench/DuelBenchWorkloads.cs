using System;
using System.Collections.Generic;
using System.Diagnostics;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/070 성능 워크로드 (AC-09). 시계 API는 코어 밖에만. EditMode와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// 실경기(010 전술 경기) 3,000틱 뒤 월드로 ① 테이크온 평가 1회 ② 태클 평가 1회 ③ 이격 탈취 판정(서브스텝당 — 반경 안 상대 찾기 + 닿는 순간 TTI 1회, 매번 닿는 최악 경우)
    /// ④ 이격 중 서브스텝 공 위치·탈취 거리 검사 ⑤ 접촉 기록 1건 (1,000회 묶음 / 1,000)과 ⑥ 실경기 틱 평균·틱당 할당(결투 포함 경로 전체), ⑦ 010 결정 틱(회귀).
    /// </summary>
    public sealed class DuelBenchWorkloads
    {
        const int Batch = 1000;
        readonly DecisionBenchWorkloads _match;

        public DuelBenchWorkloads(string dataDir) => _match = new DecisionBenchWorkloads(dataDir);

        static double Avg(long[] s, int n, double div = 1)
        {
            if (n == 0) return 0;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += s[i];
            return sum / n * 1e6 / Stopwatch.Frequency / div;
        }

        static double P95(long[] s, int n, double div = 1)
        {
            if (n == 0) return 0;
            var c = new long[n];
            Array.Copy(s, c, n);
            Array.Sort(c);
            return c[(int)Math.Min(n - 1, Math.Ceiling(n * 0.95) - 1)] * 1e6 / Stopwatch.Frequency / div;
        }

        public Dictionary<string, double> RunAll()
        {
            var m = new Dictionary<string, double>();
            Models(m);
            Match(m);
            foreach (var kv in _match.RunAll()) m[kv.Key] = kv.Value;
            return m;
        }

        /// <summary>①~⑤ 개별 평가 (묶음 평균 µs·p95, 묶음 할당 B).</summary>
        public void Models(Dictionary<string, double> m, int batches = 400)
        {
            var sim = _match.NewMatch();
            sim.RunUntilTick(3000);
            var ctx = sim.Context;
            var act = ctx.Actions;
            act.InvalidateWorld();
            act.Refresh(ctx);
            var w = act.World;
            var p = act.P;
            var bp = ctx.BallParams;
            var goal = w.OppGoal(0);
            var t = new long[batches];
            long bytes = 0;
            float sink = 0f;

            // ① 테이크온 평가 (각도 우위·커버·정면 자세·확률)
            for (int k = 0; k < batches; k++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Batch; i++)
                {
                    var att = 1 + i % 10;
                    var def = 12 + i % 10;
                    var ang = TakeOnModel.AngleAdv(w.Pos[att], w.Vel[att], w.Pos[def]);
                    var cov = TakeOnModel.Cover(p, w, att, def, goal);
                    var set = TakeOnModel.DefSetFactor(p, w.Vel[def]);
                    sink += TakeOnModel.Probability(p, w, att, def, ang, cov, set);
                }
                t[k] = Stopwatch.GetTimestamp() - t0;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            m["duel_takeon_eval_us"] = Avg(t, batches, Batch);
            m["duel_takeon_eval_p95_us"] = P95(t, batches, Batch);

            // ② 태클 평가 (공 노출·뒤에서·확률)
            for (int k = 0; k < batches; k++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Batch; i++)
                {
                    var tk = 12 + i % 10;
                    var car = 1 + i % 10;
                    var reach = TackleModel.Reach(p, (i & 1) == 0);
                    var exp = TackleModel.BallExposure(p, 0.3f + (i % 5) * 0.4f, (w.Pos[car] - w.Pos[tk]).Length(), reach);
                    var beh = TackleModel.AngleFromBehind(w.Pos[car], w.Vel[car], w.Pos[tk]);
                    sink += TackleModel.WinProbability(p, w, tk, car, exp, beh, (i & 1) == 0);
                }
                t[k] = Stopwatch.GetTimestamp() - t0;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            m["duel_tackle_eval_us"] = Avg(t, batches, Batch);
            m["duel_tackle_eval_p95_us"] = P95(t, batches, Batch);

            // ③ 이격 탈취 판정 (서브스텝당): 공 컨트롤 반경 안 상대 찾기(11명 거리) + 닿으면 소유자 TTI 1회·확률 — 매번 닿는 최악 경우로 잰다
            for (int k = 0; k < batches; k++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Batch; i++)
                {
                    var owner = 1 + i % 10;
                    var ball = w.Pos[owner] + new Vec2(2f, 0.1f * (i % 5));
                    var who = -1;
                    var best = float.MaxValue;
                    for (int o = 11; o < 22; o++)
                    {
                        var d2 = (w.Pos[o] - ball).LengthSq();
                        if (d2 < best) { best = d2; who = o; }
                    }
                    var tOwn = TimeToIntercept.Estimate(in w.Tti[owner], ball.X, ball.Y);
                    sink += DribbleModel.StealProb(p, tOwn, w.Tti[who].Reaction);
                }
                t[k] = Stopwatch.GetTimestamp() - t0;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            m["duel_steal_judge_us"] = Avg(t, batches, Batch);
            m["duel_steal_judge_p95_us"] = P95(t, batches, Batch);

            // ④ 이격 중 서브스텝: 굴림 위치 + 탈취자·소유자 거리 검사
            var table = bp.RollTable;
            var x0 = table.DistanceAtSpeed(9f);
            var s0 = table.StepAtDistance(x0);
            for (int k = 0; k < batches; k++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Batch; i++)
                {
                    var x = table.DistanceAtStep(s0 + i % 200);
                    var sp = table.SpeedAtDistance(x);
                    var pos = w.Pos[5] + new Vec2(x - x0, 0f);
                    sink += (w.Pos[15] - pos).LengthSq() <= p.CtrlRadius * p.CtrlRadius ? sp : 0f;
                    sink += (w.Pos[5] - pos).LengthSq() <= p.KickReach * p.KickReach ? 1f : 0f;
                }
                t[k] = Stopwatch.GetTimestamp() - t0;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            m["duel_gap_substep_us"] = Avg(t, batches, Batch);

            // ⑤ 접촉 기록 1건 (필드 계산 + 상세 기록 추가)
            var log = new ContactLog();
            for (int k = 0; k < batches; k++)
            {
                if (log.Count + Batch > ContactLog.Capacity) log.Clear();
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Batch; i++)
                {
                    var c = ContactBuilder.Make(p, w, (ContactKind)(i % 4), k, i % 10, 1 + i % 10, 12 + i % 10, w.Pos[1 + i % 10], 0.5f, 0.3f, 0f, (i & 1) == 0, 0.3f, 0.5f);
                    log.Add(in c);
                }
                t[k] = Stopwatch.GetTimestamp() - t0;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            m["duel_contact_us"] = Avg(t, batches, Batch);
            m["duel_gc_bytes_models"] = bytes;
            m["duel_sink"] = sink > 0f ? 1 : 0; // 최적화로 지워지지 않게
        }

        /// <summary>⑥ 실경기 틱 평균·p95와 틱당 할당 (워밍업 3,000틱 뒤 끝까지).</summary>
        public void Match(Dictionary<string, double> m, int maxTicks = 54_000)
        {
            var sim = _match.NewMatch();
            sim.RunUntilTick(3000);
            var t = new long[maxTicks];
            var n = 0;
            long bytes = 0;
            while (n < maxTicks && !sim.IsFinished)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                sim.StepTick();
                t[n++] = Stopwatch.GetTimestamp() - t0;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            var d = sim.Context.Actions.Duels;
            m["duel_match_tick_avg_us"] = Avg(t, n);
            m["duel_match_tick_p95_us"] = P95(t, n);
            m["duel_match_ticks"] = n;
            m["duel_gc_bytes_per_tick"] = n > 0 ? (double)bytes / n : 0;
            m["duel_touches"] = d.Touches;
            m["duel_tackles"] = d.TacklesResolved;
            m["duel_contacts"] = d.Contacts;
        }
    }
}
