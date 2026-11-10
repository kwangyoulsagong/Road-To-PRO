using System;
using System.Collections.Generic;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>라이브 컷 규칙 (specs/042 FR-10~13, 56). 요청을 걸러 컷 샷 목록을 만든다.</summary>
    public static class LiveCutRules
    {
        const double SameTime = 1e-6d;

        /// <param name="requests">시각순 정렬 안 해도 된다.</param>
        /// <param name="sceneStart">장면 시작도 컷이므로 최소 샷 길이의 기준이 된다.</param>
        public static void Plan(LiveCutRequest[] requests, DeadBallWindow[] windows, double sceneStart, double minShot, List<ShotPlan> into)
        {
            var sorted = (LiveCutRequest[])requests.Clone();
            Array.Sort(sorted, Compare);
            var used = new bool[windows.Length];
            double lastCut = sceneStart;

            for (int i = 0; i < sorted.Length; i++)
            {
                // FR-56: 같은 시각이면 정렬상 맨 앞(우선순위 최대)만 본다
                if (i > 0 && Math.Abs(sorted[i].Time - sorted[i - 1].Time) <= SameTime) continue;
                var r = sorted[i];
                if (r.FarSide) continue; // FR-13: 라이브는 메인 스탠드 쪽만
                int w = FindWindow(windows, r.Time);
                if (w < 0 || used[w]) continue; // FR-10·11
                if (r.Time - lastCut < minShot || windows[w].End - r.Time < minShot) continue; // FR-12

                used[w] = true;
                into.Add(new ShotPlan(r.Time, windows[w].End, r.Shot, r.Focus));
                lastCut = windows[w].End;
            }
        }

        static int Compare(LiveCutRequest a, LiveCutRequest b)
        {
            int c = a.Time.CompareTo(b.Time);
            return c != 0 ? c : b.Priority.CompareTo(a.Priority);
        }

        static int FindWindow(DeadBallWindow[] windows, double t)
        {
            for (int i = 0; i < windows.Length; i++)
                if (t >= windows[i].Start && t < windows[i].End) return i;
            return -1;
        }
    }
}
