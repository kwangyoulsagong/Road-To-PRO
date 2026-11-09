using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Tests.EditMode.Presentation.Players;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>장면 교체 (specs/041 FR-31).</summary>
    public class SwapPlanTests
    {
        [Test]
        public void SwapPlan_SecondScene_MatchesFreshInitPose()
        {
            var a = SyntheticScenes.Run(1.4f, 0.3f, 0f, 0f);
            var b = SyntheticScenes.Run(5f, 0.1f, 0f, 0f);
            var planB = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, b, NullLogger.Instance);
            float t = b.StartTime + 0.5f;

            Vector3 swapped, fresh;
            using (var kit = new AnimRigKit(a, 1))
            {
                kit.System.ApplyAt(a.StartTime + 0.5f);
                kit.System.SwapPlan(planB);
                kit.System.ApplyAt(t);
                swapped = kit.Rigs[1].Bone("LeftFoot").position;
                Assert.AreSame(planB, kit.System.Plan);
            }
            using (var kit = new AnimRigKit(b, 1))
            {
                kit.System.ApplyAt(t);
                fresh = kit.Rigs[1].Bone("LeftFoot").position;
            }

            Assert.AreEqual(fresh.x, swapped.x, 0.001f);
            Assert.AreEqual(fresh.y, swapped.y, 0.001f);
            Assert.AreEqual(fresh.z, swapped.z, 0.001f);
        }

        [Test]
        public void SwapPlan_BeforeInit_Throws()
        {
            var go = new GameObject("SwapPlanTest");
            try
            {
                var system = go.AddComponent<PlayerAnimationSystem>();
                var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, SyntheticScenes.Run(1.4f, 0.3f, 0f, 0f), NullLogger.Instance);
                Assert.Throws<System.InvalidOperationException>(() => system.SwapPlan(plan));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
