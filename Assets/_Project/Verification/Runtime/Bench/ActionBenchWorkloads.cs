using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;

namespace RoadToPro.Verification.Bench
{
    /// <summary>
    /// specs/008 성능 워크로드 (AC-14). 시계 API는 코어 밖에만. EditMode와 IL2CPP 벤치가 같은 코드를 쓴다.
    /// ① 로지스틱 xPass 1회 ② 물리 xPass 1회(3 s 궤적) ③ 압박 11×11 ④ 결정 틱당 볼 소유자 평가 합계 — 실경기(010 전술 경기)의 공을 가진 틱마다
    /// 대체 온볼과 같은 평가(동료 ~10명 × 속도 6종 + 스루볼 lead 3)를 월드 갱신 포함으로 잰다(공을 가진 모든 틱 = 상한).
    /// ⑤ 010 경기 틱 워크로드(회귀 참고).
    /// </summary>
    public sealed class ActionBenchWorkloads
    {
        readonly ActionParams _p;
        readonly BallParams _bp;
        readonly KinematicsParams _kp;
        readonly DecisionBenchWorkloads _match;

        public ActionBenchWorkloads(string dataDir)
        {
            string P(string f) => Path.Combine(dataDir, "tuning", f);
            _p = ActionParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("action.json"))));
            _bp = BallParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("ball.json"))), SurfaceCondition.Dry, PitchSpec.Default);
            _kp = KinematicsParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(P("kinematics.json"))), PitchSpec.Default);
            _match = new DecisionBenchWorkloads(dataDir);
        }

        static double P95(long[] s, int n, double div = 1)
        {
            var c = new long[n];
            Array.Copy(s, c, n);
            Array.Sort(c);
            return c[(int)Math.Min(n - 1, Math.Ceiling(n * 0.95) - 1)] * 1e6 / Stopwatch.Frequency / div;
        }

        static double Avg(long[] s, int n, double div = 1)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += s[i];
            return sum / n * 1e6 / Stopwatch.Frequency / div;
        }

        public Dictionary<string, double> RunAll()
        {
            var m = new Dictionary<string, double>();
            Logistic(m);
            Physics(m);
            Pressure(m);
            OwnerTick(m);
            foreach (var kv in _match.RunAll()) m[kv.Key] = kv.Value;
            return m;
        }

        ActionWorld World(SeedRng rng)
        {
            var w = new ActionWorld();
            for (int s = 0; s < ActionWorld.N; s++)
                w.SetPlayer(s, KinematicAttributes.Uniform(10), _kp, new Vec2(rng.Range(10f, 95f), rng.Range(5f, 63f)),
                    new Vec2(rng.Range(-4f, 4f), rng.Range(-4f, 4f)), TechnicalAttributes.Uniform(rng.Range(1, 20)));
            return w;
        }

        /// <summary>로지스틱 xPass 1회 (1,000회 묶음 시간 / 1,000 — 타이머 해상도).</summary>
        public void Logistic(Dictionary<string, double> m, int n = 2000)
        {
            const int batch = 1000;
            var rng = new SeedRng(801);
            var t = new long[n];
            float sink = 0f;
            for (int k = 0; k < n; k++)
            {
                var d = rng.Range(2f, 45f);
                var s = Stopwatch.GetTimestamp();
                for (int i = 0; i < batch; i++) sink += PassModel.XPass(_p, d + i * 0.001f, 0.3f, 0.2f, 0.6f, 0.1f, 0.5f, 0.5f);
                t[k] = Stopwatch.GetTimestamp() - s;
            }
            m["act_xpass_logistic_us"] = Avg(t, n, batch);
            m["act_xpass_logistic_p95_us"] = P95(t, n, batch);
            if (sink < -1f) m["_"] = sink;
        }

        /// <summary>물리 xPass 1회 (3 s 궤적, 22명).</summary>
        public void Physics(Dictionary<string, double> m, int n = 2000)
        {
            var rng = new SeedRng(802);
            var w = World(rng);
            var px = new PhysicsXPass();
            var t = new long[n];
            for (int k = 0; k < n + 100; k++)
            {
                var from = w.Pos[1];
                var dir = (w.Pos[5] - from).Normalized();
                var sp = rng.Range(12f, 22f);
                var b = BallState.RollingAt(from, dir * sp, _bp.Radius);
                var s = Stopwatch.GetTimestamp();
                px.Evaluate(_p, _bp, w, 1, 5, in b);
                if (k >= 100) t[k - 100] = Stopwatch.GetTimestamp() - s;
            }
            m["act_xpass_physics_avg_us"] = Avg(t, n);
            m["act_xpass_physics_p95_us"] = P95(t, n);
        }

        /// <summary>압박 강도 팀 전체 (공격 11 × 수비 11).</summary>
        public void Pressure(Dictionary<string, double> m, int n = 20000)
        {
            var rng = new SeedRng(803);
            var w = World(rng);
            var dp = new Vec2[11];
            var dv = new Vec2[11];
            for (int i = 0; i < 11; i++) { dp[i] = w.Pos[11 + i]; dv[i] = w.Vel[11 + i]; }
            var t = new long[n];
            float sink = 0f;
            for (int k = 0; k < n + 500; k++)
            {
                var s = Stopwatch.GetTimestamp();
                for (int a = 0; a < 11; a++) sink += PressureModel.Team(_p, w.Pos[a], w.Vel[a], dp, dv, 11, 68f);
                if (k >= 500) t[k - 500] = Stopwatch.GetTimestamp() - s;
            }
            m["act_pressure_team_avg_us"] = Avg(t, n);
            m["act_pressure_team_p95_us"] = P95(t, n);
            if (sink < -1f) m["_"] = sink;
        }

        /// <summary>결정 틱당 볼 소유자 평가 합계 (실경기, 공을 가진 인플레이 틱마다).</summary>
        public void OwnerTick(Dictionary<string, double> m, int maxTicks = 54_000)
        {
            var sim = _match.NewMatch();
            var ctx = sim.Context;
            var a = ctx.Actions;
            var t = new long[maxTicks];
            var n = 0;
            for (int tick = 0; tick < maxTicks && !sim.IsFinished; tick++)
            {
                sim.StepTick();
                ref readonly var ball = ref ctx.Ball;
                if (!ctx.Flow.InPlay || ball.Phase != BallPhase.Controlled || ball.OwnerId < 0) continue;
                var owner = ball.OwnerId;
                var team = Squad.TeamOfSlot(owner);
                a.InvalidateWorld();
                var s = Stopwatch.GetTimestamp();
                var press = a.PressureOn(ctx, owner);
                for (int o = team * Squad.SlotsPerTeam; o < (team + 1) * Squad.SlotsPerTeam; o++)
                {
                    if (o == owner || !ctx.IsSlotControllable(o)) continue;
                    a.EvaluatePass(ctx, owner, o, ctx.SlotState(o).Position, press, out _);
                    a.EvaluateThrough(ctx, owner, o, press, out _);
                }
                t[n++] = Stopwatch.GetTimestamp() - s;
            }
            if (n == 0) { m["act_owner_tick_avg_us"] = 0; m["act_owner_tick_p95_us"] = 0; return; }
            m["act_owner_tick_avg_us"] = Avg(t, n);
            m["act_owner_tick_p95_us"] = P95(t, n);
            m["act_owner_ticks"] = n;
        }

        /// <summary>할당 0 확인용 (평가·실행 핫 경로 반복의 할당 바이트).</summary>
        public long GcBytes()
        {
            var rng = new SeedRng(804);
            var w = World(rng);
            var r = new SeedRng(1);
            var dp = new Vec2[11];
            var dv = new Vec2[11];
            PassModel.Best(_p, _bp, w, 1, 2, w.Pos[1], w.Pos[2], 0.2f);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5000; i++)
            {
                PassModel.Best(_p, _bp, w, 1, 2 + i % 9, w.Pos[1], w.Pos[2 + i % 9], 0.2f);
                PressureModel.Team(_p, w.Pos[1], w.Vel[1], dp, dv, 11, 68f);
                PassExecution.Apply(new Vec3(14f, 0f, 0f), 3f, 0.08f, r, out _, out _);
                FirstTouchModel.Sample(_p, 0.5f, 14f, 0.2f, new Vec2(1f, 0f), r);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
