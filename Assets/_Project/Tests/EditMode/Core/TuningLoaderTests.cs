using System.IO;
using System.Text;
using NUnit.Framework;
using RoadToPro.Core.Tuning;

namespace RoadToPro.Tests.EditMode.Core
{
    public class TuningLoaderTests
    {
        const string Defs = @"{""version"":1,""definitions"":[
            {""id"":""ball.drag"",""default"":0.25,""min"":0,""max"":1,""unit"":""1/s"",""tag"":""[설계값]"",""source"":""docs/index.html § 5.1""},
            {""id"":""player.vmax"",""default"":8.95,""min"":5,""max"":12,""unit"":""m/s"",""tag"":""[V]"",""source"":""research 01 B-1""}
        ]}";

        static TuningRegistry Reg() => TuningLoader.ParseDefinitions(Defs);

        static TuningLoadException Fails(string valuesJson) =>
            Assert.Throws<TuningLoadException>(() => TuningLoader.ParseBundle(Reg(), valuesJson));

        [Test]
        public void OutOfRange_Fails_WithId()
        {
            var e = Fails(@"{""version"":1,""values"":[{""id"":""player.vmax"",""value"":20}]}");
            StringAssert.Contains("player.vmax", e.Message);
        }

        [Test]
        public void Duplicate_Fails_WithId()
        {
            var e = Fails(@"{""version"":1,""values"":[{""id"":""ball.drag"",""value"":0.3},{""id"":""ball.drag"",""value"":0.4}]}");
            StringAssert.Contains("ball.drag", e.Message);
            var d = Assert.Throws<TuningLoadException>(() => TuningLoader.ParseDefinitions(
                @"{""version"":1,""definitions"":[{""id"":""a"",""default"":0,""min"":0,""max"":1,""tag"":""[제안]""},{""id"":""a"",""default"":0,""min"":0,""max"":1,""tag"":""[제안]""}]}"));
            StringAssert.Contains("'a'", d.Message);
        }

        [Test]
        public void Unknown_Fails_WithId()
        {
            var e = Fails(@"{""version"":1,""values"":[{""id"":""nope.value"",""value"":1}]}");
            StringAssert.Contains("nope.value", e.Message);
        }

        [Test]
        public void UnitMismatch_Fails_WithId()
        {
            var e = Fails(@"{""version"":1,""values"":[{""id"":""player.vmax"",""value"":9,""unit"":""km/h""}]}");
            StringAssert.Contains("player.vmax", e.Message);
        }

        [Test]
        public void BadFiles_Fail()
        {
            Fails("");
            Fails("{");
            Fails(@"{""version"":2,""values"":[]}");
            Assert.Throws<TuningLoadException>(() => TuningLoader.LoadBundleFile(Reg(), "/nonexistent/tuning.json"));
            Assert.Throws<TuningLoadException>(() => TuningLoader.ParseDefinitions(
                @"{""version"":1,""definitions"":[{""id"":""a"",""default"":5,""min"":0,""max"":1,""tag"":""[제안]""}]}"));
            Assert.Throws<TuningLoadException>(() => TuningLoader.ParseDefinitions(
                @"{""version"":1,""definitions"":[{""id"":""a"",""default"":0,""min"":0,""max"":1,""tag"":""제안""}]}"));
        }

        [Test]
        public void Valid_HashChangesWithValue()
        {
            var reg = Reg();
            var b1 = TuningLoader.ParseBundle(reg, @"{""version"":1,""values"":[{""id"":""ball.drag"",""value"":0.3,""unit"":""1/s""}]}");
            Assert.That(b1.Get("ball.drag"), Is.EqualTo(0.3f));
            Assert.That(b1.Get("player.vmax"), Is.EqualTo(8.95f)); // 빠진 id는 기본값
            var b1Again = TuningLoader.ParseBundle(reg, @"{""version"":1,""values"":[{""id"":""ball.drag"",""value"":0.3}]}");
            Assert.That(b1Again.Hash, Is.EqualTo(b1.Hash));
            var b2 = TuningLoader.ParseBundle(reg, @"{""version"":1,""values"":[{""id"":""ball.drag"",""value"":0.31}]}");
            Assert.That(b2.Hash, Is.Not.EqualTo(b1.Hash));
            Assert.That(TuningBundle.Defaults(reg).Hash, Is.Not.EqualTo(b1.Hash));
            Assert.That(reg.Get("ball.drag").Tag, Is.EqualTo("[설계값]"));
        }

        [Test]
        public void File_RoundTrip()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rtp-tuning-test");
            Directory.CreateDirectory(dir);
            var defs = Path.Combine(dir, "defs.json");
            File.WriteAllText(defs, Defs, new UTF8Encoding(false));
            var reg = TuningLoader.LoadDefinitionsFile(defs);
            Assert.That(reg.Count, Is.EqualTo(2));
            Assert.That(reg.Definitions[0].Id, Is.EqualTo("ball.drag")); // 서수 정렬
        }
    }
}
