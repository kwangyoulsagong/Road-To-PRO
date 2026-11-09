using System;
using System.Collections.Generic;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>로딩 중 잡은 핸들 목록 (specs/041 FR-58, AC-20). 해제는 잡은 순서의 역순으로 한다.</summary>
    public sealed class LoadHandleTracker
    {
        readonly List<Action> _release = new List<Action>();

        public int Outstanding => _release.Count;

        public void Acquire(Action release) => _release.Add(release ?? throw new ArgumentNullException(nameof(release)));

        public void ReleaseAll()
        {
            for (int i = _release.Count - 1; i >= 0; i--) _release[i]();
            _release.Clear();
        }
    }
}
