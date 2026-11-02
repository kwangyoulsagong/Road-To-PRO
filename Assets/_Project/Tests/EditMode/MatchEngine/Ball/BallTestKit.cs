using System.IO;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    /// <summary>테스트 공용: Data/tuning/ball.json을 읽어 파라미터를 만들고, 공을 굴리는 도우미.</summary>
    public static class BallTestKit
    {
        public static string DefinitionsPath => Path.Combine(Directory.GetCurrentDirectory(), "Data", "tuning", "ball.json");

        static TuningRegistry _registry;

        public static TuningRegistry Registry => _registry ??= TuningLoader.LoadDefinitionsFile(DefinitionsPath);

        public static TuningBundle Defaults => TuningBundle.Defaults(Registry);

        public static BallParams Params(BallType type = BallType.Panel32, SurfaceCondition surface = SurfaceCondition.Dry) =>
            BallParams.Create(Defaults, type, surface, PitchSpec.Default);

        /// <summary>값 묶음 JSON으로 일부 값을 바꾼 파라미터.</summary>
        public static BallParams WithValues(string valuesJson, BallType type = BallType.Panel32, SurfaceCondition surface = SurfaceCondition.Dry) =>
            BallParams.Create(TuningLoader.ParseBundle(Registry, valuesJson), type, surface, PitchSpec.Default);

        public static readonly Vec2 Center = new Vec2(52.5f, 34f);

        /// <summary>최대 steps 서브스텝까지 굴린다. 끝 상태를 돌려준다.</summary>
        public static BallState Run(BallState s, BallParams p, int steps, BallEventBuffer events = null, System.Action<BallState, BallEventBuffer> onStep = null)
        {
            events ??= new BallEventBuffer();
            for (int i = 0; i < steps; i++)
            {
                events.Clear();
                BallStepper.Step(ref s, p, events, out _);
                onStep?.Invoke(s, events);
                if (s.Phase == BallPhase.Rest || s.Phase == BallPhase.Controlled) break;
            }
            return s;
        }
    }
}
