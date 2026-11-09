using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Tactics;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public class TeamInstructionsTests
    {
        [Test]
        public void Sanitize_ClampsAndFlags()
        {
            var i = TeamInstructions.Default;
            i.LineHeight = float.NaN;
            i.Compactness = -1f;
            i.PressIntensity = 2f;
            var mask = i.Sanitize();
            Assert.That(i.LineHeight, Is.EqualTo(0.5f));
            Assert.That(i.Compactness, Is.EqualTo(0f));
            Assert.That(i.PressIntensity, Is.EqualTo(1f));
            Assert.That(mask, Is.EqualTo((1 << (int)InstructionId.LineHeight) | (1 << (int)InstructionId.Compactness) | (1 << (int)InstructionId.PressIntensity)));
        }

        [Test]
        public void InvalidValues_ClampedAndWarned()
        {
            var setup = ShapeTestKit.Setup();
            var ins = TeamInstructions.Default;
            ins.LineHeight = float.NaN; ins.WidthIn = -1f; ins.OffsideTrap = 2f;
            setup.Home.Tactics.Instructions = ins;
            var sim = ShapeTestKit.Sim(setup: setup);
            Assert.DoesNotThrow(() => sim.StepTicks(3));
            Assert.That(sim.IsFinished, Is.False, "경기가 실패로 끝나면 안 된다");
            Assert.That(MatchTestKit.Count(sim, SimEventKind.TacticsInputClamped), Is.EqualTo(3));
        }

        [Test]
        public void EnumOutOfRange_Corrected()
        {
            var i = TeamInstructions.Default;
            i.Set(InstructionId.PressLine, 9f);
            Assert.That(i.Sanitize(), Is.EqualTo(1 << (int)InstructionId.PressLine));
            Assert.That(i.PressLine, Is.EqualTo(PressLineLevel.Mid));
        }
    }
}
