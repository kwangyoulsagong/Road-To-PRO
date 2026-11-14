using System;
using System.Collections.Generic;
using System.Diagnostics;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/071 성능 워크로드 (AC-10). 시계 API는 코어 밖에만. EditMode와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// 실경기(010 전술 경기) 3,000틱 뒤 월드로 ① xG 평가 1회(콘 블로커·GK 이탈 포함) ② 조준 셀 선택 1회(21셀 × P_onTarget·P_beatGK)
    /// ③ 공중볼 경합 판정 1회(참여 6명: 점프·reach·timing·점수·softmax·승자) (1,000회 묶음 / 1,000)과 ④ 실경기 틱 평균·틱당 할당, ⑤ 010 결정 틱(회귀).
    /// </summary>
    public sealed class ShotAerialBenchWorkloads
    {
        const int Batch = 1000;
        readonly DecisionBenchWorkloads _match;

        public ShotAerialBenchWorkloads(string dataDir) => _match = new DecisionBenchWorkloads(dataDir);

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

        /// <summary>①~③ 개별 평가 (묶음 평균 µs·p95, 묶음 할당 B).</summary>
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
            var goal = w.OppGoal(0);
            var aim = act.Shots.Aim;
            var keeper = act.Shots.Keeper;
            var t = new long[batches];
            long bytes = 0;
            float sink = 0f;

            // ① xG 평가
            for (int k = 0; k < batches; k++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Batch; i++)
                {
                    var from = new Vec2(goal.X - w.AttackDir[0] * (6f + i % 25), 20f + i % 28);
                    var c = ShotContextBuilder.Build(p, w.Pos, w.Active, goal, w.AttackDir[0], 1, from, (i % 5) * 0.2f, i % 7 == 0 ? 1f : 0f);
                    sink += ShotModel.Xg(p, in c);
                }
                t[k] = Stopwatch.GetTimestamp() - t0;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            m["shot_xg_eval_us"] = Avg(t, batches, Batch);
            m["shot_xg_eval_p95_us"] = P95(t, batches, Batch);

            // ② 조준 셀 선택
            for (int k = 0; k < batches; k++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Batch; i++)
                {
                    var from = new Vec2(goal.X - w.AttackDir[0] * (8f + i % 20), 24f + i % 20);
                    sink += aim.Choose(from, goal, 20f + i % 10, 2f + i % 5, -1.5f + (i % 7) * 0.5f, keeper);
                }
                t[k] = Stopwatch.GetTimestamp() - t0;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            m["shot_aim_us"] = Avg(t, batches, Batch);
            m["shot_aim_p95_us"] = P95(t, batches, Batch);

            // ③ 공중볼 경합 판정 (6명)
            var score = new float[AerialSystem.MaxParticipants];
            var prob = new float[AerialSystem.MaxParticipants];
            for (int k = 0; k < batches; k++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < Batch; i++)
                {
                    for (int j = 0; j < AerialSystem.MaxParticipants; j++)
                    {
                        var s = 1 + (i + j * 3) % 20;
                        var jump = AerialModel.JumpHeight(p, w.Jumping[s], w.Vel[s].Length(), w.Stamina[s]);
                        var reach = AerialModel.Reach(p, w.HeightCm[s], jump, false);
                        var timing = AerialModel.Timing(p, 0.1f * j, 0.3f);
                        score[j] = AerialModel.Score(p, reach, timing, w.Strength[s], w.Heading[s], w.Positioning[s]);
                    }
                    AerialModel.Softmax(score, AerialSystem.MaxParticipants, prob);
                    sink += AerialModel.Pick(prob, AerialSystem.MaxParticipants, (i % 100) * 0.01f);
                }
                t[k] = Stopwatch.GetTimestamp() - t0;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            m["aerial_contest_us"] = Avg(t, batches, Batch);
            m["aerial_contest_p95_us"] = P95(t, batches, Batch);
            m["shot_gc_bytes_models"] = bytes;
            m["shot_sink"] = sink > 0f ? 1 : 0;
        }

        /// <summary>④ 실경기 틱 평균·p95와 틱당 할당 (워밍업 3,000틱 뒤 끝까지).</summary>
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
            var a = sim.Context.Actions;
            m["shot_match_tick_avg_us"] = Avg(t, n);
            m["shot_match_tick_p95_us"] = P95(t, n);
            m["shot_match_ticks"] = n;
            m["shot_gc_bytes_per_tick"] = n > 0 ? (double)bytes / n : 0;
            m["shot_planned"] = a.Shots.Planned;
            m["shot_blocks"] = a.Shots.Blocks;
            m["aerial_contests"] = a.Aerials.Contests;
            m["aerial_headers"] = a.Aerials.Headers;
        }
    }
}
