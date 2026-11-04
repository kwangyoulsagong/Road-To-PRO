using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// 발 접지 보정 (specs/043 FR-45). 블렌드로 발이 바닥 아래로 내려가면 2본 해석 IK로 발목을 올린다(평평한 피치 y = 0).
    /// 클립 자체는 생성기가 지면 관통 0을 보장한다(player.layout.json summary.ground) — 여기서는 클립 사이 블렌드 오차만 잡는다.
    /// </summary>
    public static class FootGrounding
    {
        /// <summary>관절 굵기 근사 (생성기 GROUND_MIN과 같은 값, 기준 키 1.80 m).</summary>
        public const float AnkleMin = 0.035f, ToesMin = 0.0f;

        /// <summary>반환: 이번 프레임에 올린 최대 높이(m).</summary>
        public static float Apply(PlayerRig rig)
        {
            float s = rig.transform.lossyScale.y;
            float a = Leg(rig.Bone("LeftUpperLeg"), rig.Bone("LeftLowerLeg"), rig.Bone("LeftFoot"), rig.Bone("LeftToes"), s);
            float b = Leg(rig.Bone("RightUpperLeg"), rig.Bone("RightLowerLeg"), rig.Bone("RightFoot"), rig.Bone("RightToes"), s);
            return Mathf.Max(a, b);
        }

        static float Leg(Transform hip, Transform knee, Transform ankle, Transform toes, float s)
        {
            float lift = Mathf.Max(AnkleMin * s - ankle.position.y, ToesMin * s - toes.position.y, 0f);
            if (lift <= 1e-4f) return 0f;
            var target = ankle.position + Vector3.up * lift;
            var footRot = ankle.rotation;
            SolveTwoBone(hip, knee, ankle, target);
            ankle.rotation = footRot; // 발 방향 유지
            return lift;
        }

        /// <summary>표준 2본 IK: 무릎 굽힘 평면 유지.</summary>
        public static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target)
        {
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = (pb - pa).magnitude, lbc = (pc - pb).magnitude;
            float lat = Mathf.Clamp((target - pa).magnitude, 1e-4f, (lab + lbc) * 0.9999f);
            float cur = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pc - pa).normalized, (pb - pa).normalized), -1f, 1f));
            float cur2 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pa - pb).normalized, (pc - pb).normalized), -1f, 1f));
            float want = Mathf.Acos(Mathf.Clamp((lab * lab + lat * lat - lbc * lbc) / (2 * lab * lat), -1f, 1f));
            float want2 = Mathf.Acos(Mathf.Clamp((lab * lab + lbc * lbc - lat * lat) / (2 * lab * lbc), -1f, 1f));
            var axis = Vector3.Cross(pc - pa, pb - pa);
            if (axis.sqrMagnitude < 1e-10f) axis = a.right;
            axis.Normalize();
            a.rotation = Quaternion.AngleAxis((want - cur) * Mathf.Rad2Deg, axis) * a.rotation;
            b.rotation = Quaternion.AngleAxis((want2 - cur2) * Mathf.Rad2Deg, axis) * b.rotation;
            pc = c.position;
            a.rotation = Quaternion.FromToRotation(pc - pa, target - pa) * a.rotation;
        }
    }
}
