"""뼈대 정의 (specs/043 FR-01·30). Unity Humanoid 이름 규칙. 선수 좌표(space.py), 기준 키 1.80 m 레스트 포즈(A-포즈 팔 45°).

순수 파이썬: bpy 없이 테스트할 수 있다. 키 변화는 Unity에서 루트 균등 스케일(키 / REF_HEIGHT)로 한다.
"""
import math

REF_HEIGHT = 1.80
ARM_ANGLE = 45.0  # 레스트 포즈 팔: 수직에서 바깥으로

HIP_X = 0.10
ANKLE_Y = 0.085
SHOULDER_Y = 1.46

_S, _C = math.sin(math.radians(ARM_ANGLE)), math.cos(math.radians(ARM_ANGLE))
L_UPPER_ARM, L_LOWER_ARM, L_HAND = 0.29, 0.26, 0.17
L_THIGH, L_SHIN = 0.43, 0.415


def _arm(side):
    """side = +1 오른쪽, −1 왼쪽. 반환: 어깨 시작, 팔 시작, 팔꿈치, 손목, 손끝."""
    s = side
    sh0 = (s * 0.03, 1.45, 0.0)
    sh1 = (s * 0.17, SHOULDER_Y, 0.0)
    d = (s * _S, -_C, 0.0)
    el = tuple(sh1[i] + d[i] * L_UPPER_ARM for i in range(3))
    wr = tuple(el[i] + d[i] * L_LOWER_ARM for i in range(3))
    tip = tuple(wr[i] + d[i] * L_HAND for i in range(3))
    return sh0, sh1, el, wr, tip


def _leg(side):
    s = side
    hip = (s * HIP_X, 0.93, 0.0)
    knee = (s * HIP_X, 0.93 - L_THIGH, 0.0)
    ankle = (s * HIP_X, ANKLE_Y, 0.0)
    ball = (s * HIP_X, 0.025, 0.13)
    toe = (s * HIP_X, 0.02, 0.20)
    return hip, knee, ankle, ball, toe


def bones():
    """[(이름, 부모, head, tail)] 부모가 먼저 오는 순서."""
    b = [
        ("Hips", None, (0.0, 0.98, 0.0), (0.0, 1.08, 0.0)),
        ("Spine", "Hips", (0.0, 1.08, 0.0), (0.0, 1.22, 0.0)),
        ("Chest", "Spine", (0.0, 1.22, 0.0), (0.0, 1.36, 0.0)),
        ("UpperChest", "Chest", (0.0, 1.36, 0.0), (0.0, 1.50, 0.0)),
        ("Neck", "UpperChest", (0.0, 1.50, 0.0), (0.0, 1.60, 0.0)),
        ("Head", "Neck", (0.0, 1.60, 0.0), (0.0, 1.80, 0.0)),
    ]
    for side, n in ((-1, "Left"), (1, "Right")):
        sh0, sh1, el, wr, tip = _arm(side)
        b += [
            (n + "Shoulder", "UpperChest", sh0, sh1),
            (n + "UpperArm", n + "Shoulder", sh1, el),
            (n + "LowerArm", n + "UpperArm", el, wr),
            (n + "Hand", n + "LowerArm", wr, tip),
        ]
    for side, n in ((-1, "Left"), (1, "Right")):
        hip, knee, ankle, ball, toe = _leg(side)
        b += [
            (n + "UpperLeg", "Hips", hip, knee),
            (n + "LowerLeg", n + "UpperLeg", knee, ankle),
            (n + "Foot", n + "LowerLeg", ankle, ball),
            (n + "Toes", n + "Foot", ball, toe),
        ]
    return b


BONES = bones()
NAMES = [x[0] for x in BONES]
PARENT = {x[0]: x[1] for x in BONES}
HEAD = {x[0]: x[2] for x in BONES}
TAIL = {x[0]: x[3] for x in BONES}


def configure(joints):
    """056: 기준 선수(MPFB2) 관절로 뼈대 위치를 바꾼다. 이름·계층·순서는 043 그대로(FR-62).
    joints = {뼈 이름: (head, tail)} 선수 좌표. 클립·검사는 모듈 속성(S.HEAD 등)을 호출 시점에 읽으므로 생성 전에 한 번 부른다."""
    global BONES, NAMES, PARENT, HEAD, TAIL, HIP_X, ANKLE_Y, SHOULDER_Y, L_UPPER_ARM, L_LOWER_ARM, L_HAND, L_THIGH, L_SHIN
    old = BONES
    BONES = [(n, p, tuple(joints[n][0]), tuple(joints[n][1])) for n, p, _, _ in old]
    NAMES = [x[0] for x in BONES]
    PARENT = {x[0]: x[1] for x in BONES}
    HEAD = {x[0]: x[2] for x in BONES}
    TAIL = {x[0]: x[3] for x in BONES}

    def L(n):
        return math.dist(HEAD[n], TAIL[n])

    HIP_X = abs(HEAD["RightUpperLeg"][0])
    ANKLE_Y = HEAD["RightFoot"][1]
    SHOULDER_Y = HEAD["RightUpperArm"][1]
    L_UPPER_ARM, L_LOWER_ARM, L_HAND = L("RightUpperArm"), L("RightLowerArm"), L("RightHand")
    L_THIGH, L_SHIN = L("RightUpperLeg"), L("RightLowerLeg")
