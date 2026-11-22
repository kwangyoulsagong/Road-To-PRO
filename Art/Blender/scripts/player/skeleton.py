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
    # 062 FR-10·13: 손가락 15 × 2 (Humanoid 이름). 기본 위치는 손 뼈를 따라 부채꼴로 둔 자리 표시 — configure가 MPFB 관절로 바꾼다.
    fingers = (("Thumb", -0.55), ("Index", -0.25), ("Middle", 0.0), ("Ring", 0.22), ("Little", 0.42))
    for side, n in ((-1, "Left"), (1, "Right")):
        _, _, _, wr, tip = _arm(side)
        d = tuple(tip[i] - wr[i] for i in range(3))
        for fname, spread in fingers:
            base = (wr[0] + d[0] * 0.55, wr[1] + d[1] * 0.55, wr[2] + 0.02 * spread * -1)
            seg = tuple(c * 0.15 for c in d)
            prev = n + "Hand"
            p = base
            for k, sname in enumerate(("Proximal", "Intermediate", "Distal")):
                q = (p[0] + seg[0], p[1] + seg[1], p[2] + seg[2] + 0.01 * spread)
                b.append((n + fname + sname, prev, p, q))
                prev, p = n + fname + sname, q
    # 062 FR-21: 의상 보조 뼈 6개 (Humanoid 밖, 손가락 뒤). 위치는 kit_bone_rest가 정한다(부모 뼈 기준 비율)
    for name, parent, _ in KIT_BONES:
        h, t = kit_bone_rest(name, {x[0]: x[2] for x in b}, {x[0]: x[3] for x in b})
        b.append((name, parent, h, t))
    return b


# (이름, 부모, 자리) — 상의 밑단 앞·뒤는 골반, 소매는 위팔 끝, 반바지 밑단은 허벅지 위쪽
KIT_BONES = (
    ("Kit_HemF", "Hips", "hemF"), ("Kit_HemB", "Hips", "hemB"),
    ("Kit_Sleeve.L", "LeftUpperArm", "sleeve"), ("Kit_Sleeve.R", "RightUpperArm", "sleeve"),
    ("Kit_Short.L", "LeftUpperLeg", "short"), ("Kit_Short.R", "RightUpperLeg", "short"),
)
KIT_NAMES = tuple(k[0] for k in KIT_BONES)

# 062 FR-21: 보조 뼈 이동 상자 (m). 축 "body" = 선수 레스트 x·y·z, "bone" = (부모 뼈 방향, 직교 1, 직교 2).
# 오프셋은 부모 뼈 레스트 틀에서의 이동이고 포즈에서는 부모 회전을 따라 돈다. 안쪽(피부 쪽)은 셸 틈보다 작게 — 관통 0 (checks.kit_motion)
KIT_BOX = {   # T15 진단: 안쪽 이동·밑단 아래로(엉덩이·배)·소매 팔꿈치 쪽·반바지 반경은 피부를, 밑단 좌우·질주 때 상의×반바지가 겹쳐 0, 반바지 아래로는 무릎 굽힘 때 허벅지 뒤 피부가 부풀어 0(위로만 1 cm). 중간값도 검사(KIT_FRACTIONS)
    "Kit_HemF": ("body", (0.000, 0.000, 0.000), (0.000, 0.030, 0.030)),
    "Kit_HemB": ("body", (0.000, 0.000, -0.030), (0.000, 0.030, 0.000)),
    "Kit_Sleeve.L": ("bone", (-0.012, 0.000, 0.000), (0.000, 0.000, 0.000)),
    "Kit_Sleeve.R": ("bone", (-0.012, 0.000, 0.000), (0.000, 0.000, 0.000)),
    "Kit_Short.L": ("bone", (-0.010, 0.000, 0.000), (0.000, 0.000, 0.000)),
    "Kit_Short.R": ("bone", (-0.010, 0.000, 0.000), (0.000, 0.000, 0.000)),
}


# 062 FR-21 고관절 게이트: 허벅지가 앞으로 들리면(굴곡) 앞 밑단, 뒤로 뻗으면(신전) 뒤 밑단의 이동 상자를 줄인다.
# 질주에서 들린 허벅지의 반바지가 밑단 바로 앞·뒤에 와서 밑단을 밀면 겹친다 (T15 진단: 앞 밑단 z+ 650·뒤 밑단 z− 482, 질주 클립만).
# (방향 +1 굴곡 / −1 신전, 시작 각°, 0이 되는 각°). 각 = 골반 틀에서 허벅지 방향의 앞뒤 기울기 (아래 = 0°, 앞 = +)
KIT_HIP_GATE = {"Kit_HemF": (1, 15.0, 35.0), "Kit_HemB": (-1, 5.0, 15.0)}


def hip_flex(hips_rot, thigh_dir):
    """골반 회전(쿼터니언 같은 @ 연산 객체)·허벅지 방향(선수 좌표) → 굴곡 각(°)."""
    down = hips_rot @ type(thigh_dir)((0.0, -1.0, 0.0))
    fwd = hips_rot @ type(thigh_dir)((0.0, 0.0, 1.0))
    return math.degrees(math.atan2(float(thigh_dir.dot(fwd)), float(thigh_dir.dot(down))))


def kit_gate(name, flex_l, flex_r):
    """보조 뼈 상자 배율 0~1 (게이트 없는 뼈는 1)."""
    g = KIT_HIP_GATE.get(name)
    if g is None:
        return 1.0
    sign, a0, a1 = g
    a = max(sign * flex_l, sign * flex_r)
    t = min(max((a - a0) / (a1 - a0), 0.0), 1.0)
    return 1.0 - t * t * (3 - 2 * t)


def kit_axes(name):
    """보조 뼈 상자 축 3개 (선수 레스트 좌표, 행 = 축)."""
    frame = KIT_BOX[name][0]
    if frame == "body":
        return ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0))
    parent = PARENT[name]
    d = [TAIL[parent][i] - HEAD[parent][i] for i in range(3)]
    ln = math.sqrt(sum(x * x for x in d))
    u = [x / ln for x in d]
    ref = (0.0, 0.0, 1.0)  # 팔·다리 방향과 거의 수직 (A-포즈)
    v = [u[1] * ref[2] - u[2] * ref[1], u[2] * ref[0] - u[0] * ref[2], u[0] * ref[1] - u[1] * ref[0]]
    lv = math.sqrt(sum(x * x for x in v))
    v = [x / lv for x in v]
    w = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
    return (tuple(u), tuple(v), tuple(w))


def kit_layout():
    """Unity KitSecondaryMotion용: 뼈별 축(선수 = Unity 좌표, 레스트)·상자 최소·최대."""
    return {"bones": [{"name": n, "parent": PARENT[n], "axes": [list(a) for a in kit_axes(n)],
                       "min": list(KIT_BOX[n][1]), "max": list(KIT_BOX[n][2]),
                       "hipGate": list(KIT_HIP_GATE[n]) if n in KIT_HIP_GATE else None} for n in KIT_NAMES]}


def kit_bone_rest(name, head, tail):
    """보조 뼈 레스트 (head, tail). 스키닝 회전 중심일 뿐이고 2차 움직임은 이동만 쓴다 — 밑단 높이 근처에 둔다."""
    _, parent, where = next(k for k in KIT_BONES if k[0] == name)
    h0, t0 = head[parent], tail[parent]
    if where in ("hemF", "hemB"):
        z = 0.09 if where == "hemF" else -0.09
        h = (0.0, h0[1], z)
        return h, (0.0, h0[1] - 0.06, z)
    k = 0.55 if where == "sleeve" else 0.35
    h = tuple(h0[i] + (t0[i] - h0[i]) * k for i in range(3))
    t = tuple(h0[i] + (t0[i] - h0[i]) * (k + 0.15) for i in range(3))
    return h, t


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
    core = [(n, p, tuple(joints[n][0]), tuple(joints[n][1])) for n, p, _, _ in old if n not in KIT_NAMES]
    hd, tl = {x[0]: x[2] for x in core}, {x[0]: x[3] for x in core}
    BONES = core + [(n, p) + kit_bone_rest(n, hd, tl) for n, p, _ in KIT_BONES]  # 062: 보조 뼈는 MPFB 관절이 아니라 부모에서
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
