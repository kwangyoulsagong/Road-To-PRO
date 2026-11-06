using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Logic.Motion;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 본에 자세·발 고정 적용 (specs/054 FR-10·11·13·20~22). 그래프 평가 뒤, 지면 보정 앞. 포즈 = f(t) — 프레임 사이 상태 없음.
    /// 자세: 온몸을 지면 위 루트 기준으로 기울인다. 발 고정: 발끝이 디딤 위치에 오도록 발목 목표를 옮겨 2본 IK.
    /// </summary>
    public sealed class FootLock
    {
        const float ToeFixSq = 0.003f * 0.003f;
        const float MinStepLift = 0.05f, MaxStepLift = 0.08f, StepLiftRatio = 1.5f, MinStepDistance = 0.005f;
        /// <summary>놓는 시간 중 수평 이동 없이 드는 앞부분 비율: sin(π·0.25) × 5 cm > 3 cm.</summary>
        const float LiftFirst = 0.25f;
        readonly Transform _root, _hips, _chest;
        readonly Transform _hipL, _kneeL, _ankleL, _toesL, _hipR, _kneeR, _ankleR, _toesR;

        public FootLock(PlayerRig rig)
        {
            _root = rig.transform;
            _hips = rig.Bone("Hips");
            _chest = rig.Bone("UpperChest");
            _hipL = rig.Bone("LeftUpperLeg");
            _kneeL = rig.Bone("LeftLowerLeg");
            _ankleL = rig.Bone("LeftFoot");
            _toesL = rig.Bone("LeftToes");
            _hipR = rig.Bone("RightUpperLeg");
            _kneeR = rig.Bone("RightLowerLeg");
            _ankleR = rig.Bone("RightFoot");
            _toesR = rig.Bone("RightToes");
        }

        /// <summary>roll + = 오른쪽, pitch + = 앞 (라디안).</summary>
        public void ApplyPosture(float roll, float pitch, float twist)
        {
            if (twist != 0f) _chest.Rotate(Vector3.up, twist * Mathf.Rad2Deg, Space.World);
            if (roll == 0f && pitch == 0f) return;
            var o = _root.position;
            if (pitch != 0f) _hips.RotateAround(o, _root.right, pitch * Mathf.Rad2Deg);
            if (roll != 0f) _hips.RotateAround(o, _root.forward, -roll * Mathf.Rad2Deg);
        }

        public void ApplyFeet(FootPlantPlan plan, int e, float t)
        {
            Leg(plan, e, true, t, _hipL, _kneeL, _ankleL, _toesL);
            Leg(plan, e, false, t, _hipR, _kneeR, _ankleR, _toesR);
        }

        static void Leg(FootPlantPlan plan, int e, bool left, float t, Transform hip, Transform knee, Transform ankle, Transform toes)
        {
            float w = plan.Query(e, left, t, out var x, out var z, out var release);
            if (w <= 0f) return;
            var tp = toes.position;
            var ap = ankle.position;
            float y = ap.y;
            if (release >= 0f)
            {
                // 클립 경로로 옮기는 동안 발을 든다: 먼저 3 cm 위로 올린 뒤 수평으로 옮기는 스텝 — 땅에 끌리지 않게 (FR-10·13)
                float dx = x - tp.x, dz = z - tp.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > MinStepDistance)
                {
                    float lift = Mathf.Clamp(dist * StepLiftRatio, MinStepLift, MaxStepLift);
                    y += lift * Mathf.Sin(release * Mathf.PI);
                }
                float u = (release - LiftFirst) / (1f - LiftFirst);
                if (u <= 0f) w = 1f;
                else
                {
                    u = 1f - u;
                    w = u * u * (3f - 2f * u);
                }
            }
            var target = new Vector3(ap.x + (x - tp.x) * w, y, ap.z + (z - tp.z) * w);
            if ((target - ap).sqrMagnitude < 1e-7f) return;
            var rot = ankle.rotation;
            FootGrounding.SolveTwoBone(hip, knee, ankle, target);
            ankle.rotation = rot;
            if (w < 1f) return;
            // 다리가 다 펴져 발목이 못 미치면 발을 더 세워 발끝을 디딤 위치에 맞춘다 (뒤꿈치 들기)
            var t2 = toes.position;
            float ex = x - t2.x, ez = z - t2.z;
            if (ex * ex + ez * ez < ToeFixSq) return;
            var a2 = ankle.position;
            var want = new Vector3(x, tp.y, z) - a2;
            var have = t2 - a2;
            if (have.sqrMagnitude > 1e-6f && want.sqrMagnitude > 1e-6f)
                ankle.rotation = Quaternion.FromToRotation(have, want) * ankle.rotation;
        }
    }
}
