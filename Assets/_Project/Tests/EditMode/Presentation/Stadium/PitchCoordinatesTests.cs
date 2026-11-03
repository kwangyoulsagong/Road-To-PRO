using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Presentation.Stadium;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>시뮬 좌표 ↔ Unity 월드 (specs/044 FR-01): X = 길이, Z = 폭, Y = 위, 센터마크 원점.</summary>
    public class PitchCoordinatesTests
    {
        [Test]
        public void SimMeters_ToWorld()
        {
            Assert.AreEqual(Vector3.zero, PitchCoordinates.ToWorld(new Vec3(52.5f, 34f, 0f), PitchSpec.Default));
            Assert.AreEqual(new Vector3(-52.5f, 0f, -34f), PitchCoordinates.ToWorld(new Vec3(0f, 0f, 0f), PitchSpec.Default));
            Assert.AreEqual(new Vector3(52.5f, 2.44f, 34f), PitchCoordinates.ToWorld(new Vec3(105f, 68f, 2.44f), PitchSpec.Default));
        }

        [Test]
        public void Normalized_ToWorld()
        {
            Assert.AreEqual(Vector3.zero, PitchCoordinates.NormalizedToWorld(new Vec2(0.5f, 0.5f), 0f, PitchSpec.Default));
            Assert.AreEqual(new Vector3(52.5f, 1f, 34f), PitchCoordinates.NormalizedToWorld(new Vec2(1f, 1f), 1f, PitchSpec.Default));
        }

        [Test]
        public void RoundTrip()
        {
            var p = new Vec3(12.25f, 60.5f, 1.5f);
            var back = PitchCoordinates.ToSim(PitchCoordinates.ToWorld(p, PitchSpec.Default), PitchSpec.Default);
            Assert.AreEqual(p.X, back.X, 1e-5f);
            Assert.AreEqual(p.Y, back.Y, 1e-5f);
            Assert.AreEqual(p.Z, back.Z, 1e-5f);
        }
    }
}
