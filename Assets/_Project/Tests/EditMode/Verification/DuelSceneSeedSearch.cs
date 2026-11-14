using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Highlight.Selection;
using RoadToPro.Verification.Bench;
using RoadToPro.Verification.Samples.Highlight;

namespace RoadToPro.Tests.EditMode.Verification
{
    /// <summary>specs/070 T11 도구: 058 실경기 픽스처로 선정 장면 안에 결투 4종(테이크온 성공·실패, 스탠딩 태클, 슬라이딩 태클)이 모두 있는 시드를 찾는다. 리포트용 (Explicit).</summary>
    public class DuelSceneSeedSearch
    {
        [Test, Explicit("시드 탐색 도구"), Category("Slow"), Timeout(1_800_000)]
        public void Search_Seeds()
        {
            var data = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            var work = new DecisionBenchWorkloads(data);
            var tuning = LiveMatchFixture.Tuning(work.Tuning, data);
            var hl = TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(data, "tuning", "highlight.json")));
            const int n = 32;
            var lines = new string[n];
            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                var seed = 20261109UL + (ulong)i;
                var sim = new MatchSim(LiveMatchFixture.Prepare(MatchBenchWorkloads.Setup(seed)), tuning, LiveMatchFixture.Options());
                sim.RunToEnd();
                var sel = LiveSceneSelector.Select(sim.Result.EventLog, sim.Result.EndTick, PitchSpec.Default, LiveMatchFixture.MyPlayerId, SceneScoreTable.From(hl));
                int ok = 0, lost = 0, stand = 0, slide = 0, late = 0;
                var log = sim.Result.EventLog;
                for (int k = 0; k < log.Length; k++)
                {
                    var e = log[k];
                    var t = e.Tick * 0.1 + System.Math.Max((int)e.Substep, 0) * 0.01;
                    var inScene = false;
                    foreach (var s in sel.Scenes) if (!s.IsReplay && t >= s.Start && t <= s.End) { inScene = true; break; }
                    if (!inScene) continue;
                    if (e.Kind == SimEventKind.Dribble) { if ((e.B & EventCodes.DribbleSuccess) != 0) ok++; else lost++; }
                    if (e.Kind == SimEventKind.Tackle)
                    {
                        if ((e.A & EventCodes.TackleSliding) != 0) { slide++; if ((e.A & EventCodes.TackleLate) != 0) late++; }
                        else if ((e.A & EventCodes.TackleWon) != 0) stand++;
                    }
                }
                var all = 0; var allOk = 0; for (int k = 0; k < log.Length; k++) if (log[k].Kind == SimEventKind.Dribble) { all++; if ((log[k].B & EventCodes.DribbleSuccess) != 0) allOk++; }
                lines[i] = $"{seed}: 경기 테이크온 {all}(성공 {allOk}) / 테이크온 성공 {ok}, 실패 {lost}, 스탠딩 성공 {stand}, 슬라이딩 {slide}(늦은 {late}), 장면 {sel.Scenes.Length}" + (ok > 0 && lost > 0 && stand > 0 && slide > 0 ? " ★" : "");
            });
            foreach (var l in lines) TestContext.WriteLine(l);
        }
    }
}
