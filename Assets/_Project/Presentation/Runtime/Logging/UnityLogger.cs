using RoadToPro.Core.Logging;

namespace RoadToPro.Presentation.Logging
{
    /// <summary>
    /// 코어 <see cref="ILogger"/> → Unity 콘솔 (specs/001 FR-72). 000 하네스의 FarmLog가 Unity 로그를 logs.jsonl로 모으므로
    /// 코어의 경고·에러가 디바이스팜 판정(경고·에러 0)에 그대로 잡힌다. 접두사 <c>[RoadToPro]</c>.
    /// </summary>
    public sealed class UnityLogger : ILogger
    {
        public const string Prefix = "[RoadToPro] ";

        readonly RoadToPro.Core.Logging.LogLevel _min;

        public UnityLogger(RoadToPro.Core.Logging.LogLevel minLevel)
        {
            _min = minLevel;
        }

        public bool IsEnabled(RoadToPro.Core.Logging.LogLevel level) => level >= _min && level != RoadToPro.Core.Logging.LogLevel.None;

        public void Log(RoadToPro.Core.Logging.LogLevel level, string message)
        {
            if (!IsEnabled(level)) return;
            switch (level)
            {
                case RoadToPro.Core.Logging.LogLevel.Error:
                    UnityEngine.Debug.LogError(Prefix + message);
                    break;
                case RoadToPro.Core.Logging.LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(Prefix + message);
                    break;
                default:
                    UnityEngine.Debug.Log(Prefix + message);
                    break;
            }
        }
    }
}
