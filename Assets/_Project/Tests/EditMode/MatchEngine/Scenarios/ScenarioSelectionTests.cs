using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Scenarios;
using RoadToPro.MatchEngine.Tactics;
using static RoadToPro.Tests.EditMode.MatchEngine.Scenarios.ScenarioTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Scenarios
{
    public class TriggerEvaluatorTests
    {
        static ScenarioLibrary L() => Lib(Mini("T", CarrierOnly,
            trigger: "{\"zones\":{\"regions\":[\"T2\"],\"lanes5\":[\"W-L\"]},\"phase\":\"Progression\",\"carrierRole\":[\"W\"],\"space\":{\"spaceAheadM\":\">=25\"}}"));

        static TriggerContext Good()
        {
            var space = new float[SpaceFields.Count];
            space[(int)SpaceField.SpaceAheadM] = 30f;
            return new TriggerContext
            {
                Ball = new Vec2(0.5f, 0.1f), PhaseBit = PhaseBits.Of(TeamPhase.Progression), BallState = BallStateMask.Control,
                CarrierRole = PositionMask.W, Space = space, Minute = 10f,
            };
        }

        [Test]
        public void Matches_AndEachMismatchRejected()
        {
            var lib = L();
            var d = lib[0];
            var c = Good();
            Assert.That(TriggerEvaluator.Passes(d, in c, lib.Zones), Is.True);

            var zone = Good(); zone.Ball = new Vec2(0.5f, 0.5f);
            Assert.That(TriggerEvaluator.Passes(d, in zone, lib.Zones), Is.False, "구역");
            var phase = Good(); phase.PhaseBit = PhaseBits.Of(TeamPhase.BuildUp);
            Assert.That(TriggerEvaluator.Passes(d, in phase, lib.Zones), Is.False, "국면");
            var role = Good(); role.CarrierRole = PositionMask.CB;
            Assert.That(TriggerEvaluator.Passes(d, in role, lib.Zones), Is.False, "역할");
            var space = Good(); space.Space[(int)SpaceField.SpaceAheadM] = 24f;
            Assert.That(TriggerEvaluator.Passes(d, in space, lib.Zones), Is.False, "공간");
        }

        [Test]
        public void Candidate_InCellBucket_ForMatchingZone()
        {
            var lib = L();
            var inCell = ZoneMap.CellOf(new Vec2(0.5f, 0.1f), false);
            Assert.That(lib.ByCell(ScenarioSide.Attack, inCell), Has.Member(0));
            var far = ZoneMap.CellOf(new Vec2(0.9f, 0.9f), false);
            Assert.That(lib.ByCell(ScenarioSide.Attack, far), Has.No.Member(0));
        }

        [Test]
        public void DefenseSide_FromCategory_InOwnBucket()
        {
            var lib = Lib(Mini("D", CarrierOnly, category: "DEF"));
            Assert.That(lib[0].Side, Is.EqualTo(ScenarioSide.Defense));
            Assert.That(lib.ByCell(ScenarioSide.Defense, 0), Has.Member(0));
            Assert.That(lib.ByCell(ScenarioSide.Attack, 0), Is.Empty);
        }
    }

    public class RarityGateTests
    {
        static ScenarioDefinition Def(string gate) => Lib(Mini("G", CarrierOnly, extra: ",\"gate\":" + gate))[0];

        [Test]
        public void MaxPerMatch()
        {
            var d = Def("{\"maxPerMatch\":1}");
            var g = new RarityGate(1);
            int started = 0;
            for (int i = 0; i < 100; i++)
                if (g.Allows(d, i * 100)) { g.OnStart(d, i * 100); started++; }
            Assert.That(started, Is.EqualTo(1));
        }

        [Test]
        public void Cooldown()
        {
            var d = Def("{\"cooldownS\":30}");
            var g = new RarityGate(1);
            g.OnStart(d, 0);
            Assert.That(g.Allows(d, 299), Is.False);
            Assert.That(g.Allows(d, 300), Is.True);
        }

        [Test]
        public void ZeroCap_Disabled()
        {
            var d = Def("{\"maxPerMatch\":0}");
            Assert.That(new RarityGate(1).Allows(d, 0), Is.False);
        }
    }

    public class ScenarioSelectorTests
    {
        sealed class Tagged : IScenarioTraitSource
        {
            readonly string _trait;
            public Tagged(string trait) => _trait = trait;
            public float Bonus(int slot, ScenarioDefinition def)
            {
                foreach (var traits in def.FavoredTraits)
                    if (System.Array.IndexOf(traits, _trait) >= 0) return 2f;
                return 1f;
            }
        }

        static int Count(IScenarioTraitSource traits, int family)
        {
            var lib = Lib(
                Mini("DRIB", CarrierOnly, extra: ",\"favoredTraits\":{\"c\":[\"dribbling\"]}"),
                Mini("CROSS", CarrierOnly, extra: ",\"favoredTraits\":{\"c\":[\"crossing\"]}"),
                Mini("PLAIN", CarrierOnly));
            var p = Params();
            int hits = 0;
            var scores = new float[3];
            for (int seed = 1; seed <= 1000; seed++)
            {
                for (int i = 0; i < 3; i++) scores[i] = ScenarioSelector.Score(lib[i], p, traits.Bonus(0, lib[i]), 1f, 1f, 1f, 0f);
                if (ScenarioSelector.Pick(scores, 3, 0.2f, new SeedRng((ulong)seed)) == family) hits++;
            }
            return hits;
        }

        static void ExpectHigher(int with, int without)
        {
            double p1 = with / 1000.0, p2 = without / 1000.0, p = (with + without) / 2000.0;
            var z = (p1 - p2) / System.Math.Sqrt(p * (1 - p) * (2.0 / 1000));
            Assert.That(z, Is.GreaterThan(2.326), $"이항 검정 p < 0.01: {with} vs {without}");
        }

        [Test]
        public void TraitBias_1000Seeds()
        {
            // 정렬 순서: CROSS 0, DRIB 1, PLAIN 2
            ExpectHigher(Count(new Tagged("dribbling"), 1), Count(new NeutralTraitSource(), 1));
            ExpectHigher(Count(new Tagged("crossing"), 0), Count(new NeutralTraitSource(), 0));
        }

        [Test]
        public void RecentUseDecay_AndRecovery()
        {
            var p = Params();
            Assert.That(ScenarioSelector.Decay(-1, 100, p), Is.EqualTo(1f));
            Assert.That(ScenarioSelector.Decay(100, 100, p), Is.EqualTo(0.5f));
            Assert.That(ScenarioSelector.Decay(0, 3000, p), Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(ScenarioSelector.Decay(0, 6000, p), Is.EqualTo(1f));

            var lib = Lib(Mini("A", CarrierOnly), Mini("B", CarrierOnly));
            float RateA(float decayA)
            {
                int hits = 0;
                var s = new float[2];
                for (int seed = 1; seed <= 1000; seed++)
                {
                    s[0] = ScenarioSelector.Score(lib[0], p, 1f, 1f, 1f, decayA, 0f);
                    s[1] = ScenarioSelector.Score(lib[1], p, 1f, 1f, 1f, 1f, 0f);
                    if (ScenarioSelector.Pick(s, 2, 0.2f, new SeedRng((ulong)seed)) == 0) hits++;
                }
                return hits / 1000f;
            }
            Assert.That(RateA(0.5f), Is.LessThan(RateA(1f)));
        }

        [Test]
        public void ZeroScores_Fallback() => Assert.That(ScenarioSelector.Pick(new float[3], 3, 0.2f, new SeedRng(1UL)), Is.EqualTo(-1));

        [Test]
        public void Tactic_ScalesByCategory()
        {
            var p = Params();
            var d = Lib(Mini("D", CarrierOnly, category: "DEF"))[0];
            var ins = TeamInstructions.Default;
            ins.PressIntensity = 1f;
            Assert.That(ScenarioSelector.Tactic(d, in ins, p), Is.EqualTo(1.5f));
        }
    }

    public class CastAssignerTests
    {
        static CastFrame Frame()
        {
            var f = new CastFrame { Ball = new Vec2(0.5f, 0.5f), Owner = 5 };
            for (int s = 0; s < Squad.Slots; s++)
            {
                f.Active[s] = true;
                f.Position[s] = new Vec2(0.1f + 0.03f * s, 0.2f + 0.02f * s);
                f.Role[s] = s % 11 == 0 ? PositionMask.GK : PositionMask.CM;
            }
            f.CastCap[0] = 5;
            f.CastCap[1] = 4;
            return f;
        }

        static string Roles(int own, int opp)
        {
            var parts = new System.Collections.Generic.List<string> { "{\"role\":\"c\",\"carrier\":true,\"positions\":[\"any\"],\"steps\":[{\"do\":\"carry\"}]}" };
            for (int i = 1; i < own; i++) parts.Add("{\"role\":\"o" + i + "\",\"steps\":[{\"do\":\"drop\"}]}");
            for (int i = 0; i < opp; i++) parts.Add("{\"role\":\"x" + i + "\",\"opponent\":true,\"steps\":[{\"do\":\"track_st\"}]}");
            return "[" + string.Join(",", parts) + "]";
        }

        [Test]
        public void Caps_NoDoubleCast()
        {
            var d = Lib(Mini("A", Roles(5, 4)))[0];
            var slots = new int[ScenarioRun.MaxRoles];
            Assert.That(new CastAssigner().Assign(d, 0, Frame(), slots), Is.True);
            var seen = new System.Collections.Generic.HashSet<int>();
            int own = 0, opp = 0;
            for (int r = 0; r < d.Cast.Length; r++)
            {
                Assert.That(slots[r], Is.GreaterThanOrEqualTo(0));
                Assert.That(seen.Add(slots[r]), Is.True);
                if (Squad.TeamOfSlot(slots[r]) == 0) own++; else opp++;
            }
            Assert.That(own, Is.LessThanOrEqualTo(5));
            Assert.That(opp, Is.LessThanOrEqualTo(4));
        }

        [Test]
        public void NearestFirst_TieBySlot()
        {
            var d = Lib(Mini("A", "[{\"role\":\"o\",\"steps\":[{\"do\":\"drop\"}]}]"))[0];
            var f = Frame();
            for (int s = 0; s < 11; s++) f.Position[s] = new Vec2(0.5f, 0.4f);
            var slots = new int[ScenarioRun.MaxRoles];
            new CastAssigner().Assign(d, 0, f, slots);
            Assert.That(slots[0], Is.EqualTo(1), "GK 제외, 동점은 낮은 슬롯");
        }

        [Test]
        public void ShortHanded_Excluded()
        {
            var d = Lib(Mini("A", Roles(5, 0)))[0];
            var f = Frame();
            for (int s = 4; s < 11; s++) if (s != 5) f.Active[s] = false;
            Assert.That(new CastAssigner().Assign(d, 0, f, new int[ScenarioRun.MaxRoles]), Is.False);
        }

        [Test]
        public void OwnScenarioBusy_NotStolen_OpponentRoleStolen()
        {
            var d = Lib(Mini("A", "[{\"role\":\"o\",\"steps\":[{\"do\":\"drop\"}]}]"))[0];
            var f = Frame();
            for (int s = 1; s < 11; s++) f.Busy[s] = CastBusy.OwnScenario;
            Assert.That(new CastAssigner().Assign(d, 0, f, new int[ScenarioRun.MaxRoles]), Is.False);
            f.Busy[3] = CastBusy.OpponentRole;
            var slots = new int[ScenarioRun.MaxRoles];
            Assert.That(new CastAssigner().Assign(d, 0, f, slots), Is.True);
            Assert.That(slots[0], Is.EqualTo(3));
        }

        [Test]
        public void OpponentRole_RespectsTeamCap()
        {
            var d = Lib(Mini("A", "[{\"role\":\"x\",\"opponent\":true,\"steps\":[{\"do\":\"track_st\"}]}]"))[0];
            var f = Frame();
            f.CastCount[1] = 4;
            var slots = new int[ScenarioRun.MaxRoles];
            Assert.That(new CastAssigner().Assign(d, 0, f, slots), Is.True, "상대 배역은 선택 배역");
            Assert.That(slots[0], Is.EqualTo(-1));
        }
    }
}
