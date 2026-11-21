using System;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;
using Unity.Profiling;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 062 FR-10~13·21·22: 클립·발 접지·손 IK 뒤에 손가락 포즈와 키트 보조 뼈를 얹는다 (PlayerAnimationSystem.ApplyAt 안 = LateUpdate 경로).
    /// 둘 다 f(재생 시각, 포즈 지시, 리플레이 데이터)라 프레임 누적 상태가 없다 (AC-14). 할당 0, 마커 "Players.MotionExtras".
    /// </summary>
    public sealed class PlayerMotionExtras
    {
        public static readonly ProfilerMarker Marker = new ProfilerMarker("Players.MotionExtras");

        readonly ClipLibrary _lib;
        readonly KitSecondaryMotion _kit;
        readonly bool _hands;

        public PlayerMotionExtras(ClipLibrary lib, KitSecondaryMotion kit, PlayerRig[] rigs)
        {
            _lib = lib;
            _kit = kit;
            _hands = lib?.HandPoses != null;
            foreach (var r in rigs)
            {
                if (r == null) continue;
                if (_hands) r.BindFingers(lib.HandPoses);
                if (kit != null) r.BindKitBones(kit);
            }
        }

        public KitSecondaryMotion Kit => _kit;

        /// <summary>한 선수에 적용. data가 null이면 궤적 가속도 없이 클립 커브만 쓴다.</summary>
        public void Apply(PlayerRig rig, in PoseSample s, HighlightData data, int entity, float t)
        {
            if (_hands && rig.FingerBoneCount > 0)
            {
                Span<Quat4> q = stackalloc Quat4[HandPoseTable.Bones];
                HandPose.Blend(s, _lib, q);
                rig.ApplyFingers(q);
            }
            if (_kit != null && rig.KitBoneCount > 0)
            {
                rig.HipFlex(out float fl, out float fr);
                var src = new PoseAccelSource(_lib, data, entity, t, s);
                Span<Vec3> o = stackalloc Vec3[KitSecondaryMotion.Bones];
                _kit.Evaluate(ref src, t, fl, fr, o);
                rig.ApplyKitBones(o);
            }
        }
    }
}
