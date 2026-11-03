using System;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-12: 22명 90분(54만 서브스텝) 결정론, 입력 순서 무관.</summary>
    public class KinematicsDeterminismTests
    {
        static ulong Run90(bool shuffle)
        {
            var rng = new SeedRng(20261103UL);
            var squad = RandomSquad(rng);
            if (shuffle)
            {
                var srng = new SeedRng(99UL);
                for (int i = squad.Length - 1; i > 0; i--)
                {
                    var j = srng.Range(0, i);
                    (squad[i], squad[j]) = (squad[j], squad[i]);
                }
            }
            var k = new KinematicsSystem(Params, squad);
            var cmdRng = new SeedRng(31UL);
            for (int t = 0; t < 90 * 60 * 10; t++)
            {
                if (t % 10 == 0) RandomCommands(k, cmdRng);
                for (int s = 0; s < 10; s++) k.Step();
            }
            Assert.That(k.Substep, Is.EqualTo(540_000L));
            var h = StableHasher.Create();
            k.AddTo(ref h);
            return h.Value;
        }

        [Test, Category("Determinism")]
        public void NinetyMin22Players_TwoRuns_SameHash()
        {
            Assert.That(Run90(false), Is.EqualTo(Run90(false)));
        }

        [Test, Category("Determinism")]
        public void ShuffledInputOrder_SameHash()
        {
            Assert.That(Run90(true), Is.EqualTo(Run90(false)));
        }
    }
}
