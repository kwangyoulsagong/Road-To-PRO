using System.Collections.Generic;

namespace RoadToPro.Core.Logging
{
    /// <summary>메모리에 쌓는 로거 (테스트·헤드리스 시뮬). 최소 레벨 이상만 기록.</summary>
    public sealed class MemoryLogger : ILogger
    {
        public readonly struct Entry
        {
            public readonly LogLevel Level;
            public readonly string Message;

            public Entry(LogLevel level, string message)
            {
                Level = level;
                Message = message;
            }
        }

        readonly LogLevel _min;
        readonly List<Entry> _entries = new List<Entry>();

        public MemoryLogger(LogLevel minLevel)
        {
            _min = minLevel;
        }

        public IReadOnlyList<Entry> Entries => _entries;

        public bool IsEnabled(LogLevel level) => level >= _min && level != LogLevel.None;

        public void Log(LogLevel level, string message)
        {
            if (IsEnabled(level)) _entries.Add(new Entry(level, message));
        }

        public void Clear() => _entries.Clear();
    }
}
