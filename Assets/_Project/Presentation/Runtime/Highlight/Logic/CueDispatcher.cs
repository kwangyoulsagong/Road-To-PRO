using System;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>시뮬 시각 순 큐 발화 (specs/041 FR-55). 커서만 움직이므로 프레임 경로 할당이 없다.</summary>
    public sealed class CueDispatcher
    {
        PlaybackCue[] _cues = Array.Empty<PlaybackCue>();
        int _next;

        /// <summary>(시각, 일련번호, 종류, 엔티티) 순으로 정렬한다. 장면 구축 때 한 번 부른다.</summary>
        public static void Sort(PlaybackCue[] cues) => Array.Sort(cues, Compare);

        /// <summary>
        /// 큐 순서. 059: 같은 골의 그물 접촉 큐(같은 시각·일련번호 가능 — 모서리 동시 접촉)는 종류·엔티티(접촉 번호)로 갈라
        /// 정렬이 입력 순서에 기대지 않게 한다.
        /// </summary>
        public static int Compare(PlaybackCue a, PlaybackCue b)
        {
            int c = a.Time.CompareTo(b.Time);
            if (c != 0) return c;
            c = a.Seq.CompareTo(b.Seq);
            if (c != 0) return c;
            c = a.Kind.CompareTo(b.Kind);
            return c != 0 ? c : a.Entity.CompareTo(b.Entity);
        }

        /// <summary>정렬된 큐를 물리고 start 이전 큐를 건너뛴다. start 시각의 큐는 발화 대상이다.</summary>
        public void Reset(PlaybackCue[] sortedCues, double start)
        {
            _cues = sortedCues ?? Array.Empty<PlaybackCue>();
            _next = 0;
            while (_next < _cues.Length && _cues[_next].Time < start) _next++;
        }

        /// <summary>upTo까지의 남은 큐를 시각 순으로 한 번씩 보낸다.</summary>
        public void FireUpTo(double upTo, IHighlightAudio sink)
        {
            while (_next < _cues.Length && _cues[_next].Time <= upTo)
            {
                sink.OnCue(in _cues[_next]);
                _next++;
            }
        }
    }
}
