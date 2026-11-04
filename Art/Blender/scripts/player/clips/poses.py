"""키 포즈 문법 (specs/043 FR-31). 키 포즈 = 숫자 dict, 키 사이는 숫자 보간(smoothstep) 후 Pose로 만든다.

키(모두 선택, 없으면 기본값 = 서 있는 준비 자세):
  hips: (dx, dy, dz) m        hrot: (pitch, yaw, roll)°  — 골반
  spine: (pitch, yaw, roll)°  — Spine·Chest·UpperChest에 0.4·0.35·0.25로 나눔. pitch + = 앞으로 숙임, yaw + = 오른쪽으로 돎
  head: (pitch, yaw)°         — Neck 0.4 · Head 0.6
  armL/armR: (down, flex, elbow)°  down = 레스트 A-포즈(45°)에서 몸쪽으로 내림(35 ≈ 차렷), flex + = 앞으로 듦, elbow + = 굽힘
  legL/legR FK: (flex, knee, ankle, abduct, rot)°  flex + = 허벅지 앞으로, knee + = 굽힘, ankle + = 발끝 내림, abduct + = 바깥, rot + = 발끝 바깥
  ikL/ikR IK: (x, z, lift, yaw)  발목 목표(선수 좌표, 바닥 기준 높이 lift), 발 방향 yaw°. ik가 있으면 FK leg 무시
  footL/footR: (pitch, roll)°  IK 발의 추가 기울기
"""
import math

from mathutils import Quaternion, Vector

from .. import skeleton as S
from ..pose import Pose, X, Y, Z, leg_ik, q
from .base import smooth

ARM_DOWN_MAX = 27.0  # 레스트 45°에서 내리는 최대 각 → 수직에서 18° (056, 손이 반바지를 뚫지 않게)

STAND = {
    "hips": (0.0, -0.02, 0.0), "hrot": (0.0, 0.0, 0.0), "spine": (3.0, 0.0, 0.0), "head": (-2.0, 0.0),
    "armL": (33.0, 5.0, 15.0), "armR": (33.0, 5.0, 15.0),
    "ikL": (-0.12, 0.0, 0.0, -6.0), "ikR": (0.12, 0.0, 0.0, 6.0),
    "footL": (0.0, 0.0), "footR": (0.0, 0.0),
}
READY = dict(STAND, hips=(0.0, -0.08, -0.02), spine=(14.0, 0.0, 0.0), head=(-10.0, 0.0),
             armL=(25.0, 10.0, 45.0), armR=(25.0, 10.0, 45.0), ikL=(-0.17, 0.02, 0.0, -10.0), ikR=(0.17, 0.02, 0.0, 10.0))


def merged(base, **kw):
    d = dict(base)
    for k, v in kw.items():
        if v is None:
            d.pop(k, None)
        else:
            d[k] = v
        if k.startswith("leg"):
            d.pop("ik" + k[3:], None)
        if k.startswith("ik"):
            d.pop("leg" + k[2:], None)
    return d


def _lerp(a, b, t):
    if isinstance(a, tuple):
        return tuple(x + (y - x) * t for x, y in zip(a, b))
    return a + (b - a) * t


def blend(a, b, t):
    """두 키 포즈 숫자 보간. 한쪽에만 있는 leg/ik 키는 상대 키에서 FK↔IK 전환이 어려우므로 t < 0.5 쪽을 쓴다."""
    out = {}
    for k in set(a) | set(b):
        if k in a and k in b:
            out[k] = _lerp(a[k], b[k], t)
        else:
            src = a if t < 0.5 else b
            if k in src:
                out[k] = src[k]
    for s in ("L", "R"):
        if "ik" + s in out and "leg" + s in out:
            if t < 0.5:
                out.pop(("leg" if "ik" + s in a else "ik") + s)
            else:
                out.pop(("leg" if "ik" + s in b else "ik") + s)
    return out


def timeline(keys, ease=smooth):
    """keys = [(프레임, 키포즈)] → f(프레임) → 키포즈."""
    keys = sorted(keys, key=lambda kv: kv[0])

    def at(f):
        if f <= keys[0][0]:
            return keys[0][1]
        for (f0, a), (f1, b) in zip(keys, keys[1:]):
            if f <= f1:
                return blend(a, b, ease((f - f0) / (f1 - f0)))
        return keys[-1][1]
    return at


def _split(name_parts, vals, total):
    return {n: v * total for n, v in zip(name_parts, vals)}


def to_pose(k):
    p = Pose()
    hx, hy, hz = k.get("hips", (0.0, 0.0, 0.0))
    p.hips = Vector((hx, hy, hz))
    pitch, yaw, roll = k.get("hrot", (0.0, 0.0, 0.0))
    p.local["Hips"] = q(Y, yaw) @ q(X, pitch) @ q(Z, -roll)
    sp, sy, sr = k.get("spine", (0.0, 0.0, 0.0))
    for name, f in (("Spine", 0.4), ("Chest", 0.35), ("UpperChest", 0.25)):
        p.local[name] = q(Y, sy * f) @ q(X, sp * f) @ q(Z, -sr * f)
    hp, hy2 = k.get("head", (0.0, 0.0))
    p.local["Neck"] = q(Y, hy2 * 0.4) @ q(X, hp * 0.4)
    p.local["Head"] = q(Y, hy2 * 0.6) @ q(X, hp * 0.6)
    for s, side in (("L", -1), ("R", 1)):
        n = "Left" if side < 0 else "Right"
        down, flex, elbow = k.get("arm" + s, (33.0, 0.0, 10.0))
        down = min(down, ARM_DOWN_MAX)  # 056: MPFB 몸(엉덩이·반바지)이 043 튜브보다 넓다 — 팔을 몸통에 붙이지 않는다
        p.local[n + "UpperArm"] = q(X, -flex) @ q(Z, -side * down)
        p.local[n + "LowerArm"] = q(X, -elbow)
        p.local[n + "Hand"] = Quaternion()
    # 골반 회전 이후 엉덩이 관절 위치가 필요하다 (IK)
    hips_rw = p.local["Hips"]
    hips_head = Vector(S.HEAD["Hips"]) + p.hips
    for s, side in (("L", -1), ("R", 1)):
        n = "Left" if side < 0 else "Right"
        if "ik" + s in k:
            x, z, lift, fyaw = k["ik" + s]
            fp, fr = k.get("foot" + s, (0.0, 0.0))
            hip = hips_head + hips_rw @ (Vector(S.HEAD[n + "UpperLeg"]) - Vector(S.HEAD["Hips"]))
            ankle = Vector((x, S.ANKLE_Y + lift, z))
            pole = q(Y, fyaw) @ Z
            up, lo = leg_ik(hip, ankle, pole, n)
            p.world[n + "UpperLeg"] = up
            p.world[n + "LowerLeg"] = lo
            p.world[n + "Foot"] = q(Y, fyaw) @ q(X, fp) @ q(Z, -side * fr)
            p.world[n + "Toes"] = q(Y, fyaw) @ q(X, min(0.0, fp) * -0.6)
        else:
            flex, knee, ankle, abd, rot = k.get("leg" + s, (0.0, 0.0, 0.0, 0.0, 0.0))
            p.local[n + "UpperLeg"] = q(X, -flex) @ q(Z, side * abd) @ q(Y, side * rot)
            p.local[n + "LowerLeg"] = q(X, knee)
            p.local[n + "Foot"] = q(X, ankle)
    return p


# ---------------------------------------------------------------- 지면 보정 (FR-45)

GROUND_MIN = {"Toes": 0.0, "Foot": 0.035, "Hand": 0.02, "LowerArm": 0.03, "UpperArm": 0.04, "Head": 0.08}
TIP_MIN = 0.005


def joint_min(bone):
    for k, v in GROUND_MIN.items():
        if bone.endswith(k):
            return v
    return 0.045


def _deficit(pose):
    from ..pose import fk
    _, head, tail = fk(pose)
    d = min(p.y - joint_min(n) for n, p in head.items())
    return min(d, tail["LeftToes"].y - TIP_MIN, tail["RightToes"].y - TIP_MIN), head, tail


def _leg_low(head, tail, n):
    return min(head[n + "Foot"].y - joint_min("Foot"), head[n + "Toes"].y, tail[n + "Toes"].y - TIP_MIN)


def grounded(k):
    """키 포즈 → 바닥을 뚫지 않는 Pose. ① FK 다리: 발끝 내림(ankle)을 줄이고, 그래도 낮으면 무릎을 더 굽힌다
    ② 남은 부족분만큼 골반을 올린다(넘어짐·다이브처럼 몸 전체가 낮을 때). IK 다리는 목표가 바닥 위라 손대지 않는다."""
    from ..pose import fk
    k = dict(k)
    for s in ("L", "R"):
        key = "leg" + s
        if key not in k:
            continue
        n = "Left" if s == "L" else "Right"
        flex, knee, ankle, abd, rot = k[key]
        for _ in range(80):
            _, head, tail = fk(to_pose(k))
            if _leg_low(head, tail, n) >= -1e-4:
                break
            # 발이 엉덩이 아래로 깊게 내려간 경우(골반이 낮음)는 ②에서 처리
            if head[n + "UpperLeg"].y < 0.45:
                break
            if ankle > -10.0:
                ankle -= 3.0
            elif knee < 150.0:
                knee += 3.0
            else:
                break
            k[key] = (flex, knee, ankle, abd, rot)
    p = to_pose(k)
    d, _, _ = _deficit(p)
    if d < 0:
        hx, hy, hz = k.get("hips", (0.0, 0.0, 0.0))
        k["hips"] = (hx, hy - d, hz)
        p = to_pose(k)
    return p
