using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.PitchControl;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.PitchControl
{
    /// <summary>specs/007 테스트 공용: 무작위 고정 시드 배치(원 논문·엔진 모드).</summary>
    public static class PitchControlTestKit
    {
        public static PitchControlParams P => PitchControlParams.Defaults;

        static float R(SeedRng rng, float lo, float hi) => lo + (hi - lo) * rng.NextFloat();

        /// <summary>엔진 모드 선수 하나 (표시 능력치 display, 피로 없음).</summary>
        public static void Engine(ControlPlayers pl, int slot, Vec2 pos, Vec2 vel, int display = 12, float tackling = 0.5f, float lambdaOverride = 0f)
        {
            var kin = ActionTestKit.Kin;
            var prof = PlayerMotionProfile.Create(KinematicAttributes.Uniform(display), kin);
            var ctx = new TimeToIntercept.Context(prof, pos, vel, 1f, 0f, kin);
            var keeper = slot % 11 == 0;
            ControlPlayers.Lambdas(P, keeper, 0.5f, tackling, 0.5f, out var la, out var ld);
            if (lambdaOverride > 0f) { la = lambdaOverride; ld = lambdaOverride; }
            pl.SetEngine(slot, slot / 11, keeper, pos, vel, in ctx, la, ld);
        }

        /// <summary>무작위 배치: 팀마다 1~11명(GK 슬롯 포함 여부도 무작위), 속도 ±6 m/s.</summary>
        public static void Random(ControlPlayers pl, SeedRng rng, bool paper)
        {
            pl.Clear();
            pl.PaperArrival = paper;
            for (int team = 0; team < 2; team++)
            {
                var n = 1 + (int)(rng.NextFloat() * 11f);
                if (n > 11) n = 11;
                for (int i = 0; i < n; i++)
                {
                    var slot = team * 11 + (11 - n + i); // 적은 인원이면 앞 슬롯(GK)부터 빠진다
                    var pos = new Vec2(R(rng, 0f, 105f), R(rng, 0f, 68f));
                    var vel = new Vec2(R(rng, -6f, 6f), R(rng, -6f, 6f));
                    if (paper) pl.SetPaper(slot, team, slot % 11 == 0, pos, vel, P);
                    else Engine(pl, slot, pos, vel, 8 + (int)(rng.NextFloat() * 10f), rng.NextFloat());
                }
            }
        }

        public static Vec2 RandomPoint(SeedRng rng, float margin = 0f) => new Vec2(R(rng, -margin, 105f + margin), R(rng, -margin, 68f + margin));
    }
}
