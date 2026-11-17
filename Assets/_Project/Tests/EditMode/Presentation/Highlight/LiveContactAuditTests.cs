using System;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Tests.EditMode.Presentation.Players.Motion;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>specs/082 FR-05 · AC-04 (+ AC-01·02·06 진단 쪽): 실경기 하이라이트 접촉 진단.</summary>
    public class LiveContactAuditTests
    {
        /// <summary>AC-04: 진단 경로가 054 접촉 측정과 같은 값(±1 cm)을 낸다 — 054 합성 헤딩 장면으로 비교.</summary>
        [Test]
        public void MatchesMotionMeter_Within1cm()
        {
            var scene = MotionSceneKit.Scene("header");
            var reference = MotionRun.Measure(scene);
            var rows = LiveContactAudit.Measure(scene.Data, null, classify: ev => ev.Part == BodyPart.Head ? LiveContactAudit.Kind.Header : (LiveContactAudit.Kind?)null);
            int compared = 0;
            foreach (var r in rows)
                foreach (var c in reference.Contacts)
                {
                    if (c.Seq != r.Seq || c.Entity != r.Entity) continue;
                    Assert.AreEqual(c.Distance, r.Distance, 0.01f, $"seq {r.Seq} 거리");
                    Assert.AreEqual(c.TimeError, r.TimeError, 1e-4f, $"seq {r.Seq} 시각 차");
                    Assert.IsFalse(float.IsNaN(r.DistanceAtContact) || float.IsNaN(r.DistanceRaw), "접촉 프레임·시뮬 공 거리");
                    compared++;
                }
            Assert.Greater(compared, 0, "비교한 헤더 접촉\n" + LiveContactAudit.Summary(rows));
        }

        /// <summary>082 경계 · 예외: 접촉 시각이 장면 밖이면 진단에서 제외하고 사유를 남긴다.</summary>
        [Test]
        public void SceneBoundary_ExcludedWithReason()
        {
            var d = new HighlightData(10f, 31); // 10.0 ~ 11.0 s
            Assert.IsNull(LiveContactAudit.BoundaryReason(d, 10.5f));
            Assert.IsNull(LiveContactAudit.BoundaryReason(d, 10f));
            Assert.IsNull(LiveContactAudit.BoundaryReason(d, 11f));
            StringAssert.Contains("시작 전", LiveContactAudit.BoundaryReason(d, 9.99f));
            StringAssert.Contains("끝 뒤", LiveContactAudit.BoundaryReason(d, 11.01f));
        }

        /// <summary>AC-06 일부: 진단은 시뮬을 읽기만 한다 — 진단을 돌린 경기와 안 돌린 경기의 상태 해시가 같다.</summary>
        [Test]
        public void Audit_OnOff_SameHash()
        {
            const ulong seed = 20261247UL;
            var plain = LiveContactAudit.NewSim(seed);
            plain.RunToEnd();
            var scales = LiveContactAudit.CastScales();
            var rows = LiveContactAudit.RunSeed(seed, scales);
            var again = LiveContactAudit.NewSim(seed);
            again.RunToEnd();
            Assert.AreEqual(plain.StateHash(), again.StateHash());
            TestContext.WriteLine(LiveContactAudit.Summary(rows));
        }

        static System.Collections.Generic.List<LiveContactAudit.Row> _fixed;

        /// <summary>고정 시드 10경기 진단 (테스트 사이 공유 — 결정론이라 한 번만 잰다).</summary>
        static System.Collections.Generic.List<LiveContactAudit.Row> FixedRows()
        {
            if (_fixed != null) return _fixed;
            var scales = LiveContactAudit.CastScales();
            var all = new System.Collections.Generic.List<LiveContactAudit.Row>();
            foreach (var s in FixedSeeds()) all.AddRange(LiveContactAudit.RunSeed(s, scales));
            return _fixed = all;
        }

        const float Frame = 1f / 30f + 1e-4f; // ±1 기록 프레임 (054 AC-04)

        /// <summary>AC-01: 고정 시드 묶음 하이라이트의 헤더 전부 — 접촉 프레임 머리–공 ≤ 5 cm, 시각 차 ≤ ±1 프레임, 필요한 점프 ≥ 0.10 m면 점프 헤딩.</summary>
        [Test]
        public void Headers_WithinDistanceAndTime_JumpOrStand()
        {
            var rows = FixedRows();
            var lib = RoadToPro.Tests.EditMode.Presentation.Players.AnimTestKit.Lib;
            float standY = lib.Get(lib.IndexOf(HeaderMotion.StandClip)).ContactPoint.Y;
            int n = 0;
            var fails = new System.Text.StringBuilder();
            foreach (var r in rows)
            {
                if (r.Kind != LiveContactAudit.Kind.Header || r.Excluded || r.Missed) continue;
                n++;
                if (!(r.DistanceAtContact <= 0.05f)) fails.AppendLine($"{r.Seed}#{r.Scene} seq {r.Seq} 머리–공 {r.DistanceAtContact * 100:0.0} cm");
                if (!(r.TimeError <= Frame)) fails.AppendLine($"{r.Seed}#{r.Scene} seq {r.Seq} 시각 차 {r.TimeError * 1000:0} ms");
                var want = HeaderMotion.UseJump(HeaderMotion.RequiredJump(r.BallHeight, standY, r.Scale)) ? HeaderMotion.JumpClip : HeaderMotion.StandClip;
                if (r.Clip != want) fails.AppendLine($"{r.Seed}#{r.Scene} seq {r.Seq} 클립 {r.Clip} (기대 {want})");
            }
            TestContext.WriteLine(LiveContactAudit.Summary(rows));
            Assert.Greater(n, 0, "측정된 헤더");
            Assert.IsEmpty(fails.ToString());
        }

        /// <summary>AC-02: 같은 묶음의 슬라이딩 태클 전부 — 슬라이딩 동작, 시각 차 ≤ ±1 프레임, 빗나간 슬라이딩은 접촉 + 다운(1.2 s)까지 누운 자세.</summary>
        [Test]
        public void SlidingTackles_SlideClip_ContactTime_StaysDown()
        {
            var rows = FixedRows();
            int n = 0;
            var fails = new System.Text.StringBuilder();
            foreach (var r in rows)
            {
                if (r.Kind != LiveContactAudit.Kind.Slide || r.Excluded) continue;
                n++;
                if (r.Clip != "tackle_slide_r") fails.AppendLine($"{r.Seed}#{r.Scene} seq {r.Seq} 클립 {r.Clip ?? "-"} ({r.Reason})");
                if (!(r.TimeError <= Frame)) fails.AppendLine($"{r.Seed}#{r.Scene} seq {r.Seq} 시각 차 {r.TimeError * 1000:0} ms");
                if (r.Outcome == ActionOutcome.Fail && !(r.DownUntil >= r.Time + AnimPlan.SlideDown - 1e-3f))
                    fails.AppendLine($"{r.Seed}#{r.Scene} seq {r.Seq} 다운 정지 끝 {r.DownUntil:0.00} < 접촉 + {AnimPlan.SlideDown}");
            }
            TestContext.WriteLine(LiveContactAudit.Summary(rows));
            Assert.Greater(n, 0, "측정된 슬라이딩");
            Assert.IsEmpty(fails.ToString());
        }

        /// <summary>실경기 진단 1회 (T03·T10 기준 측정): 고정 시드 묶음 전부 → `.farm/082-audit.json`. 길어서 명시 실행.</summary>
        [Test, Explicit("082 실경기 진단 — 명시 실행")]
        public void Report_FixedSeeds()
        {
            var all = FixedRows();
            System.IO.Directory.CreateDirectory(".farm");
            System.IO.File.WriteAllText(".farm/082-audit.json", LiveContactAudit.Json(all));
            TestContext.WriteLine(LiveContactAudit.Summary(all));
        }

        /// <summary>082 FR-05: 고정 시드 10경기 (`Data/e2e/082-live-seeds.json`).</summary>
        public static ulong[] FixedSeeds()
        {
            var text = System.IO.File.ReadAllText("Data/e2e/082-live-seeds.json");
            var m = System.Text.RegularExpressions.Regex.Match(text, "\"seeds\"\\s*:\\s*\\[([^\\]]*)\\]");
            var parts = m.Groups[1].Value.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var r = new ulong[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = ulong.Parse(parts[i]);
            return r;
        }
    }
}
