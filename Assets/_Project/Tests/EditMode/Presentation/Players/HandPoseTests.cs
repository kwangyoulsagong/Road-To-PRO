using System;
using NUnit.Framework;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>062 FR-10~13 손 포즈 표·섞기·리그 손가락 (AC-02·14).</summary>
    public class HandPoseTests
    {
        static ClipLibrary Lib() => ClipLibrary.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(PlayerRigTests.Gen + "/player.clips.json").text);

        [Test]
        public void Tags_Map_FourPoses_AllClipsKnown()
        {
            var lib = Lib();
            Assert.IsNotNull(lib.HandPoses, "clips.json handPoses");
            CollectionAssert.IsSubsetOf(new[] { "open", "relax", "half", "fist" }, new[] { lib.HandPoses.Name(0), lib.HandPoses.Name(1), lib.HandPoses.Name(2), lib.HandPoses.Name(3) });
            for (int i = 0; i < lib.Count; i++)
            {
                var c = lib.Get(i);
                Assert.IsNotNull(c.HandPose, c.Id);
                Assert.AreEqual(c.HandPose, lib.HandPoses.Name(c.HandPoseIndex), c.Id);
            }
            Assert.AreEqual("fist", lib.Get(lib.IndexOf("gk_punch")).HandPose);
            Assert.AreEqual("open", lib.Get(lib.IndexOf("gk_catch_chest")).HandPose);
        }

        [Test]
        public void Blend_SingleClip_Exact_TwoClips_Between_NoState()
        {
            var lib = Lib();
            int fist = lib.IndexOf("gk_punch"), open = lib.IndexOf("gk_catch_chest");
            Span<Quat4> q = stackalloc Quat4[HandPoseTable.Bones];
            HandPose.Blend(new[] { fist }, new[] { 1f }, lib, q);
            var t = lib.HandPoses;
            int fi = t.IndexOf("fist"), oi = t.IndexOf("open");
            for (int b = 0; b < HandPoseTable.Bones; b++)
                Assert.That(Math.Abs(q[b].Dot(t.Rotation(fi, b))), Is.GreaterThan(0.99999f));
            var mc = new[] { fist, open };
            var mw = new[] { 0.5f, 0.5f };
            Span<Quat4> q2 = stackalloc Quat4[HandPoseTable.Bones];
            HandPose.Blend(mc, mw, lib, q2);
            Span<Quat4> q3 = stackalloc Quat4[HandPoseTable.Bones];
            HandPose.Blend(mc, mw, lib, q3); // 같은 입력 → 같은 출력 (상태 없음)
            int idx = Array.IndexOf(t.BoneNames, "RightIndexIntermediate");
            float dF = Math.Abs(q2[idx].Dot(t.Rotation(fi, idx))), dO = Math.Abs(q2[idx].Dot(t.Rotation(oi, idx)));
            Assert.That(dF, Is.LessThan(0.99999f));
            Assert.That(dO, Is.LessThan(0.99999f));
            Assert.AreEqual(q2[idx].W, q3[idx].W);
        }

        [Test]
        public void Rig_BindsThirtyFingers_FistCurlsTowardPalm()
        {
            var rig = PlayerRigTests.NewRig();
            try
            {
                var lib = Lib();
                rig.BindFingers(lib.HandPoses);
                Assert.AreEqual(30, rig.FingerBoneCount);
                var tip = rig.Bone("RightIndexDistal");
                var hand = rig.Bone("RightHand");
                Vector3 p0 = hand.InverseTransformPoint(tip.position);
                Span<Quat4> q = stackalloc Quat4[HandPoseTable.Bones];
                HandPose.Blend(new[] { lib.IndexOf("gk_punch") }, new[] { 1f }, lib, q);
                rig.ApplyFingers(q);
                Vector3 p1 = hand.InverseTransformPoint(tip.position);
                Assert.Greater((p1 - p0).magnitude, 0.01f, "주먹이면 검지 끝이 1 cm 넘게 움직인다");
                Span<Quat4> id = stackalloc Quat4[HandPoseTable.Bones];
                for (int i = 0; i < id.Length; i++) id[i] = Quat4.Identity;
                rig.ApplyFingers(id);
                Assert.Less((hand.InverseTransformPoint(tip.position) - p0).magnitude, 1e-4f, "항등이면 레스트로 돌아온다");
            }
            finally { UnityEngine.Object.DestroyImmediate(rig.gameObject); }
        }

        [Test]
        public void Clips_HaveNoFingerCurves_CoreBonesKept()
        {
            // FR-13: 클립은 손가락에 키가 없다(런타임 HandPose가 돌린다). glb JSON 채널을 직접 본다 —
            // AnimationUtility로 에디터 커브를 읽으면 클립 메모리 측정(PlayerImportTests)이 부풀어 오른다.
            var bytes = System.IO.File.ReadAllBytes(PlayerRigTests.Gen + "/player.glb");
            int len = BitConverter.ToInt32(bytes, 12);
            var root = (RoadToPro.Core.Serialization.JsonObject)RoadToPro.Core.Serialization.JsonReader.Parse(System.Text.Encoding.UTF8.GetString(bytes, 20, len));
            root.TryGet("nodes", out var nodesObj);
            var nodes = (System.Collections.Generic.List<object>)nodesObj;
            var finger = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < nodes.Count; i++)
                if (((RoadToPro.Core.Serialization.JsonObject)nodes[i]).TryGet("name", out var nm) && nm is string ns
                    && (ns.EndsWith("Proximal") || ns.EndsWith("Intermediate") || ns.EndsWith("Distal"))) finger.Add(i);
            Assert.AreEqual(30, finger.Count, "glb 손가락 노드");
            root.TryGet("animations", out var animsObj);
            int channels = 0;
            foreach (var an in (System.Collections.Generic.List<object>)animsObj)
            {
                ((RoadToPro.Core.Serialization.JsonObject)an).TryGet("channels", out var chObj);
                foreach (var ch in (System.Collections.Generic.List<object>)chObj)
                {
                    ((RoadToPro.Core.Serialization.JsonObject)ch).TryGet("target", out var tg);
                    ((RoadToPro.Core.Serialization.JsonObject)tg).TryGet("node", out var node);
                    Assert.IsFalse(finger.Contains((int)(double)node), "손가락 채널");
                    channels++;
                }
            }
            Assert.Greater(channels, 1000);
            var rig = PlayerRigTests.NewRig();
            try
            {
                foreach (var n in new[] { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm",
                             "LeftHand", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightToes" })
                    Assert.IsNotNull(rig.Bone(n), n);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig.gameObject); }
        }
    }
}
