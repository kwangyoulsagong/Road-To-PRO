using System.Globalization;
using System.IO;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Verification.Samples.Highlight
{
    /// <summary>
    /// specs/058 E2E 고정 경기 (041 화면과 같은 시드). 012·025·050이 없어 파울·교체를 낼 수 없으므로 심판 스크립트로 퇴장 1건을 넣는다
    /// (058 플랜: 파울 장면은 주입 카드로 대체). 내 선수는 홈 10번(id 10).
    /// 대체 판단의 평균 득점이 확정 밸런스(2.5~3.0)를 크게 넘어서(약 24골, 008·010 전) 이 경기만 대체 슈팅 확률을 낮춘다 — 3:3에
    /// 실점·박스 안 슈팅·수비·퇴장 장면이 모두 나온다 (2026-11-11 사용자 결정: AC-15 보류 + E2E 경기만 저득점). 게임 튜닝은 그대로.
    /// 011(선방 모델)로 0.05에서는 플레이어 빌드 경기가 2:0(실점 장면 없음)이라 0.08로 올렸다 — 양 팀 득점·선방·쳐내기 장면이 나온다.
    /// </summary>
    public static class LiveMatchFixture
    {
        public const int MyPlayerId = 10;
        /// <summary>원정 4번(id 104) 퇴장 — 60분.</summary>
        public const long SendOffTick = 36_000;
        public const int SendOffPlayer = 104;

        public const float ShotProb = 0.15f;

        /// <summary>경기 튜닝에서 대체 슈팅 확률만 바꾼 묶음.</summary>
        public static MatchTuning Tuning(MatchTuning baseTuning, string dataDir)
        {
            var reg = TuningLoader.LoadDefinitionsFile(Path.Combine(dataDir, "tuning", "match.json"));
            var match = TuningLoader.ParseBundle(reg, "{\"version\":1,\"values\":[{\"id\":\"standin.shotProb\",\"value\":" + ShotProb.ToString(CultureInfo.InvariantCulture) + "}]}");
            return new MatchTuning(baseTuning.Ball, baseTuning.Kinematics, match, baseTuning.Shape, baseTuning.Scenario, baseTuning.Goalkeeper, baseTuning.Decision, baseTuning.Action);
        }

        public static MatchSetup Prepare(MatchSetup setup)
        {
            setup.MyPlayerId = MyPlayerId;
            // specs/010 T16: 양 팀 전술(009 형태 → 010 판단 AI 경로) + 포지션별 대체 스타일 프리셋 (003 전, E2E 셋업에서만)
            setup.Home.Tactics = new RoadToPro.MatchEngine.Tactics.TeamTactics { FormationId = "4-4-2" };
            setup.Away.Tactics = new RoadToPro.MatchEngine.Tactics.TeamTactics { FormationId = "4-4-2" };
            Styles(setup.Home);
            Styles(setup.Away);
            return setup;
        }

        /// <summary>4-4-2 선발 순서(GK, RB·CB·CB·LB, RM·CM·CM·LM, ST·ST) 기준 스타일. 003 역할 데이터가 생기면 그쪽으로.</summary>
        static void Styles(TeamSetup t)
        {
            for (int i = 0; i < t.Players.Length; i++)
            {
                var p = t.Players[i];
                var k = System.Array.IndexOf(t.Starters, p.Id);
                PlayStyle st = k switch
                {
                    1 or 4 => PlayStyle.WingBack(),
                    5 or 8 => PlayStyle.Winger(),
                    6 => PlayStyle.Anchor(),
                    9 or 10 => PlayStyle.Poacher(),
                    _ => null,
                };
                if (st != null) t.Players[i] = p.WithMind(p.Mind, st);
            }
        }

        public static MatchSimOptions Options()
        {
            var script = new RefereeScript();
            script.Add(new RefereeAction(SendOffTick, RefereeActionKind.SendOff, SendOffPlayer));
            return new MatchSimOptions { Checkpoints = RoadToPro.MatchEngine.Match.Replay.CheckpointMode.All, Referee = script };
        }
    }
}
