using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>
    /// specs/008 AC-18: 액션 실행(킥·수신·첫 터치) 전후 0.5 s 안의 그 선수에게 005 운동 한계 위반(FR-52) 0건 — 실행 결과가 위치·속도를 직접 바꾸지 않고
    /// 005 명령·한계로만 나타난다(FR-64). 1,000 시드 = 시드마다 경기 600틱 구간. 같은 시드 2회 해시 동일. 판단 원인 위반(010 AC-24 보류)은 액션 창 밖이라 세지 않는다.
    /// </summary>
    public class ActionMotionAuditTests
    {
        sealed class Probe : IActionDiagnostics
        {
            public readonly List<(long tick, int slot)> Contacts = new List<(long, int)>();
            public void OnPassCandidate(long tick, int passer, int receiver, in PassOption option, float deltaXt, float pressure, float score) { }
            public void OnPassChosen(long tick, int passer, int receiver, in PassOption option) { }
            public void OnContact(long tick, int substep, int slot, SimEventKind kind, float value)
            {
                if (kind != SimEventKind.Tackle) Contacts.Add((tick, slot)); // 태클 접촉은 005 접촉 배율 경로(070)
            }
        }

        [Test, Explicit("≈ 수 분, 스펙 마지막 1회"), Category("Slow"), Timeout(1_800_000)]
        public void Fixtures_And1000Seeds_NoLimitBreach()
        {
            int breaches = 0, contacts = 0;
            var report = new System.Text.StringBuilder();
            for (ulong seed = 0; seed < 1000; seed++)
            {
                var sim = MatchTestKit.Sim(300_000UL + seed);
                var mon = new MotionLimitMonitor(1 << 12);
                sim.Context.Kinematics.Monitor = mon;
                var probe = new Probe();
                sim.Context.Actions.Diagnostics = probe;
                sim.RunUntilTick(600);
                contacts += probe.Contacts.Count;
                for (int i = 0; i < mon.Count; i++)
                {
                    var v = mon[i];
                    var tick = v.Substep / 10;
                    foreach (var c in probe.Contacts)
                        if (c.slot == v.PlayerId && tick >= c.tick - 5 && tick <= c.tick + 5)
                        {
                            breaches++;
                            if (breaches <= 10) report.AppendLine($"시드 {seed} 틱 {tick} 슬롯 {v.PlayerId} {v.Kind} {v.Value:F3} > {v.Limit:F3}");
                            break;
                        }
                }
            }
            TestContext.WriteLine($"접촉 {contacts}건, 액션 창 위반 {breaches}건\n{report}");
            Assert.Greater(contacts, 1000);
            Assert.AreEqual(0, breaches, report.ToString());
            var a = MatchTestKit.Sim(300_007UL);
            var b = MatchTestKit.Sim(300_007UL);
            a.RunUntilTick(600);
            b.RunUntilTick(600);
            Assert.AreEqual(a.StateHash(), b.StateHash());
        }
    }
}
