using System.Linq;
using NUnit.Framework;
using RoadToPro.MatchEngine.Scenarios;
using RoadToPro.MatchEngine.Tactics;
using static RoadToPro.Tests.EditMode.MatchEngine.Scenarios.ScenarioTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Scenarios
{
    public class ScenarioParserTests
    {
        [Test]
        public void SixDocumentSamples_FieldsMatch()
        {
            var lib = Samples();
            Assert.That(lib.Report.Errors, Is.Empty, string.Join("\n", lib.Report.Errors));
            foreach (var id in new[] { "wing_solo_run_cutback", "FT01", "PR04", "CT02", "D-PR-01", "S-CK-01" })
                Assert.That(lib.OrdinalOf(id), Is.GreaterThanOrEqualTo(0), id);

            var ft = lib[lib.OrdinalOf("FT01")];
            Assert.That(ft.Category, Is.EqualTo(ScenarioCategory.FT));
            Assert.That(ft.NameEn, Is.EqualTo("Overlap & Cross"));
            Assert.That(ft.Cast.Select(r => r.Name), Is.EqualTo(new[] { "W", "FB", "ST", "IW_far", "CM" }));
            Assert.That(ft.Cast[1].Steps.Select(s => s.Verb), Is.EqualTo(new[] { ScenarioVerb.Overlap, ScenarioVerb.Receive, ScenarioVerb.CrossDrivenLow }));
            Assert.That(ft.Cast[1].Steps[2].Alt, Is.EqualTo(ScenarioVerb.CrossLofted));
            Assert.That(ft.Trigger.OppBlock, Is.EqualTo(OppBlockMask.MB | OppBlockMask.LB));
            Assert.That(ft.Trigger.Space[0].Field, Is.EqualTo(SpaceField.FbFrontSpaceM));
            Assert.That(ft.Trigger.Space[0].Op, Is.EqualTo(CompareOp.Ge));
            Assert.That(ft.Trigger.Space[0].Value, Is.EqualTo(8f));
            var fb3 = ft.Branches.Single(b => b.Role == 1 && b.Step == 2);
            Assert.That(fb3.Success.Ids, Is.EqualTo(new[] { "FN03", "FN04", "FN05" }));
            Assert.That(fb3.Success.Weights, Is.EqualTo(new[] { 0.4f, 0.35f, 0.25f }));

            var wing = lib[lib.OrdinalOf("wing_solo_run_cutback")];
            Assert.That(wing.Trigger.Space[0].Field, Is.EqualTo(SpaceField.SpaceAheadM));
            Assert.That(wing.Trigger.Space[0].Value, Is.EqualTo(25f));
            Assert.That(wing.Cast[0].Steps[0].Verb, Is.EqualTo(ScenarioVerb.CarryLine));
            Assert.That(wing.Cast[0].Steps[0].DistanceM, Is.EqualTo(50f));
            Assert.That(wing.Cast[4].Opponent, Is.True);
            Assert.That(wing.CarrierTraitsBonus, Does.Contain("드리블 중독"));

            var trap = lib[lib.OrdinalOf("D-PR-01")];
            Assert.That(trap.Side, Is.EqualTo(ScenarioSide.Defense));
            Assert.That(trap.Trigger.RequirePress, Is.True);
            Assert.That(trap.Trigger.Event, Is.EqualTo(TriggerEventMask.OppReceive));
            Assert.That(trap.Cast[0].Steps[1].T, Is.EqualTo(1.2f));

            var corner = lib[lib.OrdinalOf("S-CK-01")];
            Assert.That(corner.Cast[1].Steps[0].T, Is.EqualTo(-1f));
            Assert.That(corner.Cast[1].Steps[0].Verb, Is.EqualTo(ScenarioVerb.DecoyRun));
            Assert.That(corner.Trigger.OppMarkingMask, Is.EqualTo((byte)((1 << (int)MarkingScheme.Zonal) | (1 << (int)MarkingScheme.Hybrid))));

            var ct = lib[lib.OrdinalOf("CT02")];
            Assert.That(ct.Trigger.OppBlock, Is.EqualTo(OppBlockMask.UNB));
            var pr = lib[lib.OrdinalOf("PR04")];
            Assert.That(pr.Cast[2].Steps[0].Verb, Is.EqualTo(ScenarioVerb.ThirdManRun));
        }

        [TestCase("SET_DEF", TeamPhase.DefHigh, TeamPhase.DefMid, TeamPhase.DefLow, false)]
        [TestCase("PRESS", TeamPhase.DefHigh, TeamPhase.DefHigh, TeamPhase.DefHigh, true)]
        [TestCase("LOW_BLOCK", TeamPhase.DefLow, TeamPhase.DefLow, TeamPhase.DefLow, false)]
        [TestCase("TRANS_D", TeamPhase.DefTransition, TeamPhase.DefTransition, TeamPhase.DefTransition, false)]
        [TestCase("TRANS_A", TeamPhase.AttTransition, TeamPhase.AttTransition, TeamPhase.AttTransition, false)]
        [TestCase("SET_PIECE", TeamPhase.SetPiece, TeamPhase.SetPiece, TeamPhase.SetPiece, false)]
        public void PhaseNotation_Mapped(string notation, TeamPhase a, TeamPhase b, TeamPhase c, bool press)
        {
            var lib = Lib(Mini("X", CarrierOnly, trigger: "{\"phase\":\"" + notation + "\"}"));
            Assert.That(lib.Report.Errors, Is.Empty);
            var t = lib[0].Trigger;
            Assert.That(t.PhaseMask, Is.EqualTo((ushort)(PhaseBits.Of(a) | PhaseBits.Of(b) | PhaseBits.Of(c))));
            Assert.That(t.RequirePress, Is.EqualTo(press));
        }

        [Test]
        public void UnknownPhase_Error()
        {
            var lib = Lib(Mini("X", CarrierOnly, trigger: "{\"phase\":\"NOPE\"}"));
            Assert.That(lib.Count, Is.EqualTo(0));
            Assert.That(lib.Report.Errors[0].Field, Is.EqualTo("trigger.phase"));
        }
    }

    public class ScenarioValidatorTests
    {
        static void ExpectError(ScenarioSources s, string field, string file)
        {
            s.Add("ok.json", Mini("OK", CarrierOnly));
            var lib = ScenarioLibrary.Load(s);
            Assert.That(lib.OrdinalOf("OK"), Is.GreaterThanOrEqualTo(0), "정상 파일은 로드된다");
            Assert.That(lib.Report.Errors.Any(e => e.Field.Contains(field) && e.File == file && e.Line > 0),
                Is.True, string.Join("\n", lib.Report.Errors));
        }

        [Test]
        public void Duplicate_Id() =>
            ExpectError(Sources().Add("a.json", Mini("D", CarrierOnly)).Add("b.json", Mini("D", CarrierOnly)), "id", "b.json");

        [Test]
        public void Missing_Branch_Target() =>
            ExpectError(Sources().Add("a.json", Mini("A", CarrierOnly, branches: "[{\"at\":\"c.1\",\"success\":\"NOPE-99\"}]")), "branches", "a.json");

        [Test]
        public void Unknown_Verb() =>
            ExpectError(Sources().Add("a.json", Mini("A", "[{\"role\":\"c\",\"steps\":[\n{\"do\":\"moonwalk\"}]}]")), "do", "a.json");

        [Test]
        public void Unconditional_Cycle() =>
            ExpectError(Sources()
                .Add("a.json", Mini("A", CarrierOnly, branches: "[{\"when\":\"always\",\"goto\":\"B\"}]"))
                .Add("b.json", Mini("B", CarrierOnly, branches: "[{\"when\":\"always\",\"goto\":\"A\"}]")), "branches", "a.json");

        [Test]
        public void Negative_Weight() =>
            ExpectError(Sources().Add("a.json", Mini("A", CarrierOnly, branches: "[{\"at\":\"c.1\",\"success\":{\"goto\":[\"OK\"],\"weights\":[-1]}}]")), "weights", "a.json");

        [Test]
        public void Line_Number_Points_To_Field()
        {
            var lib = Lib(Mini("A", "[{\"role\":\"c\",\"steps\":[\n\n{\"do\":\"moonwalk\"}]}]"));
            Assert.That(lib.Report.Errors[0].Line, Is.EqualTo(3));
        }

        [Test]
        public void Situation_Next_Rejected()
        {
            var lib = Lib(Mini("A", CarrierOnly, branches: "[{\"when\":\"regain\",\"goto\":\"next\"}]"));
            Assert.That(lib.Count, Is.EqualTo(0));
        }

        [Test]
        public void Cycle_OnlyMembersRejected()
        {
            var lib = Lib(
                Mini("C", CarrierOnly, branches: "[{\"when\":\"always\",\"goto\":\"A\"}]"),
                Mini("A", CarrierOnly, branches: "[{\"when\":\"always\",\"goto\":\"B\"}]"),
                Mini("B", CarrierOnly, branches: "[{\"when\":\"always\",\"goto\":\"A\"}]"));
            Assert.That(lib.Ids, Is.EqualTo(new[] { "C" }), "순환에 속하지 않은 C는 남는다");
        }

        [Test]
        public void Missing_Required_Field()
        {
            var lib = Lib("{\"id\":\"A\",\"name\":{\"ko\":\"가\",\"en\":\"a\"},\"category\":\"FT\",\"cast\":[],\"branches\":[]}");
            Assert.That(lib.Count, Is.EqualTo(0));
            Assert.That(lib.Report.Errors.Any(e => e.Field == "trigger"), Is.True);
        }

        [Test]
        public void Special_Requires_Gate()
        {
            var lib = Lib(Mini("A", CarrierOnly, category: "SPC"));
            Assert.That(lib.Report.Errors.Any(e => e.Field == "gate"), Is.True);
        }

        [Test]
        public void Too_Many_Cast()
        {
            var r = string.Join(",", Enumerable.Range(0, 6).Select(i => "{\"role\":\"r" + i + "\",\"steps\":[{\"do\":\"drop\"}]}"));
            var lib = Lib(Mini("A", "[" + r + "]"));
            Assert.That(lib.Report.Errors.Any(e => e.Field == "cast"), Is.True);
        }
    }

    public class ScenarioLibraryTests
    {
        [Test]
        public void CandidateStatus_ExcludedAndReported()
        {
            var files = Enumerable.Range(0, 5).Select(i => Mini("N" + i, CarrierOnly))
                .Concat(Enumerable.Range(0, 2).Select(i => Mini("C" + i, CarrierOnly, extra: ",\"상태\":\"후보\"")))
                .Concat(new[] { Mini("R", CarrierOnly, extra: ",\"상태\":\"폐기\"") }).ToArray();
            var lib = Lib(files);
            Assert.That(lib.Count, Is.EqualTo(5));
            Assert.That(lib.Report.Excluded, Is.EquivalentTo(new[] { "C0", "C1", "R" }));
            Assert.That(lib.OrdinalOf("C0"), Is.GreaterThanOrEqualTo(0), "강제 선택용으로 보관한다");
        }

        [Test]
        public void MissingBranchTarget_FallsBack()
        {
            var s = Sources(withCatalog: true).Add("a.json", Mini("A", CarrierOnly, branches: "[{\"at\":\"c.1\",\"success\":\"FT02\"}]"));
            var lib = ScenarioLibrary.Load(s);
            Assert.That(lib.Report.Errors, Is.Empty);
            Assert.That(lib.Report.Warnings, Has.Count.EqualTo(1));
            Assert.That(lib[0].Branches[0].Success.Ordinals[0], Is.EqualTo(-1));
        }

        [Test]
        public void Hash_CoversCatalog()
        {
            var a = ScenarioLibrary.Load(Sources().Add("a.json", Mini("A", CarrierOnly)));
            var b = ScenarioLibrary.Load(Sources(withCatalog: true).Add("a.json", Mini("A", CarrierOnly)));
            Assert.That(a.Hash, Is.Not.EqualTo(b.Hash));
        }

        [Test]
        public void FileOrder_DoesNotChangeLibrary()
        {
            var a = ScenarioLibrary.Load(Sources().Add("a.json", Mini("B", CarrierOnly)).Add("b.json", Mini("A", CarrierOnly)));
            var b = ScenarioLibrary.Load(Sources().Add("b.json", Mini("A", CarrierOnly)).Add("a.json", Mini("B", CarrierOnly)));
            Assert.That(a.Ids, Is.EqualTo(b.Ids));
            Assert.That(a.Hash, Is.EqualTo(b.Hash));
            Assert.That(a.Ids, Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public void EmptyLibrary_Loads()
        {
            var lib = ScenarioLibrary.Load(Sources());
            Assert.That(lib.Count, Is.EqualTo(0));
            Assert.That(lib.Report.Errors, Is.Empty);
        }

        [Test]
        public void Modifiers_Loaded_AndClamped()
        {
            var lib = Samples();
            Assert.That(lib.Modifiers.Count, Is.EqualTo(2));
            var d = lib[lib.OrdinalOf("PR04")];
            Assert.That(ScenarioSelector.Modifier(d, PhaseBits.Of(TeamPhase.Progression), lib, Params()), Is.EqualTo(1.21f).Within(1e-4f));
        }
    }
}
