using NUnit.Framework;

namespace RoadToPro.Tests.EditMode.Verification
{
    /// <summary>
    /// 하네스 셀프테스트 AC-01 전용: 실패 1 + 통과 2. [Explicit]라서 이름으로 직접 지정했을 때만 돈다
    /// (`farm.py self-test logic-fail`). 일반 로직 실행에는 포함되지 않는다.
    /// </summary>
    [Explicit("디바이스팜 셀프테스트 전용 (specs/000 AC-01)")]
    public class SelfTestFixtures
    {
        [Test]
        public void Pass_One() => Assert.AreEqual(1, 1);

        [Test]
        public void Fail_Deliberate() => Assert.AreEqual(2, 1, "셀프테스트: 의도한 실패");

        [Test]
        public void Pass_Two() => Assert.IsTrue(true);
    }
}
