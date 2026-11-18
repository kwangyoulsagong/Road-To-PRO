using System.Reflection;
using NUnit.Framework;
using RoadToPro.MatchEngine.PitchControl;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.PitchControl
{
    /// <summary>specs/007 T03: `pcm.*` 파일 기본값 = 코드 기본값, 정의 수 = 필드 수.</summary>
    public class PitchControlParamsTests
    {
        [Test]
        public void FileMatchesCodeDefaults()
        {
            var bundle = MatchTestKit.ActionTuning();
            var fromFile = PitchControlParams.Create(bundle);
            var n = 0;
            foreach (var f in typeof(PitchControlParams).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(float)) continue;
                Assert.AreEqual((float)f.GetValue(PitchControlParams.Defaults), (float)f.GetValue(fromFile), f.Name);
                n++;
            }
            var pcm = 0;
            foreach (var d in bundle.Registry.Definitions) if (d.Id.StartsWith(PitchControlTuningIds.Prefix)) pcm++;
            Assert.AreEqual(pcm, n, "pcm.* 정의 수 = 코드 필드 수");
            foreach (var f in typeof(PitchControlTuningIds).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var id = (string)f.GetValue(null);
                if (id == PitchControlTuningIds.Prefix) continue;
                Assert.IsTrue(bundle.Registry.TryGetIndex(id, out _), $"파일에 {id} 없음");
            }
        }

        [Test]
        public void PaperDefaults_AttrOff_RestSame()
        {
            var p = PitchControlParams.PaperDefaults();
            Assert.IsFalse(p.AttrOn);
            Assert.AreEqual(PitchControlParams.Defaults.Sigma, p.Sigma);
            Assert.AreEqual(PitchControlParams.Defaults.Lambda, p.Lambda);
            Assert.AreEqual(32, p.GridCols);
            Assert.AreEqual(20, p.GridRows);
        }
    }
}
