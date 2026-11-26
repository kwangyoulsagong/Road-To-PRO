using System;
using RoadToPro.Core.Logging;

namespace RoadToPro.Presentation.Stadium.Net
{
    /// <summary>
    /// 골망 표현 튜닝 (`Data/tuning/goalnet.json` + `ball.json`의 그물 깊이·포스트 반지름·공 반지름·그물 테두리 폭, specs/059).
    /// 그물 시뮬(<see cref="NetCloth"/>) 값 포함 (2026-11-26 "ㄱㄱ" 개정). 값이 말이 안 되면 <see cref="Valid"/> = false, 경고 1회 —
    /// 그물은 정지 모양으로만 그린다 (FR-56). 클래스 기본값 = goalnet.json 기본값 (`NetRippleTuningTests.ForGoal_MatchesJsonDefaults`).
    /// </summary>
    public sealed class NetRippleTuning
    {
        public float CellSize = 0.12f, Strand = 0.004f, FadePixels = 2f;
        public float MaxDisp = 1.1f;
        public float SlackBack = 1.15f, SlackSide = 1.10f, Lra = 1.3f, BallReach = 0.18f, Damping = 1.5f, HangBias = 0.5f, RestPull = 2.5f;
        public int Iterations = 4;
        public float BakeSeconds = 5f, SettleSeconds = 3f;
        public float BallRadius = 0.109f;
        public float ContactMargin = 0.02f, ContactMinSpeed = 0.5f;
        public readonly NetShapeParams Shape = new NetShapeParams();
        public bool Valid = true;
        public string Problem;

        /// <summary>`ball.net.depth` 범위 (004 ball.json min·max).</summary>
        public const float DepthMin = 0.5f, DepthMax = 4f;

        /// <param name="net">goalnet.json 값 (id → 값).</param>
        /// <param name="ball">ball.json 값 (id → 값). null이면 기본 깊이·반지름.</param>
        public static NetRippleTuning Create(Func<string, float> net, Func<string, float> ball, ILogger log)
        {
            var t = new NetRippleTuning();
            try
            {
                t.CellSize = net("net.cellSize");
                t.Strand = net("net.strand");
                t.FadePixels = net("net.fadePixels");
                t.Shape.SagTop = net("net.sagTop");
                t.Shape.BulgeBack = net("net.bulgeBack");
                t.Shape.GridCell = net("net.gridCell");
                t.MaxDisp = net("net.maxDisp");
                t.SlackBack = net("net.slackBack");
                t.SlackSide = net("net.slackSide");
                t.Lra = net("net.lra");
                t.BallReach = net("net.ballReach");
                t.Damping = net("net.damping");
                t.HangBias = net("net.hangBias");
                t.RestPull = net("net.restPull");
                t.Iterations = (int)Math.Round(net("net.iterations"));
                t.BakeSeconds = net("net.bakeSeconds");
                t.SettleSeconds = net("net.settleSeconds");
                t.ContactMargin = net("net.contactMargin");
                t.ContactMinSpeed = net("net.contactMinSpeed");
                if (ball != null)
                {
                    t.Shape.Depth = ball("ball.net.depth");
                    t.Shape.PostRadius = ball("ball.goal.postRadius");
                    t.BallRadius = ball("ball.radius");
                    t.Shape.PinBand = ball("ball.net.edgeBand");
                }
            }
            catch (Exception e)
            {
                t.Fail("튜닝 읽기 실패: " + e.Message);
            }
            if (t.Valid) t.Check();
            if (!t.Valid) log?.Log(LogLevel.Warning, "[059 골망] " + t.Problem + " — 정지 모양으로만 그린다");
            return t;
        }

        /// <summary>튜닝 파일을 넘기지 않은 재생: goalnet.json 기본값(이 클래스 기본값과 같다) + 골 상자의 깊이·공 반지름.</summary>
        public static NetRippleTuning ForGoal(in RoadToPro.Presentation.Highlight.Logic.GoalBox goal)
        {
            var t = new NetRippleTuning();
            t.Shape.HalfMouth = goal.HalfMouth;
            t.Shape.Height = goal.Height;
            t.Shape.Depth = goal.NetDepth;
            t.BallRadius = goal.BallRadius;
            t.Check();
            return t;
        }

        void Check()
        {
            if (!(CellSize > 0f) || !(Strand > 0f) || Strand >= CellSize) Fail($"셀 {CellSize} m · 줄 {Strand} m");
            else if (!(Shape.GridCell > 0f)) Fail($"격자 칸 {Shape.GridCell} m");
            else if (!(Shape.Depth >= DepthMin && Shape.Depth <= DepthMax)) Fail($"그물 깊이 {Shape.Depth} m (범위 {DepthMin}~{DepthMax})");
            else if (!(SlackBack >= 1f) || !(SlackSide >= 1f) || !(Lra >= 1f) || !(BallReach > BallRadius) || !(Damping >= 0f) || !(RestPull >= 0f)
                     || Iterations < 1 || !(BakeSeconds > 0f) || !(SettleSeconds >= 0f) || !(MaxDisp > 0f) || !(Shape.PinBand > 0f))
                Fail("그물 시뮬 값이 범위 밖");
        }

        void Fail(string why)
        {
            Valid = false;
            Problem = why;
        }

        public RoadToPro.Presentation.Highlight.Logic.NetContactSettings ContactSettings =>
            new RoadToPro.Presentation.Highlight.Logic.NetContactSettings(ContactMargin, ContactMinSpeed);
    }
}
