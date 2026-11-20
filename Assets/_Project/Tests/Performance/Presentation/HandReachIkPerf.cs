using System.Diagnostics;
using NUnit.Framework;
using RoadToPro.Presentation.Players;
using Unity.PerformanceTesting;
using UnityEngine;

namespace RoadToPro.Tests.Performance.Presentation
{
    /// <summary>specs/080 AC-07: GK 손 IK 1회(양손) ≤ 0.05 ms [제안, 2026-11-20 승인]. 에디터 측정(기록) — 판정은 이 예산.</summary>
    public class HandReachIkPerf
    {
        public const double BudgetMs = 0.05;
        const int Calls = 1000;

        static Transform Bone(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = pos;
            return t;
        }

        [Test, Performance]
        public void Solve_Under50us()
        {
            var root = new GameObject("rig").transform;
            try
            {
                var lu = Bone("LeftUpperArm", root, new Vector3(-0.20f, 1.40f, 0f));
                var ll = Bone("LeftLowerArm", lu, new Vector3(-0.45f, 1.20f, 0.05f));
                var lh = Bone("LeftHand", ll, new Vector3(-0.62f, 1.00f, 0.15f));
                var ru = Bone("RightUpperArm", root, new Vector3(0.20f, 1.40f, 0f));
                var rl = Bone("RightLowerArm", ru, new Vector3(0.45f, 1.20f, 0.05f));
                var rh = Bone("RightHand", rl, new Vector3(0.62f, 1.00f, 0.15f));
                var ik = new HandReachIk(lu, ll, lh, ru, rl, rh);
                var start = new[] { lu.localRotation, ll.localRotation, lh.localRotation, ru.localRotation, rl.localRotation, rh.localRotation };
                void Reset()
                {
                    lu.localRotation = start[0]; ll.localRotation = start[1]; lh.localRotation = start[2];
                    ru.localRotation = start[3]; rl.localRotation = start[4]; rh.localRotation = start[5];
                }
                for (int i = 0; i < 50; i++) { Reset(); ik.Apply(new Vector3(0.1f, -0.05f, 0.08f), 3); } // 데우기
                var g = new SampleGroup("HandReachIk.Solve", SampleUnit.Millisecond);
                double best = double.MaxValue;
                for (int r = 0; r < 5; r++)
                {
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < Calls; i++)
                    {
                        Reset();
                        ik.Apply(new Vector3(0.1f + (i & 7) * 0.01f, -0.05f, 0.08f), 3);
                    }
                    sw.Stop();
                    double per = sw.Elapsed.TotalMilliseconds / Calls;
                    Measure.Custom(g, per);
                    if (per < best) best = per;
                }
                TestContext.WriteLine($"손 IK 1회(양손, 본 되돌리기 포함) 최선 {best * 1000:0.0} µs (예산 {BudgetMs * 1000:0} µs)");
                Assert.LessOrEqual(best, BudgetMs);
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }
    }
}
