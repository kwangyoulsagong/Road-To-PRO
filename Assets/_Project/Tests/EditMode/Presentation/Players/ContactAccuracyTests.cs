using System;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>
    /// 접촉 정확도 (specs/043 AC-07). 패스 12·슛 10·크로스 8 = 30 장면. 60 fps 렌더 프레임 기준:
    /// ① 접촉 프레임 시각과 시뮬 접촉 시각 차이 ≤ 1 렌더 프레임 ② 접촉 순간 화면 공과 시뮬 공 차이 ≤ 5 cm
    /// ③ (보강) 실제 본으로 계산한 클립 접촉점과 시뮬 공 중심 거리 ≤ 10 cm — 축·태그·루트 보정 사슬 전체 확인.
    /// </summary>
    public class ContactAccuracyTests
    {
        [Test]
        public void ThirtyScenes_ContactFrameAndBall()
        {
            var lib = AnimTestKit.Lib;
            float worstFrame = 0, worstBall = 0, worstFoot = 0;
            for (int i = 0; i < 30; i++)
            {
                var kind = i < 12 ? SimEventKind.Pass : i < 22 ? SimEventKind.Shot : SimEventKind.Cross;
                var d = SyntheticScenes.Kick(500 + i, kind, out var contact);
                using var kit = new AnimRigKit(d, 0);
                var a = kit.Plan.Timeline(0).Get(0);
                var clip = lib.Get(a.Clip);
                Assert.IsTrue(clip.HasContact, clip.Id);
                // 접촉 순간에 가장 가까운 60 fps 렌더 프레임
                int k = (int)Math.Round((contact - SyntheticScenes.Start) * 60.0);
                float t = SyntheticScenes.Start + k / 60f;
                // 그 프레임에서 클립 시각 → 그 클립 시각이 접촉 프레임이 되는 재생 시각과의 차
                float clipTime = (t - a.Start) * a.Rate;
                float frameErr = Math.Abs(clipTime - clip.ContactTime) / a.Rate;
                worstFrame = Math.Max(worstFrame, frameErr);
                Assert.LessOrEqual(frameErr, 1f / 60f + 1e-4f, $"장면 {i} {clip.Id}: 접촉 프레임 오차 {frameErr * 1000:0.0} ms");

                kit.System.ApplyAt(contact);
                var ballVis = kit.Plan.BallVisual(contact);
                var ballSim = d.BallAt(contact);
                float bErr = (ballVis - ballSim).Length();
                worstBall = Math.Max(worstBall, bErr);
                Assert.LessOrEqual(bErr, 0.05f, $"장면 {i}: 화면 공 − 시뮬 공");

                var foot = kit.ContactPoint(0, clip.Rule);
                var evBall = d.Events[0].BallPos;
                float fErr = (foot - evBall).Length();
                worstFoot = Math.Max(worstFoot, fErr);
                Assert.LessOrEqual(fErr, 0.10f, $"장면 {i} {clip.Id}: 접촉점 {foot} vs 공 {evBall} ({fErr * 100:0.0} cm), 루트 보정 {a.RootOffset}");
            }
            TestContext.WriteLine($"최대: 접촉 프레임 {worstFrame * 1000:0.0} ms, 화면 공 {worstBall * 100:0.0} cm, 접촉점 {worstFoot * 100:0.0} cm");
        }
    }
}
