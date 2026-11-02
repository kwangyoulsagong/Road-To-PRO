using System;

namespace RoadToPro.Core.Tuning
{
    /// <summary>튜닝 로드 실패. 메시지에 문제 id(있으면)를 넣는다 (FR-82).</summary>
    public sealed class TuningLoadException : Exception
    {
        public readonly string Id;

        public TuningLoadException(string id, string message, Exception inner = null)
            : base(id != null ? $"튜닝 '{id}': {message}" : $"튜닝: {message}", inner)
        {
            Id = id;
        }
    }
}
