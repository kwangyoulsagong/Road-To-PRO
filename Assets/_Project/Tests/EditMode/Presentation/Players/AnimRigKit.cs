using System.Linq;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>테스트 공용: 합성 하이라이트 + 실제 리그·클립으로 애니메이션 시스템을 꾸린다.</summary>
    internal sealed class AnimRigKit : System.IDisposable
    {
        public readonly AnimPlan Plan;
        public readonly PlayerAnimationSystem System;
        public readonly PlayerRig[] Rigs;
        readonly GameObject _host;

        static AnimationClip[] _clips;

        public static AnimationClip[] Clips => _clips ??= PlayerAnimationSystem.Order(AnimTestKit.Lib,
            AssetDatabase.LoadAllAssetsAtPath(PlayerRigTests.Gen + "/player.glb").OfType<AnimationClip>());

        /// <param name="entities">리그를 만들 엔티티(나머지는 계획만).</param>
        public AnimRigKit(HighlightData d, params int[] entities)
        {
            Plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            _host = new GameObject("AnimRigKit");
            System = _host.AddComponent<PlayerAnimationSystem>();
            Rigs = new PlayerRig[HighlightData.Entities];
            foreach (var e in entities) Rigs[e] = PlayerRigTests.NewRig();
            System.Init(Plan, Rigs, Clips, null);
        }

        public static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>클립 접촉 규칙을 지금 본 위치로 계산한 점(접촉 순간 공 중심이 있어야 할 곳).</summary>
        public Vec3 ContactPoint(int e, ContactRule r)
        {
            var rig = Rigs[e];
            var bone = V(rig.Bone(r.Bone).position);
            var toward = r.Toward != null ? V(rig.Bone(r.Toward).position) : bone;
            return r.Evaluate(bone, toward, V(rig.Bone("LeftUpperLeg").position), V(rig.Bone("RightUpperLeg").position));
        }

        public void Dispose()
        {
            foreach (var r in Rigs)
                if (r != null) Object.DestroyImmediate(r.gameObject);
            Object.DestroyImmediate(_host);
        }
    }
}
