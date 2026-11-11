using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Replay;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Flow
{
    /// <summary>specs/058 FR-61·65, AC-17(코어): 교체·퇴장은 걸어서 나가고 들어온다. 순간이동·즉시 삭제 없음.</summary>
    public class OffPitchWalkersTests
    {
        /// <summary>홈 9번(id 10)이 60분에 들어가 75분에 나간다 — 내 선수 시나리오(AC-17). 들어갈 때 나가는 선수는 id 9.</summary>
        public const int MyId = 13, FirstOutId = 9, SecondInId = 14;
        public const long SubInTick = 36_000, SubOutTick = 45_000;

        static MatchSim _subs, _sendOff;
        static TeleportAudit _subsAudit, _sendOffAudit;
        static readonly object Gate = new object();

        static MatchSim Subs()
        {
            lock (Gate)
            {
                if (_subs != null) return _subs;
                var inputs = new InputLog();
                inputs.Add(new ExternalInput(SubInTick, FirstOutId, InputKind.Substitute, MyId));
                inputs.Add(new ExternalInput(SubOutTick, MyId, InputKind.Substitute, SecondInId));
                var sim = Sim(ReferenceSeed, new MatchSimOptions { Inputs = inputs });
                _subsAudit = new TeleportAudit(sim);
                sim.RunToEnd();
                return _subs = sim;
            }
        }

        static MatchSim SendOff()
        {
            lock (Gate)
            {
                if (_sendOff != null) return _sendOff;
                var script = new RefereeScript();
                script.Add(new RefereeAction(20_000, RefereeActionKind.SendOff, 4));
                var sim = Sim(ReferenceSeed, new MatchSimOptions { Referee = script });
                _sendOffAudit = new TeleportAudit(sim);
                sim.RunUntilTick(23_000);
                return _sendOff = sim;
            }
        }

        static SimEvent Find(MatchSim sim, SimEventKind kind, int playerId, long from = 0)
        {
            foreach (var e in Events(sim, kind)) if (e.PlayerId == playerId && e.Tick >= from) return e;
            return default;
        }

        [Test]
        public void SubIn60_SubOut75_WalkOnAndOff_NoTeleport()
        {
            var sim = Subs();
            var inEv = Find(sim, SimEventKind.Substitution, MyId);
            Assert.That(inEv.Kind, Is.EqualTo(SimEventKind.Substitution), "60분 교체 투입 없음");
            Assert.That(inEv.OtherId, Is.EqualTo(FirstOutId));
            var left = Find(sim, SimEventKind.PlayerLeftPitch, FirstOutId);
            Assert.That(left.Kind, Is.EqualTo(SimEventKind.PlayerLeftPitch), "나간 선수 이탈 이벤트 없음");
            Assert.That(left.A, Is.EqualTo(EventCodes.LeaveSubstituted));
            Assert.That(left.Tick, Is.EqualTo(inEv.Tick), "교대는 터치라인 도착 순간");
            var entered = Find(sim, SimEventKind.PlayerEntered, MyId);
            Assert.That(entered.Tick, Is.EqualTo(inEv.Tick));
            var W = sim.Context.Pitch.WidthM;
            var y = entered.Y * W; // 정규화 y
            Assert.That(y < 0f || y > W, Is.True, $"교대 지점이 터치라인 밖이 아니다 (y = {y:0.00} m)");

            var outEv = Find(sim, SimEventKind.Substitution, SecondInId);
            Assert.That(outEv.Kind, Is.EqualTo(SimEventKind.Substitution), "75분 교체 아웃 없음");
            Assert.That(outEv.OtherId, Is.EqualTo(MyId));
            Assert.That(outEv.Tick, Is.GreaterThan(SubOutTick));
            Assert.That(Find(sim, SimEventKind.PlayerLeftPitch, MyId).A, Is.EqualTo(EventCodes.LeaveSubstituted));

            foreach (var j in _subsAudit.PlayerJumpsExceptPeriodStart())
                Assert.Fail($"순간이동 {j}");
        }

        [Test]
        public void SubOut_FramesKeepOutgoingIdUntilSwap()
        {
            var sim = Subs();
            var inEv = Find(sim, SimEventKind.Substitution, MyId);
            var clip = sim.ExtractClip(inEv.Tick - 20, inEv.Tick + 20);
            int slot = -1;
            foreach (var f in clip.Frames)
                for (int e = 0; e < Squad.Slots; e++)
                    if (f.PlayerIds[e] == FirstOutId) slot = e;
            Assert.That(slot, Is.GreaterThanOrEqualTo(0), "교대 전 프레임에 나가는 선수가 없다");
            bool sawOut = false, sawIn = false;
            foreach (var f in clip.Frames)
            {
                Assert.That(f.Hints[slot], Is.Not.EqualTo(MotionHint.Absent), $"프레임 {f.Index} 빈 슬롯");
                if (f.PlayerIds[slot] == FirstOutId) { Assert.That(sawIn, Is.False, "교대 뒤 다시 나간 선수"); sawOut = true; }
                if (f.PlayerIds[slot] == MyId) sawIn = true;
            }
            Assert.That(sawOut && sawIn, Is.True);
        }

        [Test]
        public void SendOff_WalksToTouchline_ExcludedImmediately()
        {
            var sim = SendOff();
            var card = Find(sim, SimEventKind.Card, 4);
            Assert.That(card.A, Is.EqualTo(EventCodes.CardRed));
            var left = Find(sim, SimEventKind.PlayerLeftPitch, 4);
            Assert.That(left.Kind, Is.EqualTo(SimEventKind.PlayerLeftPitch), "퇴장 선수가 피치를 벗어나지 않았다");
            Assert.That(left.A, Is.EqualTo(EventCodes.LeaveSentOff));
            Assert.That(left.Tick, Is.GreaterThan(card.Tick), "즉시 삭제");
            Assert.That(sim.Context.Squad.OnPitchCount(0), Is.EqualTo(10));
            foreach (var j in _sendOffAudit.PlayerJumpsExceptPeriodStart())
                Assert.Fail($"순간이동 {j}");

            // 걷는 동안 프레임에 퇴장 선수가 보인다
            var clip = sim.ExtractClip(card.Tick + 5, card.Tick + 15);
            bool seen = false;
            foreach (var f in clip.Frames)
                for (int e = 0; e < Squad.Slots; e++)
                    if (f.PlayerIds[e] == 4 && f.Hints[e] != MotionHint.Absent) seen = true;
            Assert.That(seen, Is.True, "걸어 나가는 퇴장 선수가 프레임에 없다");
        }

        [Test, Category("Determinism")]
        public void Snapshot_WhileWalking_RestoreIdentical()
        {
            var sim = SendOff();
            var card = Find(sim, SimEventKind.Card, 4);
            var a = Sim(ReferenceSeed, new MatchSimOptions { Referee = sim.Context.RefereeScript.Clone(), Checkpoints = CheckpointMode.All });
            var left = Find(sim, SimEventKind.PlayerLeftPitch, 4);
            a.RunUntilTick(card.Tick + 10);
            var snap = a.Capture(); // 걷는 중
            a.RunUntilTick(left.Tick + 50);
            var fork = a.Fork();
            fork.Restore(snap, a.Log, a.Checkpoints);
            Assert.That(fork.Context.Flow.Walkers.Count, Is.GreaterThan(0), "걷는 중 스냅샷이 아니다");
            fork.RunUntilTick(left.Tick + 50);
            Assert.That(fork.StateHash(), Is.EqualTo(a.StateHash()));
        }
    }
}
