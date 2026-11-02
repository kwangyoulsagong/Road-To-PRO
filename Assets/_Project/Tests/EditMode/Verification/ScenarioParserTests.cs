using System;
using NUnit.Framework;
using RoadToPro.Verification;

namespace RoadToPro.Tests.EditMode.Verification
{
    // specs/000 T17: 드라이버 쪽 시나리오 해석 (FR-20, 성능 구간 안 캡처 금지, inject 제한)
    public class ScenarioParserTests
    {
        static string Wrap(string steps, string extra = "") =>
            "{\"schemaVersion\":1,\"id\":\"t\",\"spec\":\"000\",\"scene\":\"FarmSampleCards\"," +
            "\"fixture\":{\"save\":null,\"seed\":7},\"profiles\":\"smoke\"" + extra + ",\"steps\":[" + steps + "]}";

        [Test]
        public void Parse_ValidScenario_ReadsFieldsAndDefaults()
        {
            var s = ScenarioDefinition.Parse(Wrap(
                "{\"type\":\"goto\",\"screen\":\"home\"}," +
                "{\"type\":\"capture\",\"name\":\"home\",\"masks\":[{\"element\":\"clock\"},{\"rect\":[0,0,0.5,0.1]}]}," +
                "{\"type\":\"waitFor\",\"element\":\"ok\",\"timeoutSec\":30}"));
            Assert.AreEqual("t", s.Id);
            Assert.AreEqual("FarmSampleCards", s.Scene);
            Assert.AreEqual(7, s.Seed);
            Assert.IsFalse(s.SelfTest);
            Assert.AreEqual(3, s.Steps.Count);
            Assert.AreEqual("goto", s.Steps[0].Type);
            Assert.AreEqual("home", s.Steps[0].GetString("screen"));
            Assert.AreEqual(2, s.Steps[1].GetList("masks").Count);
            Assert.AreEqual(10.0, s.TimeoutFor(s.Steps[0]));
            Assert.AreEqual(30.0, s.TimeoutFor(s.Steps[2]));
        }

        [Test]
        public void CaptureInsidePerf_Rejected()
        {
            var ex = Assert.Throws<FormatException>(() => ScenarioDefinition.Parse(Wrap(
                "{\"type\":\"perfBegin\",\"name\":\"idle\"},{\"type\":\"capture\",\"name\":\"x\"},{\"type\":\"perfEnd\",\"name\":\"idle\"}")));
            StringAssert.Contains("성능 구간", ex.Message);
        }

        [Test]
        public void InjectOutsideSelfTest_Rejected()
        {
            var steps = "{\"type\":\"inject\",\"what\":\"warning\"}";
            var ex = Assert.Throws<FormatException>(() => ScenarioDefinition.Parse(Wrap(steps)));
            StringAssert.Contains("셀프테스트", ex.Message);
            var ok = ScenarioDefinition.Parse(Wrap(steps, ",\"selfTest\":true"));
            Assert.IsTrue(ok.SelfTest);
        }

        [Test]
        public void UnknownStepOrMissingField_Rejected()
        {
            Assert.Throws<FormatException>(() => ScenarioDefinition.Parse(Wrap("{\"type\":\"teleport\"}")));
            Assert.Throws<FormatException>(() => ScenarioDefinition.Parse(Wrap("{\"type\":\"tap\"}")));
            Assert.Throws<FormatException>(() => ScenarioDefinition.Parse(Wrap("{\"type\":\"perfBegin\",\"name\":\"a\"}")));
            Assert.Throws<FormatException>(() => ScenarioDefinition.Parse("{not json"));
        }

        [Test]
        public void MiniJson_ParsesNumbersStringsEscapes()
        {
            var o = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(
                "{\"a\":-1.5e2,\"b\":\"\\u0041\\n\\\"q\\\"\",\"c\":[true,false,null],\"d\":12}");
            Assert.AreEqual(-150.0, (double)o["a"]);
            Assert.AreEqual("A\n\"q\"", o["b"]);
            Assert.AreEqual(3, ((System.Collections.Generic.List<object>)o["c"]).Count);
            Assert.AreEqual(12L, o["d"]);
            Assert.AreEqual("{\"k\":\"v\\n\",\"n\":[1,2.5]}", MiniJson.Serialize(new System.Collections.Generic.Dictionary<string, object>
            {
                { "k", "v\n" }, { "n", new System.Collections.Generic.List<object> { 1L, 2.5 } }
            }));
        }
    }
}
