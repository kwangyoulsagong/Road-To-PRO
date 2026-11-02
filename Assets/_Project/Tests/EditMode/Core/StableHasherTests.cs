using System;
using NUnit.Framework;
using RoadToPro.Core.Hashing;

namespace RoadToPro.Tests.EditMode.Core
{
    public class StableHasherTests
    {
        static ulong HashLog(float extra)
        {
            // 고정 이벤트 로그: (틱, 종류, 선수 id, 위치 x, 위치 y, 설명)
            var h = StableHasher.Create();
            for (int i = 0; i < 100; i++)
            {
                h.Add((long)i * 10);
                h.Add(i % 7);
                h.Add((uint)(1000 + i));
                h.Add(i * 0.25f);
                h.Add(i == 50 ? extra : 1.5f);
                h.Add(i % 3 == 0 ? "패스" : "pass");
                h.Add(i % 2 == 0);
                h.Add(i / 3.0);
            }
            return h.Value;
        }

        [Test, Category("Determinism")]
        public void FixedLog_MatchesGoldenHash()
        {
            // 프로세스·실행과 무관한 고정 기대값. 해시 형식이 바뀌면 의도적으로 갱신해야 한다.
            Assert.That(HashLog(1.5f).ToString("x16"), Is.EqualTo(GoldenHex));
        }

        // T16에서 Python 독립 구현(struct.pack 리틀엔디언 + FNV-1a 64)으로 같은 값을 교차 확인
        const string GoldenHex = "a81a70efe79a54bb";

        [Test, Category("Determinism")]
        public void Fnv1a_PublicVectors()
        {
            Assert.That(StableHasher.Create().Value, Is.EqualTo(14695981039346656037UL)); // 빈 입력
            var h = StableHasher.Create();
            h.Add((byte)'a');
            Assert.That(h.Value, Is.EqualTo(0xaf63dc4c8601ec8cUL));
            Assert.That(default(StableHasher).Value, Is.EqualTo(StableHasher.OffsetBasis));
        }

        [Test, Category("Determinism")]
        public void OneUlp_Differs()
        {
            var next = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(1.5f) + 1);
            Assert.That(HashLog(next), Is.Not.EqualTo(HashLog(1.5f)));
        }

        [Test, Category("Determinism")]
        public void NegZero_EqualsPosZero()
        {
            Assert.That(HashLog(-0f), Is.EqualTo(HashLog(0f)));
            var a = StableHasher.Create();
            a.Add(-0.0);
            var b = StableHasher.Create();
            b.Add(0.0);
            Assert.That(a.Value, Is.EqualTo(b.Value));
        }

        [Test, Category("Determinism")]
        public void NaNPatterns_Equal()
        {
            var otherNaN = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00001u));
            Assert.That(float.IsNaN(otherNaN));
            Assert.That(HashLog(otherNaN), Is.EqualTo(HashLog(float.NaN)));
        }

        [Test]
        public void Strings_LengthPrefixed_Utf8()
        {
            var a = StableHasher.Create();
            a.Add("ab");
            a.Add("c");
            var b = StableHasher.Create();
            b.Add("a");
            b.Add("bc");
            Assert.That(a.Value, Is.Not.EqualTo(b.Value));

            var c = StableHasher.Create();
            c.Add("가😀");
            var d = StableHasher.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes("가😀");
            d.Add((uint)bytes.Length);
            d.Add(bytes);
            Assert.That(c.Value, Is.EqualTo(d.Value));
        }
    }
}
