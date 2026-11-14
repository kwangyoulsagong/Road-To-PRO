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
            return new MatchTuning(t.Ball, t.Kinematics, t.Match, t.Shape, d, t.Goalkeeper, t.Decision);
        }

        public static MatchSim Sim(ScenarioData d, MatchSimOptions o = null, ulong seed = 20261109UL, MatchTuning tuning = null) =>
            new MatchSim(ShapeTestKit.Setup(seed: seed), tuning ?? MatchTuningOf(d), o);

        /// <summary>
        /// 010 기회주의 이탈을 끈 튜닝 (θ_opp 최대) — 013 배역 규칙(이탈·회수)만 시험할 때. 010이 배역을 먼저 풀면 013 규칙의 전제가 사라진다.
        /// </summary>
        public static MatchTuning NoOpportunism(ScenarioData d)
        {
            var t = MatchTuningOf(d);
            var dec = RoadToPro.Core.Tuning.TuningLoader.ParseBundle(MatchTestKit.DecisionTuning().Registry, MatchTestKit.Values(("dec.intent.thetaOpp", 3f)));
            return new MatchTuning(t.Ball, t.Kinematics, t.Match, t.Shape, t.Scenario, t.Goalkeeper, dec);
        }

        /// <summary>배역이 의도를 항상 따른다 (준수 1, 기회주의 없음) — 072 이후 배역 공 소유자도 판단 AI가 정하므로 013 단계 규칙만 시험할 때.</summary>
        public static MatchTuning ScriptedCast(ScenarioData d)
        {
            var t = MatchTuningOf(d);
            var dec = RoadToPro.Core.Tuning.TuningLoader.ParseBundle(MatchTestKit.DecisionTuning().Registry, MatchTestKit.Values(
                ("dec.intent.thetaOpp", 3f), ("dec.comply.base", 1f), ("dec.comply.max", 1f), ("dec.comply.min", 1f)));
            return new MatchTuning(t.Ball, t.Kinematics, t.Match, t.Shape, t.Scenario, t.Goalkeeper, dec);
        }

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
        /// <remarks>기본 시드 20261109 → 20261110 (2026-11-14 specs/071: 슈팅 결정 RNG 제거로 경기 궤적이 바뀌어 강제 시작 직후 공을 잃는 시드가 됨).</remarks>
        public static MatchSim Forced(ScenarioData d, string id, int team = 0, ulong seed = 20261110UL, MatchSimOptions options = null, MatchTuning tuning = null)
        {
            var o = options ?? new MatchSimOptions();
            o.ScenarioForce = new ScenarioForceRequest { Id = id, Team = team, Allowed = true };
            var sim = Sim(d, o, seed, tuning);
            var rt = Runtime(sim);
            for (int i = 0; i < 3000 && !rt.Run(team).Active; i++) sim.StepTicks(1);
            NUnit.Framework.Assert.That(rt.Run(team).Active, NUnit.Framework.Is.True, "강제 시작 실패");
            return sim;
        }

        /// <summary>
        /// 1틱 진행 전에 공 소유자 6 m 안의 상대를 8 m 밖으로 치운다 — 010 압박이 운반자를 바로 태클해 013 규칙(체이닝·시간 초과·패스 완료)의
        /// 전제가 깨지지 않게 하는 테스트 픽스처 (2026-11-12, specs/010 T18).
        /// </summary>
        public static void StepWithoutPressure(MatchSim sim)
        {
            var ctx = sim.Context;
            ref readonly var b = ref ctx.Ball;
            if (b.Phase == RoadToPro.MatchEngine.Ball.BallPhase.Controlled && b.OwnerId >= 0)
            {
                var c = ctx.SlotState(b.OwnerId).Position;
                var opp = 1 - RoadToPro.MatchEngine.Match.Squad.TeamOfSlot(b.OwnerId);
                for (int o = opp * 11 + 1; o < (opp + 1) * 11; o++)
                {
                    if (!ctx.IsSlotActive(o)) continue;
                    var d = ctx.SlotState(o).Position - c;
                    if (d.LengthSq() >= 36f) continue;
                    var dir = d.LengthSq() > 1e-4f ? d.Normalized() : new RoadToPro.Core.Math.Vec2(0f, 1f);
                    ctx.Kinematics.Place(o, c + dir * 8f, ctx.SlotState(o).Facing);
                }
            }
            sim.StepTicks(1);
        }

        public static int LastEndReason(MatchSim sim)
        {
            var e = MatchTestKit.Events(sim, RoadToPro.MatchEngine.Match.Events.SimEventKind.ScenarioEnd);
            return e.Count == 0 ? -1 : e[e.Count - 1].B;
        }
    }
}
