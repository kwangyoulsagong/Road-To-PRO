using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>액션 클립 선택·접근 보정·원터치 (specs/043 FR-42·43·61, AC-08·11).</summary>
    public class ActionClipSelectorTests
    {
        static ActionRequest Req(float now, float contact, Vec3 simPos, float yaw, Vec3 ball, float speed, BodyPart part)
            => new ActionRequest(now, contact, simPos, yaw, ball, new Vec3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw)) * 12f, speed, part);

        [Test]
        public void IdealRequest_PicksPassInside_OnTime_NoStride()
        {
            var lib = AnimTestKit.Lib;
            var cands = AnimTestKit.Map.Candidates(SimEventKind.Pass);
            lib.TryGet("pass_inside_r", out var p);
            var ball = p.ContactPoint; // 루트 원점·yaw 0에서 클립이 기대하는 공 위치
            var r = ActionClipSelector.Select(lib, cands, Req(0f, 2f, Vec3.Zero, 0f, ball, 3f, BodyPart.RightFoot));
            Assert.AreEqual("pass_inside_r", lib.Get(r.Clip).Id);
            Assert.That(r.Start, Is.EqualTo(2f - p.ContactTime).Within(1e-5f));
            Assert.That(r.Rate, Is.EqualTo(1f));
            Assert.That(r.StrideScale, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(r.ContactAt, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void PartFilter_LeftFoot()
        {
            var lib = AnimTestKit.Lib;
            var r = ActionClipSelector.Select(lib, AnimTestKit.Map.Candidates(SimEventKind.Pass),
                Req(0f, 2f, Vec3.Zero, 0f, new Vec3(0.13f, 0.11f, 0.49f), 3f, BodyPart.LeftFoot));
            Assert.AreEqual(BodyPart.LeftFoot, lib.Get(r.Clip).ContactPart);
        }

        [Test]
        public void StrideAdjust_Within15Pct_OrOtherClip()
        {
            var lib = AnimTestKit.Lib;
            var rng = new System.Random(43);
            var kinds = new[] { SimEventKind.Pass, SimEventKind.Shot, SimEventKind.Cross, SimEventKind.PassReceived };
            int snaps = 0;
            for (int i = 0; i < 2000; i++)
            {
                float yaw = (float)(rng.NextDouble() * Math.PI * 2);
                var fwd = new Vec3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
                var right = new Vec3(fwd.Z, 0, -fwd.X);
                // 시뮬 공 위치: 선수 앞 0.2~1.0 m, 옆 −0.4~0.4 m (일부는 보정 한계를 넘는다)
                var ball = fwd * (float)(0.2 + rng.NextDouble() * 0.8) + right * (float)(rng.NextDouble() * 0.8 - 0.4) + new Vec3(0, 0.11f, 0);
                var r = ActionClipSelector.Select(lib, AnimTestKit.Map.Candidates(kinds[i % kinds.Length]),
                    Req(0f, (float)(0.6 + rng.NextDouble() * 1.5), Vec3.Zero, yaw, ball, (float)(rng.NextDouble() * 7), BodyPart.None));
                Assert.That(Math.Abs(r.StrideScale - 1f), Is.LessThanOrEqualTo(ActionClipSelector.MaxStrideChange + 1e-5f), "보폭 ±15% (AC-08)");
                if (r.Snap) snaps++;
            }
            Assert.Less(snaps, 2000, "보정 범위 안 해가 대부분이어야 한다");
        }

        [Test]
        public void OverLimit_PrefersOtherClip()
        {
            // 오른발 인사이드가 기대하는 위치에서 0.5 m 벗어난 공: 같은 클립 + 큰 보정 대신, 보정 한계 안의 다른 클립을 고른다
            var lib = AnimTestKit.Lib;
            lib.TryGet("pass_inside_r", out var p);
            var ball = p.ContactPoint + new Vec3(0.42f, 0, 0.06f);
            var r = ActionClipSelector.Select(lib, AnimTestKit.Map.Candidates(SimEventKind.Pass),
                Req(0f, 2f, Vec3.Zero, 0f, ball, 0.5f, BodyPart.None));
            Assert.IsFalse(r.Snap);
            Assert.AreNotEqual("pass_inside_r", lib.Get(r.Clip).Id);
        }

        [Test]
        public void OneTouch_NeverLate()
        {
            var lib = AnimTestKit.Lib;
            var rng = new System.Random(11);
            for (int i = 0; i < 1000; i++)
            {
                float now = (float)rng.NextDouble() * 5f;
                float avail = (float)(0.02 + rng.NextDouble() * 0.5);
                var kind = i % 2 == 0 ? SimEventKind.Pass : SimEventKind.Shot;
                var r = ActionClipSelector.Select(lib, AnimTestKit.Map.Candidates(kind),
                    Req(now, now + avail, Vec3.Zero, 0f, new Vec3(0, 0.11f, 0.5f), 4f, BodyPart.None));
                Assert.That(r.Start, Is.GreaterThanOrEqualTo(now - 1e-5f), "과거에 시작할 수 없다");
                float contact = r.Start + lib.Get(r.Clip).ContactTime / r.Rate;
                Assert.That(contact, Is.EqualTo(now + avail).Within(1e-4f), "접촉 시각 = 시뮬 접촉 시각 (AC-11)");
                Assert.That(r.Rate, Is.GreaterThanOrEqualTo(1f));
            }
        }

        /// <summary>055 FR-61·AC-09: 같은 상황 후보 안에서 선수 킥 원형과 같은 원형 클립을 고른다. 원형 없음이면 043 선택과 같다.</summary>
        [Test]
        public void KickStyle_PrefersMatchingClip()
        {
            var lib = AnimTestKit.Lib;
            var cands = AnimTestKit.Map.Candidates(SimEventKind.Shot);
            int styled = 0;
            foreach (int ci in cands)
            {
                var c = lib.Get(ci);
                if (c.KickStyle == KickArchetype.None || !c.HasContact) continue;
                styled++;
                // 그 클립이 기대하는 공 위치·접촉 부위로 요청 → 원형이 같으면 그 클립 (다른 원형 클립이 기하로 더 가까워도)
                var q = new ActionRequest(0f, 2f, Vec3.Zero, 0f, c.ContactPoint, new Vec3(0, 0, 20f), 4f, c.ContactPart, c.KickStyle);
                Assert.AreEqual(c.Id, lib.Get(ActionClipSelector.Select(lib, cands, q).Clip).Id, c.Id);
            }
            Assert.GreaterOrEqual(styled, 3, "슈팅 후보에 원형 클립 K1·K2·K3 이상");
            // 원형 없음: 043 점수 그대로
            lib.TryGet("shot_instep_r", out var ins);
            var q0 = new ActionRequest(0f, 2f, Vec3.Zero, 0f, ins.ContactPoint, new Vec3(0, 0, 20f), 4f, BodyPart.RightFoot);
            Assert.AreEqual("shot_instep_r", lib.Get(ActionClipSelector.Select(lib, cands, q0).Clip).Id);
        }

        [Test]
        public void Deterministic()
        {
            var lib = AnimTestKit.Lib;
            var q = Req(0.3f, 1.1f, new Vec3(1, 0, 2), 0.7f, new Vec3(1.3f, 0.11f, 2.4f), 4f, BodyPart.None);
            var a = ActionClipSelector.Select(lib, AnimTestKit.Map.Candidates(SimEventKind.Shot), q);
            var b = ActionClipSelector.Select(lib, AnimTestKit.Map.Candidates(SimEventKind.Shot), q);
            Assert.AreEqual(a, b);
        }
    }
}
