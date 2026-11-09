using System.Text.RegularExpressions;
using NUnit.Framework;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public class FormationCatalogTests
    {
        static readonly string[] Ids = { "4-3-3", "4-2-3-1", "4-4-2", "3-5-2", "3-4-3", "5-3-2" };

        [Test]
        public void Defaults_AllValid()
        {
            var c = ShapeTestKit.Catalog;
            Assert.That(c.Count, Is.EqualTo(6));
            foreach (var id in Ids)
            {
                var f = c[c.IndexOf(id)];
                Assert.That(f.Slots.Length, Is.EqualTo(11), id);
                Assert.That(f.Slots[0].Position, Is.EqualTo(SlotPosition.GK), id);
                foreach (var s in f.Slots) Assert.That(s.Anchors.Length, Is.EqualTo(ShapeProfiles.Count), id);
            }
        }

        [Test]
        public void MissingSlot_Rejected()
        {
            var json = Regex.Replace(ShapeTestKit.FormationsJson, @"\{\s*""id"":\s*""LB""[\s\S]*?\}\s*\}\s*,", "", RegexOptions.None, System.TimeSpan.FromSeconds(5));
            Assert.Throws<FormationLoadException>(() => FormationCatalog.Parse(json));
        }

        [Test]
        public void DuplicateId_Rejected()
        {
            var json = ShapeTestKit.FormationsJson;
            var i = json.IndexOf("\"id\": \"LCB\"", System.StringComparison.Ordinal);
            var bad = json.Substring(0, i) + "\"id\": \"LB\"" + json.Substring(i + "\"id\": \"LCB\"".Length);
            Assert.That(() => FormationCatalog.Parse(bad), Throws.TypeOf<FormationLoadException>().With.Message.Contains("중복"));
        }

        [Test]
        public void AnchorOutOfRange_Rejected()
        {
            var json = ShapeTestKit.FormationsJson;
            var bad = json.Replace("-0.1571", "-0.9000");
            Assert.That(() => FormationCatalog.Parse(bad), Throws.TypeOf<FormationLoadException>().With.Message.Contains("범위"));
        }
    }
}
