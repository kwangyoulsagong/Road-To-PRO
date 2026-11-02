using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;

namespace RoadToPro.Tests.EditMode.Core
{
    public class PitchCoordsTests
    {
        [Test]
        public void Center_Is52_5_34()
        {
            var m = PitchCoords.ToMeters(new Vec2(0.5f, 0.5f), PitchSpec.Default);
            Assert.That(m, Is.EqualTo(new Vec2(52.5f, 34f)));
            Assert.That(PitchCoords.ToNormalized(m, PitchSpec.Default), Is.EqualTo(new Vec2(0.5f, 0.5f)));
        }

        [Test]
        public void CustomPitchSize_Converts()
        {
            var spec = new PitchSpec(100f, 64f);
            Assert.That(PitchCoords.ToMeters(new Vec2(1f, 0.25f), spec), Is.EqualTo(new Vec2(100f, 16f)));
        }

        [Test]
        public void Perspective_RoundTrip_Within1Ulp()
        {
            const float Ulp1 = 1.2e-7f;
            for (int i = 0; i <= 1000; i++)
            {
                var p = new Vec2(i / 1000f, (1000 - i) / 1000f * 0.731f);
                for (int period = 0; period < 4; period++)
                {
                    var away = PitchCoords.ToPerspective(p, TeamSide.Away, period);
                    var back = PitchCoords.FromPerspective(away, TeamSide.Away, period);
                    Assert.That(DMath.Abs(back.X - p.X), Is.LessThanOrEqualTo(Ulp1));
                    Assert.That(DMath.Abs(back.Y - p.Y), Is.LessThanOrEqualTo(Ulp1));
                    // 같은 입력 → 같은 비트
                    Assert.That(PitchCoords.ToPerspective(p, TeamSide.Away, period), Is.EqualTo(away));
                }
            }
        }

        [Test]
        public void Perspective_HomeFirstHalf_Identity_SidesAndHalvesFlip()
        {
            var p = new Vec2(0.25f, 0.75f);
            Assert.That(PitchCoords.ToPerspective(p, TeamSide.Home, 0), Is.EqualTo(p));
            Assert.That(PitchCoords.ToPerspective(p, TeamSide.Away, 0), Is.EqualTo(new Vec2(0.75f, 0.25f)));
            Assert.That(PitchCoords.ToPerspective(p, TeamSide.Home, 1), Is.EqualTo(new Vec2(0.75f, 0.25f)));
            Assert.That(PitchCoords.ToPerspective(p, TeamSide.Away, 1), Is.EqualTo(p));
        }
    }
}
