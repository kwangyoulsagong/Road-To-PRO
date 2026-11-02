using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Logging;
using UnityEngine;
using UnityEngine.TestTools;

namespace RoadToPro.Tests.EditMode.Presentation
{
    public class UnityLoggerTests
    {
        [Test]
        public void Warning_Error_ReachUnityLog()
        {
            var log = new UnityLogger(RoadToPro.Core.Logging.LogLevel.Info);
            LogAssert.Expect(LogType.Warning, "[RoadToPro] 경고 1");
            LogAssert.Expect(LogType.Error, "[RoadToPro] 에러 2");
            log.Warning("경고 {0}", 1);
            log.Error("에러 {0}", 2);
            log.Debug("보이지 않음");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Info_GoesToLog()
        {
            var log = new UnityLogger(RoadToPro.Core.Logging.LogLevel.Trace);
            LogAssert.Expect(LogType.Log, "[RoadToPro] 정보");
            log.Info("정보");
        }
    }
}
