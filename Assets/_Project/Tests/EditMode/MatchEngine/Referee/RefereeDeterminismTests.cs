using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Referee;
using RoadToPro.Tests.EditMode.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Referee
{
    /// <summary>specs/012 AC-19·20·24 (결정론) + T21 상태 왕복.</summary>
    public class RefereeDeterminismTests
    {
        static MatchSim Run(ulong seed, MatchSimOptions o = null)
        {
            var sim = new MatchSim(ShapeTestKit.Setup(seed: seed), ShapeTestKit.TuningWithShape(), o);
            sim.RunToEnd();
            return sim;
        }

        [Test, Category("Determinism")]
        public void SameSeed_90min_RefereeEventHash()
        {
            var a = LiveReferee.Played()[0];
            var b = Run(LiveReferee.FirstSeed);
            Assert.Greater(LiveReferee.Count(a.Log, SimEventKind.Foul), 0, "판정 이벤트가 있는 경기");
            Assert.AreEqual(LiveReferee.RefereeHash(a.Log), LiveReferee.RefereeHash(b.Log), "판정 이벤트(종류·틱·카드·오심·VAR) 해시");
            Assert.AreEqual(a.Log.Hash, b.Log.Hash, "전체 이벤트 로그 해시");
        }

        /// <summary>같은 틱 동시 반칙 픽스처: 12경기에서 같은 틱 Foul 2건 이상이 생긴 경기를 같은 시드로 다시 돌려 같은 순서·해시인지.</summary>
        [Test, Category("Determinism")]
        public void SimultaneousFouls_Fixture_Hash()
        {
            var sims = LiveReferee.Played();
            for (int m = 0; m < sims.Length; m++)
            {
                var log = sims[m].Log;
                for (int i = 1; i < log.Count; i++)
                {
                    if (log[i].Kind != SimEventKind.Contact) continue;
                    // 같은 틱 접촉 2건 이상 = 동시 반칙 후보
                    if (log[i - 1].Kind == SimEventKind.Contact && log[i - 1].Tick == log[i].Tick)
                    {
                        var re = Run(LiveReferee.FirstSeed + (ulong)m);
                        Assert.AreEqual(LiveReferee.RefereeHash(log), LiveReferee.RefereeHash(re.Log));
                        TestContext.WriteLine($"시드 {LiveReferee.FirstSeed + (ulong)m} 틱 {log[i].Tick} 같은 틱 접촉 2건");
                        return;
                    }
                }
            }
            Assert.Inconclusive("12경기에 같은 틱 접촉이 없다");
        }

        /// <summary>VAR 연출 길이는 시뮬 입력이 아니다: 연출 길이를 다르게 쓰는 구독자(화면 대역)를 붙여도 경기 해시가 같다.</summary>
        [Test, Category("Determinism")]
        public void VarPresentationLength_DoesNotChangeHash()
        {
            var baseSim = LiveReferee.Played()[0];
            var shortView = new VarPresenter(30);
            var o = new MatchSimOptions();
            var sim = new MatchSim(ShapeTestKit.Setup(seed: LiveReferee.FirstSeed), ShapeTestKit.TuningWithShape(), o);
            sim.Subscribe(shortView);
            sim.RunToEnd();
            var longSim = new MatchSim(ShapeTestKit.Setup(seed: LiveReferee.FirstSeed), ShapeTestKit.TuningWithShape());
            var longView = new VarPresenter(120);
            longSim.Subscribe(longView);
            longSim.RunToEnd();
            Assert.AreEqual(baseSim.Log.Hash, sim.Log.Hash);
            Assert.AreEqual(sim.Log.Hash, longSim.Log.Hash, "연출 30 s vs 120 s");
            Assert.AreEqual(shortView.Checks, longView.Checks);
        }

        sealed class VarPresenter : ISimEventSubscriber
        {
            readonly double _seconds;
            public int Checks;
            public double Shown;
            public VarPresenter(double s) => _seconds = s;
            public void OnEvent(in SimEvent e, IMatchView view)
            {
                if (e.Kind != SimEventKind.VarCheck) return;
                Checks++;
                Shown += _seconds;
            }
        }

        sealed class Diag : IRefereeDiagnostics
        {
            public int Snapshots, Advantages, Officials;
            public void OnOffsideSnapshot(long tick, int attackTeam, float lineX, ReadOnlySpan<float> frontX) => Snapshots++;
            public void OnAdvantage(long tick, int victimTeam, float remainingSeconds) => Advantages++;
            public void OnOfficials(long tick, Vec2 referee, Vec2 assistantHigh, Vec2 assistantLow, float lastSight) => Officials++;
        }

        [Test, Category("Determinism")]
        public void Diagnostics_OnOff_SameHashes_ZeroAlloc()
        {
            var off = LiveReferee.Played()[1];
            var diag = new Diag();
            var on = Run(LiveReferee.FirstSeed + 1, new MatchSimOptions { RefereeDiagnostics = () => diag, Checkpoints = CheckpointMode.None });
            Assert.AreEqual(off.Log.Hash, on.Log.Hash, "이벤트 로그 해시");
            Assert.AreEqual(off.Checkpoints.Count, on.Checkpoints.Count);
            for (int i = 0; i < off.Checkpoints.Count; i++) Assert.AreEqual(off.Checkpoints[i].StateHash, on.Checkpoints[i].StateHash, $"체크포인트 {i}");
            Assert.Greater(diag.Snapshots, 0);
            Assert.Greater(diag.Officials, 0);
            // 끈 실행: 워밍업 뒤 1,000틱 할당 0
            var sim = new MatchSim(ShapeTestKit.Setup(seed: LiveReferee.FirstSeed + 2), ShapeTestKit.TuningWithShape());
            sim.StepTicks(3000);
            var before = GC.GetAllocatedBytesForCurrentThread();
            sim.StepTicks(1000);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, bytes, "진단 끔 · 심판 단계 포함 틱 할당 0 B");
        }

        [Test]
        public void RefereeState_RoundTrip_HashSame()
        {
            var sim = new MatchSim(ShapeTestKit.Setup(seed: LiveReferee.FirstSeed + 3), ShapeTestKit.TuningWithShape());
            sim.StepTicks(9000);
            var st = LiveReferee.Stage(sim).State;
            var h1 = RoadToPro.Core.Hashing.StableHasher.Create(); st.AddTo(ref h1);
            var w = new StateWriter(64 * 1024);
            st.Write(w);
            var copy = new RefereeState();
            var r = new StateReader();
            r.Reset(w.Buffer, w.Length);
            copy.Read(r);
            var h2 = RoadToPro.Core.Hashing.StableHasher.Create(); copy.AddTo(ref h2);
            Assert.AreEqual(h1.Value, h2.Value);
            // 스냅샷 복원 뒤 이어 돌린 경기 = 처음부터 돌린 경기 (심판 상태가 체크포인트에 들어간다)
            var full = new MatchSim(ShapeTestKit.Setup(seed: LiveReferee.FirstSeed + 3), ShapeTestKit.TuningWithShape(), new MatchSimOptions { Checkpoints = CheckpointMode.All });
            full.RunToEnd();
            var snap = full.NearestSnapshot(9000);
            var resumed = new MatchSim(ShapeTestKit.Setup(seed: LiveReferee.FirstSeed + 3), ShapeTestKit.TuningWithShape());
            resumed.Restore(snap, full.Log, full.Checkpoints);
            resumed.RunToEnd();
            Assert.AreEqual(full.Log.Hash, resumed.Log.Hash, "복원 후 이어 돌린 로그 해시");
        }
    }
}
