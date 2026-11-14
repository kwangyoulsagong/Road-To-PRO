using System.IO;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Highlight.Selection;
using RoadToPro.Verification.Bench;
using RoadToPro.Verification.Samples.Highlight;

namespace RoadToPro.Tests.EditMode.Verification
{
    /// <summary>specs/071 T12 도구: 058 실경기 픽스처로 선정 장면 안에 컷백 뒤 슛(골)·헤더(헤딩슛)·25 m 중거리 슛이 있는 시드를 찾는다. 리포트용 (Explicit).</summary>
    public class ShotSceneSeedSearch
    {
        static float GoalDist(in SimEvent e)
        {
            var p = PitchSpec.Default;
            var a = System.Math.Sqrt(e.X * e.X + (e.Y - p.WidthM * 0.5) * (e.Y - p.WidthM * 0.5));
            var b = System.Math.Sqrt((e.X - p.LengthM) * (e.X - p.LengthM) + (e.Y - p.WidthM * 0.5) * (e.Y - p.WidthM * 0.5));
            return (float)System.Math.Min(a, b);
        }

        [Test, Explicit("시드 탐색 도구"), Category("Slow"), Timeout(3_600_000)]
        public void Search_Seeds()
        {
            var data = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            var work = new DecisionBenchWorkloads(data);
            var tuning = LiveMatchFixture.Tuning(work.Tuning, data);
            var hl = TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(data, "tuning", "highlight.json")));
            var n = 64;
            var np = Path.Combine(Directory.GetCurrentDirectory(), ".farm", "shot-seed-n.txt");
            if (File.Exists(np)) n = int.Parse(File.ReadAllText(np).Trim());
            var lines = new string[n];
            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                var seed = 20261141UL + (ulong)i;
                var sim = new MatchSim(LiveMatchFixture.Prepare(MatchBenchWorkloads.Setup(seed)), tuning, LiveMatchFixture.Options());
                sim.RunToEnd();
                var sel = LiveSceneSelector.Select(sim.Result.EventLog, sim.Result.EndTick, PitchSpec.Default, LiveMatchFixture.MyPlayerId, SceneScoreTable.From(hl));
                var log = sim.Result.EventLog;
                int cutShot = 0, cutGoal = 0, headShot = 0, header = 0, longShot = 0, crosses = 0, headersAll = 0;
                for (int k = 0; k < log.Length; k++)
                {
                    var e = log[k];
                    if (e.Kind == SimEventKind.Cross) crosses++;
                    if (e.Kind == SimEventKind.Header) headersAll++;
                    var t = e.Tick * 0.1 + System.Math.Max((int)e.Substep, 0) * 0.01;
                    var inScene = false;
                    foreach (var s in sel.Scenes) if (!s.IsReplay && t >= s.Start && t <= s.End) { inScene = true; break; }
                    if (!inScene) continue;
                    if (e.Kind == SimEventKind.Header) header++;
                    if (e.Kind != SimEventKind.Shot) continue;
                    if (e.Part == BodyPart.Head) headShot++;
                    else if (GoalDist(in e) >= 25f) longShot++;
                    for (int j = k - 1; j >= 0 && e.Tick - log[j].Tick <= 100; j--)
                        if (log[j].Kind == SimEventKind.Cross && log[j].Team == e.Team && (log[j].A >> ActionSystem.CrossTypeShift) == (int)CrossType.Cutback + 1)
                        {
                            cutShot++;
                            for (int g = k + 1; g < log.Length && log[g].Tick - e.Tick <= 50; g++) if (log[g].Kind == SimEventKind.Goal && log[g].Team == e.Team) { cutGoal++; break; }
                            break;
                        }
                }
                var star = cutShot > 0 && (headShot > 0 || header > 0) && longShot > 0;
                lines[i] = $"{seed}: 크로스 {crosses}, 헤더(경기) {headersAll} / 장면: 컷백 슛 {cutShot}(골 {cutGoal}), 헤딩슛 {headShot}, 헤더 {header}, 25 m 슛 {longShot}, 장면 {sel.Scenes.Length}" + (star ? " ★" : "");
            });
            foreach (var l in lines) TestContext.WriteLine(l);
        }
    }
}
