using System;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>재생 속도 규칙의 원본 (specs/041 FR-10·11). 경기 중 HUD와 설정 화면이 같은 값을 공유한다.</summary>
    public sealed class PlaybackSettings
    {
        public const float DefaultRate = 1.5f;
        public const float ReplayRate = 0.5f;
        static readonly float[] Allowed = { 1f, 1.5f, 2f, 3f };

        float _rate = DefaultRate;

        public float Rate
        {
            get => _rate;
            set
            {
                if (!IsAllowed(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "허용 속도: 1, 1.5, 2, 3");
                _rate = value;
            }
        }

        public static bool IsAllowed(float rate)
        {
            for (int i = 0; i < Allowed.Length; i++)
                if (Allowed[i] == rate) return true;
            return false;
        }
    }
}
