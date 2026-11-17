using System.Collections.Generic;
using System.IO;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Highlight.Selection;
using RoadToPro.Verification.Bench;

namespace RoadToPro.Verification.Samples.Highlight
{
    /// <summary>
    /// specs/081 T19: 실경기 하이라이트(058 픽스처) 선정 장면 안의 AC-07 장면 종류. 에디터 탐색 도구(`FeelSceneSeedSearch`)와
    /// 플레이어 안 탐색(에디터·플레이어 빌드 시뮬 차이 — CLAUDE.md 부동소수점 결정론 미정)이 같은 판정을 쓴다.
    /// </summary>
    public static class FeelSceneCoverage
    {
        public const int TakeOnOk = 1, TakeOnLost = 2, TackleStand = 4, TackleSlide = 8, CutbackGoal = 16, HeaderShot = 32, Defensive = 64;
        /// <summary>specs/082 AC-05: 점프 헤딩슛 — 공 높이 ≥ 슈터 이마(캐스트 키 × act.aerial.headK) + <see cref="JumpMargin"/>.</summary>
        public const int HeaderJumpShot = 128;
        /// <summary>점프가 화면에 분명히 보이는 여유(m) — 하이라이트 점프 경계(highlight.headerJumpMin 0.10 m)보다 크게.</summary>
        public const float JumpMargin = 0.2f;

        /// <summary>E2E 픽스처 선발(홈 id 1~11, 원정 101~111)의 캐스트 키(cm). 교체 선수는 180.</summary>
        public static float CastHeightOf(int playerId)
        {
            var h = LiveMatchFixture.CastHeightsCm();
            if (playerId >= 1 && playerId <= 11) return h[playerId - 1];
            if (playerId >= 101 && playerId <= 111) return h[11 + playerId - 101];
            return RoadToPro.MatchEngine.Actions.TechnicalAttributes.HeightDefaultCm;
        }

        /// <summary>082: 헤딩슛 이벤트가 점프 헤딩(공 높이 ≥ 이마 + JumpMargin)인가. headK = act.aerial.headK 기본값.</summary>
        public static bool IsJumpHeader(in SimEvent e, float headK = 0.93f) =>
            e.Kind == SimEventKind.Shot && e.Part == BodyPart.Head && e.Z >= CastHeightOf(e.PlayerId) * headK / 100f + JumpMargin;
        public const int MainKinds = TakeOnOk | TakeOnLost | TackleStand | HeaderShot | Defensive;
        public const ulong FirstSeed = 20261181UL;

        public static int Bits(int v)
        {
            var n = 0;
            for (; v != 0; v &= v - 1) n++;
            return n;
        }

        public static string Describe(int mask)
        {
            var parts = new List<string>();
            if ((mask & TakeOnOk) != 0) parts.Add("돌파 성공");
            if ((mask & TakeOnLost) != 0) parts.Add("돌파 실패");
            if ((mask & TackleStand) != 0) parts.Add("스탠딩");
            if ((mask & TackleSlide) != 0) parts.Add("늦은 슬라이딩");
            if ((mask & CutbackGoal) != 0) parts.Add("컷백 슛 골");
            if ((mask & HeaderShot) != 0) parts.Add("헤딩슛");
            if ((mask & HeaderJumpShot) != 0) parts.Add("점프 헤딩슛");
            if ((mask & Defensive) != 0) parts.Add("수비 장면");
            return string.Join("·", parts);
        }

        /// <summary>058 픽스처 경기 하나를 돌려 선정 장면 안의 종류 마스크.</summary>
        public static int Run(ulong seed, MatchTuning tuning, SceneScoreTable table)
        {
            var sim = new MatchSim(LiveMatchFixture.Prepare(MatchBenchWorkloads.Setup(seed)), tuning, LiveMatchFixture.Options());
            sim.RunToEnd();
            var sel = LiveSceneSelector.Select(sim.Result.EventLog, sim.Result.EndTick, PitchSpec.Default, LiveMatchFixture.MyPlayerId, table);
            return Of(sim.Result.EventLog, sel);
        }

        public static int Of(SimEvent[] log, LiveSceneList sel)
        {
            var m = 0;
            foreach (var kv in sel.Info) if (kv.Value.Kind == LiveSceneKind.Defensive) m |= Defensive;
            for (int k = 0; k < log.Length; k++)
            {
                var e = log[k];
                var t = e.Tick * 0.1 + System.Math.Max((int)e.Substep, 0) * 0.01;
                var inScene = false;
                foreach (var s in sel.Scenes) if (!s.IsReplay && t >= s.Start && t <= s.End) { inScene = true; break; }
                if (!inScene) continue;
                if (e.Kind == SimEventKind.Dribble) m |= (e.B & EventCodes.DribbleSuccess) != 0 ? TakeOnOk : TakeOnLost;
                if (e.Kind == SimEventKind.Tackle)
                {
                    if ((e.A & EventCodes.TackleSliding) == 0 && (e.A & EventCodes.TackleWon) != 0) m |= TackleStand;
                    if ((e.A & EventCodes.TackleSliding) != 0 && (e.A & EventCodes.TackleLate) != 0) m |= TackleSlide;
                }
                if (e.Kind != SimEventKind.Shot) continue;
                if (e.Part == BodyPart.Head) m |= HeaderShot;
                if (IsJumpHeader(e)) m |= HeaderJumpShot;
                for (int j = k - 1; j >= 0 && e.Tick - log[j].Tick <= 100; j--)
                    if (log[j].Kind == SimEventKind.Cross && log[j].Team == e.Team && (log[j].A >> ActionSystem.CrossTypeShift) == (int)CrossType.Cutback + 1)
                    {
                        for (int g = k + 1; g < log.Length && log[g].Tick - e.Tick <= 50; g++) if (log[g].Kind == SimEventKind.Goal && log[g].Team == e.Team) { m |= CutbackGoal; break; }
                        break;
                    }
            }
            return m;
        }

        /// <summary>시드 n개 탐색 (병렬, 결과는 시드 순서). 경기는 시뮬 코어만 — 결정론은 경기 하나 안에서 유지.</summary>
        public static int[] Search(string dataDir, int n)
        {
            var work = new DecisionBenchWorkloads(dataDir);
            var tuning = LiveMatchFixture.Tuning(work.Tuning, dataDir);
            var table = SceneScoreTable.From(TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(dataDir, "tuning", "highlight.json"))));
            var masks = new int[n];
            System.Threading.Tasks.Parallel.For(0, n, i => masks[i] = Run(FirstSeed + (ulong)i, tuning, table));
            return masks;
        }

        /// <summary>주 장면 5종을 가장 많이 갖춘 시드(동점 = 늦은 슬라이딩도 있으면 우선 → 이른 시드), 늦은 슬라이딩이 있는 첫 시드.</summary>
        public static void Choose(int[] masks, out int best, out int slide)
        {
            best = -1;
            slide = -1;
            for (int i = 0; i < masks.Length; i++)
            {
                if (slide < 0 && (masks[i] & TackleSlide) != 0) slide = i;
                if (best < 0) { best = i; continue; }
                int a = Bits(masks[i] & MainKinds), b = Bits(masks[best] & MainKinds);
                if (a > b || (a == b && (masks[i] & TackleSlide) != 0 && (masks[best] & TackleSlide) == 0)) best = i;
            }
        }
    }
}
