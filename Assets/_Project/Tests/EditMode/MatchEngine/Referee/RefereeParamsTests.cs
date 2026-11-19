using System.IO;
using System.Reflection;
using NUnit.Framework;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Referee;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 T03: `ref.*` 파일 기본값 = 코드 기본값, 정의 수 = 필드 수, 프로필 파일 (실명 없음).</summary>
    public class RefereeParamsTests
    {
        [Test]
        public void FileMatchesCodeDefaults()
        {
            var bundle = TuningBundle.Defaults(MatchTestKit.MatchRegistry());
            var fromFile = RefereeParams.Create(bundle);
            var n = 0;
            foreach (var f in typeof(RefereeParams).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(float)) continue;
                Assert.AreEqual((float)f.GetValue(RefereeParams.Defaults), (float)f.GetValue(fromFile), f.Name);
                n++;
            }
            var refs = 0;
            foreach (var d in bundle.Registry.Definitions) if (d.Id.StartsWith(RefereeTuningIds.Prefix)) refs++;
            Assert.AreEqual(refs, n, "ref.* 정의 수 = 코드 필드 수");
            foreach (var f in typeof(RefereeTuningIds).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var id = (string)f.GetValue(null);
                if (id == RefereeTuningIds.Prefix) continue;
                Assert.IsTrue(bundle.Registry.TryGetIndex(id, out _), $"파일에 {id} 없음");
            }
        }

        [Test]
        public void Load_20Profiles_NoNames()
        {
            var json = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Data", "referees", "profiles.json"));
            var list = RefereeProfile.ParseList(json);
            Assert.GreaterOrEqual(list.Count, 21, "리그 평균 + 가상 20개");
            var avg = list[0];
            Assert.AreEqual(RefereeProfile.LeagueAverage.Id, avg.Id);
            var la = RefereeProfile.LeagueAverage; // 결정론 리뷰 7: 코드 리그 평균 = 데이터 첫 프로필 7개 값
            Assert.AreEqual((la.Strictness, la.FoulThreshold, la.Advantage, la.Consistency, la.HomeBias, la.Fitness, la.PressureResistance),
                (avg.Strictness, avg.FoulThreshold, avg.Advantage, avg.Consistency, avg.HomeBias, avg.Fitness, avg.PressureResistance));
            StringAssert.DoesNotContain("\"name\"", json, "이름 필드 없음 (FR-34, 01-legal)");
            Assert.Throws<System.FormatException>(() => RefereeProfile.ParseList("{\"profiles\":[{\"id\":\"x\",\"name\":\"y\"}]}"));
            Assert.Throws<System.FormatException>(() => RefereeProfile.ParseList("{\"profiles\":[{\"id\":\"x\",\"strictness\":1.5,\"foulThreshold\":0,\"advantage\":0,\"consistency\":0,\"homeBias\":0,\"fitness\":0,\"pressureResistance\":0}]}"));
        }

        [Test]
        public void RuleSet_Default_Is2026_27()
        {
            Assert.AreSame(RuleSet.Season2026_27, RuleSet.Default);
            Assert.IsTrue(RuleSet.Default.NoDogsoCautionAfterAdvantageGoal);
            Assert.IsFalse(RuleSet.Season2025_26.NoDogsoCautionAfterAdvantageGoal);
            Assert.IsTrue(RuleSet.Season2025_26.KeeperHoldCorner);
        }
    }
}
