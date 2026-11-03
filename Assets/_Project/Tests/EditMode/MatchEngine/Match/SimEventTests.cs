using NUnit.Framework;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-11: 일련번호·정렬·시각 표기. FR-32 정렬 키, FR-33 버전, 긴 경기 로그.</summary>
    public class SimEventTests
    {
        [Test]
        public void SeqMonotonic_SortKeyMatchesOutput()
        {
            var sim = Reference();
            var log = sim.Log;
            Assert.That(log.Count, Is.GreaterThan(100));
            for (int i = 0; i < log.Count; i++)
            {
                Assert.That(log[i].Seq, Is.EqualTo(i + 1L), "일련번호는 1부터 1씩");
                if (i > 0) Assert.That(SimEvent.CompareKey(in log[i - 1], in log[i]), Is.LessThanOrEqualTo(0), $"정렬 위반 @{i}: {log[i - 1]} → {log[i]}");
                Assert.That(log[i].Substep, Is.InRange((sbyte)-1, (sbyte)10));
            }
        }

        [Test]
        public void TimeFormat_90Plus3()
        {
            var e = new SimEvent { Period = 1, Clock = (int)(MatchTime.NominalTicks(MatchPeriod.SecondHalf) + 3 * 600 + 12 * 10) };
            Assert.That(e.Time.Format(false), Is.EqualTo("90+3"));
            Assert.That(e.Time.Format(), Is.EqualTo("90+3:12"));
        }

        [Test]
        public void ReferenceMatch_AnnouncementAt45And90_WhistleInAddedTime()
        {
            var sim = Reference();
            var ann = Events(sim, SimEventKind.AddedTimeAnnounced);
            Assert.That(ann.Count, Is.EqualTo(2));
            Assert.That(ann[0].Time.Format(), Is.EqualTo("45:00"));
            Assert.That(ann[1].Time.Format(), Is.EqualTo("90:00"));
            var ends = Events(sim, SimEventKind.PeriodEnd);
            Assert.That(ends.Count, Is.EqualTo(2));
            for (int p = 0; p < 2; p++)
            {
                var added = ends[p].B;
                var baseMin = p == 0 ? "45" : "90";
                Assert.That(ends[p].Time.Format(false), Is.EqualTo(added == 0 ? baseMin + ":00" : baseMin + "+" + added / 600), "휘슬 표기");
                Assert.That(added, Is.GreaterThanOrEqualTo(ann[p].A * 600), "실제 추가 ≥ 발표");
            }
        }

        [Test]
        public void SameSubstep_OrderedBySubstepStageThenEmitOrder()
        {
            var pub = new EventPublisher();
            var log = new SimEventLog(16);
            pub.Emit(5, 8, Mk(SimEventKind.Foul, 3));      // 심판, 서브스텝 3
            pub.Emit(5, 7, Mk(SimEventKind.Tackle, 3));    // 물리, 서브스텝 3
            pub.Emit(5, 7, Mk(SimEventKind.Pass, 0));      // 물리, 서브스텝 0
            pub.Emit(5, 5, Mk(SimEventKind.InputApplied, -1));
            pub.Emit(5, SimEvent.FlowStage, Mk(SimEventKind.PeriodEnd, SimEvent.TickEnd));
            pub.Emit(5, 7, Mk(SimEventKind.Duel, 3));      // 같은 서브스텝·단계 → 발생 순
            pub.Flush(log, null);
            var order = new[] { SimEventKind.InputApplied, SimEventKind.Pass, SimEventKind.Tackle, SimEventKind.Duel, SimEventKind.Foul, SimEventKind.PeriodEnd };
            Assert.That(log.Count, Is.EqualTo(order.Length));
            for (int i = 0; i < order.Length; i++) Assert.That(log[i].Kind, Is.EqualTo(order[i]), $"#{i}");
            Assert.That(pub.Pending, Is.EqualTo(0));
        }

        static SimEvent Mk(SimEventKind k, sbyte sub)
        {
            var e = SimEvent.Make(k);
            e.Substep = sub;
            return e;
        }

        [Test]
        public void LongLog_GrowsAndKeepsSeq_HashDeterministic()
        {
            var a = new SimEventLog(1024);
            var b = new SimEventLog(1024);
            for (int i = 0; i < 100_000; i++)
            {
                var e = SimEvent.Make(SimEventKind.Pass, i & 1, i);
                e.Tick = i / 4;
                a.Append(ref e);
                var e2 = SimEvent.Make(SimEventKind.Pass, i & 1, i);
                e2.Tick = i / 4;
                b.Append(ref e2);
            }
            Assert.That(a.LastSeq, Is.EqualTo(100_000L));
            Assert.That(a.Capacity, Is.GreaterThanOrEqualTo(100_000));
            Assert.That(a.Hash, Is.EqualTo(b.Hash));
            var bad = SimEvent.Make(SimEventKind.Pass);
            bad.Tick = 0;
            Assert.Throws<System.InvalidOperationException>(() => a.Append(ref bad), "정렬 키가 뒤로 가면 거부");
        }

        [Test]
        public void ReferenceMatch_LogMemoryWithinBudget()
        {
            var sim = Reference();
            Assert.That(sim.Log.ApproxBytes, Is.LessThanOrEqualTo(2L * 1024 * 1024), "이벤트 로그 ≤ 2 MB");
            Assert.That(SimEventLog.SchemaVersion, Is.EqualTo(1));
            Assert.That(System.Runtime.InteropServices.Marshal.SizeOf<SimEvent>(), Is.LessThanOrEqualTo(SimEventLog.EventSizeBytes));
        }
    }
}
