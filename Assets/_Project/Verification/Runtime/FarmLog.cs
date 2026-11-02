using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 플레이어 로그를 logs.jsonl로 모은다 (specs/000 FR-60). 한 줄 = {type, message, stack?}.
    /// 크래시 직전 줄까지 남도록 줄마다 flush한다.
    /// </summary>
    public static class FarmLog
    {
        static readonly object Gate = new object();
        static StreamWriter _w;

        public static void Start(string path)
        {
            lock (Gate)
            {
                if (_w != null) return;
                _w = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
            }
            Application.logMessageReceivedThreaded += OnLog;
        }

        public static void Stop()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            lock (Gate)
            {
                _w?.Dispose();
                _w = null;
            }
        }

        static void OnLog(string message, string stack, LogType type)
        {
            var d = new Dictionary<string, object> { { "type", type.ToString() }, { "message", message } };
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                d["stack"] = stack;
            var line = MiniJson.Serialize(d);
            lock (Gate) _w?.WriteLine(line);
        }
    }
}
