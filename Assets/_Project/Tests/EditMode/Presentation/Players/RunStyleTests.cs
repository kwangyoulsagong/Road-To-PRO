using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>선수 → 달리기·킥 원형 매핑 (specs/055 FR-51·61, AC-08·14).</summary>
    public class RunStyleTests
    {
        public const string RulesPath = "Assets/_Project/Presentation/Players/Data/055-run-styles.json";

        static RunStyleRules _rules;

        internal static RunStyleRules Rules
        {
            get
            {
                if (_rules != null) return _rules;
                var t = AssetDatabase.LoadAssetAtPath<TextAsset>(RulesPath);
                Assert.IsNotNull(t, RulesPath);
                return _rules = RunStyleRules.Parse(t.text);
            }
        }

        static RunStyleChoice C(int id, float h, float w, BodyRole r, string role = null, int dribble = -1, int tempo = -1, int runs = -1,
            int press = -1, int longShots = -1, string[] habits = null)
            => Rules.Choose(new RunStyleInput(id, h, w, r, role, dribble, tempo, runs, press, longShots, habits));

        [Test]
        public void Choose_Goalkeeper_AlwaysGkAndK8()
        {
            for (int id = 1; id <= 50; id++)
            {
                var c = C(id, 185f + id % 10, 80f, BodyRole.Goalkeeper, id % 2 == 0 ? "sweeperKeeper" : null);
                Assert.AreEqual(RunArchetype.GK, c.Run, $"id {id}");
                Assert.AreEqual(KickArchetype.K8, c.Kick, $"id {id}");
            }
        }

        [Test]
        public void Choose_FixedPoints_FollowRules()
        {
            Assert.AreEqual(RunArchetype.R1, C(1, 169f, 64f, BodyRole.Midfielder, "trequartista", dribble: 85, habits: new[] { "dribbleAddict" }).Run);
            Assert.AreEqual(RunArchetype.R2, C(2, 191f, 88f, BodyRole.Forward, "completeForward").Run);
            Assert.AreEqual(RunArchetype.R3, C(3, 177f, 72f, BodyRole.Forward, "shadowStriker", tempo: 85).Run);
            Assert.AreEqual(RunArchetype.R4, C(4, 180f, 75f, BodyRole.Forward, "raumdeuter", runs: 85).Run);
            Assert.AreEqual(RunArchetype.R5, C(5, 174f, 68f, BodyRole.Forward, "traditionalWinger", dribble: 60).Run);
            Assert.AreEqual(RunArchetype.R6, C(6, 188f, 84f, BodyRole.Defender, "stopper").Run);
            Assert.AreEqual(RunArchetype.R7, C(7, 189f, 86f, BodyRole.Forward, "targetMan", tempo: 30).Run);
            Assert.AreEqual(RunArchetype.R8, C(8, 181f, 76f, BodyRole.Midfielder, "boxToBox").Run);
            // 역할 없이 포지션·체형만
            Assert.AreEqual(RunArchetype.R6, C(9, 189f, 84f, BodyRole.Defender).Run);
            Assert.AreEqual(RunArchetype.R8, C(10, 180f, 75f, BodyRole.Midfielder).Run);
        }

        [Test]
        public void Choose_Kick_DefaultsByRole()
        {
            Assert.AreEqual(KickArchetype.K7, C(1, 186f, 82f, BodyRole.Defender, "stopper").Kick);
            Assert.AreEqual(KickArchetype.K5, C(2, 180f, 75f, BodyRole.Midfielder, "regista").Kick);
            Assert.AreEqual(KickArchetype.K3, C(3, 176f, 70f, BodyRole.Forward, "insideForward").Kick);
            Assert.AreEqual(KickArchetype.K1, C(4, 184f, 80f, BodyRole.Forward, "completeForward", longShots: 80).Kick);
        }

        [Test]
        public void RoleKeys_TwentySeven_AllAccepted()
        {
            Assert.AreEqual(27, RunStyleRules.RoleKeys.Count);
            foreach (var k in RunStyleRules.RoleKeys)
                Assert.AreNotEqual(RunArchetype.None, C(11, 180f, 75f, BodyRole.Midfielder, k).Run, k);
            Assert.Throws<ArgumentException>(() => C(12, 180f, 75f, BodyRole.Midfielder, "notARole"));
        }

        /// <summary>200명 표본: 필드 원형 8개가 모두 나오고 한 원형이 40%를 넘지 않는다. GK는 GK에게만.</summary>
        [Test]
        public void Sample200_AllStylesPresent_SharesMatchRules()
        {
            var count = new Dictionary<RunArchetype, int>();
            var keys = RunStyleRules.RoleKeys;
            for (int id = 1; id <= 200; id++)
            {
                string role = keys[(id * 7) % keys.Count];
                BodyRole br = RunStyleRules.BodyRoleOf(role);
                var a = AppearanceGenerator.Generate(new BodyInput(id, role: br));
                int t(int salt) => (int)((uint)(id * 2654435761u + salt * 40503u) % 101u);
                var c = Rules.Choose(new RunStyleInput(id, a.HeightCm, a.WeightKg, br, role, t(1), t(2), t(3), t(4), t(5)));
                count[c.Run] = count.TryGetValue(c.Run, out var n) ? n + 1 : 1;
                Assert.AreEqual(br == BodyRole.Goalkeeper, c.Run == RunArchetype.GK, $"id {id} {role}");
                Assert.That(c.Jitter, Is.InRange(0f, 1f));
            }
            for (var r = RunArchetype.R1; r <= RunArchetype.R8; r++)
            {
                Assert.IsTrue(count.ContainsKey(r), $"{r} 없음");
                Assert.LessOrEqual(count[r], 80, $"{r} {count[r]}명 > 40%");
            }
        }

        [Test, Category("Determinism")]
        public void SameId_Same()
        {
            var a = C(77, 178f, 73f, BodyRole.Midfielder, "mezzala", 60, 55, 70, 40, 20);
            var b = C(77, 178f, 73f, BodyRole.Midfielder, "mezzala", 60, 55, 70, 40, 20);
            Assert.AreEqual(a, b);
            var d = C(78, 178f, 73f, BodyRole.Midfielder, "mezzala", 60, 55, 70, 40, 20);
            Assert.AreNotEqual(a.Jitter, d.Jitter, "흔들기는 id마다 다르다");
        }
    }
}
