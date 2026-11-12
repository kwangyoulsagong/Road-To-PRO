using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Players.Motion;
using RoadToPro.Verification.Samples.Motion;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>
    /// specs/011 AC-24 (3D 쪽): 실제 경기(011 선방 모델) GK 접촉 장면을 054 측정기에 넣어 손–공 거리 ≤ 10 cm, 시각 차 ±33 ms,
    /// 결과와 동작 모순 0건을 본다. 장면은 기준 경기의 GK 접촉 이벤트 앞뒤 3 s 클립.
    /// </summary>
    public class GkContactMeterTests
    {
        static List<MotionReport> _reports;

        static List<MotionReport> Reports()
        {
            if (_reports != null) return _reports;
            _reports = new List<MotionReport>();
            var sim = Sim(20261103UL, WithCheckpoints(CheckpointMode.All));
            sim.RunUntilTick(36_000);
            int scenes = 0;
            for (int i = 0; i < sim.Log.Count && scenes < 6; i++)
            {
                var e = sim.Log[i];
                if (e.Kind != SimEventKind.Catch && e.Kind != SimEventKind.Parry && e.Kind != SimEventKind.Punch) continue;
                var clip = sim.ExtractClip(System.Math.Max(0, e.Tick - 30), e.Tick + 30);
                var data = ClipConverter.ToHighlight(clip, PitchSpec.Default);
                var keeper = Squad.TeamOfSlot(sim.Context.Squad.SlotOf(e.PlayerId)) * Squad.SlotsPerTeam;
                var scene = new MotionScene { Id = $"gk-{e.Kind}-{e.Tick}", Data = data, Actors = new[] { keeper } };
                _reports.Add(MotionRun.Measure(scene));
                scenes++;
            }
            return _reports;
        }

        /// <summary>시각 차 ±33 ms · 결과와 동작 모순 0 (부위·놓침 일치). 손–공 거리는 아래 보류 테스트.</summary>
        [Test]
        public void GkContacts_Timing33ms_NoContradiction()
        {
            var fails = new System.Text.StringBuilder();
            int measured = Check(fails, distance: false);
            Assert.Greater(measured, 0, "측정된 GK 접촉");
            Assert.IsEmpty(fails.ToString());
        }

        /// <summary>
        /// AC-24 손–공 ≤ 10 cm — 보류 (2026-11-11 사용자 결정): 지금 GK 클립은 중간 높이 다이빙(고정 도달 1 m)·가슴 잡기뿐이라
        /// 땅볼·높은 공·짧은 거리 선방에서 손이 103~165 cm 떨어진다. GK 클립(높이 3구간·땅볼·도달 변형)·손 IK 후속 스펙에서 통과시킨다.
        /// </summary>
        [Test, Explicit("011 AC-24 보류 — GK 클립 후속 스펙")]
        public void GkContacts_HandBallWithin10cm()
        {
            var fails = new System.Text.StringBuilder();
            Check(fails, distance: true);
            Assert.IsEmpty(fails.ToString());
        }

        static int Check(System.Text.StringBuilder fails, bool distance)
        {
            int measured = 0;
            foreach (var r in Reports())
            {
                TestContext.WriteLine(MotionRun.Summary(r));
                foreach (var c in r.Contacts)
                {
                    if (c.Kind != SimEventKind.Catch && c.Kind != SimEventKind.Parry && c.Kind != SimEventKind.Punch) continue;
                    if (c.Missed) continue;
                    measured++;
                    if (c.SimPart != BodyPart.Hand || c.ClipPart != BodyPart.Hand) fails.AppendLine($"{r.Scene} 부위 {c.ClipPart}/{c.SimPart}");
                    if (c.TimeError > 0.0335f) fails.AppendLine($"{r.Scene} {c.Clip} 시각 차 {c.TimeError * 1000:0} ms");
                    if (distance && c.Distance > 0.10f) fails.AppendLine($"{r.Scene} {c.Clip} 손–공 {c.Distance * 100:0.0} cm");
                }
            }
            return measured;
        }
    }
}
