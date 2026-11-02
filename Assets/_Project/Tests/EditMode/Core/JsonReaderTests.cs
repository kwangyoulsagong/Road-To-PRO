using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Serialization;

namespace RoadToPro.Tests.EditMode.Core
{
    public class JsonReaderTests
    {
        [Test]
        public void Parse_AllTypes_Nested()
        {
            var o = (JsonObject)JsonReader.Parse("{\"a\": 1.5e2, \"b\": [true, false, null, \"x\\n\\u00e9\"], \"c\": {\"d\": -0.25}}");
            Assert.That(o.TryGet("a", out var a) && (double)a == 150.0);
            Assert.That(o.TryGet("b", out var b));
            var arr = (List<object>)b;
            Assert.That(arr[0], Is.EqualTo(true));
            Assert.That(arr[1], Is.EqualTo(false));
            Assert.That(arr[2], Is.Null);
            Assert.That(arr[3], Is.EqualTo("x\né"));
            Assert.That(((JsonObject)((JsonObject)o).Members[2].Value).TryGet("d", out var d) && (double)d == -0.25);
        }

        [Test]
        public void Parse_DuplicateKeys_Preserved()
        {
            var o = (JsonObject)JsonReader.Parse("{\"k\":1,\"k\":2}");
            Assert.That(o.Members.Count, Is.EqualTo(2));
        }

        [Test]
        public void Parse_Errors_LineColumn()
        {
            var e = Assert.Throws<JsonException>(() => JsonReader.Parse("{\n  \"a\": 1,\n  \"b\" 2\n}"));
            Assert.That(e.Line, Is.EqualTo(3));
            Assert.Throws<JsonException>(() => JsonReader.Parse("[1, 2"));
            Assert.Throws<JsonException>(() => JsonReader.Parse("{\"a\": 1} x"));
            Assert.Throws<JsonException>(() => JsonReader.Parse("1,5"));
            Assert.Throws<JsonException>(() => JsonReader.Parse(""));
        }

        [Test]
        public void Parse_Number_InvariantCulture()
        {
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.That(JsonReader.Parse("0.5"), Is.EqualTo(0.5));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prev;
            }
        }
    }
}
