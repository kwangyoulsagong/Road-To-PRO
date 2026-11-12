using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 AC-25: 오프사이드 스냅샷 상세 기록 ↔ 패스 이벤트 일련번호 연결, 해시 포함, 스냅샷 왕복.</summary>
    public class ActionDetailLogTests
    {
        [Test]
        public void Offside1000_LinkedOnce_HashChangesOnEdit()
        {
            var linked = 0;
            for (ulong seed = 1; linked < 1000 && seed < 20; seed++)
            {
                var sim = MatchTestKit.Sim(seed);
                sim.RunToEnd();
                var d = sim.Context.Actions.Details;
                var seen = new HashSet<int>();
                var log = sim.Log;
                var passes = 0;
                for (int i = 0; i < log.Count; i++)
                {
                    ref readonly var e = ref log[i];
                    if (e.Kind != SimEventKind.Pass) continue;
                    passes++;
                    Assert.Greater(e.B, 0, "패스 이벤트마다 상세 기록");
                    var idx = e.B - 1;
                    Assert.IsTrue(seen.Add(idx), "중복 연결 0");
                    Assert.AreEqual(e.Seq, d.EventSeq(idx), "상세 → 이벤트 Seq");
                    Assert.AreEqual(ActionDetailKind, d.KindOf(idx));
                    Assert.AreEqual(e.Tick, d.TickOf(idx));
                }
                Assert.AreEqual(passes, d.Count, "누락 0 (상세 수 = 패스 수)");
                Assert.AreEqual(0, d.Overflow);
                linked += passes;
                if (d.Count > 0)
                {
                    var before = sim.StateHash();
                    var last = d.Count - 1; // 해시에 내용이 들어가는 최근 창
                    d.TamperForTest(last, d.BallX(last) + 1f);
                    Assert.AreNotEqual(before, sim.StateHash(), "상세 기록을 바꾸면 체크포인트 해시가 바뀐다");
                }
            }
            Assert.GreaterOrEqual(linked, 1000);
        }

        const byte ActionDetailKind = RoadToPro.MatchEngine.Actions.ActionDetailLog.KindOffsideSnapshot;

        [Test]
        public void Snapshot_RoundTrip_ContinuesIdentically()
        {
            var a = MatchTestKit.Sim(31);
            var b = MatchTestKit.Sim(31);
            // 예약 킥이 걸린 틱을 찾아 스냅샷 (틱 경계를 넘는 예약 포함)
            long snapTick = -1;
            for (long t = 0; t < 30_000; t++)
            {
                a.StepTick();
                if (t > 600 && a.Context.Actions.Pending.Active) { snapTick = t; break; }
            }
            Assert.Greater(snapTick, 0, "예약 킥 상태를 찾지 못함");
            var snap = a.Capture();
            var fork = a.Fork();
            fork.Restore(snap, a.Log, a.Checkpoints);
            Assert.AreEqual(a.StateHash(), fork.StateHash());
            for (int i = 0; i < 600; i++) { a.StepTick(); fork.StepTick(); }
            Assert.AreEqual(a.StateHash(), fork.StateHash());
            Assert.AreEqual(a.Log.Hash, fork.Log.Hash);
            b.RunUntilTick(snapTick + 601);
            Assert.AreEqual(b.StateHash(), a.StateHash(), "중단 없는 실행과 같다");
        }
    }
}
