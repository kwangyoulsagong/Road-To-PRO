using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Pipeline;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Match
{
    /// <summary>AC-04: 010이 넘긴 결정 대상 목록대로만 실행 (주기 3 → 틱/3 ±1, 주기 전환 누락·중복 0, 같은 틱 id 오름차순).</summary>
    public class DecisionLodTests
    {
        /// <summary>단계 6 자리에서 이번 틱 결정 슬롯을 기록하고 진짜 단계로 넘긴다.</summary>
        sealed class DecidedRecorder : IMatchStage
        {
            readonly IMatchStage _inner;
            public readonly List<long>[] TicksBySlot = new List<long>[22];
            public bool OrderOk = true;
            public DecidedRecorder(IMatchStage inner)
            {
                _inner = inner;
                for (int i = 0; i < 22; i++) TicksBySlot[i] = new List<long>();
            }
            public StageId Id => StageId.SteeringTargets;
            public void Execute(MatchContext ctx)
            {
                var b = ctx.Board;
                var n = b.DecidedCount;
                for (int i = 0; i < n; i++)
                {
                    var s = b.DecidedAt(i);
                    if (i > 0 && b.DecidedAt(i - 1) >= s) OrderOk = false;
                    TicksBySlot[s].Add(ctx.Tick);
                }
                _inner.Execute(ctx);
            }
            public void WriteState(StateWriter w) => _inner.WriteState(w);
            public void ReadState(StateReader r) => _inner.ReadState(r);
            public void AddTo(ref StableHasher h) => _inner.AddTo(ref h);
        }

        /// <summary>일부러 섞인 순서·중복을 내는 스케줄러 (006이 정렬·중복 제거하는지).</summary>
        sealed class MessyScheduler : IDecisionScheduler
        {
            public readonly FixedPeriodScheduler Inner = new FixedPeriodScheduler(1);
            readonly int[] _tmp = new int[22];
            public int Select(long tick, IMatchView view, int[] outSlots)
            {
                var n = Inner.Select(tick, view, _tmp);
                int m = 0;
                for (int i = n - 1; i >= 0 && m < outSlots.Length; i--) outSlots[m++] = _tmp[i]; // 내림차순
                if (n > 0 && m < outSlots.Length) outSlots[m++] = _tmp[0];                        // 중복
                return m;
            }
            public void WriteState(StateWriter w) => Inner.WriteState(w);
            public void ReadState(StateReader r) => Inner.ReadState(r);
            public void AddTo(ref StableHasher h) => Inner.AddTo(ref h);
        }

        [Test]
        public void PeriodThree_CountAndSwitch_NoMissOrDouble_IdAscending()
        {
            DecidedRecorder rec = null;
            MessyScheduler sched = null;
            var o = new MatchSimOptions
            {
                Stages = () =>
                {
                    var st = MatchSim.DefaultStages();
                    rec = new DecidedRecorder(st[6]);
                    st[6] = rec;
                    return st;
                },
                Scheduler = () => sched = new MessyScheduler(),
            };
            var sim = Sim(11UL, o);
            sched.Inner.SetPeriod(3, 3, 0);
            sched.Inner.SetPeriod(5, 3, 0);
            sim.RunUntilTick(1000);
            sched.Inner.SetPeriod(5, 1, 1000); // 주기 3 → 매 틱
            sim.RunToEnd();
            var ticks = sim.NextTick;
            Assert.That(rec.OrderOk, "같은 틱 안 결정 순서는 슬롯 오름차순, 중복 없음");

            // 슬롯 3: 90분+추가시간 동안 주기 3 — 교체가 없어 처음부터 끝까지 경기장 위
            var c3 = rec.TicksBySlot[3].Count;
            Assert.That(c3, Is.InRange(ticks / 3 - 1, ticks / 3 + 1), $"결정 {c3}회 vs 틱 {ticks}/3");
            foreach (var t in rec.TicksBySlot[3]) Assert.That(t % 3, Is.EqualTo(0L));

            // 슬롯 5: 1000 전에는 3의 배수만, 1000부터 매 틱 (누락·중복 0)
            var expected = new List<long>();
            for (long t = 0; t < ticks; t++) if (t >= 1000 || t % 3 == 0) expected.Add(t);
            Assert.That(rec.TicksBySlot[5], Is.EqualTo(expected));
            // 나머지 슬롯은 매 틱
            Assert.That(rec.TicksBySlot[7].Count, Is.EqualTo((int)ticks));
        }
    }
}
