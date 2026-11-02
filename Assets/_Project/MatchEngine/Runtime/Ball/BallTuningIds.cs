namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>공 튜닝 id (값은 Data/tuning/ball.json에만 있다, FR-07).</summary>
    public static class BallTuningIds
    {
        public const string Mass = "ball.mass";
        public const string Radius = "ball.radius";
        public const string AirDensity = "ball.airDensity";
        public const string Gravity = "ball.gravity";
        public const string DefaultType = "ball.defaultType";

        public const string Panel32CdLow = "ball.drag.panel32.cdLow";
        public const string Panel32CdHigh = "ball.drag.panel32.cdHigh";
        public const string Panel32VCrit = "ball.drag.panel32.vCrit";
        public const string Panel32W = "ball.drag.panel32.w";
        public const string ModernCdLow = "ball.drag.modern.cdLow";
        public const string ModernCdHigh = "ball.drag.modern.cdHigh";
        public const string ModernVCrit = "ball.drag.modern.vCrit";
        public const string ModernW = "ball.drag.modern.w";

        public const string ClMax = "ball.magnus.clMax";
        public const string SpScale = "ball.magnus.spScale";
        public const string MagnusMinSpeed = "ball.magnus.minSpeed";
        public const string MagnusFadeWidth = "ball.magnus.fadeWidth";

        public const string SpinDecay = "ball.spin.decay";

        public const string EDry = "ball.bounce.e.dry";
        public const string EWet = "ball.bounce.e.wet";
        public const string EArtificial = "ball.bounce.e.artificial";
        public const string Mu = "ball.bounce.mu";
        public const string GripKeep = "ball.bounce.gripKeep";
        public const string GripSpin = "ball.bounce.gripSpin";
        public const string ToRollVz = "ball.bounce.toRollVz";

        public const string RollA = "ball.roll.a";
        public const string RollB = "ball.roll.b";
        public const string ToRestSpeed = "ball.roll.toRestSpeed";
        public const string WetShortA = "ball.roll.wetShortA";
        public const string FloodedA = "ball.roll.floodedA";
        public const string FloodedB = "ball.roll.floodedB";
        public const string LongGrassA = "ball.roll.longGrassA";

        public const string LimitSpeed = "ball.limit.speed";
        public const string LimitSpin = "ball.limit.spin";

        public const string PostRadius = "ball.goal.postRadius";
        public const string PostRestitution = "ball.goal.postRestitution";
        public const string NetDepth = "ball.net.depth";
        public const string NetDamping = "ball.net.damping";
    }
}
