using System;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>접지 구간 발 미끄러짐 ≤ 3 cm (specs/043 AC-09). 실제 Unity 본(Toes) 위치를 60 fps로 잰다.</summary>
    public class LocomotionSlideTests
    {
        const float GroundY = 0.03f;   // 발끝 높이 이하 = 접지
        const float Limit = 0.03f;

        static float WorstSlide(HighlightData d, out int stances)
        {
            using var kit = new AnimRigKit(d, 0);
            var rig = kit.Rigs[0];
            float worst = 0;
            stances = 0;
            foreach (var foot in new[] { "LeftToes", "RightToes" })
            {
                var toe = rig.Bone(foot);
                bool grounded = false;
                Vector3 start = default;
                float run = 0;
                for (int k = 30; k <= (int)(SyntheticScenes.Length * 60) - 1; k++)
                {
                    kit.System.ApplyAt(SyntheticScenes.Start + k / 60f);
                    var p = toe.position;
                    bool g = p.y <= GroundY;
                    if (g && !grounded) { start = p; run = 0; stances++; }
                    if (g) run = Mathf.Max(run, new Vector2(p.x - start.x, p.z - start.z).magnitude);
                    if (!g && grounded) worst = Mathf.Max(worst, run);
                    grounded = g;
                }
                if (grounded) worst = Mathf.Max(worst, run);
            }
            return worst;
        }

        [TestCase(0f, 0f, 0f, TestName = "Stand")]
        [TestCase(1.4f, 0f, 0f, TestName = "Walk")]
        [TestCase(2.4f, 0f, 0f, TestName = "WalkJogBlend")]
        [TestCase(3.4f, 0f, 0f, TestName = "Jog")]
        [TestCase(5.5f, 0f, 0f, TestName = "JogSprintBlend")]
        [TestCase(7.6f, 0f, 0f, TestName = "Sprint")]
        [TestCase(2.5f, 45f, 0f, TestName = "DiagonalForward")]
        [TestCase(1.5f, 90f, 0f, TestName = "SideStep")]
        [TestCase(1.2f, 180f, 0f, TestName = "Backpedal")]
        [TestCase(3.4f, 0f, 0.8f, TestName = "JogTurning")]
        public void SlideUnder3cm(float speed, float dir, float turn)
        {
            var d = SyntheticScenes.Run(speed, 0.3f, dir, turn);
            float worst = WorstSlide(d, out int stances);
            TestContext.WriteLine($"속도 {speed} 방향 {dir} 회전 {turn}: 접지 {stances}회, 최대 미끄러짐 {worst * 100:0.0} cm");
            Assert.Greater(stances, 0, "접지 구간이 있어야 한다");
            Assert.LessOrEqual(worst, Limit);
        }
    }
}
