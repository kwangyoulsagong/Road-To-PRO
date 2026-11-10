using System;
using System.Collections.Generic;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>
    /// 장면 역할별 샷 순서 (specs/042 FR-20·21·33). 골 뒤 프레임이 짧으면(대체 심판의 재시작 순간이동) 구간을 줄이되 순서는 지킨다.
    /// </summary>
    public static class GoalSequenceDirector
    {
        /// <param name="baseKind">라이브 구간 샷 (모드에 따라 Main·TopView·MyPlayer).</param>
        public static void Plan(SceneBrief brief, double start, double end, ShotKind baseKind, CameraTuning t, List<ShotPlan> into)
        {
            switch (brief.Role)
            {
                case SceneRole.GoalMain:
                    PlanGoalMain(brief, start, end, baseKind, t, into);
                    break;
                case SceneRole.ReplayAngle:
                    into.Add(new ShotPlan(start, end, ReplayKind(brief), brief.Shooter));
                    break;
                case SceneRole.GoalTail:
                    double split = Math.Min(start + t.ReactionSeconds, end);
                    into.Add(new ShotPlan(start, split, ShotKind.ReactionWide, brief.Scorer));
                    if (split < end) into.Add(new ShotPlan(split, end, ShotKind.KickoffWide));
                    break;
                default:
                    PlanLive(brief, start, end, baseKind, t, into);
                    break;
            }
        }

        /// <summary>골 3개는 하이·슈터·리버스, 그 밖 2개는 하이·슈터. 긴 경로는 첫 앵글이 전체 경로 하이 카메라다 (FR-25).</summary>
        public static ShotKind ReplayKind(SceneBrief b)
        {
            if (b.LongPath && b.AngleIndex == 0) return ShotKind.FallbackHigh;
            switch (b.AngleIndex)
            {
                case 0: return ShotKind.ReplayHigh;
                case 1: return ShotKind.ReplayShooter;
                default: return ShotKind.ReplayReverse;
            }
        }

        static void PlanGoalMain(SceneBrief b, double start, double end, ShotKind baseKind, CameraTuning t, List<ShotPlan> into)
        {
            double goal = Math.Min(Math.Max(b.EventTime, start), end);
            if (goal > start) into.Add(new ShotPlan(start, goal, baseKind));
            if (goal >= end) return;
            double split = Math.Min(goal + t.GoalCloseupSeconds, end);
            into.Add(new ShotPlan(goal, split, ShotKind.Closeup, b.Scorer));
            if (split < end) into.Add(new ShotPlan(split, end, ShotKind.Celebration, b.Scorer));
        }

        static void PlanLive(SceneBrief b, double start, double end, ShotKind baseKind, CameraTuning t, List<ShotPlan> into)
        {
            var cuts = new List<ShotPlan>();
            LiveCutRules.Plan(b.LiveCuts, b.DeadBalls, start, t.MinShotSeconds, cuts);
            double at = start;
            for (int i = 0; i < cuts.Count; i++)
            {
                var c = cuts[i];
                if (c.Start >= end) break;
                if (c.Start > at) into.Add(new ShotPlan(at, c.Start, baseKind));
                double cutEnd = Math.Min(c.End, end);
                into.Add(new ShotPlan(c.Start, cutEnd, c.Kind, c.Focus));
                at = cutEnd;
            }
            if (at < end) into.Add(new ShotPlan(at, end, baseKind));
        }
    }
}
