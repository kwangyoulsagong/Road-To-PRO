using System.IO;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.Verification.Samples.Motion;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>테스트 공용: 실제 튜닝값으로 합성 장면 만들기.</summary>
    internal static class MotionSceneKit
    {
        static MotionSceneBuilder _b;

        internal static string DataDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "tuning"));
        internal static KinematicsParams Kin => KinematicsParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(DataDir, "kinematics.json"))), PitchSpec.Default);
        internal static BallParams Ball => BallParams.Create(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(DataDir, "ball.json"))), SurfaceCondition.Dry, PitchSpec.Default);

        public static MotionSceneBuilder Builder => _b ??= new MotionSceneBuilder(Kin, Ball, AnimTestKit.Lib, PitchSpec.Default);

        public static MotionScene Scene(string id) => Builder.Build(id);
    }
}
