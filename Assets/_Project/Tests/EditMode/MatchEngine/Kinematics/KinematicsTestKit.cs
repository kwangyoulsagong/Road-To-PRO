using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>테스트 공용: Data/tuning/kinematics.json 로드, 단일 선수 시스템, 표준 활동 프로필 (plan [제안] ⑥).</summary>
    public static class KinematicsTestKit
    {
        public static string DefinitionsPath => Path.Combine(Directory.GetCurrentDirectory(), "Data", "tuning", "kinematics.json");

        static TuningRegistry _registry;
        static KinematicsParams _params;

        public static TuningRegistry Registry => _registry ??= TuningLoader.LoadDefinitionsFile(DefinitionsPath);
        public static KinematicsParams Params => _params ??= KinematicsParams.Create(TuningBundle.Defaults(Registry), PitchSpec.Default);

        public static KinematicsParams WithValues(string valuesJson) =>
            KinematicsParams.Create(TuningLoader.ParseBundle(Registry, valuesJson), PitchSpec.Default);

        public static float Internal(int display) => KinematicAttributes.InternalFromDisplay(display);

        /// <summary>스피드·가속은 speedAbility, 지구력은 stamina, 나머지는 10.</summary>
        public static KinematicAttributes Attr(int speedAbility = 15, int stamina = 10, int agility = 10) =>
            new KinematicAttributes(Internal(speedAbility), Internal(speedAbility), Internal(agility), Internal(10), Internal(stamina), Internal(10), Internal(10));

        public static KinematicsSystem One(in KinematicAttributes a, Vec2 pos, Vec2 vel = default, Vec2 facing = default, KinematicsParams p = null) =>
            new KinematicsSystem(p ?? Params, new[] { new PlayerSetup(1, a, pos, vel, facing) });

        public static void Run(KinematicsSystem k, int steps)
        {
            for (int i = 0; i < steps; i++) k.Step();
        }

        /// <summary>o에서 d 방향 반직선이 피치 경계(+여유)와 만나는 점 (Seek 목표가 경계 클램프로 꺾이지 않게).</summary>
        public static Vec2 Ray(Vec2 o, Vec2 d, KinematicsParams p)
        {
            float t = 1e9f;
            if (d.X > 1e-6f) t = DMath.Min(t, (p.MaxX - o.X) / d.X);
            if (d.X < -1e-6f) t = DMath.Min(t, (p.MinX - o.X) / d.X);
            if (d.Y > 1e-6f) t = DMath.Min(t, (p.MaxY - o.Y) / d.Y);
            if (d.Y < -1e-6f) t = DMath.Min(t, (p.MinY - o.Y) / d.Y);
            return o + d * t;
        }

        /// <summary>
        /// AC-10 표준 활동 프로필 (60 s 주기 × 90): 걷기 1.1 m/s 38 s · 조깅 2.6 m/s 15 s · 러닝 4.5 m/s 4.5 s ·
        /// 고강도 6.0 m/s 1.8 s · 스프린트(최고속 목표) 0.7 s. x = 10~95 m 왕복.
        /// </summary>
        public static KinematicsSystem StandardProfile90(int stamina, int minutes = 90)
        {
            var a = new KinematicAttributes(Internal(14), Internal(14), Internal(10), Internal(10), Internal(stamina), Internal(10), Internal(10));
            var k = One(a, new Vec2(10f, 34f), default, new Vec2(1f, 0f));
            float dir = 1f;
            float[] speed = { 1.1f, 2.6f, 4.5f, 6.0f, 99f };
            int[] steps = { 3800, 1500, 450, 180, 70 };
            for (int m = 0; m < minutes; m++)
            for (int ph = 0; ph < speed.Length; ph++)
            for (int i = 0; i < steps[ph]; i++)
            {
                var x = k.State(0).Position.X;
                if (x > 95f) dir = -1f;
                if (x < 10f) dir = 1f;
                if (i % 10 == 0) k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(dir > 0f ? 104f : 1f, 34f), speed[ph]));
                k.Step();
            }
            return k;
        }

        /// <summary>합성 경기용 무작위 22명 (id 1~22, 고정 시드).</summary>
        public static PlayerSetup[] RandomSquad(SeedRng rng, int n = 22)
        {
            var ps = new PlayerSetup[n];
            for (int i = 0; i < n; i++)
            {
                var a = new KinematicAttributes(rng.Range(0f, 100f), rng.Range(0f, 100f), rng.Range(0f, 100f), rng.Range(0f, 100f),
                    rng.Range(0f, 100f), rng.Range(0f, 100f), rng.Range(0f, 100f));
                ps[i] = new PlayerSetup(i + 1, a, new Vec2(rng.Range(5f, 100f), rng.Range(5f, 63f)));
            }
            return ps;
        }

        /// <summary>
        /// 합성 경기 결정 틱 명령 (plan [제안] ⑤): Arrive(주변 15 m) 55% · Seek 10% · Pursue 10% · 저킹 8% · 마킹 8% · Evade 4% ·
        /// 대형 붙은 Arrive 5%. 가끔 무작위 접촉.
        /// </summary>
        public static void RandomCommands(KinematicsSystem k, SeedRng rng, bool immediate = false)
        {
            var n = k.Count;
            for (int i = 0; i < n; i++)
            {
                var s = k.State(i);
                var id = s.Id;
                var other = k.State((i + 1 + (int)(rng.NextFloat() * (n - 1))) % n).Id;
                var near = new Vec2(s.Position.X + rng.Range(-15f, 15f), s.Position.Y + rng.Range(-15f, 15f));
                var r = rng.NextFloat();
                SteeringCommand c;
                if (r < 0.55f) c = SteeringCommand.Arrive(near, rng.Bernoulli(0.5f) ? 0f : rng.Range(1f, 6f));
                else if (r < 0.65f) c = SteeringCommand.Seek(near, rng.Range(2f, 10f));
                else if (r < 0.75f) c = SteeringCommand.Pursue(other);
                else if (r < 0.83f) c = SteeringCommand.Jockey(other, new Vec2(rng.Bernoulli(0.5f) ? 0f : 105f, 34f), rng.Bernoulli(0.5f) ? 1f : -1f);
                else if (r < 0.91f) c = SteeringCommand.Mark(other, new Vec2(rng.Bernoulli(0.5f) ? 0f : 105f, 34f));
                else if (r < 0.95f) c = SteeringCommand.Evade(other);
                else c = SteeringCommand.Arrive(near).WithFormation(new Vec2(rng.Range(10f, 95f), rng.Range(10f, 58f)));
                if (immediate) k.SetCommandImmediate(id, c); else k.SetCommand(id, c);
                if (rng.Bernoulli(0.01f)) k.ApplyContact(id);
            }
        }
    }
}
