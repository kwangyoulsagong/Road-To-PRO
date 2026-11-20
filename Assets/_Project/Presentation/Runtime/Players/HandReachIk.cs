using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Presentation.Players
{
    /// <summary>
    /// GK 손 IK (specs/080 FR-04). 이동량은 AnimPlan이 결정론으로 정하고(HandReachTrack), 여기서는 위팔–아래팔–손 2본 해석으로 푼다.
    /// 손 목표 = 지금 손 위치 + 이동량(가중치 곱함). 손 방향은 유지. 팔이 다 펴지는 한계는 SolveTwoBone이 자른다.
    /// 본 참조는 생성 때 캐시 — 프레임 경로 할당 0. 재생 전용이라 시뮬 상태를 바꾸지 않는다.
    /// </summary>
    public sealed class HandReachIk
    {
        readonly Transform _lu, _ll, _lh, _ru, _rl, _rh;

        public HandReachIk(PlayerRig rig)
            : this(rig.Bone("LeftUpperArm"), rig.Bone("LeftLowerArm"), rig.Bone("LeftHand"),
                rig.Bone("RightUpperArm"), rig.Bone("RightLowerArm"), rig.Bone("RightHand")) { }

        /// <summary>본을 직접 준다 (성능 테스트).</summary>
        public HandReachIk(Transform leftUpper, Transform leftLower, Transform leftHand, Transform rightUpper, Transform rightLower, Transform rightHand)
        {
            _lu = leftUpper;
            _ll = leftLower;
            _lh = leftHand;
            _ru = rightUpper;
            _rl = rightLower;
            _rh = rightHand;
        }

        /// <summary>hands: 1 = 왼손, 2 = 오른손. reach = 월드 이동량(가중치 이미 곱함).</summary>
        public void Apply(Vector3 reach, int hands)
        {
            if (hands == 0 || reach.sqrMagnitude < 1e-8f) return;
            if ((hands & 1) != 0 && _lh != null) Solve(_lu, _ll, _lh, reach);
            if ((hands & 2) != 0 && _rh != null) Solve(_ru, _rl, _rh, reach);
        }

        static void Solve(Transform upper, Transform lower, Transform hand, Vector3 reach)
        {
            var rot = hand.rotation;
            FootGrounding.SolveTwoBone(upper, lower, hand, hand.position + reach);
            hand.rotation = rot;
        }
    }
}
