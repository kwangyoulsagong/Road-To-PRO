using RoadToPro.Core.Tuning;

namespace RoadToPro.Presentation.Highlight.Selection
{
    /// <summary>
    /// 장면 선정 1단계 값 (specs/058 FR-40~48, Data/tuning/highlight.json). 018 본 구현(xG·xT·드라마 점수)이 오면 교체된다.
    /// 기본값은 파일 기본값과 같다 (테스트가 확인).
    /// </summary>
    public sealed class SceneScoreTable
    {
        public float Goal = 100f, RedCard = 80f, Penalty = 75f, VarOverturn = 70f, Woodwork = 45f, Save = 40f;
        public float ShotInBox = 35f, ShotOutBox = 20f, Defensive = 25f, YellowCard = 15f, Foul = 15f;
        /// <summary>specs/070 테이크온 성공 장면 [제안] (018 연결 때 교체).</summary>
        public float TakeOn = 36f;
        public float MultLate = 1.3f, MultClose = 1.2f, MultMyPlayer = 1.5f;
        public int SceneMin = 8, SceneMax = 15;
        public double Before = 8d, After = 3d, GoalAfter = 7d, MinShot = 2.5d, SubSceneSeconds = 6d, ConsecutiveGoalSeconds = 60d;
        public float FreeKickZone = 25f;
        /// <summary>FR-40 표에 없는 채움 후보 (0:0 경기 FR-42 하한용, 058 플랜 값).</summary>
        public float Filler = 5f;

        public static SceneScoreTable From(TuningBundle t)
        {
            var s = new SceneScoreTable
            {
                Goal = t.Get("highlight.score.goal"),
                RedCard = t.Get("highlight.score.redCard"),
                Penalty = t.Get("highlight.score.penalty"),
                VarOverturn = t.Get("highlight.score.varOverturn"),
                Woodwork = t.Get("highlight.score.woodwork"),
                Save = t.Get("highlight.score.save"),
                ShotInBox = t.Get("highlight.score.shotInBox"),
                ShotOutBox = t.Get("highlight.score.shotOutBox"),
                Defensive = t.Get("highlight.score.defensive"),
                TakeOn = t.Get("highlight.score.takeOn"),
                YellowCard = t.Get("highlight.score.yellowCard"),
                Foul = t.Get("highlight.score.foul"),
                MultLate = t.Get("highlight.mult.late"),
                MultClose = t.Get("highlight.mult.close"),
                MultMyPlayer = t.Get("highlight.mult.myPlayer"),
                SceneMin = (int)t.Get("highlight.sceneMin"),
                SceneMax = (int)t.Get("highlight.sceneMax"),
                Before = t.Get("highlight.before"),
                After = t.Get("highlight.after"),
                GoalAfter = t.Get("highlight.goalAfter"),
                MinShot = t.Get("highlight.minShot"),
                SubSceneSeconds = t.Get("highlight.subSceneSeconds"),
                ConsecutiveGoalSeconds = t.Get("highlight.consecutiveGoalSeconds"),
                FreeKickZone = t.Get("highlight.freeKickZone"),
                Filler = t.Get("highlight.score.filler"),
            };
            return s;
        }
    }
}
