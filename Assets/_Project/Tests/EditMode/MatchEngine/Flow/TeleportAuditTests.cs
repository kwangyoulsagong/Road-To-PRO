using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Diagnostics;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Replay;
using static RoadToPro.Tests.EditMode.MatchEngine.Match.MatchTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Flow
{
    /// <summary>
    /// 100 Hz 원시 궤적으로 순간이동을 찾는다 (specs/058 AC-01). 서브스텝 안(pre → post)과 서브스텝 사이(앞 post → 이번 pre)를 모두 본다 —
    /// 배치(Place)는 틱 사이에 일어나서 서브스텝 안 차이만 보면 놓친다. 경기·하프 시작 배치(FR-10)만 예외.
    /// </summary>
    public sealed class TeleportAudit : IRawTrajectorySink
    {
        public struct Jump
        {
            public long Substep;
            public int Entity;
            public float Distance, Allowed;
            public override string ToString() => $"서브스텝 {Substep} (틱 {Substep / 10}) 엔티티 {Entity}: {Distance:0.000} m > {Allowed:0.000} m";
        }

        /// <summary>최고 속도 여유 (피로·접촉 보정 오차) 와 고정 허용오차.</summary>
        public const float SpeedSlack = 1.05f, Tolerance = 0.001f;
        /// <summary>공은 잡기(≤ 1 m)로 발 앞에 붙는다. 이보다 큰 한 번의 이동만 재배치로 본다.</summary>
        public const float BallJump = 3f;
        const float Dt = 0.01f;

        readonly MatchSim _sim;
        readonly ReplayFrame _last = new ReplayFrame();
        bool _hasLast;
        public readonly List<Jump> Players = new List<Jump>();
        public readonly List<Jump> Ball = new List<Jump>();

        public TeleportAudit(MatchSim sim)
        {
            _sim = sim;
            sim.Context.RawSink = this;
        }

        public void OnRawSubstep(long substep, ReplayFrame pre, ReplayFrame post)
        {
            var k = _sim.Context.Kinematics;
            for (int e = 0; e < Squad.Slots; e++)
            {
                if (pre.Hints[e] == MotionHint.Absent || post.Hints[e] == MotionHint.Absent) continue;
                var allowed = k.Profile(e).MaxSpeed * SpeedSlack * Dt + Tolerance;
                Check(Players, substep, e, pre, post, allowed);
                if (_hasLast && _last.Hints[e] != MotionHint.Absent) Check(Players, substep, e, _last, pre, Tolerance);
            }
            const int b = ReplayFrame.BallEntity;
            Check(Ball, substep, b, pre, post, BallJump);
            if (_hasLast) Check(Ball, substep, b, _last, pre, BallJump);
            _last.CopyFrom(post);
            _hasLast = true;
        }

        static void Check(List<Jump> into, long substep, int e, ReplayFrame a, ReplayFrame b, float allowed)
        {
            float dx = b.X(e) - a.X(e), dy = b.Y(e) - a.Y(e);
            var d = (float)System.Math.Sqrt(dx * dx + dy * dy);
            if (d > allowed) into.Add(new Jump { Substep = substep, Entity = e, Distance = d, Allowed = allowed });
        }

        /// <summary>경기·하프 시작 배치(FR-10)를 뺀 선수 순간이동.</summary>
        public List<Jump> PlayerJumpsExceptPeriodStart()
        {
            var starts = new HashSet<long>();
            var log = _sim.Log;
            for (int i = 0; i < log.Count; i++)
                if (log[i].Kind == SimEventKind.PeriodStart) starts.Add(log[i].Tick * 10);
            var r = new List<Jump>();
            foreach (var j in Players)
                if (!starts.Contains(j.Substep)) r.Add(j);
            return r;
        }

        /// <summary>공 재배치 이벤트가 없는 공 순간이동.</summary>
        public List<Jump> BallJumpsWithoutRelocation()
        {
            var at = new HashSet<long>();
            var log = _sim.Log;
            for (int i = 0; i < log.Count; i++)
                if (log[i].Kind == SimEventKind.BallRelocated || log[i].Kind == SimEventKind.PeriodStart) { at.Add(log[i].Tick); at.Add(log[i].Tick + 1); }
            var r = new List<Jump>();
            foreach (var j in Ball)
                if (!at.Contains(j.Substep / 10)) r.Add(j);
            return r;
        }
    }

    /// <summary>specs/058 AC-01: 고정 시드 20경기 순간이동 0건 (경기·하프 시작 배치 제외).</summary>
    public class TeleportAuditTests
    {
        public const int Seeds = 20;
        public const ulong FirstSeed = 20261111UL;

        [Test, Category("Slow")]
        public void TwentySeeds_NoTeleport()
        {
            var failures = new List<string>();
            for (int i = 0; i < Seeds; i++)
            {
                var seed = FirstSeed + (ulong)i * 7919UL;
                var sim = Sim(seed);
                var audit = new TeleportAudit(sim);
                sim.RunToEnd();
                foreach (var j in audit.PlayerJumpsExceptPeriodStart())
                    if (failures.Count < 20) failures.Add($"시드 {seed} 선수 {j}");
                foreach (var j in audit.BallJumpsWithoutRelocation())
                    if (failures.Count < 20) failures.Add($"시드 {seed} 공 {j}");
            }
            CollectionAssert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void ReferenceMatch_NoTeleport()
        {
            var sim = Sim(ReferenceSeed);
            var audit = new TeleportAudit(sim);
            sim.RunToEnd();
            var p = audit.PlayerJumpsExceptPeriodStart();
            var b = audit.BallJumpsWithoutRelocation();
            Assert.That(p, Is.Empty, string.Join("\n", p.GetRange(0, System.Math.Min(10, p.Count))));
            Assert.That(b, Is.Empty, string.Join("\n", b.GetRange(0, System.Math.Min(10, b.Count))));
        }
    }
}
