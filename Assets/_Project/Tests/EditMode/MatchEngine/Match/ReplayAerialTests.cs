using NUnit.Framework;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.Presentation.Highlight;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>
    /// specs/083 FR-01 · AC-01·06: 하이라이트 클립(ReplayClip)이 구간 안 Header 이벤트가 가리키는 071 경합 기록 묶음을 읽기 전용으로 싣는다.
    /// 시뮬 상태·해시는 바뀌지 않는다.
    /// </summary>
    public class ReplayAerialTests
    {
        const ulong Seed = 20261220UL; // 082 고정 시드: 놓친 경합이 하이라이트에 있다 (083 T02)

        static SimEvent FirstMissed(SimEvent[] log)
        {
            foreach (var e in log)
                if (e.Kind == SimEventKind.Header && e.A == (int)HeaderOutcome.Missed && e.B > 0) return e;
            Assert.Fail("놓친 경합 Header 이벤트 없음");
            return default;
        }

        [Test]
        public void ExtractClip_CarriesGroupRecords()
        {
            var sim = LiveContactAudit.Played90(Seed);
            var ev = FirstMissed(sim.Result.EventLog);
            var clip = sim.ExtractClip(ev.Tick - 30, ev.Tick + 10);
            var log = sim.Context.Actions.Details.Aerials;
            int first = ev.B - 1;
            Assert.IsTrue(log.IsAvailable(first));
            int n = log[first].GroupCount;
            int found = 0;
            foreach (var r in clip.Aerials)
            {
                if (r.GroupFirst != first) continue;
                var want = log[first + found];
                Assert.AreEqual(want.Slot, r.Slot);
                Assert.AreEqual(want.ApexSubstep, r.ApexSubstep);
                Assert.AreEqual(want.JumpSubstep, r.JumpSubstep);
                Assert.AreEqual(want.ApexHeight, r.ApexHeight);
                Assert.AreEqual(want.Won, r.Won);
                found++;
            }
            Assert.AreEqual(n, found, "묶음 참여자 전부");
        }

        [Test]
        public void CollectGroups_RecordUnavailable_Skipped()
        {
            var log = new AerialLog();
            log.Add(new AerialDetail { Slot = 3, GroupFirst = 0, GroupCount = 2, Won = true });
            log.Add(new AerialDetail { Slot = 15, GroupFirst = 0, GroupCount = 2 });
            var ok = SimEvent.Make(SimEventKind.Header); ok.B = 1;
            var gone = SimEvent.Make(SimEventKind.Header); gone.B = 9;   // 넘침·창 밖
            var none = SimEvent.Make(SimEventKind.Header); none.B = 0;   // 기록 없음
            var other = SimEvent.Make(SimEventKind.Shot); other.B = 1;   // Header 아님
            var got = log.CollectGroups(new[] { ok, gone, none, other });
            Assert.AreEqual(2, got.Length);
            Assert.AreEqual(3, got[0].Slot);
            Assert.AreEqual(15, got[1].Slot);
            Assert.AreEqual(0, log.CollectGroups(new SimEvent[0]).Length);
        }

        [Test]
        public void ExtractClip_DoesNotChangeHash()
        {
            // 073 T19: 고정 9,000틱 대신 같은 시드 90분 경기의 첫 놓친 경합 + 20틱까지 (엔진 개정으로 경합 시각이 바뀌어도 의도 유지)
            var ev = FirstMissed(LiveContactAudit.Played90(Seed).Result.EventLog);
            var sim = LiveContactAudit.NewSim(Seed);
            sim.RunUntilTick(ev.Tick + 20);
            var before = sim.StateHash();
            sim.ExtractClip(ev.Tick - 30, ev.Tick + 10);
            Assert.AreEqual(before, sim.StateHash());
            sim.RunToEnd();
            Assert.AreEqual(LiveContactAudit.Played90(Seed).StateHash(), sim.StateHash(), "추출한 경기와 안 한 경기의 끝 해시가 같다");
        }
    }
}
