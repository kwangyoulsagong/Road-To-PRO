using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Tests.EditMode.Core
{
    /// <summary>specs/006 FR-60 상태 직렬화 기반: 왕복 비트 동일, 형식 오류 거부.</summary>
    public class StateSerializationTests
    {
        [Test]
        public void RoundTrip_AllTypes_BitEqual()
        {
            var w = new StateWriter(16);
            var rng = new SeedRng(7UL);
            rng.NextU64();
            w.Write((byte)200); w.Write((sbyte)-5); w.Write(true); w.Write((ushort)65000);
            w.Write(-123456); w.Write(uint.MaxValue); w.Write(long.MinValue); w.Write(ulong.MaxValue);
            w.Write(-0f); w.Write(float.Epsilon); w.Write(1.0e-30);
            w.Write(new Vec2(1.5f, -2.25f)); w.Write(new Vec3(3f, 4f, -0f));
            w.Write(rng.SaveState());
            var r = new StateReader(w.ToArray());
            Assert.That(r.ReadByte(), Is.EqualTo(200));
            Assert.That(r.ReadSByte(), Is.EqualTo(-5));
            Assert.That(r.ReadBool(), Is.True);
            Assert.That(r.ReadUShort(), Is.EqualTo(65000));
            Assert.That(r.ReadInt(), Is.EqualTo(-123456));
            Assert.That(r.ReadUInt(), Is.EqualTo(uint.MaxValue));
            Assert.That(r.ReadLong(), Is.EqualTo(long.MinValue));
            Assert.That(r.ReadULong(), Is.EqualTo(ulong.MaxValue));
            Assert.That(BitConverter.SingleToInt32Bits(r.ReadFloat()), Is.EqualTo(BitConverter.SingleToInt32Bits(-0f)), "−0 비트 보존");
            Assert.That(r.ReadFloat(), Is.EqualTo(float.Epsilon));
            Assert.That(r.ReadDouble(), Is.EqualTo(1.0e-30));
            Assert.That(r.ReadVec2(), Is.EqualTo(new Vec2(1.5f, -2.25f)));
            Assert.That(r.ReadVec3(), Is.EqualTo(new Vec3(3f, 4f, -0f)));
            Assert.That(r.ReadRngState(), Is.EqualTo(rng.SaveState()));
            Assert.That(r.AtEnd);
        }

        [Test]
        public void Sections_LengthChecked()
        {
            var w = new StateWriter();
            w.BeginSection(3); w.Write(1); w.Write(2); w.EndSection();
            w.BeginSection(4); w.Write(9L); w.EndSection();
            var r = new StateReader(w.ToArray());
            r.BeginSection(3);
            Assert.That(r.ReadInt(), Is.EqualTo(1));
            Assert.Throws<FormatException>(() => r.EndSection(), "덜 읽으면 형식 불일치");
            r = new StateReader(w.ToArray());
            r.BeginSection(3); r.ReadInt();
            Assert.That(r.ReadInt(), Is.EqualTo(2));
            r.EndSection();
            Assert.Throws<FormatException>(() => r.BeginSection(5), "태그 불일치");
            r = new StateReader(w.ToArray());
            r.BeginSection(3); r.ReadInt(); r.ReadInt(); r.EndSection();
            r.BeginSection(4);
            Assert.Throws<FormatException>(() => { r.ReadLong(); r.ReadInt(); }, "섹션 끝을 넘어 읽으면 거부");
        }

        [Test]
        public void Truncated_Throws()
        {
            var w = new StateWriter();
            w.Write(12345L);
            var a = w.ToArray();
            var r = new StateReader();
            r.Reset(a, 5);
            Assert.Throws<FormatException>(() => r.ReadLong());
            Assert.Throws<FormatException>(() => new StateReader(new byte[] { 2 }).ReadBool(), "bool은 0/1만");
        }

        [Test]
        public void Reset_Reuses_NoGrowth()
        {
            var w = new StateWriter(64);
            for (int k = 0; k < 3; k++)
            {
                w.Reset();
                for (int i = 0; i < 8; i++) w.Write((long)i);
                Assert.That(w.Length, Is.EqualTo(64));
                Assert.That(w.Buffer.Length, Is.EqualTo(64));
            }
            w.Write(1);
            Assert.That(w.Buffer.Length, Is.EqualTo(128), "모자라면 2배");
        }
    }
}
