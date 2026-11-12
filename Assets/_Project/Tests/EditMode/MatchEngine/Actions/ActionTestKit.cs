using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 테스트 공용: 튜닝·공·운동 파라미터와 고정 상황 월드.</summary>
    public static class ActionTestKit
    {
        static BallParams _ball;
        static KinematicsParams _kin;
        static ActionParams _act;
        static readonly object Gate = new object();

        public static BallParams Ball { get { lock (Gate) return _ball ??= BallParams.Create(MatchTestKit.Tuning().Ball, SurfaceCondition.Dry, PitchSpec.Default); } }
        public static KinematicsParams Kin { get { lock (Gate) return _kin ??= KinematicsParams.Create(MatchTestKit.Tuning().Kinematics, PitchSpec.Default); } }
        public static ActionParams P { get { lock (Gate) return _act ??= ActionParams.Create(MatchTestKit.ActionTuning()); } }

        /// <summary>빈 월드 (홈 +x 공격).</summary>
        public static ActionWorld World() => new ActionWorld();

        public static void Put(ActionWorld w, int slot, Vec2 pos, Vec2 vel = default, int display = 10, int kinDisplay = 10)
            => w.SetPlayer(slot, KinematicAttributes.Uniform(kinDisplay), Kin, pos, vel, TechnicalAttributes.Uniform(display),
                KinematicAttributes.InternalFromDisplay(display), KinematicAttributes.InternalFromDisplay(display));
    }
}
