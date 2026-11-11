using System.Collections.Generic;
using System.IO;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.Scenarios;
using RoadToPro.Tests.EditMode.MatchEngine.Match;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Scenarios
{
    /// <summary>013 테스트 공용: 데이터 로드, 인라인 시나리오 작성, 강제 시작.</summary>
    public static class ScenarioTestKit
    {
        static string Root => Directory.GetCurrentDirectory();
        static string Shared(string f) => File.ReadAllText(Path.Combine(Root, "Data", "scenarios", "_shared", f));

        public static string Regions => Shared("regions.json");

        public static TuningBundle Tuning() => TuningBundle.Defaults(TuningLoader.LoadDefinitionsFile(Path.Combine(Root, "Data", "tuning", "scenario.json")));
        public static ScenarioParams Params() => new ScenarioParams(Tuning());

        public static ScenarioSources Sources(bool withCatalog = false) => new ScenarioSources
        {
            RegionsJson = Regions,
            CatalogIdsJson = withCatalog ? Shared("catalog-ids.json") : null,
        };

        public static ScenarioLibrary Lib(params string[] jsons)
        {
            var s = Sources();
            for (int i = 0; i < jsons.Length; i++) s.Add($"f{i:D3}.json", jsons[i]);
            return ScenarioLibrary.Load(s);
        }

        public static IEnumerable<string> SampleFiles()
        {
            var files = Directory.GetFiles(Path.Combine(Root, "Data", "scenarios"), "*.json");
            System.Array.Sort(files, string.CompareOrdinal);
            return files;
        }

        public static ScenarioSources SampleSources()
        {
            var s = Sources(withCatalog: true);
            s.FinishingJson = Shared("finishing.json");
            s.ModifiersJson = Shared("modifiers.json");
            foreach (var f in SampleFiles()) s.Add(Path.GetFileName(f), File.ReadAllText(f));
            return s;
        }

        public static ScenarioLibrary Samples() => ScenarioLibrary.Load(SampleSources());

        public static ScenarioData DataOf(ScenarioLibrary lib) => new ScenarioData(Tuning(), lib);

        public static MatchTuning MatchTuningOf(ScenarioData d)
        {
            var t = ShapeTestKit.TuningWithShape();
            return new MatchTuning(t.Ball, t.Kinematics, t.Match, t.Shape, d, t.Goalkeeper);
        }

        public static MatchSim Sim(ScenarioData d, MatchSimOptions o = null, ulong seed = 20261109UL) =>
            new MatchSim(ShapeTestKit.Setup(seed: seed), MatchTuningOf(d), o);

        public static ScenarioRuntime Runtime(MatchSim sim) => ((ScenarioStage)sim.Stage(StageId.ScenarioOrUtility)).Runtime;

        /// <summary>최소 시나리오 JSON. 트리거·배역·분기는 JSON 조각으로 넣는다.</summary>
        public static string Mini(string id, string cast, string trigger = "{}", string branches = "[]", string category = "FT", string extra = "") =>
            "{\"id\":\"" + id + "\",\"name\":{\"ko\":\"가\",\"en\":\"a\"},\"category\":\"" + category + "\",\"trigger\":" + trigger +
            ",\"cast\":" + cast + ",\"branches\":" + branches + ",\"kpi\":[\"x\"]" + extra + "}";

        public const string CarrierOnly = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\",\"distanceM\":30}]}]";

        public const string RunCast = "[{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\",\"distanceM\":30}]}," +
            "{\"role\":\"r\",\"steps\":[{\"do\":\"run_in_behind\"}]}," +
            "{\"role\":\"o\",\"required\":false,\"steps\":[{\"do\":\"drop\"}]}]";

        /// <summary>강제 선택이 시작될 때까지 돌린다.</summary>
        public static MatchSim Forced(ScenarioData d, string id, int team = 0, ulong seed = 20261109UL, MatchSimOptions options = null)
        {
            var o = options ?? new MatchSimOptions();
            o.ScenarioForce = new ScenarioForceRequest { Id = id, Team = team, Allowed = true };
            var sim = Sim(d, o, seed);
            var rt = Runtime(sim);
            for (int i = 0; i < 3000 && !rt.Run(team).Active; i++) sim.StepTicks(1);
            NUnit.Framework.Assert.That(rt.Run(team).Active, NUnit.Framework.Is.True, "강제 시작 실패");
            return sim;
        }

        public static int LastEndReason(MatchSim sim)
        {
            var e = MatchTestKit.Events(sim, RoadToPro.MatchEngine.Match.Events.SimEventKind.ScenarioEnd);
            return e.Count == 0 ? -1 : e[e.Count - 1].B;
        }
    }
}
