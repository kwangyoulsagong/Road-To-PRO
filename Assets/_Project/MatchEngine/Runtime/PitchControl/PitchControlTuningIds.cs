namespace RoadToPro.MatchEngine.PitchControl
{
    /// <summary>Data/tuning/action.json의 피치 컨트롤 튜닝 id (specs/007 FR-01~07·21). 코드 기본값은 <see cref="PitchControlParams"/>.</summary>
    public static class PitchControlTuningIds
    {
        public const string Prefix = "pcm.";
        public const string Sigma = "pcm.sigma";
        public const string Lambda = "pcm.lambda";
        public const string KappaDef = "pcm.kappaDef";
        public const string GkMul = "pcm.gkMul";
        public const string Dt = "pcm.dt";
        public const string MaxT = "pcm.maxT";
        public const string Tol = "pcm.tol";
        public const string Prune = "pcm.prune";
        public const string AirZ = "pcm.airZ";
        public const string GkHandZ = "pcm.gkHandZ";
        public const string PaperReaction = "pcm.paperReaction";
        public const string PaperVmax = "pcm.paperVmax";
        public const string GridBallSpeed = "pcm.gridBallSpeed";
        public const string Attr = "pcm.attr";
        public const string LambdaDefMin = "pcm.lambdaDefMin";
        public const string LambdaDefMax = "pcm.lambdaDefMax";
        public const string LambdaAttK = "pcm.lambdaAttK";
        public const string LambdaGkK = "pcm.lambdaGkK";
        public const string Horizon = "pcm.horizon";
        public const string GridW = "pcm.gridW";
        public const string GridH = "pcm.gridH";
        public const string RefineK = "pcm.refineK";
        public const string RefineExtra = "pcm.refineExtra";
        public const string MateValue = "pcm.mateValue";
        public const string Legacy = "pcm.legacy";
        public const string TtiStride = "pcm.ttiStride";
        public const string TtiNear = "pcm.ttiNear";
        public const string TrajPrune = "pcm.trajPrune";
    }
}
