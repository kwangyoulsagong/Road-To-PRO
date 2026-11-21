using System;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic;
using Unity.Profiling;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 062 FR-10~13·21·22: 클립·발 접지·손 IK 뒤에 손가락 포즈와 키트 보조 뼈를 얹는다 (PlayerAnimationSystem.ApplyAt 안 = LateUpdate 경로).
    /// FR-70·71: 표정 가중치(선수의 ExpressionBlend, 재생 시각 기준)를 LOD0·클로즈업 머리에 넣는다.
    /// 모두 f(재생 시각, 포즈 지시, 리플레이 데이터)라 프레임 누적 상태가 없다 (AC-14). 할당 0, 마커 "Players.MotionExtras".
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
            // 062 T36: LOD1 이하는 손가락이 장갑형(손 뼈로 합침)이라 손가락 뼈를 돌려도 보이지 않는다, LOD2는 키트 보조 뼈 가중치가 없다
            // → 그 LOD에서는 갱신을 건너뛴다 (062-model-perf 애니메이션 구간 p95 2.47 ms, Mono 개발 빌드)
            int lod = rig.CurrentLod;
            if (_hands && rig.FingerBoneCount > 0 && lod == 0)
            {
                Span<Quat4> q = stackalloc Quat4[HandPoseTable.Bones];
                HandPose.Blend(s, _lib, q);
                rig.ApplyFingers(q);
            }
            if (_kit != null && rig.KitBoneCount > 0 && lod <= 1)
            {
                rig.HipFlex(out float fl, out float fr);
                var src = new PoseAccelSource(_lib, data, entity, t, s);
                Span<Vec3> o = stackalloc Vec3[KitSecondaryMotion.Bones];
                _kit.Evaluate(ref src, t, fl, fr, o);
                rig.ApplyKitBones(o);
            }
            Span<float> w = stackalloc float[ExpressionLogic.Keys.Length];
            rig.ExpressionState.Weights(t, w);
            rig.SetExpression(w);
        }
    }
}
